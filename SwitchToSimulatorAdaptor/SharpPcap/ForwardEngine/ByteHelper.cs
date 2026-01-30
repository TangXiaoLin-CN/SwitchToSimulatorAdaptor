using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

public static class ByteHelper
{
    // 字节序读取
    
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
    
    
    // 字节序写入
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt8(Span<byte> destination, int offset, byte value)
        => destination[offset] = value;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt16BigEndian(Span<byte> destination, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(offset), value);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32BigEndian(Span<byte> destination, int offset, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset), value);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt16LittleEndian(Span<byte> destination, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset), value);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32LittleEndian(Span<byte> destination, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset), value);
    
    
    // IP 地址操作

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CompareIp(ReadOnlySpan<byte> ip1, ReadOnlySpan<byte> ip2)
        => ip1.Slice(0, 4).SequenceEqual(ip2.Slice(0, 4));
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CopyIp(Span<byte> dest, ReadOnlySpan<byte> src)
        => src.Slice(0, 4).CopyTo(dest);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint IpToUint(ReadOnlySpan<byte> ip)
        => BitConverter.ToUInt32(ip);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] UintToIp(uint val)
        => BitConverter.GetBytes(val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInSubnet(ReadOnlySpan<byte> ip, ReadOnlySpan<byte> net, ReadOnlySpan<byte> mask)
    {
        var ipVal = IpToUint(ip);
        var netVal = IpToUint(net);
        var maskVal = IpToUint(mask);
        return (ipVal & maskVal) == netVal;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBroadcast(ReadOnlySpan<byte> ip, ReadOnlySpan<byte> net, ReadOnlySpan<byte> mask)
    {
        var ipVal = IpToUint(ip);
        var netVal = IpToUint(net);
        var maskVal = IpToUint(mask);
        return (netVal | ~maskVal) == ipVal;
    }

    public static string IpToString(ReadOnlySpan<byte> ip)
        => $"{ip[0]}.{ip[1]}.{ip[2]}.{ip[3]}";

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
    
    
    // MAC 地址操作

    public static readonly byte[] BroadcastMac = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
    public static readonly byte[] ZeroMac = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CompareMac(ReadOnlySpan<byte> mac1, ReadOnlySpan<byte> mac2)
        => mac1.Slice(0, 6).SequenceEqual(mac2.Slice(0, 6));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CopyMac(Span<byte> dest, ReadOnlySpan<byte> src)
        => src.Slice(0, 6).CopyTo(dest);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong MacToUlong(ReadOnlySpan<byte> mac)
    {
        return ((ulong)mac[0] << 40) | ((ulong)mac[1] << 32) | ((ulong)mac[2] << 24) |
               ((ulong)mac[3] << 16) | ((ulong)mac[4] << 8) | mac[5];
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBroadcastMac(ReadOnlySpan<byte> mac)
        => mac[0] == 0xFF && mac[1] == 0xFF && mac[2] == 0xFF && 
           mac[3] == 0xFF && mac[4] == 0xFF && mac[5] == 0xFF;

    public static string MacToString(ReadOnlySpan<byte> mac)
        => $"{mac[0]:X2}:{mac[1]:X2}:{mac[2]:X2}:{mac[3]:X2}:{mac[4]:X2}:{mac[5]:X2}";
    

    // 其他工具方法

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Min(int a, int b) => a < b ? a : b;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Max(int a, int b) => a > b ? a : b;

    public static string ToHexString(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length * 2);
        foreach (var b in data)
        {
            sb.Append(b.ToString("X2"));
        }

        return sb.ToString();
    }
}