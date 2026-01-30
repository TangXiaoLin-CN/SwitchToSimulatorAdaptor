namespace SwitchToSimulatorAdaptor.ForwardEngine;

public readonly struct UdpPacket
{
    public const int HeaderLength = 8;
    public const int OffsetSrcPort = 0;
    public const int OffsetDstPort = 2;
    public const int OffsetLength = 4;
    public const int OffsetChecksum = 6;
    
    public ushort SourcePort { get; }
    public ushort DestinationPort { get; }
    public ushort Length { get; }
    public ushort Checksum { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public ReadOnlyMemory<byte> RawData { get; }

    public int PayloadLength => Length - HeaderLength;
    
    public UdpPacket(ushort sourcePort, ushort destinationPort, ushort length, ushort checksum,
        ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> rawData)
    { 
        SourcePort = sourcePort;
        DestinationPort = destinationPort;
        Length = length;
        Checksum = checksum;
        Payload = payload;
        RawData = rawData;
    }

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
        catch (Exception e)
        {
            packet = default;
            return false;
        }
    }

    public static int Build(Span<byte> buffer, ReadOnlySpan<byte> srcIp, ushort srcPort,
        ReadOnlySpan<byte> dstIp, ushort dstPort, ReadOnlySpan<byte> payload)
    {
        int totalLength = HeaderLength + payload.Length;

        if (buffer.Length < totalLength)
            throw new ArgumentException($"Buffer too short for UDP packet: {buffer.Length} < {totalLength}");
        
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetSrcPort, srcPort);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetDstPort, dstPort);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetLength, (ushort)totalLength);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, 0);

        if (!payload.IsEmpty)
        {
            payload.CopyTo(buffer.Slice(HeaderLength));
        }

        var checksum = ChecksumCalculator.CalculateUdp(srcIp, dstIp,
            buffer.Slice(0, HeaderLength), payload);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, checksum);

        return totalLength;
    }

    public static byte[] Build(ReadOnlySpan<byte> srcIp, ushort srcPort,
        ReadOnlySpan<byte> dstIp, ushort dstPort, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[HeaderLength + payload.Length];
        Build(buffer, srcIp, srcPort, dstIp, dstPort, payload);
        
        return buffer;
    }

    public static byte[] BuildIPv4Packet(ReadOnlySpan<byte> srcIp, ushort srcPort,
        ReadOnlySpan<byte> dstIp, ushort dstPort, ReadOnlySpan<byte> payload, ref ushort identification)
    {
        var udpBuffer = new byte[HeaderLength + payload.Length];
        Build(udpBuffer, srcIp, srcPort, dstIp, dstPort, payload);
        
        return IPv4Packet.Build(srcIp, dstIp, IPv4Packet.ProtocolUdp, udpBuffer, ref identification);
    }
    
    public override string ToString()
    {
        return $"UDP: {SourcePort} -> {DestinationPort}, Length: {Length}, Payload: {PayloadLength} bytes)";
    }
}