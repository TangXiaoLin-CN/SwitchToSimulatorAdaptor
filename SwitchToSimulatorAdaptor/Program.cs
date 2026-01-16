// See https://aka.ms/new-console-template for more information

using SharpPcap;
using SwitchToSimulatorAdaptor;
using SwitchToSimulatorAdaptor.Common;
using SwitchToSimulatorAdaptor.EdenRoom;
using SwitchToSimulatorAdaptor.SharpPcap;
using SwitchToSimulatorAdaptor.Utils;

Console.WriteLine("SwitchToSimulatorAdaptor");

// Console.WriteLine("Input Enter to join room:");
// Console.ReadLine();
//
if (!NativeENetHost.InitializeNativeENet())
{
    Console.WriteLine("初始化NativeENet失败");
    return;
}

IPv4Address _hostIp;
IPv4Address _clientIp;

var edenRoomClient = new EdenRoomMember();
edenRoomClient.LdnPacketReceived += ReceiveEdenLdnPacket;
edenRoomClient.ProxyPacketReceived += ReceiveEdenProxyPacket;
edenRoomClient.Join(AppSetting.EdenRoomNickname);
Console.WriteLine("成功加入房间");
// Console.ReadLine();

var sharpPcapMgr = new SharpPcapManager();

if (!sharpPcapMgr.Init())
{
    Console.WriteLine("初始化SharpPcap失败");
    return;
}


void ReceiveEdenProxyPacket(EdenProxyPacket obj)
{
    Logger.Instance?.LogInfo($"edenProxyPacket");
}

sharpPcapMgr.RegisterPacketArrivalEvent(ReceiveRawSwitchUdpPacket);

sharpPcapMgr.StartCapture();
Console.WriteLine($"开始捕获，规则：{AppSetting.BPFFilter}");
Console.ReadLine();

void ReceiveEdenLdnPacket(EdenLDNPacket edenLdnPacket)
{
    Logger.Instance?.LogInfo($"收到edenLdnPacket,type:{edenLdnPacket.Type} from:{edenLdnPacket.LocalIp.ToString()}, to:{edenLdnPacket.RemoteIp.ToString()}");
}

void ReceiveRawSwitchUdpPacket(object sender, PacketCapture e)
{
    if (!ProtocolConverter.RawSwitchLdnPacketToEdenLdnPacket(e.Data.ToArray(), 
            out var edenLdnPacket)) return;

    if (edenLdnPacket.Type == EdenLDNPacketType.Scan)
    {
        _clientIp = edenLdnPacket.LocalIp;
    }
    
    edenRoomClient.SendLdnPacket(edenLdnPacket);
}

