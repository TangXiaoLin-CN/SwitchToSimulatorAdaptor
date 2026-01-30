using System.Collections.Concurrent;
using System.Net;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

public class PacketForwardEngine : IAsyncDisposable
{
    private readonly string _networkInterface;
    private readonly Action<byte[], ushort, byte[], ushort, byte[]> _onUdpReceived;
    private readonly Action<byte[], ushort, byte[], ushort, byte[], TcpPacketFlags>? _onTcpReceived;

    private IPacketCapture? _capture;
    private ArpCache? _arpCache;
    private ArpProxy? _arpProxy;
    private CancellationTokenSource? _cts;

    private readonly byte[] _gatewayIp = AppSetting.GatewayIpBytes;
    private readonly byte[] _subnetNet = AppSetting.SubnetNetBytes;
    private readonly byte[] _subnetMask = AppSetting.SubnetMaskBytes;
    private ushort _identification;

    private bool _isRunning;

    private readonly ConcurrentDictionary<TcpSessionKey, TcpForwardSession> _tcpSessions = new();
    private readonly Timer? _gcTimer;
    private IPEndPoint? _tcpForwardTarget;

    public bool IsRunning => _isRunning;

    public PacketForwardEngine(
        string networkInterface,
        Action<byte[], ushort, byte[], ushort, byte[]> onUdpReceived,
        Action<byte[], ushort, byte[], ushort, byte[], TcpPacketFlags>? onTcpReceived)
    {
        _networkInterface = networkInterface;
        _onUdpReceived = onUdpReceived;
        _onTcpReceived = onTcpReceived;

        _gcTimer = new Timer(GarbageCollectTcpSessions, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            Logger.Instance?.LogWarning("PacketForwardEngine is already running");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _capture = new PcapCapture(_networkInterface);
        _capture.PacketReceived += OnPacketReceived;
        
        _arpCache = new ArpCache();
        _arpProxy = new ArpProxy(_arpCache);

        await _capture.StartAsync(_cts.Token);
        
        _isRunning = true;
        Logger.Instance?.LogInfo($"PacketForwardEngine started on interface: {_networkInterface}");
    }
    
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
        
        Logger.Instance?.LogInfo("PacketForwardEngine stopped");
    }

    public async Task SendUdpAsync(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort,
        ReadOnlyMemory<byte> data)
    {
        if (_capture == null || _arpCache == null)
        {
            Logger.Instance?.LogWarning("PacketForwardEngine is not initialized");
            return;
        }
        
        Logger.Instance?.LogInfo($"SendUdp: {ByteHelper.IpToString(srcIp)}:{srcPort} -> " +
                                 $"{ByteHelper.IpToString(dstIp)}:{dstPort} ({data.Length} bytes)");

        var udpPacket = UdpPacket.BuildIPv4Packet(srcIp, srcPort, dstIp, dstPort, data.Span, ref _identification);

        if (_arpCache.TryGetMac(dstIp, out var dstMac))
        {
            Logger.Instance?.LogDebug($"    -> Sending to known host {ByteHelper.MacToString(dstMac)}");
            await SendEthernetAsync(dstMac, EthernetFrame.TypeIPv4, udpPacket);
        }
        else if (ByteHelper.IsBroadcast(dstIp, _subnetNet, _subnetMask))
        {
            Logger.Instance?.LogInfo("    -> Broadcasting to all known hosts");
            foreach (var (_, mac) in _arpCache.GetAllValid())
            {
                await SendEthernetAsync(mac, EthernetFrame.TypeIPv4, udpPacket);
            }
        }
        else
        {
            Logger.Instance?.LogWarning($"    -> Target MAC not found in ARP cache, " +
                                        $"sending ARP request for {ByteHelper.IpToString(dstIp)}");

            var arpRequest = ArpProxy.BuildArpRequest(_capture.MacAddress, srcIp, dstIp);
            await _capture.SendPacketAsync(arpRequest);

            await Task.Delay(100);
            if (_arpCache.TryGetMac(dstIp, out dstMac))
            {
                Logger.Instance?.LogDebug($"    -> ARP resolved, sending to {ByteHelper.MacToString(dstMac)}");
                await SendEthernetAsync(dstMac, EthernetFrame.TypeIPv4, udpPacket);
            }
            else
            {
                Logger.Instance?.LogWarning($"    -> Failed to resolve MAC address for {ByteHelper.IpToString(dstIp)}");
            }
        }
    }
    
    public async Task SendTcpAsync(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort,
        uint seqNum, uint ackNum, byte flags, ReadOnlyMemory<byte> payload)
    {
        await SendTcpPacketAsync(srcIp, srcPort, dstIp, dstPort, seqNum, ackNum, flags, payload.ToArray());
    }

    public void SetTcpForwardTarget(IPEndPoint target)
    {
        _tcpForwardTarget = target;
        Logger.Instance?.LogInfo($"TCP forwarding target set to {target}");
    }

    public List<byte[]> GetAllKnownSwitchIps()
    {
        var result = new List<byte[]>();

        if (_arpCache == null)
            return result;
        
        foreach (var (ip, _) in _arpCache.GetAllValid())
        {
            if (ByteHelper.IsInSubnet(ip, _subnetNet, _subnetMask) &&
                !ByteHelper.CompareIp(ip, _gatewayIp))
            {
                result.Add(ip);
            }
        }
        
        return result;
    }
    
    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        foreach (var session in _tcpSessions.Values)
        {
            session.Dispose();
        }
        _tcpSessions.Clear();

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

