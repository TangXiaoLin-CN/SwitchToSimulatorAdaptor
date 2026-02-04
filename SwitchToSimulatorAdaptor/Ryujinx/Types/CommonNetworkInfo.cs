using System.Runtime.InteropServices;

namespace Ryujinx.Type
{
    [StructLayout(LayoutKind.Sequential, Size = 0x30)]
    public struct CommonNetworkInfo
    {
        public Array6<byte> MacAddress;
        public Ssid Ssid;
        public ushort Channel;
        public byte LinkLevel;
        public byte NetworkType;
        public uint Reserved;
    }
}
