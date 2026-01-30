namespace SwitchToSimulatorAdaptor.ForwardEngine;

public readonly struct IPv4Packet
{
    #region 常量定义

    /// <summary>
    /// 头部最小长度
    /// </summary>
    public const int MinHeaderLength = 20;

    /// <summary>
    /// 版本和头部长度偏移
    /// </summary>
    public const int OffsetVersionIhl = 0;

    /// <summary>
    /// DSCP/ECN 偏移
    /// </summary>
    public const int OffsetDscpEcn = 1;

    /// <summary>
    /// 总长度偏移
    /// </summary>
    public const int OffsetTotalLength = 2;

    /// <summary>
    /// 标识偏移
    /// </summary>
    public const int OffsetIdentification = 4;

    /// <summary>
    /// 标志和分片偏移
    /// </summary>
    public const int OffsetFlagsFragmentOffset = 6;

    /// <summary>
    /// TTL 偏移
    /// </summary>
    public const int OffsetTtl = 8;

    /// <summary>
    /// 协议偏移
    /// </summary>
    public const int OffsetProtocol = 9;

    /// <summary>
    /// 校验和偏移
    /// </summary>
    public const int OffsetChecksum = 10;

    /// <summary>
    /// 源 IP 偏移
    /// </summary>
    public const int OffsetSrcIp = 12;

    /// <summary>
    /// 目标 IP 偏移
    /// </summary>
    public const int OffsetDstIp = 16;

    /// <summary>
    /// 协议: ICMP
    /// </summary>
    public const byte ProtocolIcmp = 1;

    /// <summary>
    /// 协议: TCP
    /// </summary>
    public const byte ProtocolTcp = 6;

    /// <summary>
    /// 协议: UDP
    /// </summary>
    public const byte ProtocolUdp = 17;

    /// <summary>
    /// 默认 TTL
    /// </summary>
    public const byte DefaultTtl = 128;

    #endregion

    #region 属性

    /// <summary>
    /// 版本号
    /// </summary>
    public byte Version { get; }

    /// <summary>
    /// 头部长度（字节）
    /// </summary>
    public int HeaderLength { get; }

    /// <summary>
    /// DSCP
    /// </summary>
    public byte Dscp { get; }

    /// <summary>
    /// ECN
    /// </summary>
    public byte Ecn { get; }

    /// <summary>
    /// 总长度
    /// </summary>
    public ushort TotalLength { get; }

    /// <summary>
    /// 标识
    /// </summary>
    public ushort Identification { get; }

    /// <summary>
    /// 标志
    /// </summary>
    public byte Flags { get; }

    /// <summary>
    /// 分片偏移
    /// </summary>
    public ushort FragmentOffset { get; }

    /// <summary>
    /// TTL
    /// </summary>
    public byte Ttl { get; }

    /// <summary>
    /// 协议
    /// </summary>
    public byte Protocol { get; }

    /// <summary>
    /// 校验和
    /// </summary>
    public ushort Checksum { get; }

    /// <summary>
    /// 源 IP 地址
    /// </summary>
    public byte[] SourceIp { get; }

    /// <summary>
    /// 目标 IP 地址
    /// </summary>
    public byte[] DestinationIp { get; }

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

    private IPv4Packet(byte version, int headerLength, byte dscp, byte ecn, ushort totalLength,
        ushort identification, byte flags, ushort fragmentOffset, byte ttl, byte protocol,
        ushort checksum, byte[] sourceIp, byte[] destinationIp,
        ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> rawData)
    {
        Version = version;
        HeaderLength = headerLength;
        Dscp = dscp;
        Ecn = ecn;
        TotalLength = totalLength;
        Identification = identification;
        Flags = flags;
        FragmentOffset = fragmentOffset;
        Ttl = ttl;
        Protocol = protocol;
        Checksum = checksum;
        SourceIp = sourceIp;
        DestinationIp = destinationIp;
        Payload = payload;
        RawData = rawData;
    }

    #endregion

    #region 解析方法

    /// <summary>
    /// 解析 IPv4 包
    /// </summary>
    public static IPv4Packet Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < MinHeaderLength)
            throw new ArgumentException($"Data too short for IPv4 packet: {data.Length} < {MinHeaderLength}");

        var span = data.Span;

        var versionIhl = span[OffsetVersionIhl];
        var version = (byte)(versionIhl >> 4);
        var headerLength = (versionIhl & 0x0F) * 4;

        if (version != 4)
            throw new ArgumentException($"Not an IPv4 packet: version = {version}");

        if (data.Length < headerLength)
            throw new ArgumentException($"Data too short for IPv4 header: {data.Length} < {headerLength}");

        var dscpEcn = span[OffsetDscpEcn];
        var dscp = (byte)(dscpEcn >> 2);
        var ecn = (byte)(dscpEcn & 0x03);

        var totalLength = ByteHelper.ReadUInt16BigEndian(span, OffsetTotalLength);
        var identification = ByteHelper.ReadUInt16BigEndian(span, OffsetIdentification);

        var flagsFragOffset = ByteHelper.ReadUInt16BigEndian(span, OffsetFlagsFragmentOffset);
        var flags = (byte)(flagsFragOffset >> 13);
        var fragmentOffset = (ushort)(flagsFragOffset & 0x1FFF);

        var ttl = span[OffsetTtl];
        var protocol = span[OffsetProtocol];
        var checksum = ByteHelper.ReadUInt16BigEndian(span, OffsetChecksum);
        var sourceIp = span.Slice(OffsetSrcIp, 4).ToArray();
        var destinationIp = span.Slice(OffsetDstIp, 4).ToArray();

        var payloadLength = Math.Min(totalLength - headerLength, data.Length - headerLength);
        var payload = data.Slice(headerLength, Math.Max(0, payloadLength));

        return new IPv4Packet(version, headerLength, dscp, ecn, totalLength, identification,
            flags, fragmentOffset, ttl, protocol, checksum, sourceIp, destinationIp, payload, data);
    }

    /// <summary>
    /// 尝试解析 IPv4 包
    /// </summary>
    public static bool TryParse(ReadOnlyMemory<byte> data, out IPv4Packet packet)
    {
        if (data.Length < MinHeaderLength)
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
    /// 构建 IPv4 包
    /// </summary>
    /// <param name="buffer">目标缓冲区</param>
    /// <param name="srcIp">源 IP</param>
    /// <param name="dstIp">目标 IP</param>
    /// <param name="protocol">协议</param>
    /// <param name="payload">负载</param>
    /// <param name="identification">标识（引用参数，自动递增）</param>
    /// <param name="ttl">TTL</param>
    /// <returns>写入的字节数</returns>
    public static int Build(Span<byte> buffer, ReadOnlySpan<byte> srcIp, ReadOnlySpan<byte> dstIp,
        byte protocol, ReadOnlySpan<byte> payload, ref ushort identification, byte ttl = DefaultTtl)
    {
        int totalLength = MinHeaderLength + payload.Length;

        if (buffer.Length < totalLength)
            throw new ArgumentException("Buffer too small");

        // 版本(4) + 头部长度(5)
        buffer[OffsetVersionIhl] = 0x45;

        // DSCP/ECN
        buffer[OffsetDscpEcn] = 0x00;

        // 总长度
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetTotalLength, (ushort)totalLength);

        // 标识
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetIdentification, identification++);

        // 标志和分片偏移 (不分片)
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetFlagsFragmentOffset, 0x4000);

        // TTL
        buffer[OffsetTtl] = ttl;

        // 协议
        buffer[OffsetProtocol] = protocol;

        // 校验和先设为 0
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, 0);

        // 源 IP
        srcIp.Slice(0, 4).CopyTo(buffer.Slice(OffsetSrcIp));

        // 目标 IP
        dstIp.Slice(0, 4).CopyTo(buffer.Slice(OffsetDstIp));

        // 负载
        if (!payload.IsEmpty)
        {
            payload.CopyTo(buffer.Slice(MinHeaderLength));
        }

        // 计算并填入校验和
        var checksum = ChecksumCalculator.Calculate(buffer.Slice(0, MinHeaderLength));
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, checksum);

        return totalLength;
    }

    /// <summary>
    /// 构建 IPv4 包并返回字节数组
    /// </summary>
    public static byte[] Build(ReadOnlySpan<byte> srcIp, ReadOnlySpan<byte> dstIp,
        byte protocol, ReadOnlySpan<byte> payload, ref ushort identification, byte ttl = DefaultTtl)
    {
        var buffer = new byte[MinHeaderLength + payload.Length];
        Build(buffer, srcIp, dstIp, protocol, payload, ref identification, ttl);
        return buffer;
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 检查是否为 ICMP 包
    /// </summary>
    public bool IsIcmp => Protocol == ProtocolIcmp;

    /// <summary>
    /// 检查是否为 TCP 包
    /// </summary>
    public bool IsTcp => Protocol == ProtocolTcp;

    /// <summary>
    /// 检查是否为 UDP 包
    /// </summary>
    public bool IsUdp => Protocol == ProtocolUdp;

    /// <summary>
    /// 验证校验和
    /// </summary>
    public bool ValidateChecksum()
    {
        if (RawData.Length < HeaderLength)
            return false;

        return ChecksumCalculator.Verify(RawData.Span.Slice(0, HeaderLength));
    }

    /// <summary>
    /// 获取协议名称
    /// </summary>
    public string ProtocolName => Protocol switch
    {
        ProtocolIcmp => "ICMP",
        ProtocolTcp => "TCP",
        ProtocolUdp => "UDP",
        _ => $"Unknown({Protocol})"
    };

    public override string ToString()
    {
        return $"IPv4: {ByteHelper.IpToString(SourceIp)} -> {ByteHelper.IpToString(DestinationIp)}, " +
               $"Protocol: {ProtocolName}, Length: {TotalLength}, TTL: {Ttl}";
    }

    #endregion
}