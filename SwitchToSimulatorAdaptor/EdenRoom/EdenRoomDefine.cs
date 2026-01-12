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

public struct IPv4Address
{
    public byte A, B, C, D;

    public IPv4Address(byte a, byte b, byte c, byte d)
    {
        A = a; B = b; C = c ; D = d;
    }

    public override string ToString() => $"{A}.{B}.{C}.{D}";
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