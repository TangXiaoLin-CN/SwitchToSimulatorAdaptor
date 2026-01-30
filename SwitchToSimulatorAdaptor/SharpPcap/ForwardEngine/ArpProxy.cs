using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

/// <summary>
/// ARP 代理
/// 为子网内未知 IP 代答 ARP 请求
/// 来源: switch-lan-play/src/arp.c
/// </summary>
public class ArpProxy
{
    private readonly ArpCache _cache;
    private readonly byte[] _gatewayIp;
    private readonly byte[] _subnetNet;
    private readonly byte[] _subnetMask;

    /// <summary>
    /// ARP 缓存
    /// </summary>
    public ArpCache Cache => _cache;

    /// <summary>
    /// 创建 ARP 代理
    /// </summary>
    public ArpProxy(ArpCache cache)
    {
        _cache = cache;
        _gatewayIp = AppSetting.GatewayIpBytes;
        _subnetNet = AppSetting.SubnetNetBytes;
        _subnetMask = AppSetting.SubnetMaskBytes;
    }

    /// <summary>
    /// 处理 ARP 包
    /// </summary>
    /// <param name="frame">以太网帧</param>
    /// <param name="arp">ARP 包</param>
    /// <param name="localMac">本地 MAC 地址</param>
    /// <returns>需要发送的响应包，如果不需要响应则返回 null</returns>
    public byte[]? ProcessArp(EthernetFrame frame, ArpPacket arp, byte[] localMac)
    {
        // 验证 ARP 包格式
        if (!arp.IsValid)
        {
            return null;
        }

        // 更新 ARP 缓存（记录发送方的 MAC/IP 映射）
        _cache.Set(arp.SenderMac, arp.SenderIp);

        // 处理 ARP 请求
        if (arp.IsRequest)
        {
            return HandleArpRequest(arp, localMac);
        }

        // ARP 应答只需要更新缓存，不需要响应
        if (arp.IsReply)
        {
        }

        return null;
    }

    /// <summary>
    /// 处理 ARP 请求
    /// </summary>
    private byte[]? HandleArpRequest(ArpPacket arp, byte[] localMac)
    {

        // 检查目标 IP 是否在子网内
        if (!ByteHelper.IsInSubnet(arp.TargetIp, _subnetNet, _subnetMask))
        {
            return null;
        }

        // 忽略以下情况：
        // 1. 发送方 IP 为 0.0.0.0（探测包）
        // 2. 目标 IP 等于发送方 IP（免费 ARP）
        // 3. 目标 IP 已在 ARP 缓存中（已知主机）
        if (ByteHelper.CompareIp(arp.SenderIp, new byte[4]))
        {
            return null;
        }

        if (ByteHelper.CompareIp(arp.TargetIp, arp.SenderIp))
        {
            return null;
        }

        if (_cache.HasIp(arp.TargetIp))
        {
            return null;
        }


        // 代答 ARP 请求
        return BuildArpReply(arp, localMac);
    }

    /// <summary>
    /// 构建 ARP 应答
    /// </summary>
    private byte[] BuildArpReply(ArpPacket request, byte[] localMac)
    {

        // 构建以太网帧 + ARP 包
        var buffer = new byte[EthernetFrame.HeaderLength + ArpPacket.Length];

        // 以太网头部
        EthernetFrame.Build(buffer, request.SenderMac, localMac, EthernetFrame.TypeArp,
            ReadOnlySpan<byte>.Empty);

        // ARP 应答
        ArpPacket.Build(buffer.AsSpan(EthernetFrame.HeaderLength),
            ArpPacket.OpcodeReply,
            localMac, request.TargetIp,          // 发送方：本机 MAC + 请求的目标 IP
            request.SenderMac, request.SenderIp); // 目标：请求者

        return buffer;
    }

    /// <summary>
    /// 构建 ARP 请求包
    /// </summary>
    /// <param name="localMac">本地 MAC</param>
    /// <param name="localIp">本地 IP</param>
    /// <param name="targetIp">目标 IP</param>
    /// <returns>完整的以太网帧</returns>
    public static byte[] BuildArpRequest(byte[] localMac, byte[] localIp, byte[] targetIp)
    {
        var buffer = new byte[EthernetFrame.HeaderLength + ArpPacket.Length];

        // 以太网头部（广播）
        EthernetFrame.Build(buffer, ByteHelper.BroadcastMac, localMac, EthernetFrame.TypeArp,
            ReadOnlySpan<byte>.Empty);

        // ARP 请求
        ArpPacket.Build(buffer.AsSpan(EthernetFrame.HeaderLength),
            ArpPacket.OpcodeRequest,
            localMac, localIp,
            ByteHelper.ZeroMac, targetIp);

        return buffer;
    }
}