using System.Runtime.InteropServices;
using System.Text;
using SwitchToSimulatorAdaptor.Common;

namespace SwitchToSimulatorAdaptor.SwitchLdn;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 480)]
public struct NetworkInfo
{
    // 网络 ID - 用于唯一标识网络 （16 字节）
    // 格式：Intent + 随机数据
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] NetworkId;

    // 主机 MAC 地址 （6 字节）
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] HostMac;

    // 无线网络 SSID (34 字节）
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 34)]
    public Ssid Ssid;

    // 无线配置
    public byte Channel; // Wifi 信道
    public byte LinkLevel; // Wifi 连接强度
    public byte NetworkType; // 网络类型，0 = None, 1 = General, 2 = Ldn

    public byte Padding1;

    // 安全配置 （18 字节）
    public SecurityConfig Security;

    // 参与者配置
    public byte AccepPolicy; // 0 = AcceptAll, 1 = Blacklist, 2 = Whitelist
    public byte MaxParticipants; // 最大参与者数 （1 - 8）
    public byte ParticipantCount; // 当前参与者数

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public byte[] Padding2;

    // 参与者列表 - 8 个 NetInfo （544 字节）
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public NodeInfo[] Participants;

    // 广告数据 （游戏自定义数据）
    public ushort AdvertiseDataLength;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] Padding3;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 384)]
    public byte[] AdvertiseData;

    // 随机数 （8 字节）
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] Random;

    // 辅助方法
    public static NetworkInfo Create(byte maxParticipants = 8)
    {
        var info = new NetworkInfo
        {
            NetworkId = new byte[6],
            HostMac = new byte[6],
            Ssid = new Ssid(),
            MaxParticipants = maxParticipants,
            ParticipantCount = 0,
            Participants = new NodeInfo[8],
            Padding3 = new byte[2],
            AdvertiseData = new byte[384],
            Random = new byte[8]
        };

        // 生成随机 NetworkId
        new Random().NextBytes(info.NetworkId);
        new Random().NextBytes(info.Random);

        // 初始化参与者数组
        for (var i = 0; i < info.Participants.Length; i++)
        {
            info.Participants[i] = new NodeInfo();
        }

        return info;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 34)]
public struct Ssid
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] Name;

    public byte Length;
    public byte Padding;
}

public struct SecurityConfig
{
    public ushort SecurityType; // 0 = None, 1 = 需要密码

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] SecurityParameter;
}

// NodeInfo （68 字节）

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 68)]
public struct NodeInfo
{
    public IPv4Address Ip; // 节点 IP 地址 （4 字节）

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] Mac; // 节点 MAC 地址

    public byte NodeId; // 节点 ID （0 = 主机， 1 - 7 = 客户端）
    public byte IsConnected; // 节点是否已连接 （ 0 / 1）

    // 用户名 （UTF-8, 最大 32 字节）
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] UserName;

    public byte UserNameLength;

    // 本地通信版本
    public ushort LocalCommunicationVersion;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 21)]
    public byte[] Reserved;

    public static NodeInfo Create(IPv4Address ip, byte[] mac, byte nodeId, string userName)
    {
        var node = new NodeInfo
        {
            Ip = ip,
            Mac = mac ?? new byte[6],
            NodeId = nodeId,
            IsConnected = 1,
            UserName = new byte[32],
            LocalCommunicationVersion = 0,
            Reserved = new byte[21]
        };

        // 设置用户名
        var nameBytes = Encoding.UTF8.GetBytes(userName);
        var copyLen = Math.Min(nameBytes.Length, 32);
        Array.Copy(nameBytes, node.UserName, copyLen);
        node.UserNameLength = (byte)copyLen;

        return node;
    }

    public string GetUserName()
    {
        if (UserName == null || UserNameLength == 0) return "";
        return Encoding.UTF8.GetString(UserName, 0, UserNameLength);
    }
}