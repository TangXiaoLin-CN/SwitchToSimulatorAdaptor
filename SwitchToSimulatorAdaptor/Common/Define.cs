namespace SwitchToSimulatorAdaptor.Common;

public struct IPv4Address(byte a, byte b, byte c, byte d)
{
    public byte A = a, B = b, C = c, D = d;

    public IPv4Address(byte[] bytes) : this(bytes[0], bytes[1], bytes[2], bytes[3]) { }
        
    public byte[] ToBytes() => new byte[] { A, B, C, D };
    public override string ToString() => $"{A}.{B}.{C}.{D}";
    public bool IsEmpty => A == 0 && B == 0 && C == 0 && D == 0;
}