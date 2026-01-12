using System.Runtime.InteropServices;

namespace SwitchToSimulatorAdaptor.EdenRoom;

public static partial class NativeENet
{
    // ENet 事件类型
    public enum ENetEventType : int
    {
        ENET_EVENT_TYPE_NONE = 0,
        ENET_EVENT_TYPE_CONNECT = 1,
        ENET_EVENT_TYPE_DISCONNECT = 2,
        ENET_EVENT_TYPE_RECEIVE = 3,
        ENET_EVENT_TYPE_TIMEOUT = 4,
    }
        
    // ENet 数据包标志
    [Flags]
    public enum ENetPacketFlag : uint
    {
        ENET_PACKET_FLAG_RELIABLE = (1 << 0),
        ENET_PACKET_FLAG_UNSEQUENCED = (1 << 1),
        ENET_PACKET_FLAG_NO_ALLOCATE = (1 << 2),
        ENET_PACKET_FLAG_UNRELIABLE_FRAGMENT = (1 << 3),
        ENET_PACKET_FLAG_SENT = (1 << 8),
    }
        
    // ENet 地址结构
    [StructLayout(LayoutKind.Sequential)]
    public struct ENetAddress
    {
        public uint host;
        public ushort port;
    }
        
    // ENet 数据包结构（简化版，只包含必要字段）
    [StructLayout(LayoutKind.Sequential)]
    public struct ENetPacket
    {
        public IntPtr referenceCount;
        public ENetPacketFlag flags;
        public IntPtr data;
        public IntPtr dataLength;
        public IntPtr freeCallback;
        public IntPtr userData;
    }
        
    // ENet 事件结构
    // 注意：packet 字段在 ENet 中是 ENetPacket* （指针），不是值类型
    [StructLayout(LayoutKind.Sequential)]
    public struct ENetEvent
    {
        public ENetEventType type;
        public IntPtr peer;         // ENetPeer*（不透明指针）
        public byte channelID;
        public uint data;
        public IntPtr packet;   // ENetPacket* （指针，不是值类型）
    }
}