using System.Net.Sockets;

namespace SwitchToSimulatorAdaptor.EdenRoom;

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

