namespace SwitchToSimulatorAdaptor.SharpPcap;

[Flags]
public enum TcpFlags : Byte
{
    FIN = 0x01,
    SYN = 0x02,
    RSP = 0x04,
    PSH = 0x08,
    ACK = 0x10,
    URG = 0x20
}

public enum TcpState
{
    Listen,         // 等待连接
    SynReceived,    // 收到 SYN
    Established,    // 连接建立
    CloseWait,      // 等待关闭
    LastAck,        // 等待最后的 ACK
    Closed          // 连接关闭
}

/// <summary>
/// TCP 会话信息
/// </summary>
public class TcpSession
{
    // 连接标识
    public byte[] RemoteIP { get; set; } = new byte[4];
    public byte[] LocalIP { get; set; } = new byte[4];
    public ushort RemotePort { get; set; }
    public ushort LocalPort { get; set; }
        
    // MAC 地址 （用于构建以太网帧）
    public byte[] RemoteMac { get; set; } = new byte[6];
    public byte[] LocalMac { get; set; } = new byte[6];
        
    // TCP 状态
    public TcpState State { get; set; } = TcpState.Listen;
        
    // 序列号管理
    public uint SendSeq { get; set; }       // 我们发送的序列号
    public uint RecvSeq { get; set; }       // 期望收到的序列号 （对方的）
    public uint SendAck { get; set; }       // 我们发送的确认号
        
    // 接收缓冲区
    public List<byte> RecvBuffer { get; } = new();
        
    // 唯一标识 （用于查找对话）
    public string Key => $"{FormatIP(RemoteIP)}:{RemotePort}-{FormatIP(LocalIP)}:{LocalPort}";
        
    private static string FormatIP(byte[] ip) => $"{ip[0]}.{ip[1]}.{ip[2]}.{ip[3]}";
}