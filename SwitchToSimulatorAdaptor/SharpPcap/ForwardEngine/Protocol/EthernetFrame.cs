namespace SwitchToSimulatorAdaptor.ForwardEngine;

public readonly struct EthernetFrame
{
    #region 常量定义

    /// <summary>
    /// 头部长度
    /// </summary>
    public const int HeaderLength = 14;

    /// <summary>
    /// 目标 MAC 偏移
    /// </summary>
    public const int OffsetDst = 0;

    /// <summary>
    /// 源 MAC 偏移
    /// </summary>
    public const int OffsetSrc = 6;

    /// <summary>
    /// 类型偏移
    /// </summary>
    public const int OffsetType = 12;

    /// <summary>
    /// ARP 类型
    /// </summary>
    public const ushort TypeArp = 0x0806;

    /// <summary>
    /// IPv4 类型
    /// </summary>
    public const ushort TypeIPv4 = 0x0800;

    /// <summary>
    /// IPv6 类型
    /// </summary>
    public const ushort TypeIPv6 = 0x86DD;

    #endregion

    #region 属性

    /// <summary>
    /// 目标 MAC 地址
    /// </summary>
    public byte[] DestinationMac { get; }

    /// <summary>
    /// 源 MAC 地址
    /// </summary>
    public byte[] SourceMac { get; }

    /// <summary>
    /// 以太网类型
    /// </summary>
    public ushort EtherType { get; }

    /// <summary>
    /// 负载数据
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>
    /// 原始数据
    /// </summary>
    public ReadOnlyMemory<byte> RawData { get; }

    /// <summary>
    /// 原始数据长度
    /// </summary>
    public int RawLength => RawData.Length;

    #endregion

    #region 构造函数

    private EthernetFrame(byte[] dstMac, byte[] srcMac, ushort etherType,
        ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> rawData)
    {
        DestinationMac = dstMac;
        SourceMac = srcMac;
        EtherType = etherType;
        Payload = payload;
        RawData = rawData;
    }

    #endregion

    #region 解析方法

    /// <summary>
    /// 解析以太网帧
    /// </summary>
    /// <param name="data">原始数据</param>
    /// <returns>解析后的以太网帧</returns>
    public static EthernetFrame Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < HeaderLength)
            throw new ArgumentException($"Data too short for Ethernet frame: {data.Length} < {HeaderLength}");

        var span = data.Span;

        var dstMac = span.Slice(OffsetDst, 6).ToArray();
        var srcMac = span.Slice(OffsetSrc, 6).ToArray();
        var etherType = ByteHelper.ReadUInt16BigEndian(span, OffsetType);
        var payload = data.Slice(HeaderLength);

        return new EthernetFrame(dstMac, srcMac, etherType, payload, data);
    }

    /// <summary>
    /// 尝试解析以太网帧
    /// </summary>
    /// <param name="data">原始数据</param>
    /// <param name="frame">解析后的以太网帧</param>
    /// <returns>是否解析成功</returns>
    public static bool TryParse(ReadOnlyMemory<byte> data, out EthernetFrame frame)
    {
        if (data.Length < HeaderLength)
        {
            frame = default;
            return false;
        }

        try
        {
            frame = Parse(data);
            return true;
        }
        catch
        {
            frame = default;
            return false;
        }
    }

    #endregion

    #region 构建方法

    /// <summary>
    /// 构建以太网帧
    /// </summary>
    /// <param name="buffer">目标缓冲区</param>
    /// <param name="dstMac">目标 MAC</param>
    /// <param name="srcMac">源 MAC</param>
    /// <param name="etherType">以太网类型</param>
    /// <param name="payload">负载数据</param>
    /// <returns>写入的字节数</returns>
    public static int Build(Span<byte> buffer, ReadOnlySpan<byte> dstMac, ReadOnlySpan<byte> srcMac,
        ushort etherType, ReadOnlySpan<byte> payload)
    {
        if (buffer.Length < HeaderLength + payload.Length)
            throw new ArgumentException("Buffer too small");

        // 目标 MAC
        dstMac.Slice(0, 6).CopyTo(buffer.Slice(OffsetDst));

        // 源 MAC
        srcMac.Slice(0, 6).CopyTo(buffer.Slice(OffsetSrc));

        // 以太网类型
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetType, etherType);

        // 负载
        if (!payload.IsEmpty)
        {
            payload.CopyTo(buffer.Slice(HeaderLength));
        }

        return HeaderLength + payload.Length;
    }

    /// <summary>
    /// 构建以太网帧并返回字节数组
    /// </summary>
    public static byte[] Build(ReadOnlySpan<byte> dstMac, ReadOnlySpan<byte> srcMac,
        ushort etherType, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[HeaderLength + payload.Length];
        Build(buffer, dstMac, srcMac, etherType, payload);
        return buffer;
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 检查是否为 ARP 帧
    /// </summary>
    public bool IsArp => EtherType == TypeArp;

    /// <summary>
    /// 检查是否为 IPv4 帧
    /// </summary>
    public bool IsIPv4 => EtherType == TypeIPv4;

    /// <summary>
    /// 检查是否为 IPv6 帧
    /// </summary>
    public bool IsIPv6 => EtherType == TypeIPv6;

    /// <summary>
    /// 检查是否为广播帧
    /// </summary>
    public bool IsBroadcast => ByteHelper.IsBroadcastMac(DestinationMac);

    public override string ToString()
    {
        return $"Ethernet: {ByteHelper.MacToString(SourceMac)} -> {ByteHelper.MacToString(DestinationMac)}, " +
               $"Type: 0x{EtherType:X4}, Payload: {Payload.Length} bytes";
    }

    #endregion
}