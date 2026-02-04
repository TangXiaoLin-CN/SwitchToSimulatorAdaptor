namespace SwitchToSimulatorAdaptor.ForwardEngine;

public readonly struct TcpPacket
{
    public const int MinHeaderLength = 20;
    public const int OffsetSrcPort = 0;
    public const int OffsetDstPort = 2;
    public const int OffsetSeqNum = 4;
    public const int OffsetAckNum = 8;
    public const int OffsetDataOffsetFlags = 12;
    public const int OffsetWindow = 14;
    public const int OffsetChecksum = 16;
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
    
    public ushort SourcePort { get; }
    public ushort DestinationPort { get; }
    public uint SequenceNumber { get; }
    public uint AcknowledgmentNumber { get; }
    public int HeaderLength { get; }
    public byte Flags { get; }
    public ushort WindowSize { get; }
    public ushort Checksum { get; }
    public ushort UrgentPointer { get; }
    public ReadOnlyMemory<byte> Options { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public ReadOnlyMemory<byte> RawData { get; }

    public bool HasFin => (Flags & FlagFin) != 0;
    public bool HasSyn => (Flags & FlagSyn) != 0;
    public bool HasRst => (Flags & FlagRst) != 0;
    public bool HasPsh => (Flags & FlagPsh) != 0;
    public bool HasAck => (Flags & FlagAck) != 0;
    public bool HasUrg => (Flags & FlagUrg) != 0;

    public string FlagsString
    {
        get
        {
            var flags = new List<string>();
            if (HasSyn) flags.Add("SYN");
            if (HasAck) flags.Add("ACK");
            if (HasFin) flags.Add("FIN");
            if (HasPsh) flags.Add("PSH");
            if (HasUrg) flags.Add("URG");
            return string.Join("|", flags);
        }
    }

    private TcpPacket(ushort sourcePort, ushort destinationPort, uint sequenceNumber,
        uint acknowledgementNumber, int headerLength, byte flags, ushort windowSize,
        ushort checksum, ushort urgentPointer, ReadOnlyMemory<byte> options,
        ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> rawData)
    {
        SourcePort = sourcePort;
        DestinationPort = destinationPort;
        SequenceNumber = sequenceNumber;
        AcknowledgmentNumber = acknowledgementNumber;
        HeaderLength = headerLength;
        Flags = flags;
        WindowSize = windowSize;
        Checksum = checksum;
        UrgentPointer = urgentPointer;
        Options = options;
        Payload = payload;
        RawData = rawData;
    }

    public static TcpPacket Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < MinHeaderLength)
            throw new ArgumentException($"Data too short for a TCP packet: {data.Length} < {MinHeaderLength}");

        var span = data.Span;

        var sourcePort = ByteHelper.ReadUInt16BigEndian(span, OffsetSrcPort);
        var destinationPort = ByteHelper.ReadUInt16BigEndian(span, OffsetDstPort);
        var sequenceNumber = ByteHelper.ReadUInt32BigEndian(span, OffsetSeqNum);
        var acknowledgementNumber = ByteHelper.ReadUInt32BigEndian(span, OffsetAckNum);

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

        return new TcpPacket(sourcePort, destinationPort, sequenceNumber, acknowledgementNumber, 
            headerLength, flags, windowSize, checksum, urgentPointer, options, payload, data);
    }

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
        catch (Exception e)
        {
            packet = default;
            return false;
        }
    }

    public static int Build(Span<byte> buffer, ReadOnlySpan<byte> srcIp, ushort srcPort,
        ReadOnlySpan<byte> dstIp, ushort dstPort, uint seqNum, uint ackNum,
        byte flags, ushort windowSize, ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> options = default)
    {
        int headerLength = MinHeaderLength + options.Length;
        int paddedHeaderLength = (headerLength + 3) / 4 * 4;
        int totalLength = paddedHeaderLength + payload.Length;

        if (buffer.Length < totalLength)
            throw new ArgumentException("Buffer too small");
        
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetSrcPort, srcPort);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetDstPort, dstPort);
        ByteHelper.WriteUInt32BigEndian(buffer, OffsetSeqNum, seqNum);
        ByteHelper.WriteUInt32BigEndian(buffer, OffsetAckNum, ackNum);
        
        int dataOffset = paddedHeaderLength / 4;
        ushort dataOffsetFlags = (ushort)(dataOffset << 12 | flags);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetDataOffsetFlags, dataOffsetFlags);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetWindow, windowSize);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, 0);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetUrgentPointer, 0);

        if (!options.IsEmpty)
        {
            options.CopyTo(buffer.Slice(MinHeaderLength));
        }

        for (int i = headerLength; i < paddedHeaderLength; i++)
        {
            buffer[i] = 0;
        }

        if (!payload.IsEmpty)
        {
            payload.CopyTo(buffer.Slice(paddedHeaderLength));
        }

        var checksum = ChecksumCalculator.CalculateTcp(srcIp, dstIp,
            buffer.Slice(0, paddedHeaderLength), payload);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetChecksum, checksum);

        return totalLength;
    }

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

    public override string ToString()
    {
        return $"TCP: {SourcePort} -> {DestinationPort}, Seq: {SequenceNumber}, Ack: {AcknowledgmentNumber}, " + 
               $"Flags: [{Flags}], Win: {WindowSize}, Payload: {Payload.Length} bytes";
    }
}