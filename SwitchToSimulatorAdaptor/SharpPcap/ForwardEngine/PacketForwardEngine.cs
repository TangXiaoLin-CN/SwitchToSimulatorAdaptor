using System.Collections.Concurrent;
using System.Net;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

/// <summary>
/// 独立的数据包转发引擎
/// 直接使用 PcapCapture 进行数据包捕获和发送
/// 支持 UDP 和 TCP 转发
/// </summary>
public class PacketForwardEngine : IAsyncDisposable
{
    private readonly string _networkInterface;
    private readonly Action<byte[], ushort, byte[], ushort, byte[]> _onUdpReceived;
    private readonly Action<byte[], ushort, byte[], ushort, byte[], TcpPacketFlags>? _onTcpReceived;

    private IPacketCapture? _capture;
    private ArpCache? _arpCache;
    private ArpProxy? _arpProxy;
    private CancellationTokenSource? _cts;

    private readonly byte[] _gatewayIp = AppSetting.GatewayIpBytes;    // 10.13.37.1
    private readonly byte[] _subnetNet = AppSetting.SubnetNetBytes;   // 10.13.0.0
    private readonly byte[] _subnetMask = AppSetting.SubnetMaskBytes; // 255.255.0.0
    private ushort _identification;

    private bool _isRunning;

    // TCP 转发会话管理
    private readonly ConcurrentDictionary<TcpSessionKey, TcpForwardSession> _tcpSessions = new();
    private readonly System.Threading.Timer? _gcTimer;
    private IPEndPoint? _tcpForwardTarget; // TCP 转发目标（如 LdnMitmAdapter）

    /// <summary>
    /// 创建数据包转发引擎
    /// </summary>
    /// <param name="networkInterface">网络接口名称</param>
    /// <param name="onUdpReceived">UDP 数据包接收回调 (srcIp, srcPort, dstIp, dstPort, data)</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <param name="onTcpReceived">TCP 数据包接收回调 (srcIp, srcPort, dstIp, dstPort, data, flags)</param>
    public PacketForwardEngine(
        string networkInterface,
        Action<byte[], ushort, byte[], ushort, byte[]> onUdpReceived,
        Action<byte[], ushort, byte[], ushort, byte[], TcpPacketFlags>? onTcpReceived = null)
    {
        _networkInterface = networkInterface;
        _onUdpReceived = onUdpReceived;
        _onTcpReceived = onTcpReceived;

        // TCP 会话垃圾回收定时器
        _gcTimer = new System.Threading.Timer(GarbageCollectTcpSessions, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// 启动引擎
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 初始化 pcap 捕获
        _capture = new PcapCapture(
            _networkInterface);
        _capture.PacketReceived += OnPacketReceived;

        // 初始化 ARP 缓存和代理
        _arpCache = new ArpCache();
        _arpProxy = new ArpProxy(_arpCache);

        // 启动捕获
        await _capture.StartAsync(_cts.Token);

        _isRunning = true;
    }

    /// <summary>
    /// 停止引擎
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _cts?.Cancel();

        if (_capture != null)
        {
            await _capture.StopAsync();
        }

    }

    /// <summary>
    /// 发送 UDP 数据包到子网
    /// </summary>
    public async Task SendUdpAsync(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort, ReadOnlyMemory<byte> data)
    {
        if (_capture == null || _arpCache == null)
        {
            return;
        }


        // 构建 UDP 数据包
        var udpPacket = UdpPacket.BuildIPv4Packet(srcIp, srcPort, dstIp, dstPort, data.Span, ref _identification);

        // 查找目标 MAC 地址
        if (_arpCache.TryGetMac(dstIp, out var dstMac))
        {
            await SendEthernetAsync(dstMac, EthernetFrame.TypeIPv4, udpPacket);
        }
        else if (ByteHelper.IsBroadcast(dstIp, _subnetNet, _subnetMask))
        {
            // 广播到所有已知主机
            foreach (var (_, mac) in _arpCache.GetAllValid())
            {
                await SendEthernetAsync(mac, EthernetFrame.TypeIPv4, udpPacket);
            }
        }
        else
        {

            // 发送 ARP 请求
            var arpRequest = ArpProxy.BuildArpRequest(_capture.MacAddress, srcIp, dstIp);
            await _capture.SendPacketAsync(arpRequest);

            // 等待一小段时间后重试
            await Task.Delay(100);
            if (_arpCache.TryGetMac(dstIp, out dstMac))
            {
                await SendEthernetAsync(dstMac, EthernetFrame.TypeIPv4, udpPacket);
            }
            else
            {

            }
        }
    }