    private void OnPacketReceived(ReadOnlyMemory<byte> data, byte[] mac)
    {
        try
        {
            var frame = EthernetFrame.Parse(data);

            if (ByteHelper.CompareMac(frame.SourceMac, mac)) return;

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
        catch (Exception e)
        {
            Logger.Instance?.LogError("Error processing packet");
        }
    }

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

    private async void ProcessIPv4(EthernetFrame frame, byte[] mac)
    {
        if (_arpCache == null)
            return;

        if (!IPv4Packet.TryParse(frame.Payload, out var ip))
            return;
        
        _arpCache.Set(frame.SourceMac, ip.SourceIp);

        if (!ByteHelper.IsInSubnet(ip.SourceIp, _subnetNet, _subnetMask))
            return;

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
                Logger.Instance?.LogDebug($"Ignoring packet with protocol: {ip.Protocol}");
                break;
        }
    }

    private async Task ProcessUdpAsync(IPv4Packet ip, EthernetFrame frame, byte[] mac)
    {
        if (!UdpPacket.TryParse(ip.Payload, out var udp))
            return;
        
        var srcIp = ip.SourceIp;
        var dstIp = ip.DestinationIp;
        
        Logger.Instance?.LogDebug($"UDP: {ByteHelper.IpToString(srcIp)}:{udp.SourcePort}" +
                                  $"-> {ByteHelper.IpToString(dstIp)}:{udp.DestinationPort} ({udp.Payload.Length} bytes)");

        _onUdpReceived(srcIp, udp.SourcePort, dstIp, udp.DestinationPort, udp.Payload.ToArray());

        if (ByteHelper.IsBroadcast(dstIp, _subnetNet, _subnetMask))
        {
            await ForwardBroadcastAsync(frame.Payload, frame.SourceMac);
        }
    }

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
        
        Logger.Instance?.LogInfo($"TCP: {ByteHelper.IpToString(srcIp)}:{tcp.SourcePort} " +
                                 $"-> {ByteHelper.IpToString(dstIp)}:{tcp.DestinationPort}, " +
                                 $"Flags:[{tcp.FlagsString}], Payload: {tcp.Payload.Length} bytes");

        _onTcpReceived?.Invoke(srcIp, tcp.SourcePort, dstIp, tcp.DestinationPort, tcp.Payload.ToArray(), flags);

        if (_tcpForwardTarget != null)
        {
            await HandleTcpForwardAsync(ip, tcp, srcIp, dstIp);
        }
    }
    
    private async Task ProcessIcmpAsync(IPv4Packet ip, byte[] mac)
    {
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
            Logger.Instance?.LogInfo($"ICMP: Echo Reply sent to {ByteHelper.IpToString(ip.SourceIp)}");
        }
    }
    
    private async Task HandleTcpForwardAsync(IPv4Packet ip, TcpPacket tcp, byte[] srcIp, byte[] dstIp)
    {
        if (_tcpForwardTarget == null) return;

        var key = new TcpSessionKey(srcIp, tcp.SourcePort, dstIp, tcp.DestinationPort);
        
        // SYN - 新连接
        if (tcp.HasSyn && !tcp.HasAck)
        {
            Logger.Instance?.LogInfo($"TCP SYN: Creating new forward session for {key}");

            var session = new TcpForwardSession(
                srcIp, tcp.SourcePort,
                dstIp, tcp.DestinationPort,
                _tcpForwardTarget,
                SendTcpPacketAsync);

            _tcpSessions[key] = session;
            await session.HandleSynAsync(tcp.SequenceNumber);
            return;
        }

        if (!_tcpSessions.TryGetValue(key, out var existingSession))
        {
            Logger.Instance?.LogWarning($"TCP session not found for {key}, flags: {tcp.FlagsString}");
            return;
        }

        await existingSession.ProcessPacketAsync(tcp);

        if (existingSession.IsClosed)
        {
            _tcpSessions.TryRemove(key, out _);
            Logger.Instance?.LogInfo($"TCP session closed and removed: {key}");
        }
    }
    
    private async Task SendTcpPacketAsync(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort, 
        uint seqNum, uint ackNum, byte flags, byte[] payload)
    {
        if (_capture == null || _arpCache == null)
            return;

        var tcpPacket = TcpPacket.Build(srcIp, srcPort, dstIp, dstPort, seqNum, ackNum, flags, 65535, payload);
        var ipPacket = IPv4Packet.Build(srcIp, dstIp, IPv4Packet.ProtocolTcp, tcpPacket, ref _identification);

        await SendToMacAsync(dstIp, ipPacket);
        
        Logger.Instance?.LogDebug($"TCP Send: {ByteHelper.IpToString(srcIp)}:{srcPort} " +
                                  $"-> {ByteHelper.IpToString(dstIp)}:{dstIp}, " +
                                  $"Flags: 0x{flags:X2}, Payload: {payload.Length} bytes");
    }
    
    private async Task SendToMacAsync(byte[] ip, ReadOnlyMemory<byte> payload)
    {
        if (_arpCache == null)
            return;

        if (_arpCache.TryGetMac(ip, out var mac))
        {
            await SendEthernetAsync(mac, EthernetFrame.TypeIPv4, payload);
        }
    }

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

    private async Task SendEthernetAsync(byte[] dstMac, ushort etherType, ReadOnlyMemory<byte> payload)
    {
        if (_capture == null)
            return;

        var buffer = new byte[EthernetFrame.HeaderLength + payload.Length];
        EthernetFrame.Build(buffer, dstMac, _capture.MacAddress, etherType, payload.Span);
        await _capture.SendPacketAsync(buffer);
    }
    
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
                Logger.Instance?.LogDebug($"Garbage collected TCP session: {key}");
            }
        }
    }
}