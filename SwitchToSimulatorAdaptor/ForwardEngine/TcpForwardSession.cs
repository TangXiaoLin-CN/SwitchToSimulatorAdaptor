using SwitchToSimulatorAdaptor.Utils;
using System.Net;
using System.Net.Sockets;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

public enum TcpSessionState
{
    Listen,         // 等待连接
    SynReceived,    // 收到 SYN
    Established,    // 连接建立
    CloseWait,      // 等待关闭
    LastAck,        // 等待最后的 ACK
    Closed          // 连接关闭
}

public struct TcpPacketFlags
{
    public bool Syn { get; set; }
    public bool Ack { get; set; }
    public bool Fin { get; set; }
    public bool Rst { get; set; }
    public bool Psh { get; set; }
    public uint SequenceNumber { get; set; }
    public uint AcknowledgmentNumber { get; set; }

    public override string ToString()
    {
        var flags = new List<string>();
        if (Syn) flags.Add("SYN");
        if (Ack) flags.Add("ACK");
        if (Fin) flags.Add("FIN");
        if (Rst) flags.Add("RST");
        if (Psh) flags.Add("PSH");
        return string.Join("|", flags);
    }
}

public readonly struct TcpSessionKey : IEquatable<TcpSessionKey>
{
    public byte[] SrcIp { get; }
    //public ushort SrcPort { get; }
    public byte[] DstIp { get; }
    public ushort DstPort { get; }

    public TcpSessionKey(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort)
    {
        SrcIp = (byte[])srcIp.Clone();
        //SrcPort = srcPort;
        DstIp = (byte[])dstIp.Clone();
        DstPort = dstPort;
    }
    
    public override int GetHashCode()
    {
        return HashCode.Combine(
            ByteHelper.IpToUint(SrcIp),
            DstPort,
            ByteHelper.IpToUint(DstIp),
            DstPort);
    }

    public bool Equals(TcpSessionKey other)
    {
        return ByteHelper.CompareIp(SrcIp, other.SrcIp) &&
               DstPort == other.DstPort && 
               ByteHelper.CompareIp(DstIp, other.DstIp) &&
               DstPort == other.DstPort;
    }

    public override bool Equals(object? obj)
    {
        return obj is TcpSessionKey other && Equals(other);
    }
    
    public override string ToString()
    {
        return $"{ByteHelper.IpToString(SrcIp)}:{DstPort} -> {ByteHelper.IpToString(DstIp)}:{DstPort}";
    }
}

public class TcpForwardSession : IDisposable
{
    private readonly PacketForwardEngine _forwardEngine;
    private readonly byte[] _clientIp;
    private readonly ushort _clientPort;
    private readonly byte[] _serverIp;
    private readonly ushort _serverPort;
    private readonly Func<byte[], ushort, byte[], ushort, uint, uint, byte, byte[], byte[]?, Task> _sendPacket;
    
    private CancellationTokenSource? _cts;

    private uint _clientSeq;
    private uint _serverSeq;
    private uint _clientAck;

    private byte[] _tcpOptions;

    private bool _disposed;
    // private bool _isClosed;
    private TcpSessionState _state;
    
    public TcpSessionState State => _state;
    public bool IsClosed => _state == TcpSessionState.Closed;
    
    public TcpForwardSession(PacketForwardEngine packetForwardEngine,
        byte[] clientIp, ushort clientPort,
        byte[] serverIp, ushort serverPort,
        Func<byte[], ushort, byte[], ushort, uint, uint, byte, byte[], byte[]?, Task> sendPacket)
    {
        _forwardEngine = packetForwardEngine;
        _clientIp = (byte[])clientIp.Clone();
        _clientPort = clientPort;
        _serverIp = (byte[])serverIp.Clone();
        _serverPort = serverPort;
        _sendPacket = sendPacket;
         _state = TcpSessionState.Listen;
    }

