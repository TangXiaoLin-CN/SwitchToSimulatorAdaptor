// See https://aka.ms/new-console-template for more information

using SwitchToSimulatorAdaptor;
using SwitchToSimulatorAdaptor.EdenRoom;
using SwitchToSimulatorAdaptor.SharpPcap;

Console.WriteLine("SwitchToSimulatorAdaptor");

// Console.WriteLine("Input Enter to join room:");
// Console.ReadLine();
//
// if (!NativeENetHost.InitializeNativeENet())
// {
//     Console.WriteLine("初始化NativeENet失败");
// }
//
// var edenRoomClient = new EdenRoomMember();
// edenRoomClient.Join(AppSetting.EdenRoomNickname);
// Console.WriteLine("成功加入房间");
// Console.ReadLine();

var sharpPcapMgr = new SharpPcapManager();
if (!sharpPcapMgr.Init())
{
    Console.WriteLine("初始化SharpPcap失败");
    return;
}
sharpPcapMgr.RegisterPacketArrivalEvent((s, e) =>
{
    RawLdnPacketHelper.DecodePacket(e.Data.ToArray());
});
sharpPcapMgr.StartCapture();
Console.WriteLine($"开始捕获，规则：{AppSetting.BPFFilter}");

// void ParsePacket(ReadOnlySpan<byte> packet)
// {
//     if (packet.Length < 14) // 以太网帧的包头长度是 14 字节, 因此最小以太网帧至少 14 字节
//     {
//         Console.WriteLine("数据包太短, 不是有效的以太帧");
//         return;
//     }
//         
//     // 这里的 :X2 表示将 byte 转换成大写十六进制字符串, 如果是 x 则是小写十六进制字符串. 2则表示为至少两位, 不足两位则前面补0
//         
//     // AI的解释:
//     // :X2 表示将字节数值格式化为大写十六进制字符串
//     // X = 大写十六进制（如果是 x 则为小写）
//     // 2 = 至少显示 2 位数字，不足时前面补 0
//         
//     // 解析以太网头 ( 14 字节 )
//     // [0-5]   目标MAC地址
//     // [6-11]  源MAC地址
//     // [12-13] 帧类型
//         
//     var dstMac = $"{packet[0]:X2}:{packet[1]:X2}:{packet[2]:X2}:{packet[3]:X2}:{packet[4]:X2}:{packet[5]:X2}";
//     var srcMac = $"{packet[6]:X2}:{packet[7]:X2}:{packet[8]:X2}:{packet[9]:X2}:{packet[10]:X2}:{packet[11]:X2}";
//     var etherType = (packet[12] << 8) | packet[13]; //大端序
//         
//     Console.WriteLine("以太网帧");
//     Console.WriteLine($"目标MAC地址: {dstMac}");
//     Console.WriteLine($"源MAC地址: {srcMac}");
//     Console.WriteLine($"帧类型: 0x{etherType:X4} ({GetEtherTypeName(etherType)})");
//         
//     string GetEtherTypeName(int etherType)
//     { 
//         switch (etherType)
//         {
//             case 0x0800:
//                 return "IPv4";
//             case 0x0806:
//                 return "ARP";
//             case 0x86DD:
//                 return "IPv6";
//             default:
//                 return "未知";
//         }
//     }
// }

Console.ReadLine();