    /// <summary>
    /// 处理从网卡收到的数据包
    /// </summary>
    private void OnPacketReceived(ReadOnlyMemory<byte> data, byte[] mac)
    {
        try
        {
            var frame = EthernetFrame.Parse(data);

            // 忽略自己发的包
            if (ByteHelper.CompareMac(frame.SourceMac, mac))
                return;

            switch (frame.EtherType)
            {
                case EthernetFrame.TypeArp:
                    ProcessArp(frame, mac);
                    break;

                case EthernetFrame.TypeIPv4:
                    ProcessIPv4(frame, mac);
                    break;
            }
        }
        catch (Exception ex)
        {

        }
    }

    /// <summary>
    /// 处理 ARP 包
    /// </summary>
    private async void ProcessArp(EthernetFrame frame, byte[] mac)
    {
        if (_arpProxy == null || _capture == null)
            return;

        if (!ArpPacket.TryParse(frame.Payload.Span, out var arp))
            return;

        var response = _arpProxy.ProcessArp(frame, arp, mac);
        if (response != null)
        {
            await _capture.SendPacketAsync(response);
        }
    }

    /// <summary>
    /// 处理 IPv4 包
    /// </summary>
    private async void ProcessIPv4(EthernetFrame frame, byte[] mac)
    {
        if (_arpCache == null)
            return;

        if (!IPv4Packet.TryParse(frame.Payload, out var ip))
            return;

        // 更新 ARP 缓存
        _arpCache.Set(frame.SourceMac, ip.SourceIp);

        // 检查是否在 10.13.x.x 子网内
        if (!ByteHelper.IsInSubnet(ip.SourceIp, _subnetNet, _subnetMask))
        {
            return;
        }

        // 根据协议类型处理
        switch (ip.Protocol)
        {
            case IPv4Packet.ProtocolUdp:
                await ProcessUdpAsync(ip, frame, mac);
                break;

            case IPv4Packet.ProtocolTcp:
                await ProcessTcpAsync(ip, frame, mac);
                break;

            case IPv4Packet.ProtocolIcmp:
                await ProcessIcmpAsync(ip, mac);
                break;

            default:

                break;
        }
    }

    /// <summary>
    /// 处理 UDP 包
    /// </summary>
    private async Task ProcessUdpAsync(IPv4Packet ip, EthernetFrame frame, byte[] mac)
    {
        if (!UdpPacket.TryParse(ip.Payload, out var udp))
            return;

        var srcIp = ip.SourceIp;
        var dstIp = ip.DestinationIp;


        // 调用 UDP 回调函数
        _onUdpReceived(
            srcIp,
            udp.SourcePort,
            dstIp,
            udp.DestinationPort,
            udp.Payload.ToArray());

        // 如果是广播包，也转发到其他已知主机
        if (ByteHelper.IsBroadcast(dstIp, _subnetNet, _subnetMask))
        {
            await ForwardBroadcastAsync(frame.Payload, frame.SourceMac);
        }
    }

    /// <summary>
    /// 处理 TCP 包
    /// </summary>
    private async Task ProcessTcpAsync(IPv4Packet ip, EthernetFrame frame, byte[] mac)
    {
        if (!TcpPacket.TryParse(ip.Payload, out var tcp))
            return;

        var srcIp = ip.SourceIp;
        var dstIp = ip.DestinationIp;

        var flags = new TcpPacketFlags
        {
            Syn = tcp.HasSyn,
            Ack = tcp.HasAck,
            Fin = tcp.HasFin,
            Rst = tcp.HasRst,
            Psh = tcp.HasPsh,
            SequenceNumber = tcp.SequenceNumber,
            AcknowledgmentNumber = tcp.AcknowledgmentNumber
        };


        // 调用 TCP 回调函数（如果有）
        _onTcpReceived?.Invoke(
            srcIp,
            tcp.SourcePort,
            dstIp,
            tcp.DestinationPort,
            tcp.Payload.ToArray(),
            flags);

        // TCP 转发处理
        if (_tcpForwardTarget != null)
        {
            await HandleTcpForwardAsync(ip, tcp, srcIp, dstIp);
        }
    }

