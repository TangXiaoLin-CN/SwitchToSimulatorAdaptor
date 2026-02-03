using SharpPcap;
using SharpPcap.LibPcap;
using SwitchToSimulatorAdaptor.SwitchLdn;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.SharpPcap;

public class SharpPcapManager : IDisposable
{
    private bool _initialized;
    private LibPcapLiveDevice? _liveDevice;
    private PacketSender? _packetSender;
    private readonly HashSet<PacketArrivalEventHandler> _packetArrivalEventHandlers = new();
    
    public byte[] DeviceMac => _liveDevice?.MacAddress?.GetAddressBytes() ?? new byte[6];
    
    public bool Init()
    {
        if (!InitDevice()) return false;

        _packetSender = new PacketSender(_liveDevice, true);
        _packetSender.OnLog += OnPacketSenderLogEvent;
        _packetSender.OnError += OnPacketSenderErrorEvent;
        
        _liveDevice?.OnPacketArrival += OnPacketArrivalEvent;
        _liveDevice?.Open(DeviceModes.DataTransferUdp, AppSetting.ReadTimeOut);
        _liveDevice?.Filter = AppSetting.BPFFilter;
            
        _initialized = true;
        return true;
    }

    private bool InitDevice()
    {
        var devices = CaptureDeviceList.Instance;

        if (devices.Count == 0)
        {
            Logger.Instance?.LogError("[SharpPcap] 没有找到网络接口，请检查是否安装了 WinPcap 或者 Npcap");
            return false;
        }
        
        foreach (var device in devices)
        {
            if (device is not LibPcapLiveDevice libPcapLiveDevice) continue;
            if (!(libPcapLiveDevice.Interface?.FriendlyName ?? "").StartsWith(AppSetting.TargetDeviceFriendlyName))
                continue;
            
            _liveDevice = libPcapLiveDevice;
            return true;
        }

        return false;
    }

    public bool RegisterPacketArrivalEvent(PacketArrivalEventHandler cb)
    {
        return _packetArrivalEventHandlers.Add(cb);
    }
    
    public bool UnregisterPacketArrivalEvent(PacketArrivalEventHandler cb)
    {
        return _packetArrivalEventHandlers.Remove(cb);
    }
    
    public void StartCapture()
    {
        if (!_initialized) return;
        _liveDevice?.StartCapture();
    }
    
    public void StopCapture()
    {
        if (!_initialized) return;
        _liveDevice?.StopCapture();
    }

    public void SendPacket(byte[] data)
    {
        if (!_initialized) return;
        _packetSender?.Send(data);
    }
    
    private void OnPacketArrivalEvent(object sender, PacketCapture e)
    {
        foreach (var cb in _packetArrivalEventHandlers)
        {
            cb(sender, e);
        }
    }

    private void OnPacketSenderLogEvent(string message)
    {
        Logger.Instance?.LogInfo(message);
    }
    
    private void OnPacketSenderErrorEvent(Exception e)
    {
        Logger.Instance?.LogError(e.Message);
    }
    
    public static void PrintDevicesList()
    {
        //获取所有网络设备
        var devices = CaptureDeviceList.Instance;
        
        Console.WriteLine($"找到 {devices.Count} 个网络接口 \n");

        for (int i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            
            Console.WriteLine($"设备[{ i }]：{ device.Name }");
            Console.WriteLine($"     描述：{ device.Description }");

            if (device is LibPcapLiveDevice libPcapDevice)
            {
                Console.WriteLine($"     友好名称：{ libPcapDevice.Interface?.FriendlyName }");
                
                //打印 Ip 地址
                foreach (var addr in libPcapDevice.Interface?. Addresses ?? [])
                {
                    if (addr.Addr?.ipAddress != null)
                    {
                        Console.WriteLine($"     IP:{ addr.Addr?.ipAddress }");
                    }
                }
            }
        }
        
        Console.WriteLine();
    }
    
    public void Dispose()
    {
        _packetSender?.OnLog -= OnPacketSenderLogEvent;
        _packetSender?.OnError -= OnPacketSenderErrorEvent;
        _packetSender?.Dispose();
        _packetSender = null;
        
        _liveDevice?.OnPacketArrival -= OnPacketArrivalEvent;
        _liveDevice?.StopCapture();
        _liveDevice?.Close();
        _liveDevice?.Dispose();
        _liveDevice = null;
     
        _packetArrivalEventHandlers.Clear();
        
        _initialized = false;
    }
}