namespace SwitchToSimulatorAdaptor.SharpPcap;

public class RawTcpServer
    {
        // private readonly LibPcapLiveDevice? _device;
        private readonly PacketSender _sender;
        private readonly byte[] _localMac;
        private readonly byte[] _localIP;         // 我们冒充的 IP
        private readonly ushort _localPort;
        
        // 会话管理
        private readonly Dictionary<string, TcpSession> _sessions = new();
        
        // 数据接收事件
        public event Action<TcpSession, byte[]>? OnDataReceived;
        
        // public RawTcpServer(LibPcapLiveDevice? device, byte[] localMac, byte[] localIP, ushort localPort)
        public RawTcpServer(PacketSender sender, byte[] localMac, byte[] localIP, ushort localPort)
        {
            // _device = device;
            _sender = sender;
            _localMac = localMac;
            _localIP = localIP;
            _localPort = localPort;
        }

        /// <summary>
        /// 处理收到的数据包
        /// </summary>
        /// <param name="data"></param>
        public void OnPacketArrival(byte[] data)
        {
            // 解析以太网帧
            var eth = EthernetFrame.Parse(data);
            if (eth == null || !eth.IsIPv4) return;
            
            // 解析 IP 头
            var ip = IPv4Header.Parse(eth.Payload);
            if (ip == null || !ip.IsTCP) return;
            
            // 检查目标 IP 是否是我们冒充的 IP
            if (!ip.DestinationIP.SequenceEqual(_localIP)) return;
            
            // 解析 TCP 头
            var tcp = TcpHeader.Parse(ip.Payload);
            if (tcp == null) return;
            
            // 检查目标端口
            if (tcp.DestinationPort != _localPort) return;
            
            Console.WriteLine($"[TCP] {ip.SourceIPString}:{tcp.SourcePort} -> {ip.DestinationIPString}:{tcp.DestinationPort}");

            // 获取或创建会话
            var session = GetOrCreateSession(eth.SourceMac, ip.SourceIP, tcp.SourcePort);
            
            // 根据 TCP 标志处理
            if (tcp.SYN && !tcp.ACK)
            {
                HandleSyn(session, tcp);
            }
            else if (tcp.ACK)
            {
                if (session.State == TcpState.SynReceived)
                {
                    HandleAckAfterSynAck(session, tcp);
                }
                else if (session.State == TcpState.Established)
                {
                    HandleDataOrAck(session, tcp);
                }
            }

            if (tcp.FIN)
            {
                HandleFin(session, tcp);
            }
        }

        private TcpSession GetOrCreateSession(byte[] remoteMac, byte[] remoteIP, ushort remotePort)
        {
            var key = $"{FormatIP(remoteIP)}:{remotePort}-{FormatIP(_localIP)}:{_localPort}";

            if (!_sessions.TryGetValue(key, out var session))
            {
                session = new TcpSession()
                {
                    RemoteMac = remoteMac.ToArray(),
                    RemoteIP = remoteIP.ToArray(),
                    RemotePort = remotePort,
                    LocalMac = _localMac.ToArray(),
                    LocalIP = _localIP.ToArray(),
                    LocalPort = _localPort,
                    SendSeq = (uint)new Random().Next(1000, 100000),    // 随机初始序列号
                    State = TcpState.Listen
                };
                _sessions[key] = session;
            }

            return session;
        }
        
        /// <summary>
        /// 处理 SYN (第一次握手）
        /// </summary>
        /// <param name="session"></param>
        /// <param name="tcp"></param>
        private void HandleSyn(TcpSession session, TcpHeader tcp)
        { 
            Console.WriteLine($"[TCP] 收到SYN, 发送 SYN-ACK");

            session.RecvSeq = tcp.SequenceNumber + 1;   // 期望下一个序列号
            session.SendAck = session.RecvSeq;
            session.State = TcpState.SynReceived;
            
            // 发送 SYN-ACK
            SendTcpPacket(session, TcpFlags.SYN | TcpFlags.ACK, Array.Empty<byte>());
            session.SendSeq++;  // SYN 占用一个序列号
        }
        
        /// <summary>
        /// 处理 ACK （第三次握手后）
        /// </summary>
        /// <param name="session"></param>
        /// <param name="tcp"></param>
        private void HandleAckAfterSynAck(TcpSession session, TcpHeader tcp)
        {
            if (tcp.AcknowledgementNumber == session.SendSeq)
            {
                Console.WriteLine($"[TCP] 三次握手完成！建立连接");
                session.State = TcpState.Established;
            }
        }
        
        /// <summary>
        /// 处理数据或纯 ACK
        /// </summary>
        /// <param name="session"></param>
        /// <param name="tcp"></param>
        private void HandleDataOrAck(TcpSession session, TcpHeader tcp)
        {
            // 如果有数据
            if (tcp.PayLoad.Length > 0)
            {
                Console.WriteLine($"[TCP] 收到数据... 长度:{tcp.PayLoad.Length}");
                
                // 更新期望的序列号
                session.RecvSeq = tcp.SequenceNumber + (uint)tcp.PayLoad.Length;
                session.SendAck = session.RecvSeq;
                
                // 发送 ACK
                SendTcpPacket(session, TcpFlags.ACK, Array.Empty<byte>());
                
                // 触发数据接收事件
                OnDataReceived?.Invoke(session, tcp.PayLoad);
            }
        }

        /// <summary>
        /// 处理 FIN （连接关闭）
        /// </summary>
        /// <param name="session"></param>
        /// <param name="tcp"></param>
        private void HandleFin(TcpSession session, TcpHeader tcp)
        {
            Console.WriteLine($"[TCP] 收到FIN, 关闭连接");

            session.RecvSeq = tcp.SequenceNumber + 1;
            session.SendAck = session.RecvSeq;
            session.State = TcpState.CloseWait;
            
            // 发送 ACK
            SendTcpPacket(session, TcpFlags.ACK, Array.Empty<byte>());
            
            // 发送 FIN
            SendTcpPacket(session, TcpFlags.FIN | TcpFlags.ACK, Array.Empty<byte>());
            session.State = TcpState.LastAck;
        }

        /// <summary>
        /// 发送数据给客户端
        /// </summary>
        /// <param name="session"></param>
        /// <param name="data"></param>
        public void Send(TcpSession session, byte[] data)
        {
            if (session.State != TcpState.Established)
            {
                Console.WriteLine("[TCP] 连接未建立，无法发送数据");
                return;
            }

            SendTcpPacket(session, TcpFlags.PSH | TcpFlags.ACK, data);
            session.SendSeq += (uint)data.Length;
        }
        
        /// <summary>
        /// 构建并发送 TCP 数据包
        /// </summary>
        /// <param name="session"></param>
        /// <param name="flags"></param>
        /// <param name="data"></param>
        private void SendTcpPacket(TcpSession session, TcpFlags flags, byte[] data)
        {
            byte[] packet = RawTcpPacketBuilder.Build(
                session.LocalMac,
                session.RemoteMac,
                session.LocalIP,
                session.RemoteIP,
                session.LocalPort,
                session.RemotePort,
                session.SendSeq,
                session.SendAck,
                flags,
                data
            );

            try
            {
                // _device.SendPacket(packet);
                _sender.Send(packet);
            }
            catch (Exception)
            {

                Console.WriteLine("发送出错了！！");
            }
        }

        private static string FormatIP(byte[] ip) => $"{ip[0]}.{ip[1]}.{ip[2]}.{ip[3]}";
    }