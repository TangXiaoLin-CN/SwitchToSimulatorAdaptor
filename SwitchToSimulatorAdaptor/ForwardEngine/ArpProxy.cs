using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

public class ArpProxy
{
    private readonly ArpCache _cache;
    private readonly byte[] _gatewayIp;
    private readonly byte[] _subnetNet;
    private readonly byte[] _subnetMask;
    
    public ArpCache Cache => _cache;

    public ArpProxy(ArpCache cache)
    {
        _cache = cache;
        _gatewayIp = AppSetting.GatewayIpBytes;
        _subnetNet = AppSetting.SubnetNetBytes;
        _subnetMask = AppSetting.SubnetMaskBytes;
    }

    public byte[]? ProcessArp(EthernetFrame frame, ArpPacket arp, byte[] localMac)
    {
        if (!arp.IsValid)
        {
            Logger.Instance?.LogDebug("Invalid ARP packet received");
            return null;
        }
        
        _cache.Set(arp.SenderMac, arp.SenderIp);

        if (arp.IsRequest)
        {
            return HandleArpRequest(arp, localMac);
        }

        if (arp.IsReply)
        {
            Logger.Instance?.LogDebug($"ARP Reply: {ByteHelper.MacToString(arp.SenderMac)} is {ByteHelper.IpToString(arp.SenderIp)}");
        }

        return null;
    }

    public byte[]? HandleArpRequest(ArpPacket arp, byte[] localMac)
    {
        
        Logger.Instance?.LogInfo($"ARP Request: Who has {ByteHelper.IpToString(arp.TargetIp)}? Tell {ByteHelper.IpToString(arp.SenderIp)} ({ByteHelper.MacToString(arp.SenderMac)})");

        if (!ByteHelper.IsInSubnet(arp.TargetIp, _subnetNet, _subnetMask))
        {
            Logger.Instance?.LogDebug("    -> Target IP not in subnet, ignoring");
            return null;
        }

        if (ByteHelper.CompareIp(arp.SenderIp, new byte[4]))
        {
            Logger.Instance?.LogDebug("    -> Sender IP is 0.0.0.0 (probe), ignoring");
            return null;
        }

        if (ByteHelper.CompareIp(arp.TargetIp, arp.SenderIp))
        {
            Logger.Instance?.LogDebug("    -> Gratuitous ARP, ignoring");
            return null;
        }

        if (_cache.HasIp(arp.TargetIp))
        {
            Logger.Instance?.LogDebug($"    -> Target IP already in cache, ignoring");
            return null;
        }
        
        Logger.Instance?.LogInfo($"    -> Responding as proxy for {ByteHelper.IpToString(arp.TargetIp)}");

        return BuildArpReply(arp, localMac);
    }

    private byte[] BuildArpReply(ArpPacket request, byte[] localMac)
    {
        Logger.Instance?.LogDebug($"Sending ARP Reply: {ByteHelper.IpToString(request.TargetIp)} is at {ByteHelper.MacToString(localMac)}");

        var buffer = new byte[EthernetFrame.HeaderLength + ArpPacket.Length];

        EthernetFrame.Build(buffer, request.SenderMac, localMac, EthernetFrame.TypeArp,
            ReadOnlySpan<byte>.Empty);
        ArpPacket.Build(buffer.AsSpan(EthernetFrame.HeaderLength),
            ArpPacket.OpcodeReply,
            localMac, request.TargetIp,
            request.SenderMac, request.SenderIp);
        
        return buffer;
    }

    public static byte[] BuildArpRequest(byte[] localMac, byte[] localIp, byte[] targetIp)
    {
        var buffer = new byte[EthernetFrame.HeaderLength + ArpPacket.Length];

        EthernetFrame.Build(buffer, ByteHelper.BroadcastMac, localMac, EthernetFrame.TypeArp,
            ReadOnlySpan<byte>.Empty);
        ArpPacket.Build(buffer.AsSpan(EthernetFrame.HeaderLength),
            ArpPacket.OpcodeRequest,
            localMac, localIp,ByteHelper.ZeroMac, targetIp);

        return buffer;
    }
    
}