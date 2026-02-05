using SwitchToSimulatorAdaptor.Common;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.EdenRoom;

/// <summary>
/// Eden RoomMember 的 C# 实现，使用原生 ENet 进行房间通信
/// </summary>
public class EdenRoomMember : IDisposable
{
    public enum State : byte
    {
        Uninitialized,
        Idle,
        Joining,
        Joined,
        Moderator,
    }

    public enum Error : byte
    {
        LostConnection,
        HostKicked,
        UnknownError,
        NameCollision,
        IpCollision,
        WrongVersion,
        WrongPassword,
        CouldNotConnect,
        RoomIsFull,
        HostBanned,
        PermissionDenied,
        NoSuchUser,
    }

    private NativeENetHost? _client;
    private IntPtr _server;
    private bool _serverInitialized = false;
    private State _state = State.Uninitialized;
    private bool _disposed = false;

    private readonly List<Member> _memberInformation = new();
    private RoomInformation _roomInformation;
    private GameInfo _currentGameInfo;
    private string _nickname = "";
    private IPv4Address _fakeIp;

    private Thread? _loopThread;
    private bool _sholdStop = false;
    
    private readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    
    // 事件
    public event Action<State>? StateChanged;
    public event Action<Error>? ErrorOccurred;
    public event Action<EdenLDNPacket>? LdnPacketReceived;
    public event Action<RoomInformation>? RoomInformationChanged;
    public event Action<EdenProxyPacket>? ProxyPacketReceived;
    
    public State GetState() => _state;
    public List<Member> GetMemberInformation() => new(_memberInformation);
    public RoomInformation GetRoomInformation() => _roomInformation;
    public string GetNickname() => _nickname;
    public IPv4Address GetFakeIpAddress() => _fakeIp;
    public bool IsConnected() => _serverInitialized;

    public void Join(string nickname, string serverAddr = "127.0.0.1", ushort serverPort = AppSetting.DefaultRoomPort,
        ushort clientPort = 0, IPv4Address preferredFakeIp = default, string password = "", string token = "")
    {
        if (_loopThread != null && _loopThread.IsAlive)
        {
            Leave();
        }

        if (_client == null)
        {
            _client = NativeENetHost.CreateClient(AppSetting.NumChannels);
        }

        SetState(State.Joining);
        
        Logger.Instance?.LogInfo($"Attempting to connect to {serverAddr}:{serverPort}");

        _server = _client.Connect(serverAddr, serverPort, AppSetting.NumChannels, 0);
        if (_server == IntPtr.Zero)
        {
            Logger.Instance?.LogInfo($"Failed to create connection object to {serverAddr}:{serverPort}");
            SetState(State.Idle);
            ErrorOccurred?.Invoke(Error.UnknownError);
            return;
        }
        _serverInitialized = true;
        
        // 等待连接
        bool connected = false;
        bool disconnected = false;
        int timeout = AppSetting.ConnectionTimeoutMs;
        Logger.Instance?.LogInfo($"Waiting for connection (timeout: {timeout} ms...");

        while (timeout > 0 && _client != null)
        {
            if (_client.CheckEvents(out var netEvent))
            {
                Logger.Instance?.LogInfo($"Received event: {netEvent.Type}");

                switch (netEvent.Type)
                {
                    case NativeENet.ENetEventType.ENET_EVENT_TYPE_CONNECT:
                        connected = true;
                        Logger.Instance?.LogInfo("Connection established!");
                        break;
                    case NativeENet.ENetEventType.ENET_EVENT_TYPE_DISCONNECT:
                        disconnected = true;
                        Logger.Instance?.LogInfo($"Connection disconnected. Data:{netEvent.Data}");
                        break;
                    case NativeENet.ENetEventType.ENET_EVENT_TYPE_TIMEOUT:
                        disconnected = true;
                        Logger.Instance?.LogInfo("Connection timeout.");
                        break;
                }
            }
            else
            {
                // No Event, wait a bit
                if (!_client.Service(out var serviceEvent, 5))
                {
                    timeout -= 5;
                    continue;
                }
                
                Logger.Instance?.LogInfo($"Received event: {serviceEvent.Type}");

                switch (serviceEvent.Type)
                {
                    case NativeENet.ENetEventType.ENET_EVENT_TYPE_CONNECT:
                        connected = true;
                        Logger.Instance?.LogInfo("Connection established!");
                        break;
                    case NativeENet.ENetEventType.ENET_EVENT_TYPE_DISCONNECT:
                        disconnected = true;
                        Logger.Instance?.LogInfo($"Connection disconnected. Data:{serviceEvent.Data}");
                        break;
                    case NativeENet.ENetEventType.ENET_EVENT_TYPE_TIMEOUT:
                        disconnected = true;
                        Logger.Instance?.LogInfo("Connection timeout.");
                        break;
                }

                if (connected || disconnected) break;
                
                timeout -= 5;
            }
        }
        
        if (timeout <= 0)
        {
            Logger.Instance?.LogInfo("Connection timeout expired");
        }

        if (connected)
        {
            _nickname = nickname;
            Logger.Instance?.LogInfo("Starting member loop and sending join request");
            StartLoop();
                
            // 等待一小段时间确保连接稳定
            Thread.Sleep(100);

            SendJoinRequest(nickname, new IPv4Address([192, 168, 166, 2]), password, token);
            SendGameInfo(_currentGameInfo);
        }
        else
        {
            Logger.Instance?.LogInfo($"Connection failed. Disconnected: {disconnected}, Timeout:{timeout < 0}");
            if (_serverInitialized && _client != null && _server != IntPtr.Zero)
            {
                try
                {
                    NativeENet.enet_peer_disconnect_now(_server, 0);
                    _client.Flush();
                }
                catch (Exception e)
                {
                    Logger.Instance?.LogError($"Error while disconnecting: {e.Message}");
                }
            }

            _serverInitialized = false;
            SetState(State.Idle);
            ErrorOccurred?.Invoke(disconnected ? Error.LostConnection : Error.CouldNotConnect);
        }
    }

