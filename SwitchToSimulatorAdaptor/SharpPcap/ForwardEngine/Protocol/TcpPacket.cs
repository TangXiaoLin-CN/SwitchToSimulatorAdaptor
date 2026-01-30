namespace SwitchToSimulatorAdaptor.ForwardEngine;

public readonly struct TcpPacket
{
    #region 常量定义

    /// <summary>
    /// 头部最小长度
    /// </summary>
    public const int MinHeaderLength = 20;

    /// <summary>
    /// 源端口偏移
    /// </summary>
    public const int OffsetSrcPort = 0;

    /// <summary>
    /// 目标端口偏移
    /// </summary>
    public const int OffsetDstPort = 2;

    /// <summary>
    /// 序列号偏移
    /// </summary>
    public const int OffsetSeqNum = 4;

    /// <summary>
    /// 确认号偏移
    /// </summary>
    public const int OffsetAckNum = 8;

    /// <summary>
    /// 数据偏移和标志偏移
    /// </summary>
    public const int OffsetDataOffsetFlags = 12;

    /// <summary>
    /// 窗口大小偏移
    /// </summary>
    public const int OffsetWindow = 14;

    /// <summary>
    /// 校验和偏移
    /// </summary>
    public const int OffsetChecksum = 16;

    /// <summary>
    /// 紧急指针偏移
    /// </summary>
    public const int OffsetUrgentPointer = 18;

    // TCP 标志位
    public const byte FlagFin = 0x01;
    public const byte FlagSyn = 0x02;
    public const byte FlagRst = 0x04;
    public const byte FlagPsh = 0x08;
    public const byte FlagAck = 0x10;
    public const byte FlagUrg = 0x20;
    public const byte FlagEce = 0x40;
    public const byte FlagCwr = 0x80;

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
    /// 序列号
    /// </summary>
    public uint SequenceNumber { get; }

    /// <summary>
    /// 确认号
    /// </summary>
    public uint AcknowledgmentNumber { get; }

    /// <summary>
    /// 头部长度（字节）
    /// </summary>
    public int HeaderLength { get; }

    /// <summary>
    /// 标志位
    /// </summary>
    public byte Flags { get; }

    /// <summary>
    /// 窗口大小
    /// </summary>
    public ushort WindowSize { get; }

    /// <summary>
    /// 校验和
    /// </summary>
    public ushort Checksum { get; }

    /// <summary>
    /// 紧急指针
    /// </summary>
    public ushort UrgentPointer { get; }

    /// <summary>
    /// 选项数据
    /// </summary>
    public ReadOnlyMemory<byte> Options { get; }

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

    private TcpPacket(ushort sourcePort, ushort destinationPort, uint sequenceNumber,
        uint acknowledgmentNumber, int headerLength, byte flags, ushort windowSize,
        ushort checksum, ushort urgentPointer, ReadOnlyMemory<byte> options,
        ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> rawData)
    {
        SourcePort = sourcePort;
        DestinationPort = destinationPort;
        SequenceNumber = sequenceNumber;
        AcknowledgmentNumber = acknowledgmentNumber;
        HeaderLength = headerLength;
        Flags = flags;
        WindowSize = windowSize;
        Checksum = checksum;
        UrgentPointer = urgentPointer;
        Options = options;
        Payload = payload;
        RawData = rawData;
    }

    #endregion

    #region 解析方法

    /// <summary>
    /// 解析 TCP 包
    /// </summary>
    public static TcpPacket Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < MinHeaderLength)
            throw new ArgumentException($"Data too short for TCP packet: {data.Length} < {MinHeaderLength}");

        var span = data.Span;

        var sourcePort = ByteHelper.ReadUInt16BigEndian(span, OffsetSrcPort);
        var destinationPort = ByteHelper.ReadUInt16BigEndian(span, OffsetDstPort);
        var sequenceNumber = ByteHelper.ReadUInt32BigEndian(span, OffsetSeqNum);
        var acknowledgmentNumber = ByteHelper.ReadUInt32BigEndian(span, OffsetAckNum);

        var dataOffsetFlags = ByteHelper.ReadUInt16BigEndian(span, OffsetDataOffsetFlags);
        var headerLength = ((dataOffsetFlags >> 12) & 0x0F) * 4;
        var flags = (byte)(dataOffsetFlags & 0xFF);

        var windowSize = ByteHelper.ReadUInt16BigEndian(span, OffsetWindow);
        var checksum = ByteHelper.ReadUInt16BigEndian(span, OffsetChecksum);
        var urgentPointer = ByteHelper.ReadUInt16BigEndian(span, OffsetUrgentPointer);

        var options = headerLength > MinHeaderLength
            ? data.Slice(MinHeaderLength, headerLength - MinHeaderLength)
            : ReadOnlyMemory<byte>.Empty;

        var payload = data.Length > headerLength
            ? data.Slice(headerLength)
            : ReadOnlyMemory<byte>.Empty;

        return new TcpPacket(sourcePort, destinationPort, sequenceNumber, acknowledgmentNumber,
            headerLength, flags, windowSize, checksum, urgentPointer, options, payload, data);
    }

    /// <summary>
    /// 尝试解析 TCP 包
    /// </summary>
    public static bool TryParse(ReadOnlyMemory<byte> data, out TcpPacket packet)
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
    /// 构建 TCP 包
    /// </summary>
    public static int Build(Span<byte> buffer, ReadOnlySpan<byte> srcIp, ushort srcPort,
        ReadOnlySpan<byte> dstIp, ushort dstPort, uint seqNum, uint ackNum,
        byte flags, ushort windowSize, ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> options = default)
    {
        int headerLength = MinHeaderLength + options.Length;
        // 确保头部长度是 4 的倍数
        int paddedHeaderLength = (headerLength + 3) / 4 * 4;
        int totalLength = paddedHeaderLength + payload.Length;

        if (buffer.Length < totalLength)
            throw new ArgumentException("Buffer too small");

        // 源端口
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetSrcPort, srcPort);

        // 目标端口
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetDstPort, dstPort);

        // 序列号
        ByteHelper.WriteUInt32BigEndian(buffer, OffsetSeqNum, seqNum);

        // 确认号
        ByteHelper.WriteUInt32BigEndian(buffer, OffsetAckNum, ackNum);

        // 数据偏移和标志
        int dataOffset = paddedHeaderLength / 4;
        ushort dataOffsetFlags = (ushort)((dataOffset << 12) | flags);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetDataOffsetFlags, dataOffsetFlags);

        // 窗口大小
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetWindow, windowSize);

        // 校验和先设为 0
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, 0);

        // 紧急指针
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetUrgentPointer, 0);

        // 选项
        if (!options.IsEmpty)
        {
            options.CopyTo(buffer.Slice(MinHeaderLength));
        }

        // 填充到 4 字节对齐
        for (int i = headerLength; i < paddedHeaderLength; i++)
        {
            buffer[i] = 0;
        }

        // 负载
        if (!payload.IsEmpty)
        {
            payload.CopyTo(buffer.Slice(paddedHeaderLength));
        }

        // 计算校验和
        var checksum = ChecksumCalculator.CalculateTcp(srcIp, dstIp,
            buffer.Slice(0, paddedHeaderLength), payload);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, checksum);

        return totalLength;
    }

    /// <summary>
    /// 构建 TCP 包并返回字节数组
    /// </summary>
    public static byte[] Build(ReadOnlySpan<byte> srcIp, ushort srcPort,
        ReadOnlySpan<byte> dstIp, ushort dstPort, uint seqNum, uint ackNum,
        byte flags, ushort windowSize, ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> options = default)
    {
        int headerLength = MinHeaderLength + options.Length;
        int paddedHeaderLength = (headerLength + 3) / 4 * 4;
        var buffer = new byte[paddedHeaderLength + payload.Length];
        Build(buffer, srcIp, srcPort, dstIp, dstPort, seqNum, ackNum, flags, windowSize, payload, options);
        return buffer;
    }

    #endregion

    #region 标志位辅助方法

    public bool HasFin => (Flags & FlagFin) != 0;
    public bool HasSyn => (Flags & FlagSyn) != 0;
    public bool HasRst => (Flags & FlagRst) != 0;
    public bool HasPsh => (Flags & FlagPsh) != 0;
    public bool HasAck => (Flags & FlagAck) != 0;
    public bool HasUrg => (Flags & FlagUrg) != 0;

    /// <summary>
    /// 获取标志位字符串表示
    /// </summary>
    public string FlagsString
    {
        get
        {
            var flags = new List<string>();
            if (HasSyn) flags.Add("SYN");
            if (HasAck) flags.Add("ACK");
            if (HasFin) flags.Add("FIN");
            if (HasRst) flags.Add("RST");
            if (HasPsh) flags.Add("PSH");
            if (HasUrg) flags.Add("URG");
            return string.Join("|", flags);
        }
    }

    public override string ToString()
    {
        return $"TCP: {SourcePort} -> {DestinationPort}, Seq: {SequenceNumber}, Ack: {AcknowledgmentNumber}, " +
               $"Flags: [{FlagsString}], Win: {WindowSize}, Payload: {Payload.Length} bytes";
    }

    #endregion
}