namespace SwitchToSimulatorAdaptor.ForwardEngine;

public readonly struct EthernetFrame
{
    public const int HeaderLength = 14;
    public const int OffsetDst = 0;
    public const int OffsetSrc = 6;
    public const int OffsetType = 12;
    public const ushort TypeArp = 0x0806;
    public const ushort TypeIPv4 = 0x0800;
    public const ushort TypeIPv6 = 0x86DD;
    
    public byte[] DestinationMac { get; }
    public byte[] SourceMac { get; }
    public ushort EtherType { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public ReadOnlyMemory<byte> RawData { get; }
    public int RawLength => RawData.Length;
    
    public bool IsArp => EtherType == TypeArp;
    public bool IsIPv4 => EtherType == TypeIPv4;
    public bool IsIPv6 => EtherType == TypeIPv6;
    public bool IsBroadcast => ByteHelper.IsBroadcastMac(DestinationMac);


    private EthernetFrame(byte[] dstMac, byte[] srcMac, ushort etherType,
        ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> rawData)
    {
        DestinationMac = dstMac;
        SourceMac = srcMac;
        EtherType = etherType;
        Payload = payload;
        RawData = rawData;
    }

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

    public static int Build(Span<byte> buffer, ReadOnlySpan<byte> dstMac, ReadOnlySpan<byte> srcMac,
        ushort etherType, ReadOnlySpan<byte> payload)
    {
        if (buffer.Length < HeaderLength + payload.Length)
            throw new ArgumentException("Buffer too small");
        
        dstMac.Slice(0, 6).CopyTo(buffer.Slice(OffsetDst));
        srcMac.Slice(0, 6).CopyTo(buffer.Slice(OffsetSrc));
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetType, etherType);

        if (!payload.IsEmpty)
        {
            payload.CopyTo(buffer.Slice(HeaderLength));
        }

        return HeaderLength + payload.Length;
    }

    public static byte[] Build(ReadOnlySpan<byte> dstMac, ReadOnlySpan<byte> srcMac,
        ushort etherType, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[HeaderLength + payload.Length];
        Build(buffer, dstMac, srcMac, etherType, payload);
        return buffer;
    }

    public override string ToString()
    {
        return $"Ethernet: {ByteHelper.MacToString(SourceMac)} -> {ByteHelper.MacToString(DestinationMac)}, " +
               $"Type: 0x{EtherType:X4}, Payload: {Payload.Length} bytes";
    }
}