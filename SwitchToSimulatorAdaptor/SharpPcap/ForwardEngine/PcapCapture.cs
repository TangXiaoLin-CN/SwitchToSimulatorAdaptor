using SharpPcap;
using SharpPcap.LibPcap;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

/// <summary>
/// 基于 SharpPcap 的数据包捕获实现
/// 来源: switch-lan-play/src/pcaploop.cpp
/// </summary>
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

    public event Action<ReadOnlyMemory<byte>, byte[]>? PacketReceived;

    /// <summary>
    /// 创建数据包捕获实例
    /// </summary>
    /// <param name="deviceName">设备名称或描述的部分匹配</param>
    /// <param name="logger">日志记录器</param>
    public PcapCapture(string deviceName)
    {


        var devices = CaptureDeviceList.Instance;
        if (devices.Count == 0)
            throw new InvalidOperationException("No capture devices found. Make sure WinPcap/Npcap is installed.");

        // 查找匹配的设备
        _device = FindDevice(devices, deviceName)
            ?? throw new ArgumentException($"Device not found: {deviceName}. Available devices: {string.Join(", ", devices.Select(d => d.Name))}");

        _macAddress = GetMacAddress(_device);
    }

    /// <summary>
    /// 获取所有可用网络接口
    /// </summary>
    public static List<NetworkInterfaceInfo> GetAllDevices()
    {
        var result = new List<NetworkInterfaceInfo>();
        var devices = CaptureDeviceList.Instance;

        foreach (var device in devices)
        {
            var info = new NetworkInterfaceInfo
            {
                Name = device.Name,
                Description = device.Description ?? "",
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

    /// <summary>
    /// 开始捕获
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            throw new InvalidOperationException("Capture is already running");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 打开设备
        _device.Open(DeviceModes.Promiscuous, 1000);

        // 设置过滤器
        SetFilter();

        // 注册事件处理
        _device.OnPacketArrival += OnPacketArrival;

        // 开始捕获
        _device.StartCapture();
        _isRunning = true;


        // 等待取消
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
        }, _cts.Token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止捕获
    /// </summary>
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
        catch (Exception ex)
        {

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
    }

    /// <summary>
    /// 发送数据包
    /// </summary>
    public Task SendPacketAsync(ReadOnlyMemory<byte> data)
    {
        SendPacket(data.Span);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 发送数据包（同步）
    /// </summary>
    public void SendPacket(ReadOnlySpan<byte> data)
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
        catch (Exception ex)
        {

            throw;
        }
    }

    /// <summary>
    /// 数据包到达处理
    /// </summary>
    private void OnPacketArrival(object sender, PacketCapture e)
    {
        try
        {
            var rawPacket = e.GetPacket();
            if (rawPacket.Data.Length >= 14)
            {
                PacketReceived?.Invoke(rawPacket.Data, _macAddress);
            }
        }
        catch (Exception ex)
        {

        }
    }

    /// <summary>
    /// 设置 BPF 过滤器
    /// </summary>
    private void SetFilter()
    {
        try
        {
            // 只捕获子网内的数据包，排除自己发送的
            var macStr = string.Join(":", _macAddress.Select(b => b.ToString("x2")));
            var filter = $"net {AppSetting.SubnetNet}/16 and not ether src {macStr}";

            _device.Filter = filter;

        }
        catch (Exception ex)
        {

        }
    }

    /// <summary>
    /// 查找匹配的设备
    /// </summary>
    private static ICaptureDevice? FindDevice(CaptureDeviceList devices, string deviceName)
    {
        // 精确匹配名称
        var device = devices.FirstOrDefault(d =>
            d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));

        if (device != null)
            return device;

        // 部分匹配名称或描述
        device = devices.FirstOrDefault(d =>
            d.Name.Contains(deviceName, StringComparison.OrdinalIgnoreCase) ||
            (d.Description?.Contains(deviceName, StringComparison.OrdinalIgnoreCase) ?? false));

        if (device != null)
            return device;

        // 如果是 "all"，返回第一个非回环设备
        if (deviceName.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return devices.FirstOrDefault(d =>
                !d.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    /// <summary>
    /// 获取设备 MAC 地址
    /// </summary>
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

    /// <summary>
    /// 安全获取 MAC 地址
    /// </summary>
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

    /// <summary>
    /// 释放资源
    /// </summary>
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