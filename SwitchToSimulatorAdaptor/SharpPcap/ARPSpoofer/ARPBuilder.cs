namespace SwitchToSimulatorAdaptor.SharpPcap;

public class ArpBuilder
{
    public static byte[] BuildArpReply(
        byte[] myMac, // 我们的MAC
        byte[] fakeIP, // 我们要冒充的 IP（如 192.168.166.50）
        byte[] targetMac, // Switch 的 MAC
        byte[] targetIP) // Switch 的 IP
    {
        // 完整数据包 = 以太网头 （14） + ARP 数据（28） = 42 字节
        byte[] packet = new byte[42];

        // === 以太网头 （14 字节） ===
        // 目标 MAC （发送给 Switch）
        Array.Copy(targetMac, 0, packet, 0, 6);

        // 源 MAC （ 我们的 MAC）
        Array.Copy(myMac, 0, packet, 6, 6);

        // 以太网类型（ARP） EtherType = ARP (0x0806)
        packet[12] = 0x08;
        packet[13] = 0x06;

        // === ARP 数据 （28 字节） ===
        int offset = 14;

        // Hardware Type = Ethernet （0x0001）
        packet[offset + 0] = 0x00;
        packet[offset + 1] = 0x01;

        // Protocol Type = IPv4 (0x0800)
        packet[offset + 2] = 0x08;
        packet[offset + 3] = 0x00;

        // Hardware Size = 6
        packet[offset + 4] = 6;

        // Protocol Size = 4
        packet[offset + 5] = 4;

        // Opcode = Reply (2)
        packet[offset + 6] = 0x00;
        packet[offset + 7] = 0x02;

        // Sender MAC = 我们的 MAC （关键！ switch 会记住这个）
        Array.Copy(myMac, 0, packet, offset + 8, 6);

        // Sender IP = 我们要冒充的 IP （关键！）
        Array.Copy(fakeIP, 0, packet, offset + 14, 4);

        // Target MAC = Switch 的 MAC 
        Array.Copy(targetMac, 0, packet, offset + 18, 6);

        // Target IP = Switch 的 IP
        Array.Copy(targetIP, 0, packet, offset + 24, 4);

        return packet;
    }
}