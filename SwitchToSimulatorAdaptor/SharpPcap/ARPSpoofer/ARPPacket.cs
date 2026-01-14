namespace SwitchToSimulatorAdaptor.SharpPcap;

public enum ArpOpcode : ushort
{
    Request = 1,
    Reply = 2
}

public class ArpPacket
{
    public ushort HardwareType { get; set; } // 通常 0x0001 = 以太网
    public ushort ProtocolType { get; set; } // 通常 0x0800 = IPv4
    public byte HardwareSize { get; set; } // 通常 6
    public byte ProtocolSize { get; set; } // 通常 4
    public ArpOpcode Opcode { get; set; }
    public byte[] SenderMac { get; set; } = new byte[6];
    public byte[] SenderIP { get; set; } = new byte[4];
    public byte[] TargetMac { get; set; } = new byte[6];
    public byte[] TargetIP { get; set; } = new byte[4];

    public bool IsRequest => Opcode == ArpOpcode.Request;
    public bool IsReply => Opcode == ArpOpcode.Reply;

    public string SenderMacString => FormatMAC(SenderMac);
    public string SenderIPString => FormatIP(SenderIP);
    public string TargetMacString => FormatMAC(TargetMac);
    public string TargetIPString => FormatIP(TargetIP);

    private static string FormatMAC(byte[] mac)
        => string.Join(":", mac.Select(b => b.ToString("X2")));

    private static string FormatIP(byte[] ip)
        => string.Join(".", ip);

    /// <summary>
    /// 从原始字节解析 ARP 数据包
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public static ArpPacket? Parse(byte[] data)
    {
        if (data.Length < 28) return null;

        var arp = new ArpPacket();

        // Hardware Type （大端序）
        arp.HardwareType = (ushort)((data[0] << 8) | data[1]);

        // Protocol Type （大端序）
        arp.ProtocolType = (ushort)((data[2] << 8) | data[3]);

        // Hardware Size
        arp.HardwareSize = data[4];

        // Protocol Size
        arp.ProtocolSize = data[5];

        // Opcode （大端序）
        arp.Opcode = (ArpOpcode)((data[6] << 8) | data[7]);

        // Sender MAC
        Array.Copy(data, 8, arp.SenderMac, 0, 6);

        // Sender IP
        Array.Copy(data, 14, arp.SenderIP, 0, 4);

        // Target MAC
        Array.Copy(data, 18, arp.TargetMac, 0, 6);

        // Target IP
        Array.Copy(data, 24, arp.TargetIP, 0, 4);

        return arp;
    }
}