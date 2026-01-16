using SwitchToSimulatorAdaptor.Common;

namespace SwitchToSimulatorAdaptor.SwitchLdn;


public class LdnStateMachine
{
    private LdnState _state = LdnState.Idle;
    private NetworkInfo _networkInfo;
    private readonly Dictionary<byte, ClientSession> _clients = new();

    // 配置
    private readonly byte[] _hostMac;
    private readonly IPv4Address _hostIp;

    // 回调
    public Action<byte[], ushort, byte[]>? SendUdpPacket;   // （目标IP, 目标端口, 数据）
    public Action<ClientSession, byte[]>?  SendTcpPacket;   // （会话, 数据）

    // 事件
    public Action<ClientSession>? OnClientConnected;
    public Action<ClientSession>? OnClientDisconnected;

    public LdnStateMachine(byte[] hostMac, IPv4Address hostIp)
    {
        _hostMac = hostMac;
        _hostIp = hostIp;
    }

    /// <summary>
    /// 开启接入点
    /// </summary>
    /// <param name="maxParticipants"></param>
    /// <param name="advertiseData"></param>
    public void OpenAccessPoint(byte maxParticipants = 8, byte[]? advertiseData = null)
    {
        _networkInfo = NetworkInfo.Create(maxParticipants);

        // 设置主机信息
        Array.Copy(_hostMac, _networkInfo.HostMac, 6);

        // 主机作为第一个参与者
        _networkInfo.Participants[0] = NodeInfo.Create(_hostIp, _hostMac, 0, "Host");
        _networkInfo.ParticipantCount = 1;

        // 设置广告数据
        if (advertiseData != null && advertiseData.Length > 0)
        {
            var copyLen = Math.Min(advertiseData.Length, 384);
            Array.Copy(advertiseData, _networkInfo.AdvertiseData, copyLen);
            _networkInfo.AdvertiseDataLength = (ushort)copyLen;
        }

        _state = LdnState.AccessPoint;
        Console.WriteLine("[LDN Host] 接入点已开启");
    }

    /// <summary>
    /// 关闭接入点
    /// </summary>
    public void CloseAccessPoint()
    {
        // 断开所有客户端
        foreach (var client in _clients.Values.ToList())
        {
            DisconnectClient(client.NodeId, DisconnectReason.NetworkDestroyed);
        }

        _clients.Clear();
        _state = LdnState.Idle;
        Console.WriteLine("[LDN Host] 接入点已关闭");
    }

    /// <summary>
    /// 处理 UDP Scan 请求
    /// </summary>
    /// <param name="srcIp"></param>
    /// <param name="srcPort"></param>
    public void HandleScan(byte[] srcIp, ushort srcPort)
    {
        if (_state == LdnState.Idle) return;

        Console.WriteLine($"[LDN Host] 接收到 Scan，来自 {srcIp[0]}.{srcIp[1]}.{srcIp[2]}.{srcIp[3]}");

        // 构建并发送 ScanResponse
        var response = new ScanResponsePacket { NetworkInfo = _networkInfo };
        var data = response.Build();

        SendUdpPacket?.Invoke(srcIp, srcPort, data);
        Console.WriteLine($"[LDN Host] 发送 ScanResponse，目标 {srcIp[0]}.{srcIp[1]}.{srcIp[2]}.{srcIp[3]}:{srcPort}");
    }

