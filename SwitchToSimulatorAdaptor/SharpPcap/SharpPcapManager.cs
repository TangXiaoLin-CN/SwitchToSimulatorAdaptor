using SharpPcap;
using SharpPcap.LibPcap;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.SharpPcap;

public class SharpPcapManager : IDisposable
{
    private bool _initialized;
    private LibPcapLiveDevice? _liveDevice;
    private PacketSender? _packetSender;
    private readonly HashSet<PacketArrivalEventHandler> _packetArrivalEventHandlers = new();
    
    public bool Init()
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

            _packetSender = new PacketSender(libPcapLiveDevice, true);
            _packetSender.OnLog += OnPacketSenderLogEvent;
            _packetSender.OnError += OnPacketSenderErrorEvent;
            
            _liveDevice = libPcapLiveDevice;
            _liveDevice.OnPacketArrival += OnPacketArrivalEvent;
            _liveDevice.Open(DeviceModes.DataTransferUdp, AppSetting.ReadTimeOut);
            _liveDevice.Filter = AppSetting.BPFFilter;
            
            _initialized = true;
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
        
        _initialized = false;
    }
}