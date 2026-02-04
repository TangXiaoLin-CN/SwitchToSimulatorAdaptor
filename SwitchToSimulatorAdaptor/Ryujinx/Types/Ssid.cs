using System.Runtime.InteropServices;

namespace Ryujinx.Type
{
    [StructLayout(LayoutKind.Sequential, Size = 0x22)]
    public struct Ssid
    {
        public byte Length;
        public Array33<byte> Name;
    }
}
