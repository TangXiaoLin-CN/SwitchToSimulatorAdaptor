using System.Runtime.InteropServices;

namespace Ryujinx.Type
{
    [StructLayout(LayoutKind.Sequential, Size = 0x10)]
    public struct IntentId
    {
        public long LocalCommunicationId;
        public ushort Reserved1;
        public ushort SceneId;
        public uint Reserved2;
    }
}
