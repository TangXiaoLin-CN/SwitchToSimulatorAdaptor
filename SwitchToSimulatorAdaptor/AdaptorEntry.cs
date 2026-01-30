using System.Net;
using System.Runtime.InteropServices;
using SharpPcap;
using SwitchToSimulatorAdaptor.Common;
using SwitchToSimulatorAdaptor.EdenRoom;
using SwitchToSimulatorAdaptor.ForwardEngine;
using SwitchToSimulatorAdaptor.SharpPcap;
using SwitchToSimulatorAdaptor.SwitchLdn;
using SwitchToSimulatorAdaptor.Utils;
using EthernetFrame = SwitchToSimulatorAdaptor.SharpPcap.EthernetFrame;

namespace SwitchToSimulatorAdaptor;

public class AdaptorEntry
{
    IPv4Address? _switchIp;
    byte[]? _switchMac;
    private NetworkInfo _networkInfo;
    private SharpPcapManager _sharpPcapMgr;
    private EdenRoomMember _edenRoomClient;

    // LDN 协议常量
    private const uint LdnMagic = 0x11451400;

    private const int
        LdnHeaderSize =
            12; // LanPacketHeader 大小: Magic(4) + Type(1) + Compressed(1) + Length(2) + DecompressLength(2) + Reserved(2) = 12 bytes

    private CancellationTokenSource? _cancellationTokenSource;
    private PacketForwardEngine? _forwardEngine;
     private string? _networkInterface = "\\Device\\NPF_{422739E8-9756-4B09-83B7-903BD906AD7E}";
    //private string? _networkInterface = "\\Device\\NPF_{199BDD7E-457F-43A0-8932-7B08B815E9FE}";
    private IPEndPoint? _tcpForwardTarget;
    private readonly object _lock = new object();
    
    private const string LogFlag = "[AdaptorEntry]";

    public void Start()
    {
        Console.WriteLine("SwitchToSimulatorAdaptor");

        // Console.WriteLine("Input Enter to join room:");
        // Console.ReadLine();
        //
        if (!NativeENetHost.InitializeNativeENet())
        {
            Console.WriteLine("初始化NativeENet失败");
            return;
        }


        _edenRoomClient = new EdenRoomMember();
        _edenRoomClient.LdnPacketReceived += ReceiveEdenLdnPacket;
        _edenRoomClient.ProxyPacketReceived += ReceiveEdenProxyPacket;
        _edenRoomClient.Join(AppSetting.EdenRoomNickname);
        Console.WriteLine("成功加入房间");
        // Console.ReadLine();

        // _sharpPcapMgr = new SharpPcapManager();
        //
        // if (!_sharpPcapMgr.Init())
        // {
        //     Console.WriteLine("初始化SharpPcap失败");
        //     return;
        // }
        //
        //
        // _sharpPcapMgr.RegisterPacketArrivalEvent(ReceiveRawSwitchUdpPacket);
        //
        // _sharpPcapMgr.StartCapture();
        // Console.WriteLine($"开始捕获，规则：{AppSetting.BPFFilter}");
        //
        // _switchMac = new byte[6] { 255, 255, 255, 255, 255, 255 };
        // ReceiveEdenLdnPacket(new EdenLDNPacket
        // {
        //     Type = EdenLDNPacketType.Scan,
        //     LocalIp = new Common.IPv4Address(192, 168, 1, 40),
        //     RemoteIp = new Common.IPv4Address(192, 168, 1, 255),
        //     Broadcast = true,
        //     Data = new byte[] { }
        // });

        StartForwardEngine();
    }

