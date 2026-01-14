using SwitchToSimulatorAdaptor.SharpPcap;

namespace SwitchToSimulatorAdaptor.SwitchLdn;

public enum LdnState
{
    Idle, // 空闲
    AccessPoint, // 作为接入点 （等待客户端）
    Active, // 有客户端连接
}

/// <summary>
/// 客户端会话信息
/// </summary>
public class ClientSession
{
    public byte NodeId { get; set; }
    public NodeInfo NodeInfo { get; set; }
    public TcpSession? TcpSession { get; set; }
        
    // 用于追踪
    public DateTime ConnectedAt { get; set; } = DateTime.Now;
    public DateTime LastActivity { get; set; } = DateTime.Now;
}