using SharpPcap;
using SwitchToSimulatorAdaptor.Common;
using SwitchToSimulatorAdaptor.EdenRoom;
using SwitchToSimulatorAdaptor.ForwardEngine;
using SwitchToSimulatorAdaptor.Utils;
using System.Net;
using System.Runtime.InteropServices;
using Ryujinx.Type;

namespace SwitchToSimulatorAdaptor;

public class AdaptorEntry
{
    IPv4Address? _switchIp;
    byte[]? _switchMac;
    private NetworkInfo _networkInfo;
    private EdenRoomMember _edenRoomClient;

    // LDN 协议常量
    private const uint LdnMagic = 0x11451400;

    private const int
        LdnHeaderSize =
            12; // LanPacketHeader 大小: Magic(4) + Type(1) + Compressed(1) + Length(2) + DecompressLength(2) + Reserved(2) = 12 bytes

    private CancellationTokenSource? _cancellationTokenSource;
    private PacketForwardEngine? _forwardEngine;
     //private string? _networkInterface = "\\Device\\NPF_{422739E8-9756-4B09-83B7-903BD906AD7E}"; // WIFI
    private string? _networkInterface = "\\Device\\NPF_{199BDD7E-457F-43A0-8932-7B08B815E9FE}"; // 以太网
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
    
    private void OnTcpPacketFromSwitch(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort, byte[] data)
    {
        HandleSwitchData(srcIp, srcPort, dstIp, dstPort, data);
    }

    private void OnPacketFromSwitch(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort, byte[] data)
    {
        HandleSwitchData(srcIp, srcPort, dstIp, dstPort, data);
    }

    private void HandleSwitchData(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort, byte[] data)
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

            Logger.Instance?.LogInfo($"{LogFlag} LDN 协议头: Type={packetType} ({(LanPacketType)packetType}), Compressed={compressed}, Length={length}, DecompressLength={decompressLength}");

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
            var lanPacketType = (LanPacketType)packetType;
            switch (lanPacketType)
            {
                case LanPacketType.Scan:
                    HandleSwitchLanPlayScan(switchIp, destIp, payload);
                    break;

                case LanPacketType.ScanResponse:
                    // HandleSwitchLanPlayScanResponse(switchIp, destIp, payload);
                    break;

                case LanPacketType.Connect:
                    HandleSwitchLanPlayConnect(switchIp, destIp, payload);
                    break;

                case LanPacketType.SyncNetwork:
                    // HandleSwitchLanPlaySyncNetwork(switchIp, destIp, payload);
                    break;

                case LanPacketType.Disconnect:
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

    private void HandleSwitchLanPlayConnect(IPv4Address switchIp, IPv4Address destIp, byte[] payload)
    {
        Logger.Instance?.LogInfo($"{LogFlag} 处理 Switch Connect: 来自 {switchIp.A}.{switchIp.B}.{switchIp.C}.{switchIp.D}");

        if (payload.Length < Marshal.SizeOf<NodeInfo>())
        {
            Logger.Instance?.LogWarning($"{LogFlag} Connect payload 太短: {payload.Length}");
            return;
        }

        // 解析 NodeInfo
        NodeInfo nodeInfo = MemoryMarshal.Cast<byte, NodeInfo>(payload.AsSpan())[0];
        Logger.Instance?.LogInfo($"{LogFlag} Connect NodeInfo: NodeId={nodeInfo.NodeId}, Ipv4Address={nodeInfo.Ipv4Address}, IsConnected={nodeInfo.IsConnected}");

        // 创建 LDN 数据包
        var connectPacket = new EdenLDNPacket
        {
            Type = EdenLDNPacketType.Connect,
            LocalIp = switchIp,
            RemoteIp = destIp,
            Broadcast = false,
            Data = payload
        };

        _edenRoomClient.SendLdnPacket(connectPacket);
    }

    void ReceiveEdenProxyPacket(EdenProxyPacket obj)
    {
        Logger.Instance?.LogInfo($"edenProxyPacket");
    }


    void ReceiveEdenLdnPacket(EdenLDNPacket packet)
    {
        SendLdnPacketToSwitchViaSwitchLanPlay(packet);
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

    LanPacketType EdenTypeToSwitchType(EdenLDNPacketType edenLdnType)
    {
        return edenLdnType switch
        {
            EdenLDNPacketType.Scan => LanPacketType.Scan,
            EdenLDNPacketType.ScanResp => LanPacketType.ScanResponse,
            EdenLDNPacketType.Connect => LanPacketType.Connect,
            EdenLDNPacketType.SyncNetwork => LanPacketType.SyncNetwork,
            EdenLDNPacketType.Disconnect => LanPacketType.Disconnect,
            _ => LanPacketType.Scan
        };
    }

    EdenLDNPacketType SwitchTypeToEdenType(LanPacketType switchType)
    {
        return switchType switch
        {
            LanPacketType.Scan => EdenLDNPacketType.Scan,
            LanPacketType.ScanResponse => EdenLDNPacketType.ScanResp,
            LanPacketType.Connect => EdenLDNPacketType.Connect,
            LanPacketType.SyncNetwork => EdenLDNPacketType.SyncNetwork,
            LanPacketType.Disconnect => EdenLDNPacketType.Disconnect,
            _ => EdenLDNPacketType.Scan
        };
    }
}