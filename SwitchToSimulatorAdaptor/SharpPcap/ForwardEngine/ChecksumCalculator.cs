using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

/// <summary>
/// 校验和计算器
/// 来源: switch-lan-play/src/ipv4/ipv4.c (calc_checksum)
/// </summary>
public static class ChecksumCalculator
{
    /// <summary>
    /// 计算 IP/ICMP 校验和
    /// </summary>
    /// <param name="data">数据</param>
    /// <returns>校验和（网络字节序）</returns>
    public static ushort Calculate(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        int i = 0;
        int len = data.Length;

        // 按 16 位累加
        while (i + 1 < len)
        {
            sum += BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i));
            i += 2;
        }

        // 如果长度为奇数，补充最后一个字节
        if (i < len)
        {
            sum += (uint)(data[i] << 8);
        }

        // 将高 16 位加到低 16 位
        while (sum > 0xFFFF)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
    }

    /// <summary>
    /// 计算 UDP 校验和（包含伪首部）
    /// </summary>
    /// <param name="srcIp">源 IP 地址</param>
    /// <param name="dstIp">目标 IP 地址</param>
    /// <param name="udpHeader">UDP 头部</param>
    /// <param name="payload">UDP 负载</param>
    /// <returns>校验和（网络字节序）</returns>
    public static ushort CalculateUdp(ReadOnlySpan<byte> srcIp, ReadOnlySpan<byte> dstIp,
        ReadOnlySpan<byte> udpHeader, ReadOnlySpan<byte> payload)
    {
        uint sum = 0;
        int udpLength = udpHeader.Length + payload.Length;

        // 伪首部: 源 IP
        sum += BinaryPrimitives.ReadUInt16BigEndian(srcIp);
        sum += BinaryPrimitives.ReadUInt16BigEndian(srcIp.Slice(2));

        // 伪首部: 目标 IP
        sum += BinaryPrimitives.ReadUInt16BigEndian(dstIp);
        sum += BinaryPrimitives.ReadUInt16BigEndian(dstIp.Slice(2));

        // 伪首部: 协议 (UDP = 17) 和长度
        sum += 17;
        sum += (ushort)udpLength;

        // UDP 头部
        sum += SumWords(udpHeader);

        // UDP 负载
        sum += SumWords(payload);

        // 将高 16 位加到低 16 位
        while (sum > 0xFFFF)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        var result = (ushort)~sum;
        // UDP 校验和为 0 时应设为 0xFFFF
        return result == 0 ? (ushort)0xFFFF : result;
    }

    /// <summary>
    /// 计算 TCP 校验和（包含伪首部）
    /// </summary>
    /// <param name="srcIp">源 IP 地址</param>
    /// <param name="dstIp">目标 IP 地址</param>
    /// <param name="tcpHeader">TCP 头部</param>
    /// <param name="payload">TCP 负载</param>
    /// <returns>校验和（网络字节序）</returns>
    public static ushort CalculateTcp(ReadOnlySpan<byte> srcIp, ReadOnlySpan<byte> dstIp,
        ReadOnlySpan<byte> tcpHeader, ReadOnlySpan<byte> payload)
    {
        uint sum = 0;
        int tcpLength = tcpHeader.Length + payload.Length;

        // 伪首部: 源 IP
        sum += BinaryPrimitives.ReadUInt16BigEndian(srcIp);
        sum += BinaryPrimitives.ReadUInt16BigEndian(srcIp.Slice(2));

        // 伪首部: 目标 IP
        sum += BinaryPrimitives.ReadUInt16BigEndian(dstIp);
        sum += BinaryPrimitives.ReadUInt16BigEndian(dstIp.Slice(2));

        // 伪首部: 协议 (TCP = 6) 和长度
        sum += 6;
        sum += (ushort)tcpLength;

        // TCP 头部
        sum += SumWords(tcpHeader);

        // TCP 负载
        sum += SumWords(payload);

        // 将高 16 位加到低 16 位
        while (sum > 0xFFFF)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
    }

    /// <summary>
    /// 验证校验和是否正确
    /// </summary>
    /// <param name="data">包含校验和的数据</param>
    /// <returns>校验和是否正确</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Verify(ReadOnlySpan<byte> data)
    {
        return Calculate(data) == 0;
    }

    /// <summary>
    /// 计算数据的 16 位字之和
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint SumWords(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        int i = 0;
        int len = data.Length;

        while (i + 1 < len)
        {
            sum += BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i));
            i += 2;
        }

        if (i < len)
        {
            sum += (uint)(data[i] << 8);
        }

        return sum;
    }
}