namespace SwitchToSimulatorAdaptor.ForwardEngine;

public readonly struct UdpPacket
{
    #region 常量定义

    /// <summary>
    /// 头部长度
    /// </summary>
    public const int HeaderLength = 8;

    /// <summary>
    /// 源端口偏移
    /// </summary>
    public const int OffsetSrcPort = 0;

    /// <summary>
    /// 目标端口偏移
    /// </summary>
    public const int OffsetDstPort = 2;

    /// <summary>
    /// 长度偏移
    /// </summary>
    public const int OffsetLength = 4;

    /// <summary>
    /// 校验和偏移
    /// </summary>
    public const int OffsetChecksum = 6;

    #endregion

    #region 属性

    /// <summary>
    /// 源端口
    /// </summary>
    public ushort SourcePort { get; }

    /// <summary>
    /// 目标端口
    /// </summary>
    public ushort DestinationPort { get; }

    /// <summary>
    /// 长度（包含头部）
    /// </summary>
    public ushort Length { get; }

    /// <summary>
    /// 校验和
    /// </summary>
    public ushort Checksum { get; }

    /// <summary>
    /// 负载数据
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>
    /// 原始数据
    /// </summary>
    public ReadOnlyMemory<byte> RawData { get; }

    #endregion

    #region 构造函数

    private UdpPacket(ushort sourcePort, ushort destinationPort, ushort length, ushort checksum,
        ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> rawData)
    {
        SourcePort = sourcePort;
        DestinationPort = destinationPort;
        Length = length;
        Checksum = checksum;
        Payload = payload;
        RawData = rawData;
    }

    #endregion

    #region 解析方法

    /// <summary>
    /// 解析 UDP 包
    /// </summary>
    public static UdpPacket Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < HeaderLength)
            throw new ArgumentException($"Data too short for UDP packet: {data.Length} < {HeaderLength}");

        var span = data.Span;

        var sourcePort = ByteHelper.ReadUInt16BigEndian(span, OffsetSrcPort);
        var destinationPort = ByteHelper.ReadUInt16BigEndian(span, OffsetDstPort);
        var length = ByteHelper.ReadUInt16BigEndian(span, OffsetLength);
        var checksum = ByteHelper.ReadUInt16BigEndian(span, OffsetChecksum);

        var payloadLength = Math.Min(length - HeaderLength, data.Length - HeaderLength);
        var payload = data.Slice(HeaderLength, Math.Max(0, payloadLength));

        return new UdpPacket(sourcePort, destinationPort, length, checksum, payload, data);
    }

    /// <summary>
    /// 尝试解析 UDP 包
    /// </summary>
    public static bool TryParse(ReadOnlyMemory<byte> data, out UdpPacket packet)
    {
        if (data.Length < HeaderLength)
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
    /// 构建 UDP 包
    /// </summary>
    /// <param name="buffer">目标缓冲区</param>
    /// <param name="srcIp">源 IP（用于校验和计算）</param>
    /// <param name="srcPort">源端口</param>
    /// <param name="dstIp">目标 IP（用于校验和计算）</param>
    /// <param name="dstPort">目标端口</param>
    /// <param name="payload">负载</param>
    /// <returns>写入的字节数</returns>
    public static int Build(Span<byte> buffer, ReadOnlySpan<byte> srcIp, ushort srcPort,
        ReadOnlySpan<byte> dstIp, ushort dstPort, ReadOnlySpan<byte> payload)
    {
        int totalLength = HeaderLength + payload.Length;

        if (buffer.Length < totalLength)
            throw new ArgumentException("Buffer too small");

        // 源端口
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetSrcPort, srcPort);

        // 目标端口
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetDstPort, dstPort);

        // 长度
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetLength, (ushort)totalLength);

        // 校验和先设为 0
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, 0);

        // 负载
        if (!payload.IsEmpty)
        {
            payload.CopyTo(buffer.Slice(HeaderLength));
        }

        // 计算校验和
        var checksum = ChecksumCalculator.CalculateUdp(srcIp, dstIp,
            buffer.Slice(0, HeaderLength), payload);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, checksum);

        return totalLength;
    }

    /// <summary>
    /// 构建 UDP 包并返回字节数组
    /// </summary>
    public static byte[] Build(ReadOnlySpan<byte> srcIp, ushort srcPort,
        ReadOnlySpan<byte> dstIp, ushort dstPort, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[HeaderLength + payload.Length];
        Build(buffer, srcIp, srcPort, dstIp, dstPort, payload);
        return buffer;
    }

    /// <summary>
    /// 构建完整的 UDP/IPv4 包（以太网负载）
    /// </summary>
    public static byte[] BuildIPv4Packet(ReadOnlySpan<byte> srcIp, ushort srcPort,
        ReadOnlySpan<byte> dstIp, ushort dstPort, ReadOnlySpan<byte> payload, ref ushort identification)
    {
        // 先构建 UDP 包
        var udpBuffer = new byte[HeaderLength + payload.Length];
        Build(udpBuffer, srcIp, srcPort, dstIp, dstPort, payload);

        // 再构建 IPv4 包
        return IPv4Packet.Build(srcIp, dstIp, IPv4Packet.ProtocolUdp, udpBuffer, ref identification);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 负载长度
    /// </summary>
    public int PayloadLength => Length - HeaderLength;

    public override string ToString()
    {
        return $"UDP: {SourcePort} -> {DestinationPort}, Length: {Length}, Payload: {PayloadLength} bytes";
    }

    #endregion
}
