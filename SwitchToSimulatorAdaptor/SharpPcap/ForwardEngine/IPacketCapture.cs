namespace SwitchToSimulatorAdaptor.ForwardEngine;

/// <summary>
/// 数据包捕获接口
/// </summary>
public interface IPacketCapture : IAsyncDisposable
{
    /// <summary>
    /// 网卡 MAC 地址
    /// </summary>
    byte[] MacAddress { get; }

    /// <summary>
    /// 网卡名称
    /// </summary>
    string DeviceName { get; }

    /// <summary>
    /// 网卡描述
    /// </summary>
    string DeviceDescription { get; }

    /// <summary>
    /// 是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 数据包接收事件
    /// 参数: 原始数据包, 接收时的网卡 MAC
    /// </summary>
    event Action<ReadOnlyMemory<byte>, byte[]>? PacketReceived;

    /// <summary>
    /// 开始捕获数据包
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止捕获
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// 发送数据包
    /// </summary>
    Task SendPacketAsync(ReadOnlyMemory<byte> data);

    /// <summary>
    /// 发送数据包（同步版本）
    /// </summary>
    void SendPacket(ReadOnlySpan<byte> data);
}

/// <summary>
/// 网络接口信息
/// </summary>
public class NetworkInterfaceInfo
{
    /// <summary>
    /// 设备名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 设备描述
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// MAC 地址
    /// </summary>
    public byte[]? MacAddress { get; init; }

    /// <summary>
    /// IP 地址列表
    /// </summary>
    public List<string> IpAddresses { get; init; } = new();

    /// <summary>
    /// 是否为回环接口
    /// </summary>
    public bool IsLoopback { get; init; }

    public override string ToString()
    {
        var mac = MacAddress != null
            ? string.Join(":", MacAddress.Select(b => b.ToString("X2")))
            : "N/A";
        return $"{Name} - {Description} [{mac}]";
    }
}