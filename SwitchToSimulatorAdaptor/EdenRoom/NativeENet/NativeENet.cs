using System.Runtime.InteropServices;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.EdenRoom;

/// <summary>
/// 原生 ENet 1.3 的 P/Invoke 绑定
/// 与 Eden 模拟器使用相同 ENet 版本，确保完全兼容
/// </summary>
public static partial class NativeENet
{
    // 使用常量 DLL 名称（系统会在应用程序目录中搜索）
    // 注意：确保正确的 DLL（约 43000 字节） 在输出目录中
    // private const string ENetLibrary = "enet";

    // ENet 常量
    public const uint ENET_HOST_ANY = 0;
    public const uint ENET_HOST_BROADCAST = 0xFFFFFFFF;
    public const ushort ENET_PORT_ANY = 0;


    // 静态构造函数：验证 DLL 是否正确
    static NativeENet()
    {
        // 检查应用程序目录中的 DLL
        // string appDir = AppDomain.CurrentDomain.BaseDirectory;
        // string dllPath = Path.Combine(appDir, "enet.dll");

        if (File.Exists(AppSetting.ENetDllPath))
        {
            // var fileInfo = new FileInfo(dllPath);
            var fileInfo = new FileInfo(AppSetting.ENetDllPath);
            // Logger.Instance?.LogInfo($"[NativeENet] Found DLL: {dllPath} （{fileInfo.Length}）bytes");
            Logger.Instance?.LogInfo($"[NativeENet] Found DLL: {AppSetting.ENetDllPath} （{fileInfo.Length}）bytes");

            // 我们编译的 DLL 大小应该 > 30 KB
            if (fileInfo.Length < 30000)
            {
                Logger.Instance?.LogInfo($"[NativeENet] WARNING：DLL 大小可能过小（{fileInfo.Length} bytes）. 期望大小 > 30 KB");
                Logger.Instance?.LogInfo($"[NativeENet] 这也许是一个老的或不正确的 DLL 文件，请检查 DLL 是否正确！");
            }
        }
        else
        {
            Logger.Instance?.LogInfo($"[NativeENet] WARNING：enet.dll not found in {AppSetting.ENetDllPath}");
        }
    }

    // ENet API 函数声明
    // 注意：EntryPoint 显式指定函数名，确保正确连接
    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_initialize", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern int enet_initialize();

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_deinitialize", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern void enet_deinitialize();

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_address_set_host",
        CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern int enet_address_set_host(ref ENetAddress address,
        [MarshalAs(UnmanagedType.LPStr)] string hostName);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_address_get_host_ip",
        CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int enet_address_get_host_ip(ref ENetAddress address, IntPtr hostName, IntPtr nameLength);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_address_get_host",
        CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int enet_address_get_host(ref ENetAddress address, IntPtr hostName, IntPtr nameLength);

    // 注意： ENet 1.3 的 enet_host_create 接受 address 指针（可以为 null）和 size_t 类型的参数
    // 使用 IntPtr 来处理 address 指针（可以为 IntPtr.Zero 标识 ENET_HOST_ANY）
    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_host_create", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern IntPtr enet_host_create(IntPtr address, IntPtr peerCount, IntPtr chanelLimit,
        uint incomingBandwidth, uint outgoingBandwidth);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_host_destroy", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern void enet_host_destroy(IntPtr host);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_host_connect", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern IntPtr enet_host_connect(IntPtr host, ref ENetAddress address, IntPtr channelCount, uint data);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_host_service", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern int enet_host_service(IntPtr host, ref ENetEvent @event, uint timeout);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_host_check_events",
        CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int enet_host_check_events(IntPtr host, ref ENetEvent event_);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_host_flush", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern void enet_host_flush(IntPtr host);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_host_broadcast", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern void enet_host_broadcast(IntPtr host, byte channelID, IntPtr packet);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_packet_create", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern IntPtr enet_packet_create(IntPtr data, IntPtr dataLength, ENetPacketFlag flags);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_packet_destroy", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern void enet_packet_destroy(IntPtr packet);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_peer_send", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern int enet_peer_send(IntPtr peer, byte channelID, IntPtr packet);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_peer_disconnect", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern void enet_peer_disconnect(IntPtr peer, uint data);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_peer_disconnect_now",
        CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void enet_peer_disconnect_now(IntPtr peer, uint data);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_peer_disconnect_later",
        CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void enet_peer_disconnect_later(IntPtr peer, uint data);

    [DllImport(AppSetting.ENetLibrary, EntryPoint = "enet_peer_reset", CallingConvention = CallingConvention.Cdecl,
        ExactSpelling = true)]
    public static extern void enet_peer_reset(IntPtr peer);
}