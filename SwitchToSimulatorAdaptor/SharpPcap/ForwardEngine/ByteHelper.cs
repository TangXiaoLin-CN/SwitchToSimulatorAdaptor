using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

/// <summary>
/// 字节操作辅助类
/// 来源: switch-lan-play/src/helper.h
/// </summary>
public static class ByteHelper
{
    #region 网络字节序读取

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ReadUInt8(ReadOnlySpan<byte> source, int offset)
        => source[offset];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> source, int offset)
        => BinaryPrimitives.ReadUInt16BigEndian(source.Slice(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadUInt32BigEndian(ReadOnlySpan<byte> source, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(source.Slice(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> source, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> source, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset));

    #endregion

    #region 网络字节序写入

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt8(Span<byte> dest, int offset, byte value)
        => dest[offset] = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt16BigEndian(Span<byte> dest, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16BigEndian(dest.Slice(offset), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32BigEndian(Span<byte> dest, int offset, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(dest.Slice(offset), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt16LittleEndian(Span<byte> dest, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(offset), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32LittleEndian(Span<byte> dest, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(offset), value);

    #endregion

    #region IP 地址操作

    /// <summary>
    /// 比较两个 IP 地址是否相等
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CompareIp(ReadOnlySpan<byte> ip1, ReadOnlySpan<byte> ip2)
        => ip1.Slice(0, 4).SequenceEqual(ip2.Slice(0, 4));

    /// <summary>
    /// 复制 IP 地址
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CopyIp(Span<byte> dest, ReadOnlySpan<byte> src)
        => src.Slice(0, 4).CopyTo(dest);

    /// <summary>
    /// IP 地址转换为 uint
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint IpToUint(ReadOnlySpan<byte> ip)
        => BitConverter.ToUInt32(ip);

    /// <summary>
    /// uint 转换为 IP 地址
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] UintToIp(uint val)
        => BitConverter.GetBytes(val);

    /// <summary>
    /// 检查 IP 是否在子网内
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInSubnet(ReadOnlySpan<byte> ip, ReadOnlySpan<byte> net, ReadOnlySpan<byte> mask)
    {
        var ipVal = IpToUint(ip);
        var netVal = IpToUint(net);
        var maskVal = IpToUint(mask);
        return (ipVal & maskVal) == netVal;
    }

    /// <summary>
    /// 检查 IP 是否为广播地址
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBroadcast(ReadOnlySpan<byte> ip, ReadOnlySpan<byte> net, ReadOnlySpan<byte> mask)
    {
        var ipVal = IpToUint(ip);
        var netVal = IpToUint(net);
        var maskVal = IpToUint(mask);
        return (netVal | ~maskVal) == ipVal;
    }

    /// <summary>
    /// IP 地址转换为字符串
    /// </summary>
    public static string IpToString(ReadOnlySpan<byte> ip)
        => $"{ip[0]}.{ip[1]}.{ip[2]}.{ip[3]}";

    /// <summary>
    /// 字符串转换为 IP 地址
    /// </summary>
    public static byte[] StringToIp(string ipStr)
    {
        var parts = ipStr.Split('.');
        if (parts.Length != 4)
            throw new ArgumentException("Invalid IP address format", nameof(ipStr));

        return new byte[]
        {
            byte.Parse(parts[0]),
            byte.Parse(parts[1]),
            byte.Parse(parts[2]),
            byte.Parse(parts[3])
        };
    }

    #endregion

    #region MAC 地址操作

    /// <summary>
    /// 比较两个 MAC 地址是否相等
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CompareMac(ReadOnlySpan<byte> mac1, ReadOnlySpan<byte> mac2)
        => mac1.Slice(0, 6).SequenceEqual(mac2.Slice(0, 6));

    /// <summary>
    /// 复制 MAC 地址
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CopyMac(Span<byte> dest, ReadOnlySpan<byte> src)
        => src.Slice(0, 6).CopyTo(dest);

    /// <summary>
    /// MAC 地址转换为 ulong
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong MacToUlong(ReadOnlySpan<byte> mac)
    {
        return ((ulong)mac[0] << 40) | ((ulong)mac[1] << 32) | ((ulong)mac[2] << 24) |
               ((ulong)mac[3] << 16) | ((ulong)mac[4] << 8) | mac[5];
    }

    /// <summary>
    /// MAC 地址转换为字符串
    /// </summary>
    public static string MacToString(ReadOnlySpan<byte> mac)
        => $"{mac[0]:X2}:{mac[1]:X2}:{mac[2]:X2}:{mac[3]:X2}:{mac[4]:X2}:{mac[5]:X2}";

    /// <summary>
    /// 检查是否为广播 MAC 地址
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBroadcastMac(ReadOnlySpan<byte> mac)
        => mac[0] == 0xFF && mac[1] == 0xFF && mac[2] == 0xFF &&
           mac[3] == 0xFF && mac[4] == 0xFF && mac[5] == 0xFF;

    /// <summary>
    /// 广播 MAC 地址
    /// </summary>
    public static readonly byte[] BroadcastMac = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

    /// <summary>
    /// 零 MAC 地址
    /// </summary>
    public static readonly byte[] ZeroMac = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

    #endregion

    #region 其他工具方法

    /// <summary>
    /// 返回两个值中较小的一个
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Min(int a, int b) => a < b ? a : b;

    /// <summary>
    /// 返回两个值中较大的一个
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Max(int a, int b) => a > b ? a : b;

    /// <summary>
    /// 将字节数组转换为十六进制字符串
    /// </summary>
    public static string ToHexString(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length * 2);
        foreach (var b in data)
        {
            sb.Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    #endregion
}
