using SharpPcap;
using SharpPcap.LibPcap;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

public class PcapCapture : IPacketCapture
{
    private readonly ICaptureDevice _device;
    private readonly byte[] _macAddress;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private bool _isRunning;

    public byte[] MacAddress => _macAddress;
    public string DeviceName => _device.Name;
    public string DeviceDescription => _device.Description ?? "";
    public bool IsRunning => _isRunning;
    
    public event Action<ReadOnlyMemory<byte>,byte[]>? PacketReceived;

    public PcapCapture(string deviceName)
    {
        var devices = CaptureDeviceList.Instance;
        if (devices.Count == 0)
            throw new InvalidOperationException("No capture devices found. Make sure WinPcap/Npcap is installed.");

        _device = FindDevice(devices, deviceName)
                  ?? throw new ArgumentException($"Device not found: {deviceName}. Available devices: {string.Join(", ", devices.Select(d => d.Name))}");
        _macAddress = GetMacAddress(_device);
        
        Logger.Instance?.LogInfo($"Using device: {_device.Name} ({_device.Description}), MAC: {ByteHelper.MacToString(_macAddress)}");
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            throw new InvalidOperationException("Capture is already running");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        _device.Open(DeviceModes.Promiscuous, 1000);
        
        SetFilter();

        _device.OnPacketArrival += OnPacketArrival;
        _device.StartCapture();

        _isRunning = true;
        
        Logger.Instance?.LogInfo($"Packet capture started on {DeviceName}");

        _captureTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
        });

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
            return;
        
        _cts?.Cancel();

        try
        {
            _device.StopCapture();
            _device.OnPacketArrival -= OnPacketArrival;
        }
        catch (Exception e)
        {
            Logger.Instance?.LogError("Error stopping capture", e);
        }

        if (_captureTask != null)
        {
            try
            {
                await _captureTask;
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
        }

        _isRunning = false;
        Logger.Instance?.LogInfo("Packet capture stopped");
    }

    public Task SendPacketAsync(ReadOnlyMemory<byte> data)
    {
        SendPacketSync(data.Span);
        return Task.CompletedTask;
    }

    public void SendPacketSync(ReadOnlyMemory<byte> data)
    {
        throw new NotImplementedException();
    }

    public void SendPacketSync(ReadOnlySpan<byte> data)
    {
        if (!_isRunning)
            throw new InvalidOperationException("Capture is not running");

        try
        {
            if (_device is IInjectionDevice injectionDevice)
            {
                injectionDevice.SendPacket(data);
            }
            else
            {
                throw new NotSupportedException("Device does not support packet injection");
            }
        }
        catch (Exception e)
        {
            Logger.Instance?.LogError("Error sending packet", e);
            throw;
        }
        
    }

    public static List<NetworkInterfaceInfo> GetAllDevices()
    {
        var result = new List<NetworkInterfaceInfo>();
        var devices = CaptureDeviceList.Instance;

        foreach (var device in devices)
        {
            var info = new NetworkInterfaceInfo
            {
                Name = device.Name,
                Description = device.Description,
                MacAddress = GetMacAddressSafe(device),
                IsLoopback = device.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
            };
            
            // 获取 IP 地址
            if (device is LibPcapLiveDevice libPcapDevice)
            {
                foreach (var addr in libPcapDevice.Addresses)
                {
                    if (addr.Addr?.ipAddress != null)
                    {
                        info.IpAddresses.Add(addr.Addr.ipAddress.ToString());
                    }
                }
            }
            
            result.Add(info);
        }

        return result;
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        try
        {
            var rawPacket = e.GetPacket();
            if (rawPacket.Data.Length >= EthernetFrame.HeaderLength)
            {
                PacketReceived?.Invoke(rawPacket.Data, _macAddress);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance?.LogError("Error processing received packet", ex);
        }
    }

    private void SetFilter()
    {
        try
        {
            var macStr = string.Join(":", _macAddress.Select(b => b.ToString("X2")));
            var filter = $"net {AppSetting.SubnetNet}/16 and not ether src {macStr}";
            
            _device.Filter = filter;
            Logger.Instance?.LogDebug($"BPF filter set: {filter}");
        }
        catch (Exception e)
        {
            Logger.Instance?.LogError("Failed to set BPF filter, capturing all packets", e);
        }
    }
    
    private static ICaptureDevice? FindDevice(CaptureDeviceList devices, string deviceName)
    {
        var device = devices.FirstOrDefault(d =>
            d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));

        if (device != null)
            return device;

        device = devices.FirstOrDefault(d =>
            d.Name.Contains(deviceName, StringComparison.OrdinalIgnoreCase) ||
            (d.Description?.Contains(deviceName, StringComparison.OrdinalIgnoreCase) ?? false));

        if (device != null)
            return device;

        if (deviceName.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return devices.FirstOrDefault(d => 
                !d.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static byte[] GetMacAddress(ICaptureDevice device)
    {
        if (device is LibPcapLiveDevice libPcapDevice)
        {
            var mac = libPcapDevice.MacAddress;
            if (mac != null)
            {
                return mac.GetAddressBytes();
            }
        }

        // 如果无法获取，返回一个随机 MAC
        var randomMac = new byte[6];
        Random.Shared.NextBytes(randomMac);
        randomMac[0] = (byte)(randomMac[0] & 0xFE | 0x02); // 本地管理地址
        return randomMac;
    }

    private static byte[]? GetMacAddressSafe(ICaptureDevice device)
    {
        try
        {
            if (device is LibPcapLiveDevice libPcapDevice)
            {
                return libPcapDevice.MacAddress?.GetAddressBytes();
            }
        }
        catch
        {
            // 忽略错误
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        try
        {
            _device.Close();
        }
        catch
        {
            // 忽略关闭错误
        }
        
        _cts?.Dispose();
    }
}