    public void SendLdnPacket(EdenLDNPacket ldnPacket)
    {
        _rwLock.EnterReadLock();
        try
        {
            if (!IsConnected()) return;
            if (!_serverInitialized || _client == null || _server == IntPtr.Zero) return;
            
            EdenNetworkPacket packet = new();
            packet.Write((byte)RoomMessageTypes.IdLdnPacket);
            packet.Write((byte)ldnPacket.Type);
            // LDN 数据包头部使用大端字节序（A.B.C.D）
            packet.Write(ldnPacket.LocalIp);
            packet.Write(ldnPacket.RemoteIp);
            packet.Write(ldnPacket.Broadcast);
            packet.Write(ldnPacket.Data);

            byte[] data = packet.GetData();
            if (data == null || data.Length == 0) return;
            
            // 调试日志：检查数据包内容
            Logger.Instance?.LogInfo($"[EdenRoomMember] SendLdnPacket: 数据包总长度={data.Length} 字节");
            Logger.Instance?.LogInfo($"[EdenRoomMember] SendLdnPacket: Type={ldnPacket.Type}, LocalIp = {ldnPacket.LocalIp.A}.{ldnPacket.LocalIp.B}.{ldnPacket.LocalIp.C}.{ldnPacket.LocalIp.D}, RemoteIp = {ldnPacket.RemoteIp.A}.{ldnPacket.RemoteIp.B}.{ldnPacket.RemoteIp.C}.{ldnPacket.RemoteIp.D}, Broadcast={ldnPacket.Broadcast}, Data={ldnPacket.Data}");
            if (data.Length >= 11) // 至少需要 1+1+4+4+1 = 11 个字节
            {
                Logger.Instance?.LogInfo($"[EdenRoomMember] SendLdnPacket: 数据包前 11 字节：{BitConverter.ToString(data, 0,Math.Min(11, data.Length))}");
                Logger.Instance?.LogInfo($"[EdenRoomMember] SendLdnPacket: LocalIp 字节（偏移2-5，大端）：{data[2]}.{data[3]}.{data[4]}.{data[5]}");
                Logger.Instance?.LogInfo($"[EdenRoomMember] SendLdnPacket: RemoteIp 字节（偏移6-9，大端）：{data[6]}.{data[7]}.{data[8]}.{data[9]}");
                Logger.Instance?.LogInfo($"[EdenRoomMember] SendLdnPacket: Broadcast 字节（偏移10）：{data[10]}");
            }
            else
            {
                Logger.Instance?.LogInfo($"[EdenRoomMember] SendLdnPacket: 数据包长度不足 11 字节，仅有：{data.Length} 字节，无法打印数据包内容");
            }

            IntPtr enetPacket = _client.CreatePacket(data, NativeENet.ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE);
            if (enetPacket == IntPtr.Zero)
            {
                Logger.Instance?.LogInfo($"Failed to create ENet packet");
                return;
            }
            
            int result = NativeENet.enet_peer_send(_server, 0, enetPacket);
            if (result < 0)
            {
                Logger.Instance?.LogInfo($"Failed to send packet: {result}");
                NativeENetHost.DestroyPacket(enetPacket);
            }
            else
            {
                _client.Flush();
            }
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public void SendProxyPacket(EdenProxyPacket proxyPacket)
    {
        _rwLock.EnterReadLock();
        try
        {
            if (!IsConnected()) return;
            if (!_serverInitialized || _client == null || _server == IntPtr.Zero) return;
            
            EdenNetworkPacket packet = new();
            packet.Write((byte)RoomMessageTypes.IdProxyPacket);
            packet.Write((byte)proxyPacket.LocalEndpoint.Family);
            packet.Write(proxyPacket.LocalEndpoint.Ip);
            packet.Write(proxyPacket.LocalEndpoint.Port);
            packet.Write((byte)proxyPacket.RemoteEndpoint.Family);
            packet.Write(proxyPacket.RemoteEndpoint.Ip);
            packet.Write(proxyPacket.RemoteEndpoint.Port);
            packet.Write((byte)proxyPacket.EdenProtocol);
            packet.Write(proxyPacket.Broadcast);
            packet.Write(proxyPacket.Data);

            byte[] data = packet.GetData();
            if (data == null || data.Length == 0) return;

            Logger.Instance?.LogDebug(
                $"[EdenRoomMember] SendProxyPacket 到 Eden：Local={proxyPacket.LocalEndpoint.Ip.A}.{proxyPacket.LocalEndpoint.Ip.B}.{proxyPacket.LocalEndpoint.Ip.C}.{proxyPacket.LocalEndpoint.Ip.D}:{proxyPacket.LocalEndpoint}" +
                $", Remote={proxyPacket.RemoteEndpoint.Ip.A}.{proxyPacket.RemoteEndpoint.Ip.B}.{proxyPacket.RemoteEndpoint.Ip.C}.{proxyPacket.RemoteEndpoint.Ip.D}:{proxyPacket.RemoteEndpoint})" +
                $", EdenProtocol={proxyPacket.EdenProtocol}, Broadcast={proxyPacket.Broadcast}, DataLength={proxyPacket.Data.Length}");

            IntPtr enetPacket = _client.CreatePacket(data, NativeENet.ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE);
            if (enetPacket == IntPtr.Zero)
            {
                Logger.Instance?.LogWarning($"[EdenRoomMember] SendProxyPacket: Failed to create ENet packet");
                return;
            }

            int result = NativeENet.enet_peer_send(_server, 0, enetPacket);
            if (result < 0)
            {
                Logger.Instance?.LogWarning($"[EdenRoomMember] SendProxyPacket: Failed to send packet: {result}");
                NativeENetHost.DestroyPacket(enetPacket);
            }
            else
            {
                _client.Flush();
            }
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public void SendGameInfo(GameInfo gameInfo)
    {
        _currentGameInfo = gameInfo;
        
        _rwLock.EnterReadLock();
        try
        {
            if (!IsConnected()) return;
            if (!_serverInitialized || _client == null || _server == IntPtr.Zero) return;
            
            EdenNetworkPacket packet = new();
            packet.Write((byte)RoomMessageTypes.IdSetGameInfo);
            packet.Write(gameInfo.Name);
            packet.Write(gameInfo.Id.ToString());
            packet.Write(gameInfo.Version);

            byte[] data = packet.GetData();
            if (data == null || data.Length == 0) return;
            
            IntPtr enetPacket = _client.CreatePacket(data, NativeENet.ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE);
            if (enetPacket == IntPtr.Zero)
            {
                Logger.Instance?.LogWarning($"[EdenRoomMember] SendGameInfo: Failed to create ENet packet");
                return;
            }
            
            int result = NativeENet.enet_peer_send(_server, 0, enetPacket);
            if (result < 0)
            {
                Logger.Instance?.LogWarning($"[EdenRoomMember] SendGameInfo: Failed to send packet: {result}");
                NativeENetHost.DestroyPacket(enetPacket);
            }
            else
            {
                _client.Flush();
            }
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public void Leave()
    {
        _sholdStop = true;
        
        _rwLock.EnterWriteLock();
        try
        {
            if (_serverInitialized && _client != null && _server != IntPtr.Zero)
            {
                try
                {
                    NativeENet.enet_peer_disconnect_now(_server, 0);
                    _client.Flush();
                }
                catch (Exception e)
                {
                    // 忽略断开连接时的错误
                }
            }
            _serverInitialized = false;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        if (_loopThread != null && _loopThread.IsAlive)
        {
            _loopThread.Join(1000);
        }
        
        _memberInformation.Clear();
        _roomInformation = default;
        SetState(State.Idle);
    }

    private void StartLoop()
    {
        _sholdStop = false;
        _loopThread = new Thread(MemberLoop)
        {
            IsBackground = true
        };
        _loopThread.Start();
    }
    
    private void MemberLoop()
    {
        while (!_sholdStop && IsConnected() && _client != null)
        {
            if (_client.CheckEvents(out NativeENetEvent netEvent)) // Q: 这里为什么下上内容是一样的？
            {
                HandleNativeENetEvent(netEvent);
            }
            else
            {
                if (!_client.Service(out NativeENetEvent serviceEvent, 5)) continue;

                HandleNativeENetEvent(serviceEvent);
            }
        }
    }

    private void HandleNativeENetEvent(NativeENetEvent netEvent)
    {
        switch (netEvent.Type)
        {
            case NativeENet.ENetEventType.ENET_EVENT_TYPE_RECEIVE:
                HandlePacket(netEvent);
                break;
            case NativeENet.ENetEventType.ENET_EVENT_TYPE_DISCONNECT:
                SetState(State.Idle);
                ErrorOccurred?.Invoke(Error.LostConnection);
                return;
            case NativeENet.ENetEventType.ENET_EVENT_TYPE_CONNECT:
            case NativeENet.ENetEventType.ENET_EVENT_TYPE_NONE:
            case NativeENet.ENetEventType.ENET_EVENT_TYPE_TIMEOUT:
                break;
        }
    }

    private void HandlePacket(NativeENetEvent netEvent)
    {
        try
        {
            byte[] data = netEvent.GetPacketData();
            if (data == null || data.Length == 0) return;
            
            RoomMessageTypes messageType = (RoomMessageTypes)data[0];
            var packet = new EdenNetworkPacket();
            packet.Append(data);

            switch (messageType)
            {
                case RoomMessageTypes.IdLdnPacket:
                    HandleLdnPacket(packet);
                    break;
                case RoomMessageTypes.IdProxyPacket:
                    HandleProxyPacket(packet);
                    break;
                case RoomMessageTypes.IdRoomInformation: 
                    HandleRoomInformation(packet);
                    break;
                case RoomMessageTypes.IdJoinSuccess:
                case RoomMessageTypes.IdJoinSuccessAsMod:
                    HandleJoinSuccess(packet, messageType == RoomMessageTypes.IdJoinSuccessAsMod);
                    break;
                case RoomMessageTypes.IdRoomIsFull:
                    SetState(State.Idle);
                    ErrorOccurred?.Invoke(Error.RoomIsFull);
                    break;
                case RoomMessageTypes.IdNameCollision:
                    SetState(State.Idle);
                    ErrorOccurred?.Invoke(Error.NameCollision);
                break;
            case RoomMessageTypes.IdIpCollision:
                SetState(State.Idle);
                ErrorOccurred?.Invoke(Error.IpCollision);
                break;
            case RoomMessageTypes.IdVersionMismatch:
                SetState(State.Idle);
                ErrorOccurred?.Invoke(Error.WrongVersion);
                break;
            case RoomMessageTypes.IdWrongPassword:
                SetState(State.Idle);
                ErrorOccurred?.Invoke(Error.WrongPassword);
                break;
            case RoomMessageTypes.IdHostKicked:
                SetState(State.Idle);
                ErrorOccurred?.Invoke(Error.HostKicked);
                break;
            case RoomMessageTypes.IdHostBanned:
                SetState(State.Idle);
                ErrorOccurred?.Invoke(Error.HostBanned);
                break;
            }
        }
        finally
        {
            // 重要：必须销毁 ENet 数据包，否则会导致内存泄漏
            netEvent.DestroyPacket();
        }
    }

    private void HandleLdnPacket(EdenNetworkPacket packet)
    {
        packet.IgnoreBytes(1);  // Skip message type

        EdenLDNPacket ldnPacket = new EdenLDNPacket
        {
            Type = (EdenLDNPacketType)packet.ReadByte(),
            // LDN 数据包头部使用大端字节序（A.B.C.D），与 Eden 房间协议一致
            LocalIp = packet.ReadIPv4Address(),
            RemoteIp = packet.ReadIPv4Address(),
            Broadcast = packet.ReadBool(),
            Data = packet.ReadBytes()
        };
        
        LdnPacketReceived?.Invoke(ldnPacket);
    }
    
    private void HandleProxyPacket(EdenNetworkPacket packet)
    {
        packet.IgnoreBytes(1); // Skip message type

        var proxyPacket = new EdenProxyPacket
        {
            LocalEndpoint = new EdenSockAddrIn
            {
                Family = (AddressFamily)packet.ReadByte(),
                Ip = packet.ReadIPv4Address(),
                Port = packet.ReadUInt16()
            },
            RemoteEndpoint = new EdenSockAddrIn
            {
                Family = (AddressFamily)packet.ReadByte(),
                Ip = packet.ReadIPv4Address(),
                Port = packet.ReadUInt16()
            },
            EdenProtocol = (EdenProtocolType)packet.ReadByte(),
            Broadcast = packet.ReadBool(),
            Data = packet.ReadBytes()
        };
        
        ProxyPacketReceived?.Invoke(proxyPacket);
    }

    private void HandleRoomInformation(EdenNetworkPacket packet)
    {
        packet.IgnoreBytes(1); // Skip message type
        
        Logger.Instance?.LogInfo($"[EdenRoomMember] HandleRoomInformation: 开始解析，数据包大小= {packet.DataSize}，读取位置={packet.ReadPosition}");

        _roomInformation = new RoomInformation()
        {
            Name = packet.ReadString(),
            Description = packet.ReadString(),
            MemberSlots = packet.ReadUInt32(),
            Port = packet.ReadUInt16(),
            PreferredGame = new GameInfo
            {
                Name = packet.ReadString(),
                // 注意： Eden 的 RoomInformation 中 preferred_game 只有 name，没有 id 和 version
                Id = 0,
                Version = ""
            }
        };
        
        Logger.Instance?.LogInfo($"[EdenRoomMember] HandleRoomInformation: 房间信息解析完成：" +
                                 $"名称={_roomInformation.Name}, 描述={_roomInformation.Description},端口={_roomInformation.Port}, 读取位置= {packet.ReadPosition}");

        uint numMembers = packet.ReadUInt32();
        Logger.Instance?.LogInfo($"[EdenRoomMember] HandleRoomInformation: 读取成员数量={numMembers}, " +
                                 $"读取位置= {packet.ReadPosition}， 剩余字节= {packet.DataSize - packet.ReadPosition}");
        
        // 记录剩余数据的十六进制内容，用于调试
        if (packet.DataSize - packet.ReadPosition > 0)
        {
            byte[] remainingData = packet.GetData();
            int remainingStart = packet.ReadPosition;
            int remainingLength = Math.Min(100, packet.DataSize - remainingStart); // 只记录前 100 字节
            byte[] preview = new byte[remainingLength];
            Array.Copy(remainingData, remainingStart, preview, 0, remainingLength);
            string hex = BitConverter.ToString(preview);
            Logger.Instance?.LogInfo($"[EdenRoomMember] HandleRoomInformation: 剩余数据预览（前{remainingLength}字节）:{hex}");
        }

        _memberInformation.Clear();
        for (uint i = 0; i < numMembers; i++)
        {
            try
            {
                int memberStartPos = packet.ReadPosition;
                Logger.Instance?.LogDebug($"[EdenRoomMember] HandleRoomInformation: 开始解析第{i + 1}/{numMembers}个成员，" +
                                          $"当前读取位置={memberStartPos}，剩余字节={packet.DataSize - memberStartPos}");
                
                // 记录成员数据的开始部分（前 32 字节）
                if (packet.DataSize - memberStartPos > 0)
                {
                    byte[] memberData = packet.GetData();
                    int previewLength = Math.Min(32, packet.DataSize - memberStartPos);
                    byte[] preview = new byte[previewLength];
                    Array.Copy(memberData, memberStartPos, preview, 0, previewLength);
                    string hex = BitConverter.ToString(preview);
                    Logger.Instance?.LogInfo($"[EdenRoomMember] HandleRoomInformation: 成员 {i + 1} 数据预览（前{previewLength}字节）:{hex}");
                }

                string nickname = packet.ReadString();
                Logger.Instance?.LogDebug($"[EdenRoomMember] HandleRoomInformation: 成员 {i + 1} 昵称={nickname}, 读取位置={packet.ReadPosition}");
                
                // 在读取 fakeIP 前，记录接下来 4 个字节
                int beforeFakeIpPos = packet.ReadPosition;
                if (packet.DataSize - beforeFakeIpPos >= 4)
                {
                    byte[] ipBytes = new byte[4];
                    byte[] memberData = packet.GetData();
                    for (int j = 0; j < 4; j++)
                    {
                        ipBytes[j] = memberData[beforeFakeIpPos + j];
                    }
                    string ipHex = BitConverter.ToString(ipBytes);
                    Logger.Instance?.LogDebug($"[EdenRoomMember] HandleRoomInformation: 成员 {i + 1} fakeIP 原始字节={ipHex}");
                }

                IPv4Address fakeIp = packet.ReadIPv4Address();
                Logger.Instance?.LogDebug($"[EdenRoomMember] HandleRoomInformation: " +
                                          $"成员 {i + 1} fakeIp = {fakeIp.A}.{fakeIp.B}.{fakeIp.C}.{fakeIp.D}, 读取位置={packet.ReadPosition}");
                
                // 在读取 gameName 前，检查读取位置和剩余数据
                int beforeGameNamePos = packet.ReadPosition;
                int remainingBeforeGameName = packet.DataSize - beforeGameNamePos;
                Logger.Instance?.LogDebug($"[EdenRoomMember] HandleRoomInformation:" +
                                          $" 成员 {i + 1} 准备读取 gameName, 读取位置={beforeGameNamePos}, 剩余字节={remainingBeforeGameName}");
                
                // 记录接下来 4 个字节（可能是长度字段）
                if (remainingBeforeGameName >= 4)
                {
                    byte[] lengthBytes = new byte[4];
                    byte[] memberData = packet.GetData();
                    for (int j = 0; j < 4; j++)
                    {
                        lengthBytes[j] = memberData[beforeGameNamePos + j];
                    }
                    string lengthHex = BitConverter.ToString(lengthBytes);
                    string lengthAsAscii = "";
                    for (int j = 0; j < 4; j++)
                    {
                        if (lengthBytes[j] >= 32 && lengthBytes[j] < 127)
                        {
                            lengthAsAscii += (char)lengthBytes[j];
                        }
                        else
                        {
                            lengthAsAscii += "?";
                        }
                    }
                    Logger.Instance?.LogInfo($"[EdenRoomMember] HandleRoomInformation: 成员 {i + 1} gameName 长度字段：hex = {lengthHex} (ascii ={lengthAsAscii})");
                }
                
                // 尝试读取 gameName, 如果失败则跳过这个成员
                string gameName;
                try
                {
                    gameName = packet.ReadString();
                    Logger.Instance?.LogDebug($"[EdenRoomMember] HandleRoomInformation: 成员 {i + 1} gameName={gameName}, 读取位置={packet.ReadPosition}");
                }catch(Exception e)
                {
                    // 如果读取 gameName 时出错，可能是数据格式不匹配
                    Logger.Instance?.LogError($"[EdenRoomMember] HandleRoomInformation: 读取成员 {i + 1} gameName 失败，可能是数据格式不匹配或成员信息不完整。跳过此成员。");
                    Logger.Instance?.LogError($"[EdenRoomMember] HandleRoomInformation: 错误信息：{e.Message}");
                    Logger.Instance?.LogError($"[EdenRoomMember] HandleRoomInformation: 读取位置={packet.ReadPosition}， 数据包大小={packet.DataSize}");
                    
                    // 记录剩余数据的完整内容
                    if (packet.DataSize - packet.ReadPosition > 0)
                    {
                        byte[] remainingData = packet.GetData();
                        int remainingStart = packet.ReadPosition;
                        int remainingLength = Math.Min(50, packet.DataSize - remainingStart);
                        byte[] preview = new byte[remainingLength];
                        Array.Copy(remainingData, remainingStart, preview, 0, remainingLength);
                        string hex = BitConverter.ToString(preview);
                        Logger.Instance?.LogWarning($"[EdenRoomMember] HandleRoomInformation: 剩余数据（前{remainingLength}字节）：{hex}");
                    }
                    
                    // 跳过这个成员，继续处理下一个（如果有）
                    continue;
                }
                
                // game_info.id 是 ulong(u64)，不是 string!
                ulong gameId = packet.ReadUInt64();
                Logger.Instance?.LogDebug($"[EdenRoomMember] HandleRoomInformation: 成员 {i + 1} gameId={gameId} （0x{gameId:X}）, 读取位置={packet.ReadPosition}");
                
                string gameVersion = packet.ReadString();
                Logger.Instance?.LogDebug($"[EdenRoomMember] HandleRoomInformation: 成员 {i + 1} gameVersion={gameVersion}, 读取位置={packet.ReadPosition}");

                string username = packet.ReadString();
                Logger.Instance?.LogDebug($"[EdenRoomMember] HandleRoomInformation: 成员 {i + 1} username={username}, 读取位置={packet.ReadPosition}");
                
                string displayName = packet.ReadString();
                Logger.Instance?.LogDebug($"[EdenRoomMember] HandleRoomInformation: 成员 {i + 1} displayName={displayName}, 读取位置={packet.ReadPosition}");
                
                string avatarUrl = packet.ReadString();
                Logger.Instance?.LogDebug($"[EdenRoomMember] HandleRoomInformation: 成员 {i + 1} avatarUrl={avatarUrl}, 读取位置={packet.ReadPosition}");

                Member member = new Member
                {
                    Nickname = gameName,
                    FakeIp = fakeIp,
                    Game = new GameInfo
                    {
                        Name = gameName,
                        Id = gameId,
                        Version = gameVersion
                    },
                    Username = username,
                    DisplayName = displayName,
                    AvatarUrl = avatarUrl
                };
                _memberInformation.Add(member);
                Logger.Instance?.LogInfo($"[EdenRoomMember] HandleRoomInformation: 成功添加成员{i+1}/{numMembers} 到列表：" +
                                         $"Nickname={nickname}, FakeIp={fakeIp.A}.{fakeIp.B}.{fakeIp.C}.{fakeIp.D}, Game = {gameName}");
                Logger.Instance?.LogDebug($"[EdenRoomMember] HandleRoomInformation：" +
                                          $"添加成员{i+1}/{numMembers}：{nickname},游戏={gameName},IP={fakeIp.A}.{fakeIp.B}.{fakeIp.C}.{fakeIp.D}");
                // 检查是否是当前成员
                if (member.Nickname == _nickname)
                {
                    _fakeIp = member.FakeIp;
                    Logger.Instance?.LogInfo($"[EdenRoomMember] 设置 SwitchToSimulatorAdaptor 的 FakeIP = {_fakeIp.A}.{_fakeIp.B}.{_fakeIp.C}.{_fakeIp.D} （来自房间成员信息）");
                    Logger.Instance?.LogInfo($"[EdenRoomMember] HandleRoomInformation：找到自己的 FakeIP: {fakeIp.A}.{fakeIp.B}.{fakeIp.C}.{fakeIp.D}");
                }

            }catch(Exception e)
            {
                Logger.Instance?.LogError($"[EdenRoomMember] HandleRoomInformation：解析成员 {i+1}/{numMembers} 时出错：{e.Message}");
                Logger.Instance?.LogError($"[EdenRoomMember] HandleRoomInformation：异常堆栈：{e.StackTrace}");
                continue; // 使用 continue 跳过损坏的成员，继续处理下一个
            }
        }
        
        Logger.Instance?.LogInfo($"[EdemRoomMember] HandleRoomInformation： 解析完成， 最终成员列表数量：{_memberInformation.Count}");
        for (int i = 0; i < _memberInformation.Count; i++)
        {
            var member = _memberInformation[i];
            Logger.Instance?.LogInfo($"[EdenRoomMember] HandleRoomInformation：" +
                                     $"成员列表：[{i}]：Nickname = {member.Nickname}, FakeIp = {member.FakeIp.A}.{member.FakeIp.B}.{member.FakeIp.C}.{member.FakeIp.D}");
        }
        RoomInformationChanged?.Invoke(_roomInformation);
    }

    private void HandleJoinSuccess(EdenNetworkPacket packet, bool asModerator)
    {
        packet.IgnoreBytes(1);  // Skip message type
        _fakeIp = packet.ReadIPv4Address();
        Logger.Instance?.LogInfo($"[EdenRoomMember] 设置 SwitchToSimulatorAdaptor 的 FakeIp = {_fakeIp.A}.{_fakeIp.B}.{_fakeIp.C}.{_fakeIp.D} (来自房间信息数据包)");
        SetState(asModerator ? State.Moderator : State.Joined);
    }

    private void SendJoinRequest(string nickname, IPv4Address preferredFakeIP, string password, string token)
    {
        _rwLock.EnterReadLock();
        try
        {
            if (!IsConnected()) return;
            if (!_serverInitialized || _client == null || _server == IntPtr.Zero) return;

            EdenNetworkPacket packet = new EdenNetworkPacket();
            packet.Write((byte)RoomMessageTypes.IdJoinRequest);
            packet.Write(nickname); // 顺序： nickname 在前
            packet.Write(preferredFakeIP);
            packet.Write((uint)1);  // network_version
            packet.Write(password);
            packet.Write(token);
            
            byte[] data = packet.GetData();
            if (data == null || data.Length == 0) return;

            IntPtr enetPacket = _client.CreatePacket(data, NativeENet.ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE);
            if (enetPacket == IntPtr.Zero)
            {
                Logger.Instance?.LogInfo("Failed to create ENet packet");
                return;
            }

            int result = NativeENet.enet_peer_send(_server, 0, enetPacket);
            if (result < 0)
            {
                Logger.Instance?.LogInfo($"Failed to send packet: {result}");
                NativeENetHost.DestroyPacket(enetPacket);
            }
            else
            {
                _client.Flush();
            }
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }
    
    private void SetState(State newState)
    {
        if (_state != newState)
        {
            _state = newState;
            StateChanged?.Invoke(newState);
        }
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            // Leave() 已经处理了断开连接和获取写锁的逻辑
            // 只需要调用 Leave() 来确保清理
            Leave();
            
            // 在 Leave() 之后，_loopThread 应该已经停止
            // 现在可以安全地释放 _client
            _rwLock.EnterWriteLock();
            try
            {
                _client?.Dispose();
                _client = null;
                _server = IntPtr.Zero;
                _disposed = true;
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
            
            _rwLock.Dispose();
        }
    }
}