    /// <summary>
    /// 处理 TCP 转发
    /// </summary>
    private async Task HandleTcpForwardAsync(IPv4Packet ip, TcpPacket tcp, byte[] srcIp, byte[] dstIp)
    {
        if (_tcpForwardTarget == null)
            return;

        var key = new TcpSessionKey(srcIp, tcp.SourcePort, dstIp, tcp.DestinationPort);

        // SYN - 新连接
        if (tcp.HasSyn && !tcp.HasAck)
        {

            var session = new TcpForwardSession(
                srcIp, tcp.SourcePort,
                dstIp, tcp.DestinationPort,
                _tcpForwardTarget,
                SendTcpPacketAsync);

            _tcpSessions[key] = session;
            await session.HandleSynAsync(tcp.SequenceNumber);
            return;
        }

        // 查找现有会话
        if (!_tcpSessions.TryGetValue(key, out var existingSession))
        {
            return;
        }

        await existingSession.ProcessPacketAsync(tcp);

        // 连接关闭后清理
        if (existingSession.IsClosed)
        {
            _tcpSessions.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 发送 TCP 包到 Switch
    /// </summary>
    private async Task SendTcpPacketAsync(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort,
        uint seqNum, uint ackNum, byte flags, byte[] data)
    {
        if (_capture == null || _arpCache == null)
            return;

        // 构建 TCP 包
        var tcpPacket = TcpPacket.Build(srcIp, srcPort, dstIp, dstPort, seqNum, ackNum, flags, 65535, data);

        // 构建 IPv4 包
        var ipPacket = IPv4Packet.Build(srcIp, dstIp, IPv4Packet.ProtocolTcp, tcpPacket, ref _identification);

        // 发送
        await SendToMacAsync(dstIp, ipPacket);

    }

    /// <summary>
    /// 发送 TCP 包（公开方法）
    /// </summary>
    public async Task SendTcpAsync(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort,
        uint seqNum, uint ackNum, byte flags, ReadOnlyMemory<byte> data)
    {
        await SendTcpPacketAsync(srcIp, srcPort, dstIp, dstPort, seqNum, ackNum, flags, data.ToArray());
    }

    /// <summary>
    /// 设置 TCP 转发目标
    /// </summary>
    public void SetTcpForwardTarget(IPEndPoint target)
    {
        _tcpForwardTarget = target;
    }

    /// <summary>
    /// 清理过期的 TCP 会话
    /// </summary>
    private void GarbageCollectTcpSessions(object? state)
    {
        var closedSessions = _tcpSessions
            .Where(kvp => kvp.Value.IsClosed)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in closedSessions)
        {
            if (_tcpSessions.TryRemove(key, out var session))
            {
                session.Dispose();
            }
        }
    }

    /// <summary>
    /// 处理 ICMP 包
    /// </summary>
    private async Task ProcessIcmpAsync(IPv4Packet ip, byte[] mac)
    {
        // 只处理发给网关的 ICMP
        if (!ByteHelper.CompareIp(ip.DestinationIp, _gatewayIp))
            return;

        if (!IcmpPacket.TryParse(ip.Payload, out var icmp))
            return;

        if (icmp.IsEchoRequest)
        {
            var reply = icmp.BuildReply();
            var response = IPv4Packet.Build(_gatewayIp, ip.SourceIp,
                IPv4Packet.ProtocolIcmp, reply, ref _identification);

            await SendToMacAsync(ip.SourceIp, response);
        }
    }

    /// <summary>
    /// 转发广播包
    /// </summary>
    private async Task ForwardBroadcastAsync(ReadOnlyMemory<byte> payload, byte[] excludeMac)
    {
        if (_arpCache == null)
            return;

        foreach (var (_, mac) in _arpCache.GetAllValid())
        {
            if (!ByteHelper.CompareMac(mac, excludeMac))
            {
                await SendEthernetAsync(mac, EthernetFrame.TypeIPv4, payload);
            }
        }
    }

    /// <summary>
    /// 发送到指定 MAC
    /// </summary>
    private async Task SendToMacAsync(byte[] ip, ReadOnlyMemory<byte> payload)
    {
        if (_arpCache == null)
            return;

        if (_arpCache.TryGetMac(ip, out var mac))
        {
            await SendEthernetAsync(mac, EthernetFrame.TypeIPv4, payload);
        }
    }

    /// <summary>
    /// 发送以太网帧
    /// </summary>
    private async Task SendEthernetAsync(byte[] dstMac, ushort etherType, ReadOnlyMemory<byte> payload)
    {
        if (_capture == null)
            return;

        var buffer = new byte[EthernetFrame.HeaderLength + payload.Length];
        EthernetFrame.Build(buffer, dstMac, _capture.MacAddress, etherType, payload.Span);
        await _capture.SendPacketAsync(buffer);
    }

    /// <summary>
    /// 获取 ARP 缓存中的所有 Switch IP
    /// </summary>
    public System.Collections.Generic.List<byte[]> GetAllKnownSwitchIps()
    {
        var result = new System.Collections.Generic.List<byte[]>();
        if (_arpCache == null)
            return result;

        foreach (var (ip, _) in _arpCache.GetAllValid())
        {
            // 只返回 10.13.x.x 子网中的 IP（排除网关）
            if (ByteHelper.IsInSubnet(ip, _subnetNet, _subnetMask) &&
                !ByteHelper.CompareIp(ip, _gatewayIp))
            {
                result.Add(ip);
            }
        }
        return result;
    }

    public bool IsRunning => _isRunning;

    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        // 清理 TCP 会话
        foreach (var session in _tcpSessions.Values)
        {
            session.Dispose();
        }
        _tcpSessions.Clear();

        // 停止垃圾回收定时器
        if (_gcTimer != null)
        {
            await _gcTimer.DisposeAsync();
        }

        if (_capture != null)
        {
            await _capture.DisposeAsync();
        }

        _cts?.Dispose();
    }
}