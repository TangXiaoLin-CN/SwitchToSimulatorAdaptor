using System.Runtime.InteropServices;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.EdenRoom;

public class NativeENetHost : IDisposable
{
    private IntPtr _host;
    private bool _disposed = false;
    private readonly List<IntPtr> _peers = new();

    public IntPtr Handle => _host;
    public bool IsValid => _host != IntPtr.Zero;

    private NativeENetHost(IntPtr host)
    {
        _host = host;
    }
    
    /// <summary>
    /// 创建服务器主机
    /// </summary>
    public static NativeENetHost CreateServer(string host, ushort port, int maxConnections, int channelLimit)
    {
        NativeENet.ENetAddress address = default;
        if (string.IsNullOrEmpty(host) || host == "0.0.0.0")
        {
            address.host = NativeENet.ENET_HOST_ANY;
        }
        else
        {
            int result = NativeENet.enet_address_set_host(ref address, host);
            if (result != 0)
            {
                throw new Exception($"Failed to set host address: {host}");
            }
        }

        address.port = port;

        // 分配地址结构的内存
        IntPtr addressPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeENet.ENetAddress>());
        Marshal.StructureToPtr(address, addressPtr, false);
        
        IntPtr hostPtr = NativeENet.enet_host_create(addressPtr, new IntPtr(maxConnections), new IntPtr(channelLimit), 0, 0);
        
        Marshal.FreeHGlobal(addressPtr);

        if (hostPtr == IntPtr.Zero)
        {
            throw new Exception("Failed to create ENet server host");
        }

