namespace SwitchToSimulatorAdaptor.ForwardEngine;

public readonly struct IPv4Packet
{
    public const int MinHeaderLength = 20;
    public const int OffsetVersionIhl = 0;
    public const int OffsetDscpEcn = 1;
    public const int OffsetTotalLength = 2;
    public const int OffsetIdentification = 4;
    public const int OffsetFlagsFragmentOffset = 6;
    public const int OffsetTtl = 8;
    public const int OffsetProtocol = 9;
    public const int OffsetChecksum = 10;
    public const int OffsetSrcIp = 12;
    public const int OffsetDstIp = 16;
    public const byte ProtocolIcmp = 1;
    public const byte ProtocolTcp = 6;
    public const byte ProtocolUdp = 17;
    public const byte DefaultTtl = 128;
    
    public byte Version { get; }
    public int HeaderLength { get; }
    public byte Dscp { get; }
    public byte Ecn { get; }
    public ushort TotalLength { get; }
    public ushort Identification { get; }
    public byte Flags { get; }
    public ushort FragmentOffset { get; }
    public byte Ttl { get; }
    public byte Protocol { get; }
    public ushort Checksum { get; }
    public byte[] SourceIp { get; }
    public byte[] DestinationIp { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public ReadOnlyMemory<byte> RawData { get; }

    
    public bool IsIcmp => Protocol == ProtocolIcmp;
    public bool IsTcp => Protocol == ProtocolTcp;
    public bool IsUdp => Protocol == ProtocolUdp;

    public string ProtocolName => Protocol switch
    {
        ProtocolIcmp => "ICMP",
        ProtocolTcp => "TCP",
        ProtocolUdp => "UDP",
        _ => $"Unknown({Protocol})"
    };

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

    public static IPv4Packet Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < MinHeaderLength)
            throw new ArgumentException($"Data too short for IPv4 packet: {data.Length} < {MinHeaderLength}");

        var span = data.Span;

        var versionIhl = span[OffsetVersionIhl];
        var version = (byte)(versionIhl >> 4);
        var headerLength = (versionIhl & 0x0F) * 4;

        if (version != 4)
            throw new ArgumentException($"Not an IPv4 packet: Version = {version}");

        if (data.Length < headerLength)
            throw new ArgumentException($"Data too short for IPv4 packet: {data.Length} < {headerLength}");

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
        catch (Exception e)
        {
            packet = default;
            return false;
        }
    }

    public static int Build(Span<byte> buffer, ReadOnlySpan<byte> srcIp, ReadOnlySpan<byte> dstIp,
        byte protocol, ReadOnlySpan<byte> payload, ref ushort identification, byte ttl = DefaultTtl)
    {
        int totalLength = MinHeaderLength + payload.Length;
        
        if (buffer.Length < totalLength)
            throw new ArgumentException($"Buffer too short for IPv4 packet: {buffer.Length} < {totalLength}");
        
        // 版本（4） + 头部长度（5）
        buffer[OffsetVersionIhl] = 0x45;
        
        // DSCP/ECN
        buffer[OffsetDscpEcn] = 0x00;
        
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetTotalLength, (ushort)totalLength);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetIdentification, identification++);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetFlagsFragmentOffset, 0x4000);
        
        buffer[OffsetTtl] = ttl;
        buffer[OffsetProtocol] = protocol;
        
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, 0);
        
        srcIp.Slice(0, 4).CopyTo(buffer.Slice(OffsetSrcIp));
        dstIp.Slice(0, 4).CopyTo(buffer.Slice(OffsetDstIp));

        if (!payload.IsEmpty)
        {
            payload.CopyTo(buffer.Slice(MinHeaderLength));
        }

        var checksum = ChecksumCalculator.Calculate(buffer.Slice(0, MinHeaderLength));
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, checksum);

        return totalLength;
    }

    public static byte[] Build(ReadOnlySpan<byte> srcIp, ReadOnlySpan<byte> dstIp,
        byte protocol, ReadOnlySpan<byte> payload, ref ushort identification, byte ttl = DefaultTtl)
    {
        var buffer = new byte[MinHeaderLength + payload.Length];
        Build(buffer.AsSpan(), srcIp, dstIp, protocol, payload, ref identification, ttl);
        return buffer;
    }

    public bool ValidateChecksum()
    {
        if (RawData.Length < HeaderLength)
            return false;
        
        return ChecksumCalculator.Verify(RawData.Span.Slice(0, HeaderLength));
    }
    
    public override string ToString()
    {
        return $"IPv4: {ByteHelper.IpToString(SourceIp)} -> {ByteHelper.IpToString(DestinationIp)}, " +
               $"Protocol: {ProtocolName}, length: {TotalLength}, Ttl: {Ttl}";
    }
}