namespace SwitchToSimulatorAdaptor.ForwardEngine;

public interface IPacketCapture : IAsyncDisposable
{
    byte[] MacAddress { get; }
    string DeviceName { get; }
    string DeviceDescription { get; }
    bool IsRunning { get; }

    event Action<ReadOnlyMemory<byte>, byte[]>? PacketReceived;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task SendPacketAsync(ReadOnlyMemory<byte> data);
    void SendPacketSync(ReadOnlySpan<byte> data);
}

public class NetworkInterfaceInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public byte[]? MacAddress { get; init; }
    public List<string> IpAddresses { get; init; } = new();
    public bool IsLoopback { get; init; }

    public override string ToString()
    {
        var mac = MacAddress != null
            ? string.Join(":", MacAddress.Select(b => b.ToString("X2")))
            : "N/A";
        return $"{Name} - {Description} [{mac}]";
    }
}