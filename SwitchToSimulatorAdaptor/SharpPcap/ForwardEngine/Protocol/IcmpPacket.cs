namespace SwitchToSimulatorAdaptor.ForwardEngine;

public readonly struct IcmpPacket
{
    #region 常量定义

    /// <summary>
    /// 头部最小长度
    /// </summary>
    public const int MinHeaderLength = 8;

    /// <summary>
    /// 类型偏移
    /// </summary>
    public const int OffsetType = 0;

    /// <summary>
    /// 代码偏移
    /// </summary>
    public const int OffsetCode = 1;

    /// <summary>
    /// 校验和偏移
    /// </summary>
    public const int OffsetChecksum = 2;

    /// <summary>
    /// 标识符偏移（用于 Echo）
    /// </summary>
    public const int OffsetIdentifier = 4;

    /// <summary>
    /// 序列号偏移（用于 Echo）
    /// </summary>
    public const int OffsetSequence = 6;

    // ICMP 类型
    public const byte TypeEchoReply = 0;
    public const byte TypeDestinationUnreachable = 3;
    public const byte TypeSourceQuench = 4;
    public const byte TypeRedirect = 5;
    public const byte TypeEchoRequest = 8;
    public const byte TypeTimeExceeded = 11;
    public const byte TypeParameterProblem = 12;
    public const byte TypeTimestamp = 13;
    public const byte TypeTimestampReply = 14;

    #endregion

    #region 属性

    /// <summary>
    /// 类型
    /// </summary>
    public byte Type { get; }

    /// <summary>
    /// 代码
    /// </summary>
    public byte Code { get; }

    /// <summary>
    /// 校验和
    /// </summary>
    public ushort Checksum { get; }

    /// <summary>
    /// 标识符（用于 Echo）
    /// </summary>
    public ushort Identifier { get; }

    /// <summary>
    /// 序列号（用于 Echo）
    /// </summary>
    public ushort SequenceNumber { get; }

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

    private IcmpPacket(byte type, byte code, ushort checksum, ushort identifier,
        ushort sequenceNumber, ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> rawData)
    {
        Type = type;
        Code = code;
        Checksum = checksum;
        Identifier = identifier;
        SequenceNumber = sequenceNumber;
        Payload = payload;
        RawData = rawData;
    }

    #endregion

    #region 解析方法

    /// <summary>
    /// 解析 ICMP 包
    /// </summary>
    public static IcmpPacket Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < MinHeaderLength)
            throw new ArgumentException($"Data too short for ICMP packet: {data.Length} < {MinHeaderLength}");

        var span = data.Span;

        var type = span[OffsetType];
        var code = span[OffsetCode];
        var checksum = ByteHelper.ReadUInt16BigEndian(span, OffsetChecksum);
        var identifier = ByteHelper.ReadUInt16BigEndian(span, OffsetIdentifier);
        var sequenceNumber = ByteHelper.ReadUInt16BigEndian(span, OffsetSequence);

        var payload = data.Length > MinHeaderLength
            ? data.Slice(MinHeaderLength)
            : ReadOnlyMemory<byte>.Empty;

        return new IcmpPacket(type, code, checksum, identifier, sequenceNumber, payload, data);
    }

    /// <summary>
    /// 尝试解析 ICMP 包
    /// </summary>
    public static bool TryParse(ReadOnlyMemory<byte> data, out IcmpPacket packet)
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
    /// 构建 ICMP 包
    /// </summary>
    public static int Build(Span<byte> buffer, byte type, byte code,
        ushort identifier, ushort sequenceNumber, ReadOnlySpan<byte> payload)
    {
        int totalLength = MinHeaderLength + payload.Length;

        if (buffer.Length < totalLength)
            throw new ArgumentException("Buffer too small");

        // 类型
        buffer[OffsetType] = type;

        // 代码
        buffer[OffsetCode] = code;

        // 校验和先设为 0
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, 0);

        // 标识符
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetIdentifier, identifier);

        // 序列号
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetSequence, sequenceNumber);

        // 负载
        if (!payload.IsEmpty)
        {
            payload.CopyTo(buffer.Slice(MinHeaderLength));
        }

        // 计算校验和
        var checksum = ChecksumCalculator.Calculate(buffer.Slice(0, totalLength));
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, checksum);

        return totalLength;
    }

    /// <summary>
    /// 构建 Echo 请求包
    /// </summary>
    public static byte[] BuildEchoRequest(ushort identifier, ushort sequenceNumber, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[MinHeaderLength + payload.Length];
        Build(buffer, TypeEchoRequest, 0, identifier, sequenceNumber, payload);
        return buffer;
    }

    /// <summary>
    /// 构建 Echo 应答包
    /// </summary>
    public static byte[] BuildEchoReply(ushort identifier, ushort sequenceNumber, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[MinHeaderLength + payload.Length];
        Build(buffer, TypeEchoReply, 0, identifier, sequenceNumber, payload);
        return buffer;
    }

    /// <summary>
    /// 从 Echo 请求构建 Echo 应答
    /// </summary>
    public byte[] BuildReply()
    {
        if (Type != TypeEchoRequest)
            throw new InvalidOperationException("Can only build reply from Echo Request");

        return BuildEchoReply(Identifier, SequenceNumber, Payload.Span);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 是否为 Echo 请求
    /// </summary>
    public bool IsEchoRequest => Type == TypeEchoRequest;

    /// <summary>
    /// 是否为 Echo 应答
    /// </summary>
    public bool IsEchoReply => Type == TypeEchoReply;

    /// <summary>
    /// 获取类型名称
    /// </summary>
    public string TypeName => Type switch
    {
        TypeEchoReply => "Echo Reply",
        TypeDestinationUnreachable => "Destination Unreachable",
        TypeSourceQuench => "Source Quench",
        TypeRedirect => "Redirect",
        TypeEchoRequest => "Echo Request",
        TypeTimeExceeded => "Time Exceeded",
        TypeParameterProblem => "Parameter Problem",
        TypeTimestamp => "Timestamp",
        TypeTimestampReply => "Timestamp Reply",
        _ => $"Unknown({Type})"
    };

    /// <summary>
    /// 验证校验和
    /// </summary>
    public bool ValidateChecksum()
    {
        return ChecksumCalculator.Verify(RawData.Span);
    }

    public override string ToString()
    {
        return $"ICMP: {TypeName}, Code: {Code}, Id: {Identifier}, Seq: {SequenceNumber}, " +
               $"Payload: {Payload.Length} bytes";
    }

    #endregion
}