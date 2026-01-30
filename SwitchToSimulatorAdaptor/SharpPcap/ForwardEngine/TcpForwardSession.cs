using System.Net;
using System.Net.Sockets;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.ForwardEngine;

/// <summary>
/// TCP 数据包标志
/// </summary>
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
        var flags = new System.Collections.Generic.List<string>();
        if (Syn) flags.Add("SYN");
        if (Ack) flags.Add("ACK");
        if (Fin) flags.Add("FIN");
        if (Rst) flags.Add("RST");
        if (Psh) flags.Add("PSH");
        return string.Join("|", flags);
    }
}

/// <summary>
/// TCP 会话键
/// </summary>
public readonly struct TcpSessionKey : IEquatable<TcpSessionKey>
{
    public byte[] SrcIp { get; }
    public ushort SrcPort { get; }
    public byte[] DstIp { get; }
    public ushort DstPort { get; }

    public TcpSessionKey(byte[] srcIp, ushort srcPort, byte[] dstIp, ushort dstPort)
    {
        SrcIp = (byte[])srcIp.Clone();
        SrcPort = srcPort;
        DstIp = (byte[])dstIp.Clone();
        DstPort = dstPort;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            ByteHelper.IpToUint(SrcIp),
            SrcPort,
            ByteHelper.IpToUint(DstIp),
            DstPort);
    }

    public bool Equals(TcpSessionKey other)
    {
        return ByteHelper.CompareIp(SrcIp, other.SrcIp) &&
               SrcPort == other.SrcPort &&
               ByteHelper.CompareIp(DstIp, other.DstIp) &&
               DstPort == other.DstPort;
    }

    public override bool Equals(object? obj)
    {
        return obj is TcpSessionKey other && Equals(other);
    }

    public override string ToString()
    {
        return $"{ByteHelper.IpToString(SrcIp)}:{SrcPort} -> {ByteHelper.IpToString(DstIp)}:{DstPort}";
    }
}

/// <summary>
/// TCP 转发会话
/// 将 10.13.x.x 网络的 TCP 连接转发到本地物理 IP
/// </summary>
public class TcpForwardSession : IDisposable
{
    private readonly byte[] _clientIp;       // Switch IP (10.13.x.x)
    private readonly ushort _clientPort;
    private readonly byte[] _serverIp;       // 目标 IP (10.13.37.1)
    private readonly ushort _serverPort;
    private readonly IPEndPoint _targetEndpoint;
    private readonly Func<byte[], ushort, byte[], ushort, uint, uint, byte, byte[], Task> _sendPacket;

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;

    private uint _clientSeq;          // 客户端的序列号
    private uint _serverSeq;          // 服务器的序列号
    private uint _clientAck;          // 对客户端的确认号

    private bool _disposed;
    private bool _isClosed;

    public bool IsClosed => _isClosed;

    public TcpForwardSession(
        byte[] clientIp, ushort clientPort,
        byte[] serverIp, ushort serverPort,
        IPEndPoint targetEndpoint,
        Func<byte[], ushort, byte[], ushort, uint, uint, byte, byte[], Task> sendPacket)
    {
        _clientIp = (byte[])clientIp.Clone();
        _clientPort = clientPort;
        _serverIp = (byte[])serverIp.Clone();
        _serverPort = serverPort;
        _targetEndpoint = targetEndpoint;
        _sendPacket = sendPacket;
    }

    /// <summary>
    /// 处理 SYN 包，建立到目标的连接
    /// </summary>
    public async Task HandleSynAsync(uint clientSeq)
    {
        _clientSeq = clientSeq;
        _serverSeq = (uint)Random.Shared.Next();

        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_targetEndpoint);
            _stream = _tcpClient.GetStream();

            // 发送 SYN+ACK 给 Switch
            _clientAck = _clientSeq + 1;
            await SendToClientAsync(TcpPacket.FlagSyn | TcpPacket.FlagAck, Array.Empty<byte>());
            _serverSeq++;

            _cts = new CancellationTokenSource();
            _ = StartReceivingAsync();

        }
        catch (Exception ex)
        {
            // 发送 RST
            await SendToClientAsync(TcpPacket.FlagRst, Array.Empty<byte>());
            _isClosed = true;
        }
    }

    /// <summary>
    /// 处理 TCP 包
    /// </summary>
    public async Task ProcessPacketAsync(TcpPacket tcp)
    {
        if (_isClosed || _stream == null)
            return;

        // ACK 包
        if (tcp.HasAck)
        {
            // 更新客户端确认号（用于流控制）
        }

        // FIN 包
        if (tcp.HasFin)
        {
            _clientAck = tcp.SequenceNumber + 1;
            await SendToClientAsync(TcpPacket.FlagAck | TcpPacket.FlagFin, Array.Empty<byte>());
            _isClosed = true;
            return;
        }

        // RST 包
        if (tcp.HasRst)
        {
            _isClosed = true;
            return;
        }

        // 数据包
        if (tcp.Payload.Length > 0)
        {
            try
            {
                await _stream.WriteAsync(tcp.Payload);
                await _stream.FlushAsync();

                _clientAck = tcp.SequenceNumber + (uint)tcp.Payload.Length;
                await SendToClientAsync(TcpPacket.FlagAck, Array.Empty<byte>());

            }
            catch (Exception ex)
            {
                _isClosed = true;
            }
        }
    }

    /// <summary>
    /// 从目标接收数据并转发回客户端
    /// </summary>
    private async Task StartReceivingAsync()
    {
        if (_stream == null || _cts == null)
            return;

        var buffer = new byte[4096];

        try
        {
            while (!_cts.Token.IsCancellationRequested && !_isClosed)
            {
                var read = await _stream.ReadAsync(buffer, _cts.Token);
                if (read == 0)
                {
                    // 连接关闭
                    await SendToClientAsync(TcpPacket.FlagAck | TcpPacket.FlagFin, Array.Empty<byte>());
                    _isClosed = true;
                    break;
                }

                // 发送数据给客户端 (Switch)
                var data = buffer.AsSpan(0, read).ToArray();
                await SendToClientAsync(TcpPacket.FlagAck | TcpPacket.FlagPsh, data);
                _serverSeq += (uint)read;
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {

            _isClosed = true;
        }
    }

    /// <summary>
    /// 发送 TCP 包给客户端 (Switch)
    /// </summary>
    private async Task SendToClientAsync(byte flags, byte[] data)
    {
        // 源：网关 IP，目标：Switch IP
        await _sendPacket(_serverIp, _serverPort, _clientIp, _clientPort,
            _serverSeq, _clientAck, flags, data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _stream?.Dispose();
        _tcpClient?.Dispose();
    }
}