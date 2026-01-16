using SwitchToSimulatorAdaptor.SharpPcap;
using SwitchToSimulatorAdaptor.SwitchLdn;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.Utils;

public class SwitchPacket
{
    public byte[] SrcIp;
    public byte[] DstIp;
    public ushort SrcPort;
    public ushort DstPort;
    public bool Broadcast;
    public LdnHeader LdnPacket;
}

public class RawLdnPacketHelper
{
    /// <summary>
    /// ldn数据包解析
    /// </summary>
    public static bool TryDecodePacket(byte[] data, out SwitchPacket? switchPacket)
    {
        switchPacket = null;
        var ethernet = EthernetFrame.Parse(data);
        if (ethernet == null) return false;
        
        var ip = IPv4Header.Parse(ethernet.Payload);
        if (ip == null) return false;
        
        if (ip.IsUDP)
        {
            var udp = UdpHeader.Parse(ip.Payload);
            if (udp == null) return false;
            
            Logger.Instance?.LogDebug($"[UDP] 端口:{ udp.SourcePort } -> { udp.DestinationPort }");
            
            if (udp.DestinationPort == 11452 || udp.SourcePort == 11452)
            { 
                var ldn = LdnHeader.Parse(udp.Payload);
                if (ldn != null && ldn.IsValid)
                {
                    switchPacket = new SwitchPacket
                    {
                        SrcIp = ip.SourceIP,
                        DstIp = ip.DestinationIP,
                        SrcPort = udp.SourcePort,
                        DstPort = udp.DestinationPort,
                        Broadcast = ethernet.IsBroadcast,
                        LdnPacket = ldn
                    };
                    Logger.Instance?.LogDebug($"[LDN] 类型：{ ldn.Type }， 长度：{ldn.Length}， 压缩状态：{ldn.IsCompressed}");
                    return true;
                }
            }
        }else if (ip.IsTCP)
        {
            var tcp = TcpHeader.Parse(ip.Payload);
            if (tcp == null) return false;
            
            Logger.Instance?.LogDebug($"[TCP] 端口:{ tcp.SourcePort } -> { tcp.DestinationPort }");
        }

        return false;
    }
}