        return new NativeENetHost(hostPtr);
    }

    /// <summary>
    /// 创建客户端主机
    /// </summary>
    public static NativeENetHost CreateClient(int channelLimit)
    {
        IntPtr hostPtr = NativeENet.enet_host_create(
        IntPtr.Zero,
        new IntPtr(1),
        new IntPtr(channelLimit),
        0,
        0
            );

        if (hostPtr == IntPtr.Zero)
        {
            throw new Exception("Failed to create ENet client host");
        }

        return new NativeENetHost(hostPtr);
    }

    /// <summary>
    /// 连接到服务器
    /// </summary>
    public IntPtr Connect(string host, ushort port, int channelCount, uint data = 0)
    {
        if (_host == IntPtr.Zero) throw new InvalidOperationException("Host is not initialized");
        
        NativeENet.ENetAddress address = default;
        int result = NativeENet.enet_address_set_host(ref address, host);
        if (result != 0)
        {
            throw new Exception($"Failed to set host address: {host}");
        }
        address.port = port;
        
        IntPtr peer = NativeENet.enet_host_connect(_host, ref address, new IntPtr(channelCount), data);
        if (peer != IntPtr.Zero)
        {
            _peers.Add(peer);
        }
        return peer;
    }

    /// <summary>
    /// 服务事件（阻塞，直到有事件或超时）
    /// </summary>
    public bool Service(out NativeENetEvent event_, int timeout)
    {
        event_ = default;
        if (_host == IntPtr.Zero) return false;
        
        NativeENet.ENetEvent nativeEvent = default;
        // 将 int timeout 转换为 uint （ENet API 要求）
        uint timeoutUint = timeout >= 0 ? (uint)timeout : 0;
        int result = NativeENet.enet_host_service(_host, ref nativeEvent, timeoutUint);
        
        if (result > 0)
        {
            event_ = new NativeENetEvent(nativeEvent);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 检查事件（非阻塞）
    /// </summary>
    public bool CheckEvents(out NativeENetEvent event_)
    {
        event_ = default;
        if (_host == IntPtr.Zero) return false;

        NativeENet.ENetEvent nativeEvent = default;
        int result = NativeENet.enet_host_check_events(_host, ref nativeEvent);

        if (result > 0)
        {
            event_ = new NativeENetEvent(nativeEvent);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 刷新主机（发送所有待发送的数据包）
    /// </summary>
    public void Flush()
    {
        if (_host != IntPtr.Zero)
        {
            NativeENet.enet_host_flush(_host);
        }
    }

    /// <summary>
    /// 广播数据包
    /// </summary>
    public void Broadcast(byte channelID, byte[] data,
        NativeENet.ENetPacketFlag flags = NativeENet.ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE)
    {
        if (_host == IntPtr.Zero || data == null || data.Length == 0) return;

        IntPtr packet = CreatePacket(data, flags);
        if (packet != IntPtr.Zero)
        {
            NativeENet.enet_host_broadcast(_host, channelID, packet);
        }
    }

    /// <summary>
    /// 创建数据包
    /// </summary>
    public IntPtr CreatePacket(byte[] data,
        NativeENet.ENetPacketFlag flags = NativeENet.ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE)
    {
        if (data == null || data.Length == 0) return IntPtr.Zero;

        IntPtr dataPtr = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, dataPtr, data.Length);

        IntPtr packet = NativeENet.enet_packet_create(dataPtr, new IntPtr(data.Length), flags);
        
        // 注意： ENet 会复制数据，所以我们可以立即释放原始内存
        Marshal.FreeHGlobal(dataPtr);
        
        return packet;
    }

    /// <summary>
    /// 销毁数据包
    /// </summary>
    public static void DestroyPacket(IntPtr packet)
    {
        if (packet != IntPtr.Zero)
        {
            NativeENet.enet_packet_destroy(packet);
        }
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_host != IntPtr.Zero)
            {
                // 断开所有连接
                foreach (var peer in _peers)
                {
                    if (peer != IntPtr.Zero)
                    {
                        NativeENet.enet_peer_disconnect_now(peer, 0);
                    }
                }
                _peers.Clear();
                
                // 销毁主机
                NativeENet.enet_host_destroy(_host);
                _host = IntPtr.Zero;
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// ENet 地址的包装类
/// </summary>
public struct NativeENetAddress
{
    private NativeENet.ENetAddress _address;

    public uint Host
    {
        get => _address.host;
        set => _address.host = value;
    }
    
    public ushort Port
    {
        get => _address.port;
        set => _address.port = value;
    }

    public void SetHost(string hostName)
    {
        if (string.IsNullOrEmpty(hostName))
        {
            _address.host = NativeENet.ENET_HOST_ANY;
            return;
        }
        
        int result = NativeENet.enet_address_set_host(ref _address, hostName);
        if (result != 0)
        {
            throw new Exception($"Failed to set host address: {hostName}");
        }
    }

    public NativeENet.ENetAddress ToNative()
    {
        return _address;
    }
}

/// <summary>
/// ENet 事件的包装类
/// </summary>
public struct NativeENetEvent
{
    private readonly NativeENet.ENetEvent _nativeEvent;

    public NativeENet.ENetEventType Type => _nativeEvent.type;
    public IntPtr Peer => _nativeEvent.peer;
    public byte ChannelID => _nativeEvent.channelID;
    public uint Data => _nativeEvent.data;
    
    public IntPtr PacketData
    {
        get
        {
            var packet = GetPacket();
            return packet.data;
        }
    }

    public IntPtr PacketDataLength
    {
        get
        {
            var packet = GetPacket();
            return packet.dataLength;
        }
    }
    
    // 数据包指针 （ENetEvent.packet 是指向 ENetPacket 的指针）
    private IntPtr PacketPtr => _nativeEvent.packet;

    public NativeENetEvent(NativeENet.ENetEvent nativeEvent)
    {
        _nativeEvent = nativeEvent;
    }
    
    // 从数据包指针读取数据包结构
    private NativeENet.ENetPacket GetPacket()
    {
        if (PacketPtr == IntPtr.Zero) return default;
        return Marshal.PtrToStructure<NativeENet.ENetPacket>(PacketPtr);
    }

    /// <summary>
    /// 获取数据包数据
    /// </summary>
    public byte[] GetPacketData()
    {
        if (Type != NativeENet.ENetEventType.ENET_EVENT_TYPE_RECEIVE)
        {
            Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketData: Not a RECEIVE event, type = {Type}]");
        }
        
        Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketPtr = 0x{PacketPtr.ToInt64():X}, Peer = 0x {Peer.ToInt64():X}");

        if (PacketPtr == IntPtr.Zero)
        {
            Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketData: PacketPtr is zero");
            return Array.Empty<byte>();
        }

        var packet = GetPacket();
        Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketData: Packet.data = 0x{packet.data.ToInt64():X}, Packet.dataLength = 0x{packet.dataLength.ToInt64():X} (={packet.dataLength})");

        if (packet.data == IntPtr.Zero)
        {
            Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketData: Packet.data is zero");
            return Array.Empty<byte>();
        }
        
        // 从 dataLength 字段获取长度（IntPtr 在 64 位系统上是 64 位）
        long length = packet.dataLength.ToInt64();
        Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketData: Packet.dataLength = {length} (0x{packet.dataLength.ToInt64():X})");

        if (length <= 0)
        {
            Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketData: Invalid length: {length}");
            return Array.Empty<byte>();
        }

        if (length > int.MaxValue)
        {
            Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketData: Length is too large: {length}");
            return Array.Empty<byte>();
        }
        
        byte[] data = new byte[(int)length];
        Marshal.Copy(packet.data, data, 0, (int)length);
        
        // 记录前几个字节用于调试
        if (data.Length > 0)
        {
            string hex = BitConverter.ToString(data, 0, Math.Min(16, data.Length));
            Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketData: Copied {data.Length} bytes, first bytes: {hex}");
        }

        return data;
    }

    /// <summary>
    /// 获取数据包长度
    /// </summary>
    public int GetPacketDataLength()
    {
        if (Type != NativeENet.ENetEventType.ENET_EVENT_TYPE_RECEIVE)
        {
            Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketDataLength: Not a RECEIVE event, type = {Type}]");
            return 0;
        }

        if (PacketPtr == IntPtr.Zero)
        {
            Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketDataLength: PacketPtr is zero");
            return 0;
        }

        var packet = GetPacket();
        if (packet.data == IntPtr.Zero)
        {
            Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketDataLength: Packet.data is zero");
            return 0;
        }

        long length = packet.dataLength.ToInt64();
        Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketDataLength: Packet.dataLength = {length} (0x{packet.dataLength.ToInt64():X}), dataPtr = 0x{packet.data.ToInt64():X}");

        if (length <= 0)
        {
            Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketDataLength: Invalid length: {length}");
            return 0;
        }

        if (length > int.MaxValue)
        {
            Logger.Instance?.LogDebug($"[NativeENetEvent] GetPacketDataLength: Length is too large: {length}");
            return 0;
        }
        
        return (int)length;
    }
    
}