    private void StartForwardEngine()
    {
        try
        {
            _cancellationTokenSource = new CancellationTokenSource();

            _forwardEngine = new PacketForwardEngine(_networkInterface, OnPacketFromSwitch, OnTcpPacketFromSwitch);

            if (_tcpForwardTarget != null)
            {
                _forwardEngine.SetTcpForwardTarget(_tcpForwardTarget);
                Logger.Instance?.LogInfo($"TCP 转发目标已配置：{_tcpForwardTarget}");
            }

            Task.Run(async () =>
            {
                try
                {
                    await _forwardEngine.StartAsync(_cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    // 正常取消
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private void SendUdpToSwitch(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort, byte[] data)
    {
        if (_forwardEngine == null)
            return;

        Task.Run(async () =>
        {
            try
            {
                await _forwardEngine.SendUdpAsync(srcIp, srcPort, dstIp, dstPort, data);
            }
            catch (Exception e)
            {
                Logger.Instance?.LogError($"{LogFlag} 发送 UDP 失败", e);
            }
        });
    }
    
    private void OnTcpPacketFromSwitch(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPor, byte[] data,
        TcpPacketFlags tcpFlags)
    {
    }

    private void OnPacketFromSwitch(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort, byte[] data)
    {
        try
        {
            // 创建 IPv4Address 对象
            var switchIp = new IPv4Address(srcIp[0], srcIp[1], srcIp[2], srcIp[3]);
            var destIp = new IPv4Address(dstIp[0], dstIp[1], dstIp[2], dstIp[3]);

            Logger.Instance?.LogInfo($"{LogFlag} 收到 SwitchLanPlay LDN 数据包: {switchIp.A}.{switchIp.B}.{switchIp.C}.{switchIp.D}:{srcPort} -> {destIp.A}.{destIp.B}.{destIp.C}.{destIp.D}:{dstPort}, {data.Length} bytes");

            // 检查数据长度是否足够包含 LDN 协议头
            if (data.Length < LdnHeaderSize)
            {
                Logger.Instance?.LogWarning($"{LogFlag} LDN 数据包太短: {data.Length} < {LdnHeaderSize}");
                return;
            }

            // 解析 LDN 协议头
            // LanPacketHeader 结构: Magic(4) + Type(1) + Compressed(1) + Length(2) + DecompressLength(2) + Reserved(2) = 12 bytes
            uint magic = BitConverter.ToUInt32(data, 0);
            if (magic != LdnMagic)
            {
                Logger.Instance?.LogWarning($"{LogFlag} LDN 数据包 magic 不匹配: 0x{magic:X8} != 0x{LdnMagic:X8}");
                return;
            }

            byte packetType = data[4];
            byte compressed = data[5];
            ushort length = BitConverter.ToUInt16(data, 6);
            ushort decompressLength = BitConverter.ToUInt16(data, 8);

            Logger.Instance?.LogInfo($"{LogFlag} LDN 协议头: Type={packetType} ({(LdnPacketType)packetType}), Compressed={compressed}, Length={length}, DecompressLength={decompressLength}");

            // 检查数据包完整性
            int totalSize = LdnHeaderSize + length;
            if (data.Length < totalSize)
            {
                Logger.Instance?.LogWarning($"{LogFlag} LDN 数据包不完整: 期望 {totalSize} 字节, 实际 {data.Length} 字节");
                return;
            }

            // 提取 payload 数据
            byte[] payload = new byte[length];
            if (length > 0)
            {
                Array.Copy(data, LdnHeaderSize, payload, 0, length);
            }

            // 如果数据被压缩，进行解压缩
            if (compressed == 1 && length > 0)
            {
                byte[] decompressedPayload = DecompressLdnData(payload);
                if (decompressedPayload == null)
                {
                    Logger.Instance?.LogError($"{LogFlag} LDN 数据包解压缩失败");
                    return;
                }
                if (decompressedPayload.Length != decompressLength)
                {
                    Logger.Instance?.LogWarning($"{LogFlag} LDN 数据包解压缩长度不匹配: 期望 {decompressLength}, 实际 {decompressedPayload.Length}");
                }
                payload = decompressedPayload;
                Logger.Instance?.LogInfo($"{LogFlag} LDN 数据包已解压缩: {length} -> {payload.Length} 字节");
            }

            // 根据数据包类型处理
            var lanPacketType = (LdnPacketType)packetType;
            switch (lanPacketType)
            {
                case LdnPacketType.Scan:
                    HandleSwitchLanPlayScan(switchIp, destIp, payload);
                    break;

                case LdnPacketType.ScanResponse:
                    // HandleSwitchLanPlayScanResponse(switchIp, destIp, payload);
                    break;

                case LdnPacketType.Connect:
                    // HandleSwitchLanPlayConnect(switchIp, destIp, payload);
                    break;

                case LdnPacketType.SyncNetwork:
                    // HandleSwitchLanPlaySyncNetwork(switchIp, destIp, payload);
                    break;

                case LdnPacketType.Disconnect:
                    // HandleSwitchLanPlayDisconnect(switchIp, destIp, payload);
                    break;

                default:
                    Logger.Instance?.LogWarning($"{LogFlag} 未处理的 LDN 数据包类型: {lanPacketType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Instance?.LogError($"{LogFlag} 处理 SwitchLanPlay LDN 数据包时出错: {ex.Message}");
            Logger.Instance?.LogError($"{LogFlag} 堆栈跟踪: {ex.StackTrace}");
        }
    }

    private void HandleSwitchLanPlayScan(IPv4Address switchIp, IPv4Address destIp, byte[] payload)
    {
        Logger.Instance?.LogInfo($"{LogFlag} 处理 SwitchLanPlay Scan: 来自 {switchIp.A}.{switchIp.B}.{switchIp.C}.{switchIp.D}");

        // 保存 Switch 的 Scan 请求发送者 IP，用于后续转发 ScanResponse
        _switchIp = switchIp;

        // 创建 LDN 数据包并转发到 ldn_eden
        var scanPacket = new EdenLDNPacket
        {
            Type = EdenLDNPacketType.Scan,
            LocalIp = switchIp,
            RemoteIp = new (AppSetting.BroadcastBytes),
            Broadcast = true,
            Data = payload
        };
        
        _edenRoomClient.SendLdnPacket(scanPacket);
    }

    void ReceiveEdenProxyPacket(EdenProxyPacket obj)
    {
        Logger.Instance?.LogInfo($"edenProxyPacket");
    }


    void ReceiveEdenLdnPacket(EdenLDNPacket packet)
    {
        SendLdnPacketToSwitchViaSwitchLanPlay(packet);
        
        return;
        lock (_lock)
        {
            Logger.Instance?.LogInfo($"{LogFlag} 收到Eden数据包, Type={packet.Type}, 大小={packet.Data?.Length ?? 0}");
            
            switch (packet.Type)
            {
                case EdenLDNPacketType.Scan:
                    // HandleScanPacket(packet);
                    break;
                    
                case EdenLDNPacketType.ScanResp:
                    HandleScanResponsePacket(packet);
                    break;
                    
                case EdenLDNPacketType.Connect:
                    // HandleConnectPacket(packet);
                    break;
                    
                case EdenLDNPacketType.Disconnect:
                    // HandleDisconnectPacket(packet);
                    break;
                    
                case EdenLDNPacketType.SyncNetwork:
                    // HandleSyncNetworkPacket(packet);
                    break;
                    
                case EdenLDNPacketType.DestroyNetwork:
                    // HandleDestroyNetworkPacket(packet);
                    break;
                    
                default:
                    Logger.Instance?.LogWarning($"{LogFlag} 未处理的数据包类型: {packet.Type}");
                    break;
            }
            
        }
    }
    
    private void HandleScanResponsePacket(EdenLDNPacket packet)
    {
        Logger.Instance?.LogInfo($"{LogFlag} 收到来着Eden的 ScanResponse");
        
        if (packet.Data == null || packet.Data.Length < Marshal.SizeOf<NetworkInfo>())
        {
            Logger.Instance?.LogWarning($"{LogFlag} ScanResponse 数据包太小: {packet.Data?.Length ?? 0} 字节");
            return;
        }
        
        _networkInfo = MemoryMarshal.Read<NetworkInfo>(packet.Data);
        
        
    }
    
    public void SendLdnPacketToSwitchViaSwitchLanPlay(EdenLDNPacket packet)
    {
        try
        {
            byte[] payloadData = packet.Data ?? Array.Empty<byte>();
            byte compressed = 0;
            ushort decompressLength = 0;

            // 尝试压缩数据（如果有数据）
            if (payloadData.Length > 0)
            {
                byte[]? compressedData = CompressLdnData(payloadData);
                if (compressedData != null && compressedData.Length < payloadData.Length)
                {
                    // 压缩成功且确实减小了大小
                    decompressLength = (ushort)payloadData.Length;
                    payloadData = compressedData;
                    compressed = 1;
                    Logger.Instance?.LogDebug($"{LogFlag} LDN 数据包已压缩: {packet.Data?.Length ?? 0} -> {payloadData.Length} 字节");
                }
            }

            // 构建 LDN 协议头 (12 字节)
            // Magic(4) + Type(1) + Compressed(1) + Length(2) + DecompressLength(2) + Reserved(2)
            byte[] header = new byte[LdnHeaderSize];
            BitConverter.GetBytes(LdnMagic).CopyTo(header, 0);
            header[4] = (byte)EdenTypeToSwitchType(packet.Type);
            header[5] = compressed;
            BitConverter.GetBytes((ushort)payloadData.Length).CopyTo(header, 6);
            BitConverter.GetBytes(decompressLength).CopyTo(header, 8);
            // Reserved bytes (offset 10-11) remain 0

            // 合并头和数据
            byte[] fullPacket = new byte[LdnHeaderSize + payloadData.Length];
            header.CopyTo(fullPacket, 0);
            if (payloadData.Length > 0)
            {
                payloadData.CopyTo(fullPacket, LdnHeaderSize);
            }

            // 获取目标 IP
            byte[] srcIp = AppSetting.GatewayIpBytes; // 网关 IP
            byte[] dstIp;
            if (packet.Broadcast)
            {
                dstIp = AppSetting.BroadcastBytes; // 广播地址
            }
            else
            {
                dstIp = packet.RemoteIp.ToBytes();
            }

            // 发送
            SendUdpToSwitch(srcIp, 11452, dstIp, 11452, fullPacket);
            Logger.Instance?.LogInfo($"{LogFlag} 已通过 SwitchLanPlay 发送 LDN 数据包: Type={packet.Type}, 目标={dstIp[0]}.{dstIp[1]}.{dstIp[2]}.{dstIp[3]}, Compressed={compressed}");
        }
        catch (Exception ex)
        {
            Logger.Instance?.LogError($"{LogFlag} 发送 LDN 数据包失败: {ex.Message}");
        }
    }

    void ReceiveRawSwitchUdpPacket(object sender, PacketCapture e)
    {
        if (!RawSwitchLdnPacketToEdenLdnPacket(e.Data.ToArray(),
                out var edenLdnPacket)) return;

        //_edenRoomClient.SendLdnPacket(edenLdnPacket);
    }

    byte[] EdenPacketToRawSwitchPacket(EdenLDNPacket edenLdnPacket)
    {
        byte[] data = null;


        try
        {
            byte[] payloadData = edenLdnPacket.Data ?? Array.Empty<byte>();
            byte compressed = 0;
            ushort decompressLength = 0;

            // 尝试压缩数据（如果有数据）
            if (payloadData.Length > 0)
            {
                byte[]? compressedData = CompressLdnData(payloadData);
                if (compressedData != null && compressedData.Length < payloadData.Length)
                {
                    // 压缩成功且确实减小了大小
                    decompressLength = (ushort)payloadData.Length;
                    payloadData = compressedData;
                    compressed = 1;
                    Logger.Instance?.LogDebug(
                        $"{LogFlag} LDN 数据包已压缩: {edenLdnPacket.Data?.Length ?? 0} -> {payloadData.Length} 字节");
                }
            }

            // 构建 LDN 协议头 (12 字节)
            // Magic(4) + Type(1) + Compressed(1) + Length(2) + DecompressLength(2) + Reserved(2)
            byte[] header = new byte[LdnHeaderSize];
            BitConverter.GetBytes(LdnMagic).CopyTo(header, 0);
            header[4] = (byte)EdenTypeToSwitchType((edenLdnPacket.Type));
            header[5] = compressed;
            BitConverter.GetBytes((ushort)payloadData.Length).CopyTo(header, 6);
            BitConverter.GetBytes(decompressLength).CopyTo(header, 8);
            // Reserved bytes (offset 10-11) remain 0

            // 合并头和数据
            byte[] fullPacket = new byte[LdnHeaderSize + payloadData.Length];
            header.CopyTo(fullPacket, 0);
            if (payloadData.Length > 0)
            {
                payloadData.CopyTo(fullPacket, LdnHeaderSize);
            }

            data = fullPacket;

            data = PacketBuilder.BuildUdpPacket(_sharpPcapMgr.DeviceMac, _switchMac, edenLdnPacket.LocalIp.ToBytes(),
                edenLdnPacket.RemoteIp.ToBytes(), AppSetting.LdnPacketPort, AppSetting.LdnPacketPort, data);
        }
        catch (Exception ex)
        {
            Logger.Instance?.LogError($"{LogFlag} 发送 LDN 数据包失败: {ex.Message}");
        }


        return data;
    }


    bool RawSwitchLdnPacketToEdenLdnPacket(byte[] rawSwitchData, out EdenLDNPacket edenLdnPacket)
    {
        edenLdnPacket = new EdenLDNPacket();
        var ethernet = EthernetFrame.Parse(rawSwitchData);
        if (ethernet == null) return false;

        var ip = IPv4Header.Parse(ethernet.Payload);
        if (ip == null) return false;

        if (ip.IsUDP)
        {
            var udp = UdpHeader.Parse(ip.Payload);
            if (udp == null) return false;

            Logger.Instance?.LogDebug($"[UDP] 端口:{udp.SourcePort} -> {udp.DestinationPort}");

            if (udp.DestinationPort == 11452 || udp.SourcePort == 11452)
            {
                var ldn = LdnHeader.Parse(udp.Payload);
                if (ldn != null && ldn.IsValid)
                {
                    _switchIp = new(ip.SourceIP);
                    _switchMac = ethernet.SourceMac;

                    edenLdnPacket = new EdenLDNPacket()
                    {
                        Type = SwitchTypeToEdenType(ldn.Type),
                        LocalIp = new(ip.SourceIP),
                        RemoteIp = new(ip.DestinationIP),
                        Broadcast = ethernet.IsBroadcast,
                        Data = ldn.Payload
                    };
                    Logger.Instance?.LogDebug($"[LDN] 类型：{ldn.Type}， 长度：{ldn.Length}， 压缩状态：{ldn.IsCompressed}");
                    return true;
                }
            }
        }
        else if (ip.IsTCP)
        {
            var tcp = TcpHeader.Parse(ip.Payload);
            if (tcp == null) return false;

            Logger.Instance?.LogDebug($"[TCP] 端口:{tcp.SourcePort} -> {tcp.DestinationPort}");
        }

        return false;
    }

    private byte[]? EdenLdnPacketToRawSwitchLdnPacket(EdenLDNPacket packet)
    {
        byte[] payloadData = packet.Data ?? Array.Empty<byte>();
        byte compressed = 0;
        ushort decompressLength = 0;

        // 尝试压缩数据（如果有数据）
        if (payloadData.Length > 0)
        {
            byte[]? compressedData = CompressLdnData(payloadData);
            if (compressedData != null && compressedData.Length < payloadData.Length)
            {
                // 压缩成功且确实减小了大小
                decompressLength = (ushort)payloadData.Length;
                payloadData = compressedData;
                compressed = 1;
                Logger.Instance?.LogDebug(
                    $"{LogFlag} LDN 数据包已压缩: {packet.Data?.Length ?? 0} -> {payloadData.Length} 字节");
            }
        }

        // 构建 LDN 协议头 (12 字节)
        // Magic(4) + Type(1) + Compressed(1) + Length(2) + DecompressLength(2) + Reserved(2)
        byte[] header = new byte[LdnHeaderSize];
        BitConverter.GetBytes(LdnMagic).CopyTo(header, 0);
        header[4] = (byte)EdenTypeToSwitchType((packet.Type));
        header[5] = compressed;
        BitConverter.GetBytes((ushort)payloadData.Length).CopyTo(header, 6);
        BitConverter.GetBytes(decompressLength).CopyTo(header, 8);
        // Reserved bytes (offset 10-11) remain 0

        // 合并头和数据
        byte[] fullPacket = new byte[LdnHeaderSize + payloadData.Length];
        header.CopyTo(fullPacket, 0);
        if (payloadData.Length > 0)
        {
            payloadData.CopyTo(fullPacket, LdnHeaderSize);
        }

        return fullPacket;
    }

    /// <summary>
    /// 解压缩 LDN 数据（零字节游程编码）
    /// 算法：0 后跟一个字节表示额外的 0 的数量
    /// </summary>
    private byte[]? DecompressLdnData(byte[] input)
    {
        const int BufferSize = 2048;
        var outputList = new System.Collections.Generic.List<byte>();
        int i = 0;

        while (i < input.Length && outputList.Count < BufferSize)
        {
            byte inputByte = input[i++];
            outputList.Add(inputByte);

            if (inputByte == 0)
            {
                if (i >= input.Length)
                {
                    // 压缩格式错误：0 后没有计数字节
                    return null;
                }

                int count = input[i++];
                for (int j = 0; j < count; j++)
                {
                    if (outputList.Count >= BufferSize)
                    {
                        break;
                    }

                    outputList.Add(0);
                }
            }
        }

        if (i != input.Length)
        {
            Logger.Instance.LogWarning($"{LogFlag} LDN 解压缩未完全消费输入: 消费 {i}/{input.Length} 字节");
        }

        return outputList.ToArray();
    }

    /// <summary>
    /// 压缩 LDN 数据（零字节游程编码）
    /// 算法：连续的 0 被压缩为 0 + 额外 0 的数量
    /// </summary>
    private byte[]? CompressLdnData(byte[] input)
    {
        const int BufferSize = 2048;
        var outputList = new System.Collections.Generic.List<byte>();
        int i = 0;
        int maxCount = 0xFF;

        while (i < input.Length)
        {
            byte inputByte = input[i++];
            int count = 0;

            if (inputByte == 0)
            {
                while (i < input.Length && input[i] == 0 && count < maxCount)
                {
                    count++;
                    i++;
                }
            }

            if (inputByte == 0)
            {
                outputList.Add(0);
                if (outputList.Count >= BufferSize)
                {
                    return null;
                }

                outputList.Add((byte)count);
            }
            else
            {
                outputList.Add(inputByte);
            }
        }

        return i == input.Length ? outputList.ToArray() : null;
    }

    LdnPacketType EdenTypeToSwitchType(EdenLDNPacketType edenLdnType)
    {
        return edenLdnType switch
        {
            EdenLDNPacketType.Scan => LdnPacketType.Scan,
            EdenLDNPacketType.ScanResp => LdnPacketType.ScanResponse,
            EdenLDNPacketType.Connect => LdnPacketType.Connect,
            EdenLDNPacketType.SyncNetwork => LdnPacketType.SyncNetwork,
            EdenLDNPacketType.Disconnect => LdnPacketType.Disconnect,
            _ => LdnPacketType.Scan
        };
    }

    EdenLDNPacketType SwitchTypeToEdenType(LdnPacketType switchType)
    {
        return switchType switch
        {
            LdnPacketType.Scan => EdenLDNPacketType.Scan,
            LdnPacketType.ScanResponse => EdenLDNPacketType.ScanResp,
            LdnPacketType.Connect => EdenLDNPacketType.Connect,
            LdnPacketType.SyncNetwork => EdenLDNPacketType.SyncNetwork,
            LdnPacketType.Disconnect => EdenLDNPacketType.Disconnect,
            _ => EdenLDNPacketType.Scan
        };
    }

    // 2. UDP 校验和计算（可选）
    // UDP 校验和在 IPv4 中是可选的，可以填 0 表示不使用。但如果要计算：


    // 3. 构建 UDP 数据包（完整示例）
    public class PacketBuilder
    {
        public static byte[] BuildUdpPacket(
            byte[] srcMac, // 源 MAC （6 字节）
            byte[] dstMac, // 目标 MAC （6 字节）
            byte[] srcIp, // 源 IP （4 字节）
            byte[] dstIp, // 目标 IP （4 字节）
            ushort srcPort, // 源端口
            ushort dstPort, // 目标端口
            byte[] payload // 应用层数据
        )
        {
            // === 1. 构建 UDP 头 （8 字节） ===
            byte[] udpHeader = new byte[8];

            // 源端口 （大端序）
            udpHeader[0] = (byte)(srcPort >> 8);
            udpHeader[1] = (byte)(srcPort & 0xFF);

            // 目标端口
            udpHeader[2] = (byte)(dstPort >> 8);
            udpHeader[3] = (byte)(dstPort & 0xFF);

            // UDP 长度 （头 + 数据）
            ushort udpLength = (ushort)(payload.Length + 8);
            udpHeader[4] = (byte)(udpLength >> 8);
            udpHeader[5] = (byte)(udpLength & 0xFF);

            // 校验和 （设为 0， 表示不使用）
            udpHeader[6] = 0;
            udpHeader[7] = 0;

            ushort udpChecksum = CalculateUDPChecksum(srcIp, dstIp, udpHeader, payload);
            udpHeader[6] = (byte)(udpChecksum >> 8);
            udpHeader[7] = (byte)(udpChecksum & 0xFF);

            // === 2. 构建 IP 头 （20 字节，无选项） ===
            byte[] ipHeader = new byte[20];

            // Version（4） + IHL (5) = 0X45
            ipHeader[0] = 0x45;

            // TOS
            ipHeader[1] = 0;

            // Total Length (IP头 + UDP 头 + 数据)
            ushort totalLength = (ushort)(20 + 8 + payload.Length);
            ipHeader[2] = (byte)(totalLength >> 8);
            ipHeader[3] = (byte)(totalLength & 0xFF);

            // Identification (可以是随机数或递增）
            ushort id = (ushort)(new Random().Next(0, 65535));
            ipHeader[4] = (byte)(id >> 8);
            ipHeader[5] = (byte)(id & 0xFF);

            // Flags + Fragment Offset (不分片）
            ipHeader[6] = 0x40;
            ipHeader[7] = 0;

            // TTL
            ipHeader[8] = 128;

            // Protocol (UDP = 17)
            ipHeader[9] = 17;

            // IP 头校验和 （先填 0 ，后面计算）
            ipHeader[10] = 0;
            ipHeader[11] = 0;

            // 源 IP
            Array.Copy(srcIp, 0, ipHeader, 12, 4);

            // 目标 IP
            Array.Copy(dstIp, 0, ipHeader, 16, 4);

            // 计算 IP 校验和
            ushort ipChecksum = CalculateIPChecksum(ipHeader);
            ipHeader[10] = (byte)(ipChecksum >> 8);
            ipHeader[11] = (byte)(ipChecksum & 0xFF);

            // === 3. 构建以太网帧头 （14 字节） ===
            byte[] ethernetHeader = new byte[14];

            // 目标 MAC
            Array.Copy(dstMac, 0, ethernetHeader, 0, 6);

            // 源 MAC
            Array.Copy(srcMac, 0, ethernetHeader, 6, 6);

            // EtherType (IPv4 = 0x0800)
            ethernetHeader[12] = 0x08;
            ethernetHeader[13] = 0x00;

            // === 4. 组装完整数据包 ===
            int totalSize = 14 + 20 + 8 + payload.Length;
            byte[] packet = new byte[totalSize];

            int offset = 0;
            Array.Copy(ethernetHeader, 0, packet, offset, 14);
            offset += 14;
            Array.Copy(ipHeader, 0, packet, offset, 20);
            offset += 20;
            Array.Copy(udpHeader, 0, packet, offset, 8);
            offset += 8;
            Array.Copy(payload, 0, packet, offset, payload.Length);

            return packet;
        }

        /// <summary>
        /// 计算 UDP 校验和 （包含伪头部）
        /// </summary>
        /// <returns></returns>
        public static ushort CalculateUDPChecksum(
            byte[] sourceIP, // 4 字节
            byte[] destIP, // 4 字节
            byte[] udpHeader, // 8 字节
            byte[] udpData
        )
        {
            uint sum = 0;

            // 1. 伪头部 （12 字节）
            // 源 IP
            sum += (uint)((sourceIP[0] << 8) | sourceIP[1]);
            sum += (uint)((sourceIP[2] << 8) | sourceIP[3]);

            // 目标 IP
            sum += (uint)((destIP[0] << 8) | destIP[1]);
            sum += (uint)((destIP[2] << 8) | destIP[3]);

            // 协议 （UDP = 17)
            sum += 17;

            // UDP 长度
            ushort udpLength = (ushort)(udpHeader.Length + udpData.Length);
            sum += udpLength;

            // 2. UDP 头 + 数据
            byte[] udpPacket = new byte[udpHeader.Length + udpData.Length];
            Array.Copy(udpHeader, 0, udpPacket, 0, udpHeader.Length);
            Array.Copy(udpData, 0, udpPacket, udpHeader.Length, udpData.Length);

            // 确保校验和字段为 0 （字节 6-7）
            udpPacket[6] = 0;
            udpPacket[7] = 0;

            for (int i = 0; i < udpPacket.Length; i += 2)
            {
                ushort word;
                if (i + 1 < udpPacket.Length)
                    word = (ushort)((udpPacket[i] << 8) | udpPacket[i + 1]);
                else
                    word = (ushort)(udpPacket[i] << 8);
                sum += word;
            }

            // 处理进位
            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }

            // 取反，如果结果为 0， 返回 0xFFFF
            ushort checksum = (ushort)~sum;
            return checksum == 0 ? (ushort)0xFFFF : checksum;
        }

        /// <summary>
        /// 计算 IP 头校验和
        /// 算法：将头部按 16 位分组求和，取反
        /// </summary>
        /// <param name="header"></param>
        /// <returns></returns>
        private static ushort CalculateIPChecksum(byte[] header)
        {
            // 确保校验和字段为 0 （计算时不包含自己）
            // 校验和位于字节 10-11

            uint sum = 0;

            // 按 16 位 （2字节） 分组求和
            for (int i = 0; i < header.Length; i += 2)
            {
                ushort word;

                if (i + 1 < header.Length)
                {
                    word = (ushort)((header[i] << 8) | header[i + 1]);
                }
                else
                {
                    // 奇数长度，最后一个字节补 0 
                    word = (ushort)(header[i] << 8);
                }

                sum += word;
            }

            // 处理进位：将高 16 位加到低 16 位
            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }

            // 取反
            return (ushort)~sum;
        }
    }
}