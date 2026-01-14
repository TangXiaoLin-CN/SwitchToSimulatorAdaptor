namespace SwitchToSimulatorAdaptor.SharpPcap;

    /// <summary>
    /// 以太网帧解析结果
    /// </summary>
    public class EthernetFrame
    {
        public byte[] DestinationMac { get; set; } = new byte[6]; 
        public byte[] SourceMac { get; set; } = new byte[6];
        public ushort EtherType { get; set; } 
        public byte[] Payload { get; set; } = []; 
        
        // 常见的 EtherType(以太网类型) 值
        public const ushort Ipv4 = 0x800;
        public const ushort Arp = 0x806;
        public const ushort Ipv6 = 0x86DD;
        
        public bool IsIPv4 => EtherType == Ipv4;
        public bool IsARP => EtherType == Arp;
        public bool IsIPv6 => EtherType == Ipv6;

        public string DestinationMacString => FormatMAC(DestinationMac);
        public string SourceMacString => FormatMAC(SourceMac);
        public bool IsBroadcast => DestinationMac.All(b => b == 0xFF); 

        private static string FormatMAC(byte[] mac)
            => string.Join(":", mac.Select(b => b.ToString("X2"))); 
        
        /// <summary>
        /// 从原始字节解析以太网帧
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static EthernetFrame? Parse(byte[] data)
        { 
            if (data.Length < 14) return null;

            var frame = new EthernetFrame();
            
            Array.Copy(data,0, frame.DestinationMac, 0, 6); 
            Array.Copy(data,6, frame.SourceMac, 0, 6); 

            frame.EtherType = (ushort)((data[12] << 8) | data[13]); 
            
            frame.Payload = new byte[data.Length - 14];
            Array.Copy(data, 14, frame.Payload, 0, frame.Payload.Length);
            
            return frame;
        }
    }
    
    /// <summary>
    /// Ipv4 头解析结果
    /// </summary>
    public class IPv4Header
    {
        public byte Version { get; set; }                               
        public byte IHL { get; set; }                                   
        public byte TOS { get; set; }                                   
        public ushort TotalLength { get; set; }                         
        public ushort Identification { get; set; }                      
        public byte Flags { get; set; }                                 
        public ushort FragmentOffset { get; set; }                      
        public byte TTL { get; set; }                                   
        public byte Protocol { get; set; }                              
        public ushort HeaderChecksum { get; set; }                      
        public byte[] SourceIP { get; set; } = new byte[4];             
        public byte[] DestinationIP { get; set; } = new byte[4];      
        public byte[] Payload { get; set; } = []; 
        
        // 常见协议号
        public const byte ICMP = 1; 
        public const byte TCP = 6;
        public const byte UDP = 17;
        
        public bool IsTCP => Protocol == TCP;
        public bool IsUDP => Protocol == UDP;

        public int HeaderLength => IHL * 4; // 实际头长度 （字节）
        
        public string SourceIPString => $"{SourceIP[0]}.{SourceIP[1]}.{SourceIP[2]}.{SourceIP[3]}";
        public string DestinationIPString => $"{DestinationIP[0]}.{DestinationIP[1]}.{DestinationIP[2]}.{DestinationIP[3]}";

        public static IPv4Header? Parse(byte[] data)
        {
            if (data.Length < 20) return null;
            
            var header = new IPv4Header();
            header.Version = (byte)(data[0] >> 4);
            header.IHL = (byte)(data[0] & 0x0F);
            
            if (header.Version != 4) return null;
            if (header.IHL < 5) return null;

            int headerLen = header.IHL * 4;
            if (data.Length < headerLen) return null;
            
            header.TOS = data[1];
            header.TotalLength = (ushort)((data[2] << 8) | data[3]);
            header.Identification = (ushort)((data[4] << 8) | data[5]);
            header.Flags = (byte)(data[6] >> 5);
            header.FragmentOffset = (ushort)((data[6] & 0x1F) << 8 | data[7]);
            header.TTL = data[8];
            header.Protocol = data[9];
            header.HeaderChecksum = (ushort)((data[10] << 8) | data[11]);
            Array.Copy(data, 12, header.SourceIP, 0, 4);
            Array.Copy(data, 16, header.DestinationIP, 0, 4);
            
            if (data.Length > headerLen)
            {
                header.Payload = new byte[data.Length - headerLen];
                Array.Copy(data, headerLen, header.Payload, 0, data.Length - headerLen);
            }

            return header;
        }
    }

    /// <summary>
    /// UDP 头解析结果
    /// </summary>
    public class UdpHeader
    {
        public ushort SourcePort { get; set; }
        public ushort DestinationPort { get; set; }
        public ushort Length { get; set; }
        public ushort Checksum { get; set; }
        public byte[] Payload { get; set; } = [];
        
        /// <summary>
        /// 从原始字节中解析 UDP 头
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static UdpHeader? Parse(byte[] data)
        { 
            if (data.Length < 8) return null;
            
            var header = new UdpHeader();
            header.SourcePort = (ushort)((data[0] << 8) | data[1]);
            header.DestinationPort = (ushort)((data[2] << 8) | data[3]);
            header.Length = (ushort)((data[4] << 8) | data[5]);
            header.Checksum = (ushort)((data[6] << 8) | data[7]);
            
            if (data.Length > 8)
            {
                header.Payload = new byte[data.Length - 8];
                Array.Copy(data, 8, header.Payload, 0, data.Length - 8);
            }

            return header;
        }
    }
    
    /// <summary>
    /// TCP 头解析结果
    /// </summary>
    public class TcpHeader
    { 
        public ushort SourcePort { get; set; }          
        public ushort DestinationPort { get; set; }     
        public uint SequenceNumber { get; set; }        
        public uint AcknowledgementNumber { get; set; } 
        public byte DataOffset { get; set; }            
        public byte Flags { get; set; }               
        public ushort Window { get; set; }              
        public ushort Checksum { get; set; }            
        public ushort UrgentPointer { get; set; }       
        public byte[] PayLoad { get; set; } = [];       
        
        // TCP 标志位
        public bool FIN => (Flags & 0x01) != 0;         
        public bool SYN => (Flags & 0x02) != 0;         
        public bool RST => (Flags & 0x04) != 0; 
        public bool PSH => (Flags & 0x08) != 0;   
        public bool ACK => (Flags & 0x10) != 0;      
        public bool URG => (Flags & 0x20) != 0;       
        
        public int HeaderLength => DataOffset * 4;

        public string FlagsString
        {
            get
            {
                var flags = new List<string>();
                if (SYN) flags.Add("SYN");
                if (ACK) flags.Add("ACK");
                if (FIN) flags.Add("FIN");
                if (RST) flags.Add("RST");
                if (PSH) flags.Add("PSH");
                if (URG) flags.Add("URG");
                return string.Join(",", flags);
            }
        }
        
        /// <summary>
        /// 从原始字节解析 TCP 头
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static TcpHeader? Parse(byte[] data)
        { 
            if (data.Length < 20) return null;
            
            var header = new TcpHeader();
            header.SourcePort = (ushort)((data[0] << 8) | data[1]);
            header.DestinationPort = (ushort)((data[2] << 8) | data[3]);
            header.SequenceNumber = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);
            header.AcknowledgementNumber = (uint)((data[8] << 24) | (data[9] << 16) | (data[10] << 8) | data[11]);
            header.DataOffset = (byte)(data[12] >> 4);
            header.Flags = data[13];
            header.Window = (ushort)((data[14] << 8) | data[15]);
            header.Checksum = (ushort)((data[16] << 8) | data[17]);
            header.UrgentPointer = (ushort)((data[18] << 8) | data[19]);
            
            int headerLen = header.DataOffset * 4;
            if (data.Length > headerLen)
            {
                header.PayLoad = new byte[data.Length - headerLen];
                Array.Copy(data, headerLen, header.PayLoad, 0, header.PayLoad.Length);
            }

            return header;
        }

    }