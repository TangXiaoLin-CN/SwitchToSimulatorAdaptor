using System.Runtime.InteropServices;

namespace Ryujinx.Type
{
    [StructLayout(LayoutKind.Sequential, Size = 0x20)]
    public struct NetworkId
    {
        public IntentId IntentId;
        public Array16<byte> SessionId;
    }
}
