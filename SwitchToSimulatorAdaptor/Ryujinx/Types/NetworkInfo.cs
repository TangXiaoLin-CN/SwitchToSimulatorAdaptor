using System.Runtime.InteropServices;

namespace Ryujinx.Type
{
    [StructLayout(LayoutKind.Sequential, Size = 0x480)]
    public struct NetworkInfo
    {
        public NetworkId NetworkId;
        public CommonNetworkInfo Common;
        public LdnNetworkInfo Ldn;
    }
}
