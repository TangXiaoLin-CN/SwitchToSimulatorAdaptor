using SharpPcap;
using SwitchToSimulatorAdaptor.SharpPcap;

namespace SwitchToSimulatorAdaptor.SharpPcap;

public class ArpSpoofer
{
    // private readonly ICaptureDevice _device;
    // private readonly LibPcapLiveDevice? _device;
    private readonly byte[] _myMac; // 我们的 MAC
    private readonly byte[] _fakeIP; // 要冒充的 IP （如 192.168.166.50）
    private readonly PacketSender _sender;

    // 保存发现的 Switch 信息
    private byte[]? _switchMac;
    private byte[]? _switchIP;

    // public ArpSpoofer(ICaptureDevice device, byte[] myMac, byte[] fakeIP)
    // public ArpSpoofer(ICaptureDevice device, byte[] myMac, byte[] fakeIP)
    public ArpSpoofer(PacketSender sender, byte[] myMac, byte[] fakeIP)
    {
        // _device = device;
        _sender = sender;
        _myMac = myMac;
        _fakeIP = fakeIP;
    }

    /// <summary>
    /// 处理收到的数据包
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public void OnPacketArrival(object sender, PacketCapture e)
    {
        var data = e.Data;

        // 检查是否是 ARP 包 （EtherType = 0x0806）
        if (data.Length < 42) return;

        ushort etherType = (ushort)((data[12] << 8) | data[13]);
        if (etherType != 0x0806) return;

        // 解析 ARP
        byte[] arpData = new byte[28];
        Array.Copy(data.ToArray(), 14, arpData, 0, 28);

        var arp = ArpPacket.Parse(arpData);
        if (arp == null) return;

        Console.WriteLine(
            $"[ARP] {arp.Opcode} : {arp.SenderIPString} ({arp.SenderMacString}) -> {arp.TargetIPString}");

        // 如果是 ARP Request, 检查是否在问我们冒充的 IP
        if (arp.IsRequest && arp.TargetIP.SequenceEqual(_fakeIP))
        {
            Console.WriteLine($"[ARP] ★ Switch 在问 {arp.TargetIPString} 的 MAC，我们来回答！");

            // 保存 Switch 的信息
            _switchMac = arp.SenderMac;
            _switchIP = arp.SenderIP;

            // 发送 ARP Reply 欺骗
            SendArpReply();
        }
    }

    /// <summary>
    /// 发送 ARP Reply 欺骗包
    /// </summary>
    private void SendArpReply()
    {
        if (_switchMac == null || _switchIP == null) return;

        byte[] reply = ArpBuilder.BuildArpReply(
            _myMac, // 我们的 MAC
            _fakeIP, // 我们要冒充的 IP
            _switchMac, // Switch 的 MAC
            _switchIP // Switch 的 IP
        );

        // _device.SendPacket(reply);
        _sender.Send(reply);

        Console.WriteLine($"[ARP] √ 已发送 ARP Reply：{FormatIP(_fakeIP)} -> {FormatMac(_myMac)}");
    }

    /// <summary>
    /// 定期发送免费 ARP, 维持欺骗
    /// </summary>
    public void SendGratuitousArp()
    {
        // 免费 ARP：主动告诉网络 “我是这个IP”
        // 目标 MAC 用广播
        byte[] broadcast = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        byte[] reply = ArpBuilder.BuildArpReply(
            _myMac,
            _fakeIP,
            broadcast, // 广播给所有人
            _fakeIP // TargetIP = Sender IP (免费 ARP 的特征）
        );

        _sender.Send(reply);
        Console.WriteLine("[ARP] 发送免费 ARP 广播");
    }

    private static string FormatMac(byte[] mac)
        => string.Join(":", mac.Select(b => b.ToString("X2")));

    private static string FormatIP(byte[] ip)
        => string.Join(".", ip);
}