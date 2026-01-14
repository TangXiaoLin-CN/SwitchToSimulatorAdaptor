using System.Text;
using SwitchToSimulatorAdaptor.Common;
using SwitchToSimulatorAdaptor.Utils;

namespace SwitchToSimulatorAdaptor.EdenRoom;

/// <summary>
/// 数据包序列化类，对应 Eden 的 Network::Packet
/// </summary>
public class EdenNetworkPacket
{
    private readonly List<byte> _data = new();
    private int _readPos = 0;
    
    // 用于调试：暴露内部状态
    public int ReadPosition => _readPos;
    public int DataSize => _data.Count;
    public bool EndOfPacket => _readPos >= _data.Count;

    public void Append(byte[] data, int offset = 0, int count = -1)
    {
        if (count < 0) count = data.Length - offset;
        _data.AddRange(data.Skip(offset).Take(count));
    }

    public void Write(byte value) => _data.Add(value);
    public void Write(bool value) => _data.Add((byte)(value ? 1 : 0));

    public void Write(ushort value)
    {
        // 使用网络字节序（大端序），与 Eden 一致
        // htons: host to network short (16-bit)
        _data.Add((byte)((value >> 8) & 0xFF));   // 高字节
        _data.Add((byte)(value & 0xFF));          // 低字节
    }

    public void Write(uint value)
    {
        // 使用网络字节序（大端序），与 Eden 一致
        // htonl: host to network long (32-bit)
        _data.Add((byte)((value >> 24) & 0xFF));    // 最高字节
        _data.Add((byte)((value >> 16) & 0xFF));
        _data.Add((byte)((value >> 8) & 0xFF));
        _data.Add((byte)(value & 0xFF));            // 最低字节
    }

    private void Write(ulong value)
    {
        Write((uint)(value & 0xFFFFFFFF));
        Write((uint)(value >> 32) & 0xFFFFFFFF);
    }

    public void Write(string value)
    {
        // 处理 null 字符串，将其视为空字符串
        if (value == null)
        {
            value = "";
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        Write((uint)bytes.Length);
        _data.AddRange(bytes);
    }

    public void Write(byte[] value)
    {
        Write((uint)value.Length);
        _data.AddRange(value);
    }

    public void Write(IPv4Address ip)
    {
        Write(ip.A);
        Write(ip.B);
        Write(ip.C);
        Write(ip.D);
    }

    public byte ReadByte()
    {
        if (_readPos >= _data.Count) throw new EndOfStreamException();
        return _data[_readPos++];
    }

    public bool ReadBool() => ReadByte() != 0;

    public ushort ReadUInt16()
    {
        // 使用网络字节序（大端序），与 Eden 一致
        // ntohs: network to host short (16-bit)
        byte b1 = ReadByte();
        byte b2 = ReadByte();
        return (ushort)((b1 << 8) | b2);
    }
    
    public uint ReadUInt32()
    {
        // 使用网络字节序（大端序），与 Eden 一致
        // ntohl: network to host long (32-bit)
        byte b1 = ReadByte();
        byte b2 = ReadByte();
        byte b3 = ReadByte();
        byte b4 = ReadByte();
        return (uint)((b1 << 24) | (b2 << 16) | (b3 << 8) | b4);
    }

    public ulong ReadUInt64()
    {
        uint low = ReadUInt32();
        uint high = ReadUInt32();
        return (ulong)(high << 32) | low;
    }

    public string ReadString()
    {
        // 记录读取位置和剩余数据，用于调试
        int startPos = _readPos;
        int remaining = _data.Count - startPos;
        
        // 检查是否有足够的数据读取长度（至少需要 4 字节）
        if (remaining < 4)
        {
            Logger.Instance?.LogError($"[EdenPacket] ReadString: Not enough data for length! readPos = {_readPos}, dataSize = {_data.Count}, remaining = {remaining}");
            throw new EndOfStreamException($"Attempted to read string length (4 bytes), but only {remaining} bytes available.");
        }
        
        // 读取长度前，先记录前 4 个字节用于调试
        byte[] lengthBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            lengthBytes[i] = _data[_readPos + i];
        }

        string lengthHex = BitConverter.ToString(lengthBytes);
        
        // 尝试将字节解释位 ASCII 字符串（用于调试）
        string lengthAsAscii = "";
        for (int i = 0; i < 4; i++)
        {
            if (lengthBytes[i] >= 32 && lengthBytes[i] < 127)
            {
                lengthAsAscii += (char)lengthBytes[i];
            }
            else
            {
                lengthAsAscii += "?";
            }
        }

        uint length = ReadUInt32();
        
        // 检查长度是否合理（字符串长度不应该超过剩余数据，也不应该太大）
        if (length > int.MaxValue || length > remaining)
        {
            Logger.Instance?.LogError($"[EdenPacket] ReadString: Invalid length value! length={length} (0x{length:X}), readPos = {startPos}, remaining = {remaining}");
            Logger.Instance?.LogError($"[EdenPacket] ReadString: Length bytes(hex): {lengthHex} (ASCII: {lengthAsAscii})");
            Logger.Instance?.LogError( "[EdenPacket] ReadString: This might be a byte order issue, data corruption, or reading position misalignment");
            
            // 如果长度看起来像是 ASCII 字符， 可能是读取位置错位
            if (lengthAsAscii.All(c => c != '?' && char.IsLetterOrDigit(c)))
            {
                Logger.Instance?.LogError($"[EdenPacket] ReadString: Length bytes appear to be ASCII text. '{lengthAsAscii}', suggesting reading position misalignment");
            }

            throw new EndOfStreamException($"Invalid string length: {length} (0x{length:X}). Length bytes:{lengthHex} ('{lengthAsAscii}'). This might indicate a byte order issue or read position misalignment.");
        }

        // 检查是否有足够的数据
        if (_readPos + length > _data.Count)
        {
            Logger.Instance?.LogError($"[EdenPacket] ReadString: Not enough data! length{length}, readPos = {_readPos}, dataSize = {_data.Count}, remaining = {_data.Count - _readPos}");
            Logger.Instance?.LogError($"[EdenPacket] ReadString: Length bytes(hex): {lengthHex} (ASCII: {lengthAsAscii})");
            throw new EndOfStreamException($"Attempted to read string of length {length}, but only {_data.Count - _readPos} bytes available.");
        }

        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = ReadByte();
        }
        return Encoding.UTF8.GetString(bytes);
    }

    public byte[] ReadBytes()
    {
        uint length = ReadUInt32();
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = ReadByte();
        return bytes;
    }
    
    public IPv4Address ReadIPv4Address()
    {
        return new IPv4Address(ReadByte(), ReadByte(), ReadByte(), ReadByte());
    }

    public void IgnoreBytes(int count)
    {
        _readPos += count;
        if (_readPos > _data.Count) _readPos = _data.Count;
    }

    public byte[] GetData() => _data.ToArray();

    public void Clear()
    {
        _data.Clear();
        _readPos = 0;
    }
}