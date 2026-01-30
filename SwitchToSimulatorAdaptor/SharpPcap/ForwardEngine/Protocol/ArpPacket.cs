using System.Reflection.Emit;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

public readonly struct ArpPacket
{
    public const int Length = 28;
    public const int OffsetHardwareType = 0;
    public const int OffsetProtocolType = 2;
    public const int OffsetHardwareSize = 4;
    public const int OffsetProtocolSize = 5;
    public const int OffsetOpcode = 6;
    public const int OffsetSenderMac = 8;
    public const int OffsetSenderIp = 14;
    public const int OffsetTargetMac = 18;
    public const int OffsetTargetIp = 24;
    public const ushort HardwareTypeEthernet = 1;
    public const ushort ProtocolTypeIPv4 = 0x0800;
    public const ushort OpcodeRequest = 1;
    public const ushort OpcodeReply = 2;
    
    public ushort HardwareType { get; }
    public ushort ProtocolType { get; }
    public byte HardwareSize { get; }
    public byte ProtocolSize { get; }
    public ushort Opcode { get; }
    public byte[] SenderMac { get; }
    public byte[] SenderIp { get; }
    public byte[] TargetMac { get; }
    public byte[] TargetIp { get; }
    
    public bool IsRequest => Opcode == OpcodeRequest;
    public bool IsReply => Opcode == OpcodeReply;
    public bool IsValid => HardwareType == HardwareTypeEthernet && 
                           ProtocolType == ProtocolTypeIPv4 &&
                           HardwareSize == 6 && ProtocolSize == 4;

    public ArpPacket(ushort hardwareType, ushort protocolType, byte hardwareSize, byte protocolSize,
        ushort opcode, byte[] senderMac, byte[] senderIp, byte[] targetMac, byte[] targetIp)
    {
        HardwareType = hardwareType;
        ProtocolType = protocolType;
        HardwareSize = hardwareSize;
        ProtocolSize = protocolSize;
        Opcode = opcode;
        SenderMac = senderMac;
        SenderIp = senderIp;
        TargetMac = targetMac;
        TargetIp = targetIp;
    }

    public static ArpPacket Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Length)
            throw new ArgumentException($"Data too short for ARP packet: {data.Length} < {Length}");

        var hardwareType = ByteHelper.ReadUInt16BigEndian(data, OffsetHardwareType);
        var protocolType = ByteHelper.ReadUInt16BigEndian(data, OffsetProtocolType);
        var hardwareSize = data[OffsetHardwareSize];
        var protocolSize = data[OffsetProtocolSize];
        var opcode = ByteHelper.ReadUInt16BigEndian(data, OffsetOpcode);
        var senderMac = data.Slice(OffsetSenderMac, 6).ToArray();
        var senderIp = data.Slice(OffsetSenderIp, 4).ToArray();
        var targetMac = data.Slice(OffsetTargetMac, 6).ToArray();
        var targetIp = data.Slice(OffsetTargetIp, 4).ToArray();

        return new ArpPacket(hardwareType, protocolType, hardwareSize, protocolSize, 
            opcode, senderMac, senderIp, targetMac, targetIp);
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out ArpPacket packet)
    {
        if (data.Length < Length)
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

    public static int Build(Span<byte> buffer, ushort opcode,
        ReadOnlySpan<byte> senderMac, ReadOnlySpan<byte> senderIp,
        ReadOnlySpan<byte> targetMac, ReadOnlySpan<byte> targetIp)
    {
        if (buffer.Length < Length)
            throw new ArgumentException("Buffer too small");
        
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetHardwareType, HardwareTypeEthernet);
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetProtocolType, ProtocolTypeIPv4);
        buffer[OffsetHardwareSize] = 6;
        buffer[OffsetProtocolSize] = 4;
        ByteHelper.WriteUInt16BigEndian(buffer, OffsetOpcode, opcode);
        senderMac.Slice(0, 6).CopyTo(buffer.Slice(OffsetSenderMac));
        senderIp.Slice(0, 4).CopyTo(buffer.Slice(OffsetSenderIp));
        targetMac.Slice(0, 6).CopyTo(buffer.Slice(OffsetTargetMac));
        targetIp.Slice(0, 4).CopyTo(buffer.Slice(OffsetTargetIp));

        return Length;
    }

    public static byte[] BuildRequest(ReadOnlySpan<byte> senderMac, ReadOnlySpan<byte> senderIp,
        ReadOnlySpan<byte> targetIp)
    {
        var buffer = new byte[Length];
        Build(buffer, OpcodeRequest, senderMac, senderIp, ByteHelper.ZeroMac, targetIp);
        return buffer;
    }

    public static byte[] BuildReply(ReadOnlySpan<byte> senderMac, ReadOnlySpan<byte> senderIp,
        ReadOnlySpan<byte> targetMac, ReadOnlySpan<byte> targetIp)
    {
        var buffer = new byte[Length];
        Build(buffer, OpcodeReply, senderMac, senderIp, targetMac, targetIp);
        return buffer;
    }
    
    public override string ToString()
    {
        var opcodeStr = Opcode switch
        {
            OpcodeRequest => "Request",
            OpcodeReply => "Reply",
            _ => $"Unknown({Opcode})"
        };
        
        return $"ARP: {opcodeStr}: {ByteHelper.MacToString(SenderMac)} ({ByteHelper.IpToString(SenderIp)}) -> " +
               $"{ByteHelper.MacToString(TargetMac)} ({ByteHelper.IpToString(TargetIp)})";
    }
}