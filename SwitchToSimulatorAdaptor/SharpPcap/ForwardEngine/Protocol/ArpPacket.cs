using System.Reflection.Emit;

namespace SwitchToSimulatorAdaptor.ForwardEngine;
/// <summary>
/// ARP 包解析和构建
/// 来源: switch-lan-play/src/packet.h, switch-lan-play/src/arp.c
/// </summary>
public readonly struct ArpPacket
{
    #region 常量定义

    /// <summary>
    /// ARP 包长度
    /// </summary>
    public const int Length = 28;

    /// <summary>
    /// 硬件类型偏移
    /// </summary>
    public const int OffsetHardwareType = 0;

    /// <summary>
    /// 协议类型偏移
    /// </summary>
    public const int OffsetProtocolType = 2;

    /// <summary>
    /// 硬件地址长度偏移
    /// </summary>
    public const int OffsetHardwareSize = 4;

    /// <summary>
    /// 协议地址长度偏移
    /// </summary>
    public const int OffsetProtocolSize = 5;

    /// <summary>
    /// 操作码偏移
    /// </summary>
    public const int OffsetOpcode = 6;

    /// <summary>
    /// 发送方 MAC 偏移
    /// </summary>
    public const int OffsetSenderMac = 8;

    /// <summary>
    /// 发送方 IP 偏移
    /// </summary>
    public const int OffsetSenderIp = 14;

    /// <summary>
    /// 目标 MAC 偏移
    /// </summary>
    public const int OffsetTargetMac = 18;

    /// <summary>
    /// 目标 IP 偏移
    /// </summary>
    public const int OffsetTargetIp = 24;

    /// <summary>
    /// 硬件类型: 以太网
    /// </summary>
    public const ushort HardwareTypeEthernet = 1;

    /// <summary>
    /// 协议类型: IPv4
    /// </summary>
    public const ushort ProtocolTypeIPv4 = 0x0800;

    /// <summary>
    /// 操作码: 请求
    /// </summary>
    public const ushort OpcodeRequest = 1;

    /// <summary>
    /// 操作码: 应答
    /// </summary>
    public const ushort OpcodeReply = 2;

    #endregion

    #region 属性

    /// <summary>
    /// 硬件类型
    /// </summary>
    public ushort HardwareType { get; }

    /// <summary>
    /// 协议类型
    /// </summary>
    public ushort ProtocolType { get; }

    /// <summary>
    /// 硬件地址长度
    /// </summary>
    public byte HardwareSize { get; }

    /// <summary>
    /// 协议地址长度
    /// </summary>
    public byte ProtocolSize { get; }

    /// <summary>
    /// 操作码
    /// </summary>
    public ushort Opcode { get; }

    /// <summary>
    /// 发送方 MAC 地址
    /// </summary>
    public byte[] SenderMac { get; }

    /// <summary>
    /// 发送方 IP 地址
    /// </summary>
    public byte[] SenderIp { get; }

    /// <summary>
    /// 目标 MAC 地址
    /// </summary>
    public byte[] TargetMac { get; }

    /// <summary>
    /// 目标 IP 地址
    /// </summary>
    public byte[] TargetIp { get; }

    #endregion

    #region 构造函数

    private ArpPacket(ushort hardwareType, ushort protocolType, byte hardwareSize, byte protocolSize,
        ushort opcode, byte[] senderMac, byte[] senderIp, byte[] targetMac, byte[] targetIp)
    {
        HardwareType = hardwareType;
        ProtocolType = protocolType;
        HardwareSize = hardwareSize;
        ProtocolSize = protocolSize;
        Opcode = opcode;
        SenderMac = senderMac;
        SenderIp = senderIp;
        TargetMac = targetMac;
        TargetIp = targetIp;
    }

    #endregion

    #region 解析方法

    /// <summary>
    /// 解析 ARP 包
    /// </summary>
    /// <param name="data">原始数据</param>
    /// <returns>解析后的 ARP 包</returns>
    public static ArpPacket Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Length)
            throw new ArgumentException($"Data too short for ARP packet: {data.Length} < {Length}");

        var hardwareType = ByteHelper.ReadUInt16BigEndian(data, OffsetHardwareType);
        var protocolType = ByteHelper.ReadUInt16BigEndian(data, OffsetProtocolType);
        var hardwareSize = data[OffsetHardwareSize];
        var protocolSize = data[OffsetProtocolSize];
        var opcode = ByteHelper.ReadUInt16BigEndian(data, OffsetOpcode);
        var senderMac = data.Slice(OffsetSenderMac, 6).ToArray();
        var senderIp = data.Slice(OffsetSenderIp, 4).ToArray();
        var targetMac = data.Slice(OffsetTargetMac, 6).ToArray();
        var targetIp = data.Slice(OffsetTargetIp, 4).ToArray();

        return new ArpPacket(hardwareType, protocolType, hardwareSize, protocolSize,
            opcode, senderMac, senderIp, targetMac, targetIp);
    }

    /// <summary>
    /// 尝试解析 ARP 包
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out ArpPacket packet)
    {
        if (data.Length < Length)
        {
            packet = default;
            return false;
        }

        try
        {
            packet = Parse(data);
            return true;
        }
        catch
        {
            packet = default;
            return false;
        }
    }

    #endregion

    #region 构建方法

    /// <summary>
    /// 构建 ARP 包
    /// </summary>
    /// <param name="buffer">目标缓冲区</param>
    /// <param name="opcode">操作码</param>
    /// <param name="senderMac">发送方 MAC</param>
    /// <param name="senderIp">发送方 IP</param>
    /// <param name="targetMac">目标 MAC</param>
    /// <param name="targetIp">目标 IP</param>
    /// <returns>写入的字节数</returns>
    public static int Build(Span<byte> buffer, ushort opcode,
        ReadOnlySpan<byte> senderMac, ReadOnlySpan<byte> senderIp,
        ReadOnlySpan<byte> targetMac, ReadOnlySpan<byte> targetIp)
    {
        if (buffer.Length < Length)
            throw new ArgumentException("Buffer too small");

        // 硬件类型: 以太网
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetHardwareType, HardwareTypeEthernet);

        // 协议类型: IPv4
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetProtocolType, ProtocolTypeIPv4);

        // 硬件地址长度: 6
        buffer[OffsetHardwareSize] = 6;

        // 协议地址长度: 4
        buffer[OffsetProtocolSize] = 4;

        // 操作码
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetOpcode, opcode);

        // 发送方 MAC
        senderMac.Slice(0, 6).CopyTo(buffer.Slice(OffsetSenderMac));

        // 发送方 IP
        senderIp.Slice(0, 4).CopyTo(buffer.Slice(OffsetSenderIp));

        // 目标 MAC
        targetMac.Slice(0, 6).CopyTo(buffer.Slice(OffsetTargetMac));

        // 目标 IP
        targetIp.Slice(0, 4).CopyTo(buffer.Slice(OffsetTargetIp));

        return Length;
    }

    /// <summary>
    /// 构建 ARP 请求包
    /// </summary>
    public static byte[] BuildRequest(ReadOnlySpan<byte> senderMac, ReadOnlySpan<byte> senderIp,
        ReadOnlySpan<byte> targetIp)
    {
        var buffer = new byte[Length];
        Build(buffer, OpcodeRequest, senderMac, senderIp, ByteHelper.ZeroMac, targetIp);
        return buffer;
    }

    /// <summary>
    /// 构建 ARP 应答包
    /// </summary>
    public static byte[] BuildReply(ReadOnlySpan<byte> senderMac, ReadOnlySpan<byte> senderIp,
        ReadOnlySpan<byte> targetMac, ReadOnlySpan<byte> targetIp)
    {
        var buffer = new byte[Length];
        Build(buffer, OpcodeReply, senderMac, senderIp, targetMac, targetIp);
        return buffer;
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 检查是否为 ARP 请求
    /// </summary>
    public bool IsRequest => Opcode == OpcodeRequest;

    /// <summary>
    /// 检查是否为 ARP 应答
    /// </summary>
    public bool IsReply => Opcode == OpcodeReply;

    /// <summary>
    /// 验证 ARP 包格式是否正确
    /// </summary>
    public bool IsValid =>
        HardwareType == HardwareTypeEthernet &&
        ProtocolType == ProtocolTypeIPv4 &&
        HardwareSize == 6 &&
        ProtocolSize == 4;

    public override string ToString()
    {
        var opcodeStr = Opcode switch
        {
            OpcodeRequest => "Request",
            OpcodeReply => "Reply",
            _ => $"Unknown({Opcode})"
        };

        return $"ARP {opcodeStr}: {ByteHelper.MacToString(SenderMac)} ({ByteHelper.IpToString(SenderIp)}) -> " +
               $"{ByteHelper.MacToString(TargetMac)} ({ByteHelper.IpToString(TargetIp)})";
    }

    #endregion
}