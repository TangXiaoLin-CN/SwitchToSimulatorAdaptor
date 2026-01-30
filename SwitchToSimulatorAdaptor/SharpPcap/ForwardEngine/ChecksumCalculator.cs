using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

public static class ChecksumCalculator
{
    public static ushort Calculate(ReadOnlySpan<byte> data)
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

        while (sum > 0xFFFF)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
    }

    public static ushort CalculateUdp(ReadOnlySpan<byte> srcIp, ReadOnlySpan<byte> dstIp,
        ReadOnlySpan<byte> udpHeader, ReadOnlySpan<byte> payload)
    {
        uint sum = 0;
        int udpLength = udpHeader.Length + payload.Length;

        sum += BinaryPrimitives.ReadUInt16BigEndian(srcIp);
        sum += BinaryPrimitives.ReadUInt16BigEndian(srcIp.Slice(2));
        
        sum += BinaryPrimitives.ReadUInt16BigEndian(dstIp);
        sum += BinaryPrimitives.ReadUInt16BigEndian(dstIp.Slice(2));

        sum += 17;
        sum += (uint)udpLength;

        sum += SumWords(udpHeader);
        sum += SumWords(payload);

        while (sum > 0xFFFF)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        var result = (ushort)~sum;
        return result == 0 ? (ushort)0xFFFF : result;
    }

    public static ushort CalculateTcp(ReadOnlySpan<byte> srcIp, ReadOnlySpan<byte> dstIp,
        ReadOnlySpan<byte> tcpHeader, ReadOnlySpan<byte> payload)
    {
        uint sum = 0;
        int tcpLength = tcpHeader.Length + payload.Length;
        
        sum += BinaryPrimitives.ReadUInt16BigEndian(srcIp);
        sum += BinaryPrimitives.ReadUInt16BigEndian(srcIp.Slice(2));
        
        sum += BinaryPrimitives.ReadUInt16BigEndian(dstIp);
        sum += BinaryPrimitives.ReadUInt16BigEndian(dstIp.Slice(2));
        
        sum += 6;
        sum += (uint)tcpLength;
        
        sum += SumWords(tcpHeader);
        sum += SumWords(payload);

        while (sum > 0xFFFF)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Verify(ReadOnlySpan<byte> data)
    {
        return Calculate(data) == 0;
    }

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