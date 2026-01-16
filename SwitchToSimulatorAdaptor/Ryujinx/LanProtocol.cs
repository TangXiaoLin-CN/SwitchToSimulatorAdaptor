using System.Runtime.InteropServices;

namespace Ryujinx
{
    internal class LanProtocol
    {
        private const uint LanMagic = 0x11451400;

        public const int BufferSize = 2048;
        public const int TcpTxBufferSize = 0x800;
        public const int TcpRxBufferSize = 0x1000;
        public const int TxBufferSizeMax = 0x2000;
        public const int RxBufferSizeMax = 0x2000;

        private readonly int _headerSize = Marshal.SizeOf<LanPacketHeader>();
        
        

        private LanPacketHeader PrepareHeader(LanPacketHeader header, LanPacketType type)
        {
            header.Magic = LanMagic;
            header.Type = type;
            header.Compressed = 0;
            header.Length = 0;
            header.DecompressLength = 0;
            header.Reserved = new Array2<byte>();

            return header;
        }

        private byte[] PreparePacket(LanPacketType type, byte[] data)
        {
            LanPacketHeader header = PrepareHeader(new LanPacketHeader(), type);
            header.Length = (ushort)data.Length;

            byte[] buf;
            if (data.Length > 0)
            {
                if (Compress(data, out byte[] compressed) == 0)
                {
                    header.DecompressLength = header.Length;
                    header.Length = (ushort)compressed.Length;
                    header.Compressed = 1;

                    buf = new byte[compressed.Length + _headerSize];

                    SpanHelpers.AsSpan<LanPacketHeader, byte>(ref header).ToArray().CopyTo(buf, 0);
                    compressed.CopyTo(buf, _headerSize);
                }
                else
                {
                    buf = new byte[data.Length + _headerSize];
                    SpanHelpers.AsSpan<LanPacketHeader, byte>(ref header).ToArray().CopyTo(buf, 0);
                    data.CopyTo(buf, _headerSize);
                }
            }
            else
            {
                buf = new byte[_headerSize];
                SpanHelpers.AsSpan<LanPacketHeader, byte>(ref header).ToArray().CopyTo(buf, 0);
            }

            return buf;
        }

        private int Compress(byte[] input, out byte[] output)
        {
            List<byte> outputList = [];
            int i = 0;
            int maxCount = 0xFF;

            while (i < input.Length)
            {
                byte inputByte = input[i++];
                int count = 0;

                if (inputByte == 0)
                {
                    while (i < input.Length && input[i] == 0 && count < maxCount)
                    {
                        count += 1;
                        i++;
                    }
                }

                if (inputByte == 0)
                {
                    outputList.Add(0);

                    if (outputList.Count == BufferSize)
                    {
                        output = null;

                        return -1;
                    }

                    outputList.Add((byte)count);
                }
                else
                {
                    outputList.Add(inputByte);
                }
            }

            output = outputList.ToArray();

            return i == input.Length ? 0 : -1;
        }

        private int Decompress(byte[] input, out byte[] output)
        {
            List<byte> outputList = [];
            int i = 0;

            while (i < input.Length && outputList.Count < BufferSize)
            {
                byte inputByte = input[i++];

                outputList.Add(inputByte);

                if (inputByte == 0)
                {
                    if (i == input.Length)
                    {
                        output = null;

                        return -1;
                    }

                    int count = input[i++];

                    for (int j = 0; j < count; j++)
                    {
                        if (outputList.Count == BufferSize)
                        {
                            break;
                        }

                        outputList.Add(inputByte);
                    }
                }
            }

            output = outputList.ToArray();

            return i == input.Length ? 0 : -1;
        }
    }
}
