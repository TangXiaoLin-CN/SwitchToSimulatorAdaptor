using SwitchToSimulatorAdaptor.EdenRoom;
using SwitchToSimulatorAdaptor.SharpPcap;
using SwitchToSimulatorAdaptor.SwitchLdn;

namespace SwitchToSimulatorAdaptor.Utils;

public static class ProtocolConverter
{
    public static void EdenPacketToSwitchPacket(EdenLDNPacket edenLdnPacket, out LdnHeader switchLdnPacket)
    {
        switchLdnPacket = new LdnHeader
        {
            Magic = LdnHeader.MAGIC,
            Type =  EdenTypeToSwitchType(edenLdnPacket.Type),
            IsCompressed = false,
            Payload = edenLdnPacket.Data
        };
    }
    
    public static bool RawSwitchLdnPacketToEdenLdnPacket(byte[] rawSwitchData, out EdenLDNPacket edenLdnPacket)
    {
        edenLdnPacket = new EdenLDNPacket();
        var ethernet = EthernetFrame.Parse(rawSwitchData);
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
                    edenLdnPacket = new EdenLDNPacket()
                    {
                        Type = SwitchTypeToEdenType(ldn.Type),
                        LocalIp = new(ip.SourceIP),
                        RemoteIp = new(ip.DestinationIP),
                        Broadcast = ethernet.IsBroadcast,
                        Data = ldn.Payload
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

    public static LdnPacketType EdenTypeToSwitchType(EdenLDNPacketType edenLdnType)
    {
        return edenLdnType switch
        {
            EdenLDNPacketType.Scan => LdnPacketType.Scan,
            EdenLDNPacketType.ScanResp => LdnPacketType.ScanResponse,
            EdenLDNPacketType.Connect => LdnPacketType.Connect,
            EdenLDNPacketType.SyncNetwork => LdnPacketType.SyncNetwork,
            EdenLDNPacketType.Disconnect => LdnPacketType.Disconnect,
            _ => LdnPacketType.Scan
        };
    }

    public static EdenLDNPacketType SwitchTypeToEdenType(LdnPacketType switchType)
    {
        return switchType switch
        {
            LdnPacketType.Scan => EdenLDNPacketType.Scan,
            LdnPacketType.ScanResponse => EdenLDNPacketType.ScanResp,
            LdnPacketType.Connect => EdenLDNPacketType.Connect,
            LdnPacketType.SyncNetwork => EdenLDNPacketType.SyncNetwork,
            LdnPacketType.Disconnect => EdenLDNPacketType.Disconnect,
            _ => EdenLDNPacketType.Scan
        };
    }
}