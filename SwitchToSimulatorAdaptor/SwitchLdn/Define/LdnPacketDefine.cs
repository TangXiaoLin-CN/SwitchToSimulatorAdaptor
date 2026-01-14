using System.Runtime.InteropServices;
using K4os.Compression.LZ4;
using SwitchToSimulatorAdaptor.Common;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.SwitchLdn;

/// <summary>
/// LDN 包类型
/// </summary>
public enum LdnPacketType : byte
{
    Scan = 0, // 扫描请求
    ScanResponse = 1, // 扫描响应
    Connect = 2, // 连接请求
    ConnectResponse = 3, // 连接响应
    Disconnect = 4, // 断开连接
    SyncNetwork = 5, // 同步网络信息
    DisconnectClient = 6, // 断开客户端
}

public class LdnHeader
{
    public const uint MAGIC = 0x11451400;
    public const int HEADER_SIZE = 12;

    public uint Magic { get; set; }
    public LdnPacketType Type { get; set; }
    public bool IsCompressed { get; set; }
    public ushort Length { get; set; } // 压缩后的长度 （或原始长度）
    public ushort DecompressLength { get; set; } // 解压后的长度
    public byte[] Payload { get; set; } = [];
    
    public bool IsValid => Magic == MAGIC;

    public static LdnHeader? Parse(byte[] data)
    {
        if (data.Length < HEADER_SIZE) return null;

        var header = new LdnHeader()
        {
            // 小端序读取
            Magic = BitConverter.ToUInt32(data, 0),
            Type = (LdnPacketType)data[4],
            IsCompressed = data[5] == 1,
            Length = BitConverter.ToUInt16(data, 6),
            DecompressLength = BitConverter.ToUInt16(data, 8)
        };

        if (header.Magic != MAGIC) return null;

        // 提取 payload
        if (data.Length > HEADER_SIZE)
        {
            int payloadLen = Math.Min(header.Length, data.Length - HEADER_SIZE);
            header.Payload = new byte[payloadLen];
            Array.Copy(data, HEADER_SIZE, header.Payload, 0, payloadLen);

            // 如果压缩了， 解压
            if (header.IsCompressed && header.Payload.Length > 0)
            {
                header.Payload = Lz4Decompress(header.Payload, header.DecompressLength);
            }
        }

        return header;
    }

    public byte[] ToBytes(byte[] payload, bool compress = false)
    {
        byte[] finalPayload = payload;
        ushort decompressLen = (ushort)payload.Length;

        if (compress && payload.Length > 0)
        {
            finalPayload = Lz4Compress(payload);
        }

        var data = new byte[HEADER_SIZE + finalPayload.Length];

        // Magic （小端序）
        BitConverter.GetBytes(MAGIC).CopyTo(data, 0);

        // Type
        data[4] = (byte)Type;

        // Compressed
        data[5] = compress ? (byte)1 : (byte)0;

        // Length （小端序）
        BitConverter.GetBytes((ushort)finalPayload.Length).CopyTo(data, 6);

        // DecompressLength
        BitConverter.GetBytes(decompressLen).CopyTo(data, 8);

        // Reserved
        data[10] = 0;
        data[11] = 0;

        // Payload
        finalPayload.CopyTo(data, HEADER_SIZE);

        return data;
    }

    // LZ4 压缩 / 解压 （使用 K4os.Compression.LZ4 库）
    private static byte[] Lz4Compress(byte[] data)
    {
        return LZ4Pickler.Pickle(data);
    }

    private static byte[] Lz4Decompress(byte[] data, ushort decompressLen)
    {
        return LZ4Pickler.Unpickle(data);
    }
}

public class ScanPacket
{
    // Scan 包没有 payload, 只有 LDN 头
    public static byte[] Build()
    {
        var header = new LdnHeader() { Type = LdnPacketType.Scan };

        return header.ToBytes(Array.Empty<byte>());
    }
}

public class ScanResponsePacket
{
    public NetworkInfo NetworkInfo { get; set; }

    public static ScanResponsePacket Parse(byte[] payload)
    {
        return new ScanResponsePacket()
        {
            NetworkInfo = StructHelper.BytesToStruct<NetworkInfo>(payload)
        };
    }

    public byte[] Build()
    {
        var header = new LdnHeader() { Type = LdnPacketType.ScanResponse };
        var payload = StructHelper.StructToBytes(NetworkInfo);
        return header.ToBytes(payload, false); // ScanResponse 不通常不压缩
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ConnectRequest
{
    public uint SecurityMode;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] ClientMac;

    public IPv4Address ClientIp;
    public NodeInfo NodeInfo;

    // 安全数据（如果 SecurityMode != 0)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] SecurityData;
}

public class ConnectPacket
{
    public ConnectRequest Request { get; set; }

    public static ConnectPacket Parse(byte[] payload)
    {
        return new ConnectPacket()
        {
            Request = StructHelper.BytesToStruct<ConnectRequest>(payload)
        };
    }

    public byte[] Build()
    {
        var header = new LdnHeader() { Type = LdnPacketType.Connect };
        var payload = StructHelper.StructToBytes(Request);
        return header.ToBytes(payload);
    }
}

public enum ConnectResult : byte
{
    Success = 0,
    ConnectionFailed = 1,
    InvalidNetworkId = 2,
    InvalidPassword = 3,
    RejectedByUser = 4,
    RoomFull = 5,
    NodeCountLimitReached = 6,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ConnectResponseData
{
    public ConnectResult Result; // 连接结果
    public byte NodeId; // 分配给客户端的 NodeId

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] Reserved;
}

public class ConnectResponsePacket
{
    public ConnectResponseData Response { get; set; }

    public byte[] Build()
    {
        var header = new LdnHeader() { Type = LdnPacketType.ConnectResponse };
        var payload = StructHelper.StructToBytes(Response);
        return header.ToBytes(payload);
    }
}

public class SyncNetworkPacket
{
    public NetworkInfo NetworkInfo { get; set; }

    public static SyncNetworkPacket Parse(byte[] payload)
    {
        // SyncNetwork 通常会被压缩
        return new SyncNetworkPacket
        {
            NetworkInfo = StructHelper.BytesToStruct<NetworkInfo>(payload)
        };
    }

    public byte[] Build(bool compress = true)
    {
        var header = new LdnHeader { Type = LdnPacketType.SyncNetwork };
        var payload = StructHelper.StructToBytes(NetworkInfo);
        return header.ToBytes(payload, compress);
    }
}

public enum DisconnectReason : uint
{
    Node = 0,
    UserRequest = 1,
    MemberLeft = 2,
    NetworkDestroyed = 3,
    NetworkFailed = 4,
    GameFinished = 5,
}

public class DisconnectPacket
{
    public DisconnectReason Reason { get; set; }

    public byte[] Build()
    {
        var header = new LdnHeader() { Type = LdnPacketType.Disconnect };
        var payload = BitConverter.GetBytes((uint)Reason);
        return header.ToBytes(payload);
    }
}