    public async Task HandleSynAsync(uint clientSeq, byte[] tcpOptions)
    {
        _clientSeq = clientSeq;
        _serverSeq = (uint)Random.Shared.Next();
        _tcpOptions = tcpOptions;
        
        try
        {
            _clientAck = _clientSeq + 1;
            await SendToClientAsync(TcpPacket.FlagSyn | TcpPacket.FlagAck, Array.Empty<byte>(), _tcpOptions);
            _serverSeq++;
            
            _state = TcpSessionState.SynReceived;

            _cts = new CancellationTokenSource();
            
            Logger.Instance?.LogInfo($"TCP forward session established to {ByteHelper.IpToString(_serverIp)} " +
                                     $"for {ByteHelper.IpToString(_clientIp)}:{_clientPort}");
        }
        catch (Exception e)
        {
            Logger.Instance?.LogError($"Failed to connect to {ByteHelper.IpToString(_clientIp)}", e);
            // 发送 RST
            await SendToClientAsync(TcpPacket.FlagRst, Array.Empty<byte>());
            _state = TcpSessionState.Closed;
        }
    }

    public async Task ProcessPacketAsync(IPv4Packet ip, TcpPacket tcp, byte[] srcIp, byte[] dstIp)
    {
        if (_state == TcpSessionState.Closed)
            return;

        if (tcp.HasAck)
        {
            if (_state == TcpSessionState.SynReceived)
            {
                _state = TcpSessionState.Established;
                Logger.Instance?.LogInfo($"TCP connection established: {ByteHelper.IpToString(_clientIp)}:{_clientPort}");
            }
            else if (_state == TcpSessionState.LastAck)
            {
                _state = TcpSessionState.Closed;
                Logger.Instance?.LogInfo($"TCP connection closed: {ByteHelper.IpToString(_clientIp)}:{_clientPort}");
            }
            else if (_state == TcpSessionState.Established)
            {
                // 如果已建立连接，则转发TCP消息
                try
                {
                    await _forwardEngine.ForwardTcpPacket(this, tcp, srcIp, dstIp);
                }
                catch (Exception e)
                {
                    Logger.Instance?.LogError($"[TcpForwardSession] 发送 TCP 失败", e);
                }
            }
        }

        if (tcp.HasFin)
        {
            Logger.Instance?.LogInfo("TCP FIN received from client");
            _clientAck = tcp.SequenceNumber + 1;
            _state = TcpSessionState.CloseWait;
            await SendToClientAsync(TcpPacket.FlagAck | TcpPacket.FlagFin, Array.Empty<byte>());
            _state = TcpSessionState.LastAck;
            return;
        }

        if (tcp.HasRst)
        {
            Logger.Instance?.LogInfo("TCP RST received from client");
            _state = TcpSessionState.Closed;
            return;
        }

        if (tcp.Payload.Length > 0 && _state == TcpSessionState.Established)
        {
            try
            {
                _clientAck = tcp.SequenceNumber + (uint)tcp.Payload.Length;
                await SendToClientAsync(TcpPacket.FlagAck, Array.Empty<byte>());
                Logger.Instance?.LogDebug($"TCP forwarded {tcp.Payload.Length} bytes to target");
            }
            catch (Exception e)
            {
                Logger.Instance?.LogError("Error writing to TCP stream", e);
                _state = TcpSessionState.Closed;
            }
        }
    }

    public async Task SendTcpAsync(ReadOnlyMemory<byte> payload)
    {
        if (_state != TcpSessionState.Established)
            return;

        await SendToClientAsync(TcpPacket.FlagAck | TcpPacket.FlagPsh, payload.ToArray());
        _serverSeq += (uint)payload.Length;
    }

    private async Task SendToClientAsync(byte flags, byte[] data, byte[]? options = null)
    {
        await _sendPacket(_serverIp, _serverPort, _clientIp, _clientPort, 
            _serverSeq, _clientAck, flags, data, options ?? Array.Empty<byte>());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
    }
}