    /// <summary>
    /// 处理 TCP Connect 请求
    /// </summary>
    /// <param name="session"></param>
    /// <param name="payload"></param>
    public void HandleConnect(ClientSession session, byte[] payload)
    {
        Console.WriteLine($"[LDN Host] 收到 Connect 请求");

        // 解析请求
        var connectPacket = ConnectPacket.Parse(payload);
        var request = connectPacket.Request;

        // 检查是否已满
        if (_networkInfo.ParticipantCount >= _networkInfo.MaxParticipants)
        {
            Console.WriteLine("[LDN Host] 房间已满，拒绝连接");
            // SendConnectResponse(session, ConnectResult.RoomFull, 0);
            return;
        }

        // 分配 NodeId （找第一个空位）
        byte nodeId = 0;
        for (byte i = 0; i < 8; i++)
        {
            if (_networkInfo.Participants[i].IsConnected == 0)
            {
                nodeId = i;
                break;
            }
        }

        // if (nodeId == 0)
        // {
        //     SendConnectResponse(session, ConnectResult.NodeCountLimitReached, 0);
        //     return;
        // }

        // 更新 NodeInfo
        var nodeInfo = request.NodeInfo;
        nodeInfo.NodeId = nodeId;
        nodeInfo.IsConnected = 1;
        nodeInfo.Ip = request.ClientIp;
        Array.Copy(request.ClientMac, nodeInfo.Mac, 6);

        // 添加到参与者列表
        _networkInfo.Participants[nodeId] = nodeInfo;
        _networkInfo.ParticipantCount++;

        // 保存会话
        session.NodeId = nodeId;
        session.NodeInfo = nodeInfo;
        _clients[nodeId] = session;

        _state = LdnState.Active;

        Console.WriteLine($"[LDN Host] 客户端已连接： NodeId = {nodeId}, IP = {nodeInfo.Ip}, Name = {nodeInfo.GetUserName()}");

        // // 发送成功响应
        // SendConnectResponse(session, ConnectResult.Success, nodeId);

        // 广播 SyncNetwork
        BroadcastSyncNetwork();

        OnClientConnected?.Invoke(session);
    }

    /// <summary>
    /// 处理客户端断开
    /// </summary>
    /// <param name="session"></param>
    public void HandleDisconnect(ClientSession session)
    {
        if (!_clients.ContainsKey(session.NodeId)) return;

        var nodeId = session.NodeId;
        Console.WriteLine($"[LDN Host] 客户端断开连接：NodeId = {nodeId}");

        // 从参与者列表移除
        _networkInfo.Participants[nodeId] = new NodeInfo();
        _networkInfo.ParticipantCount--;

        // 移除会话
        _clients.Remove(nodeId);
        OnClientDisconnected?.Invoke(session);

        // 广播更新
        BroadcastSyncNetwork();

        // 如果没有客户端了，回到 AccessPoint 状态
        if (_clients.Count == 0)
        {
            _state = LdnState.AccessPoint;
        }
    }

    /// <summary>
    /// 主动断开客户端
    /// </summary>
    /// <param name="nodeId"></param>
    /// <param name="reason"></param>
    public void DisconnectClient(byte nodeId, DisconnectReason reason)
    {
        if (!_clients.TryGetValue(nodeId, out var session)) return;

        // 发送 Disconnect 包
        var packet = new DisconnectPacket { Reason = reason };
        SendTcpPacket?.Invoke(session, packet.Build());

        HandleDisconnect(session);
    }

    /// <summary>
    /// 广播 SyncNetwork 给所有客户端
    /// </summary>
    public void BroadcastSyncNetwork()
    {
        var packet = new SyncNetworkPacket { NetworkInfo = _networkInfo };
        var data = packet.Build(compress: true); // 这又是什么语法？

        foreach (var session in _clients.Values)
        {
            SendTcpPacket?.Invoke(session, data);
        }

        Console.WriteLine($"[LDN Host] 广播 SyncNetwork, 当前 {_networkInfo.ParticipantCount} 个参与者");
    }

    /// <summary>
    /// 更新广告数据并广播
    /// </summary>
    /// <param name="data"></param>
    public void UpdateAdvertiseData(byte[] data)
    {
        var copyLen = Math.Min(data.Length, 384);
        Array.Clear(_networkInfo.AdvertiseData, 0, 384);
        Array.Copy(data, _networkInfo.AdvertiseData, copyLen);
        _networkInfo.AdvertiseDataLength = (ushort)copyLen;

        BroadcastSyncNetwork();
    }

    // private void SendConnectResponse(ClientSession session, ConnectResult result, byte nodeId)
    // {
    //     var response = new ConnectResponsePacket
    //     {
    //         Response = new ConnectResponseData
    //         {
    //             Result = result,
    //             NodeId = nodeId,
    //             Reserved = new byte[2]
    //         }
    //     };
    //
    //     SendTcpPacket?.Invoke(session, response.Build());
    // }

    // 属性
    public LdnState State => _state;
    public NetworkInfo NetworkInfo => _networkInfo;
    public int ClientCount => _clients.Count;
}