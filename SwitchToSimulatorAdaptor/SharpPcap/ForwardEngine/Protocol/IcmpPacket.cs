namespace SwitchToSimulatorAdaptor.ForwardEngine;

public class IcmpPacket
{
    public const int MinHeaderLength = 8;
    public const int OffsetType = 0;
    public const int OffsetCode = 1;
    public const int OffsetChecksum = 2;
    public const int OffsetIdentifier = 4;
    public const int OffsetSequence = 6;
    
    // ICMP类型
    public const byte TypeEchoReply = 0;
    public const byte TypeDestinationUnreachable = 3;
    public const byte TypeSourceQuench = 4;
    public const byte TypeRedirect = 5;
    public const byte TypeEchoRequest = 8;
    public const byte TypeTimeExceeded = 11;
    public const byte TypeParameterProblem = 12;
    public const byte TypeTimestamp = 13;
    public const byte TypeTimestampReply = 14;
    
    public byte Type { get; }
    public byte Code { get; }
    public ushort Checksum { get; }
    public ushort Identifier { get; }
    public ushort SequenceNumber { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public ReadOnlyMemory<byte> RawData { get; }
    
    public bool IsEchoRequest => Type == TypeEchoRequest;
    public bool IsEchoReply => Type == TypeEchoReply;

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

    public static IcmpPacket Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < MinHeaderLength)
            throw new ArgumentException($"Data too short for a ICMP packet: {data.Length} < {MinHeaderLength}");

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

    public static int Build(Span<byte> buffer, byte type, byte code,
        ushort identifier, ushort sequenceNumber, ReadOnlySpan<byte> payload)
    {
        int totalLength = MinHeaderLength + payload.Length;
        if (buffer.Length < totalLength)
            throw new ArgumentException("Buffer too small");

        buffer[OffsetType] = type;
        buffer[OffsetCode] = code;
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, 0);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetIdentifier, identifier);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetSequence, sequenceNumber);

        if (!payload.IsEmpty)
        {
            payload.CopyTo(buffer.Slice(MinHeaderLength));
        }

        var checksum = ChecksumCalculator.Calculate(buffer.Slice(0, totalLength));
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, checksum);
        
        return totalLength;
    }

    public static byte[] BuildEchoRequest(ushort identifier, ushort sequenceNumber, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[MinHeaderLength + payload.Length];
        Build(buffer, TypeEchoRequest, 0, identifier, sequenceNumber, payload);
        
        return buffer;
    }
    
    public static byte[] BuildEchoReply(ushort identifier, ushort sequenceNumber, ReadOnlySpan<byte> requestData)
    {
        var buffer = new byte[requestData.Length];
        Build(buffer, TypeEchoReply, 0, identifier, sequenceNumber, requestData);
        
        return buffer;
    }

    public byte[] BuildReply()
    {
        if (Type != TypeEchoRequest)
            throw new InvalidOperationException("Can only build reply from Echo Request");

        return BuildEchoReply(Identifier, SequenceNumber, Payload.Span);
    }

    public bool ValidateChecksum()
    {
        return ChecksumCalculator.Verify(RawData.Span);
    }
    
    public override string ToString()
    {
        return $"ICMP: {TypeName}, Code: {Code}, Id: {Identifier}, Seq: {SequenceNumber}, " +
               $"Payload: {Payload.Length} bytes";
    }
}