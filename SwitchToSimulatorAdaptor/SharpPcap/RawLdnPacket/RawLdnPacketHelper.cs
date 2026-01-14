using SwitchToSimulatorAdaptor.SwitchLdn;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.SharpPcap;

public class RawLdnPacketHelper
{
    /// <summary>
    /// ldn数据包解析
    /// </summary>
    public static void DecodePacket(byte[] data)
    {
        var ethernet = EthernetFrame.Parse(data);
        if (ethernet == null) return;
        
        var ip = IPv4Header.Parse(ethernet.Payload);
        if (ip == null) return;
        
        if (ip.IsUDP)
        {
            var udp = UdpHeader.Parse(ip.Payload);
            if (udp == null) return;
            
            Logger.Instance?.LogDebug($"[UDP] 端口:{ udp.SourcePort } -> { udp.DestinationPort }");
            
            if (udp.DestinationPort == 11452 || udp.SourcePort == 11452)
            { 
                var ldn = LdnHeader.Parse(udp.Payload);
                if (ldn != null && ldn.IsValid)
                {
                    Logger.Instance?.LogDebug($"[LDN] 类型：{ ldn.Type }， 长度：{ldn.Length}， 压缩状态：{ldn.IsCompressed}");
                }
            }
        }else if (ip.IsTCP)
        {
            var tcp = TcpHeader.Parse(ip.Payload);
            if (tcp == null) return;
            
            Logger.Instance?.LogDebug($"[TCP] 端口:{ tcp.SourcePort } -> { tcp.DestinationPort }");
        }
        
        Console.WriteLine();
    }
}