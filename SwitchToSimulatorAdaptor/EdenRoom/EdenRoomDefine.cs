using SwitchToSimulatorAdaptor.Common;

namespace SwitchToSimulatorAdaptor.EdenRoom;

/// <summary>
/// 房间列表项
/// </summary>
public class RoomListItem
{
    public string Name { get; set; } = "";
    public string ServerAddress { get; set; } = "";
    public ushort Port { get; set; }
    public string Password { get; set; } = "";
    public override string ToString() => $"{Name}({ServerAddress}:{Port})";
}

/// <summary>
/// Eden 网络类型定义
/// </summary>
public enum EdenLDNPacketType : byte
{
    Scan = 0,
    ScanResp = 1,
    Connect = 2,
    SyncNetwork = 3,
    Disconnect = 4,
    DestroyNetwork = 5,
}

public struct EdenLDNPacket
{
    public EdenLDNPacketType Type;
    public IPv4Address LocalIp;
    public IPv4Address RemoteIp;
    public bool Broadcast;
    public byte[] Data;
}


public struct GameInfo
{
    public string Name;
    public ulong Id;
    public string Version;
}

public class Member
{
    public string Username;
    public string Nickname;
    public string DisplayName;
    public string AvatarUrl;
    public IPv4Address FakeIp;
    public GameInfo Game;
}

public struct RoomInformation
{
    public string Name;
    public string Description;
    public uint MemberSlots;
    public ushort Port;
    public GameInfo PreferredGame;
    public string HostUsername;
}

public enum RoomMessageTypes : byte
{
    IdJoinRequest = 1, // Q: 这里的Id是啥？
    IdJoinSuccess,
    IdRoomInformation,
    IdSetGameInfo,
    IdProxyPacket,
    IdLdnPacket,
    IdChatMessage,
    IdNameCollision,
    IdIpCollision,
    IdVersionMismatch,
    IdWrongPassword,
    IdCloseRoom,
    IdRoomIsFull,
    IdStatusMessage,
    IdHostKicked,
    IdHostBanned,
    IdModKick,
    IdModBan, // Q： 这里的Mod又是啥？
    IdModUnban,
    IdModBanListResponse,
    IdModPermissionDenied,
    IdModNoSuchUser,
    IdJoinSuccessAsMod,
}

public enum StatusMessageTypes : byte
{
    IdMemberJoin = 1,
    IdMemberLeave,
    IdMemberKicked,
    IdMemberBanned,
    IdAddressUnbanned,
}

/// <summary>
/// EdenProxyPacket 结构，对应 Eden 的 ProxyPacket
/// 用于在 Eden 房间中转发游戏数据包
/// </summary>
public struct EdenProxyPacket
{
    /// <summary>
    /// 本地端点 （发送者）
    /// </summary>
    public EdenSockAddrIn LocalEndpoint;

    /// <summary>
    /// 远程端点（接收者）
    /// </summary>
    public EdenSockAddrIn RemoteEndpoint;

    /// <summary>
    /// 协议类型（UDP = 3, TCP =2, 对应 Eden 的 Protocol 枚举）
    /// </summary>
    public EdenProtocolType EdenProtocol;

    /// <summary>
    /// 是否广播
    /// </summary>
    public bool Broadcast;

    /// <summary>
    /// 数据 payload （需要 ZSTD 压缩）
    /// </summary>
    public byte[] Data;
}

/// <summary>
/// EdenSockAddrIn 结构，对应 Eden 的 SockAddrIn
/// </summary>
public struct EdenSockAddrIn
{
    /// <summary>
    /// 协议族 （INET = 1)
    /// </summary>
    public AddressFamily Family;

    /// <summary>
    /// IP 地址（4 字节）
    /// </summary>
    public IPv4Address Ip;

    /// <summary>
    /// 端口号（网络字节序）
    /// </summary>
    public ushort Port;
}

/// <summary>
/// 协议类型枚举，对应 Eden 的 Protocol 枚举
/// 注意：Eden 使用的枚举值（不是 IP 协议号）
/// - Unspecified = 0
/// - ICMP = 1
/// - TCP = 2
/// - UDP = 3
/// </summary>
public enum EdenProtocolType : byte
{
    Unspecified = 0,
    ICMP = 1,
    TCP = 2,
    UDP = 3
}

/// <summary>
/// 地址族枚举，对应 Eden 的 Domain
/// </summary>
public enum AddressFamily : byte
{
    Unspecified = 0,
    INET = 1        // IPv4
}

