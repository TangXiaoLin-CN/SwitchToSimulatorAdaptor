namespace SwitchToSimulatorAdaptor.SharpPcap;

public static class RawTcpPacketBuilder
{
    public static byte[] Build(
        byte[] srcMac,
        byte[] dstMac,
        byte[] srcIP,
        byte[] dstIP,
        ushort srcPort,
        ushort dstPort,
        uint seqNum,
        uint ackNum,
        TcpFlags flags,
        byte[] data)
    {
        // TCP 头 （20 字节， 无选项）
        byte[] tcpHeader = new byte[20];

        // 源端口
        tcpHeader[0] = (byte)(srcPort >> 8);
        tcpHeader[1] = (byte)(srcPort & 0xFF);

        // 目标端口
        tcpHeader[2] = (byte)(dstPort >> 8);
        tcpHeader[3] = (byte)(dstPort & 0xFF);

        // 序列号
        tcpHeader[4] = (byte)(seqNum >> 24);
        tcpHeader[5] = (byte)(seqNum >> 16);
        tcpHeader[6] = (byte)(seqNum >> 8);
        tcpHeader[7] = (byte)(seqNum & 0xFF);

        // 确认号
        tcpHeader[8] = (byte)(ackNum >> 24);
        tcpHeader[9] = (byte)(ackNum >> 16);
        tcpHeader[10] = (byte)(ackNum >> 8);
        tcpHeader[11] = (byte)(ackNum & 0xFF);

        // 数据偏移 （5 * 4 = 20 字节） + 保留位
        tcpHeader[12] = 0x50; // 5 << 4

        // 标志位
        tcpHeader[13] = (byte)flags;

        // 窗口大小
        tcpHeader[14] = 0xFF; // 65535
        tcpHeader[15] = 0xFF;

        // 校验和
        tcpHeader[16] = 0;
        tcpHeader[17] = 0;

        // 紧急指针
        tcpHeader[18] = 0;
        tcpHeader[19] = 0;

        // 计算 TCP 校验和
        ushort checksum = CalculateTcpChecksum(srcIP, dstIP, tcpHeader, data);
        tcpHeader[16] = (byte)(checksum >> 8);
        tcpHeader[17] = (byte)(checksum & 0xFF);

        // 构建 IP 头
        int totalLength = 20 + 20 + data.Length; // IP头 + TCP 头 + 数据
        byte[] ipHeader = BuildIPHeader(srcIP, dstIP, (ushort)totalLength);

        // 构建以太网帧
        byte[] packet = new byte[14 + 20 + 20 + data.Length];
        int offset = 0;

        // 以太网头
        Array.Copy(dstMac, 0, packet, offset, 6);
        offset += 6;
        Array.Copy(srcMac, 0, packet, offset, 6);
        offset += 6;
        packet[offset++] = 0x08; // IPv4
        packet[offset++] = 0x00;

        // IP 头
        Array.Copy(ipHeader, 0, packet, offset, 20);
        offset += 20;

        // TCP 头
        Array.Copy(tcpHeader, 0, packet, offset, 20);
        offset += 20;

        // 数据
        if (data.Length > 0)
        {
            Array.Copy(data, 0, packet, offset, data.Length);
        }

        return packet;
    }

    private static byte[] BuildIPHeader(byte[] srcIP, byte[] dstIP, ushort totalLength)
    {
        byte[] header = new byte[20];

        header[0] = 0x45; // Version + IHL
        header[1] = 0; // TOS
        header[2] = (byte)(totalLength >> 8);
        header[3] = (byte)(totalLength & 0xFF);
        header[4] = (byte)(new Random().Next(0, 255)); // ID
        header[5] = (byte)(new Random().Next(0, 255));
        header[6] = 0x40; // Dont Fragment
        header[7] = 0;
        header[8] = 64; // TTL
        header[9] = 6; // Protocol: TCP
        header[10] = 0; // Checksum (先填0）
        header[11] = 0;
        Array.Copy(srcIP, 0, header, 12, 4);
        Array.Copy(dstIP, 0, header, 16, 4);

        // 计算 IP 校验和
        ushort checksum = CalculateIPChecksum(header);
        header[10] = (byte)(checksum >> 8);
        header[11] = (byte)(checksum & 0xFF);

        return header;
    }

    private static ushort CalculateTcpChecksum(byte[] srcIP, byte[] dstIP, byte[] tcpHeader, byte[] data)
    {
        uint sum = 0;

        // 伪头部
        sum += (uint)((srcIP[0] << 8) | srcIP[1]);
        sum += (uint)((srcIP[2] << 8) | srcIP[3]);
        sum += (uint)((dstIP[0] << 8) | dstIP[1]);
        sum += (uint)((dstIP[2] << 8) | dstIP[3]);
        sum += 6;
        sum += (uint)(tcpHeader.Length + data.Length);

        // TCP 头
        for (int i = 0; i < tcpHeader.Length; i += 2)
        {
            sum += (uint)((tcpHeader[i] << 8) | tcpHeader[i + 1]);
        }

        // 数据
        for (int i = 0; i < data.Length; i += 2)
        {
            if (i + 1 < data.Length)
            {
                sum += (uint)((data[i] << 8) | data[i + 1]);
            }
            else
            {
                sum += (uint)(data[i] << 8);
            }
        }

        // 处理进位
        while (sum >> 16 != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
    }

    private static ushort CalculateIPChecksum(byte[] ipHeader)
    {
        uint sum = 0;

        for (int i = 0; i < ipHeader.Length; i += 2)
        {
            sum += (uint)((ipHeader[i] << 8) | ipHeader[i + 1]);
        }

        while (sum >> 16 != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
    }
}