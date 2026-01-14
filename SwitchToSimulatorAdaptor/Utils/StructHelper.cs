using System.Runtime.InteropServices;

namespace SwitchToSimulatorAdaptor.Utils;

public static class StructHelper
{
    /// <summary>
    /// 结构体转字节数组
    /// </summary>
    /// <param name="structure"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static byte[] StructToBytes<T>(T structure) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] bytes = new byte[size];
            
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(structure, ptr, false);
            Marshal.Copy(ptr, bytes, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
            
        return bytes;
    }

    /// <summary>
    /// 字节数组转结构体
    /// </summary>
    /// <param name="bytes"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T BytesToStruct<T>(byte[] bytes) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        if (bytes.Length < size)
        {
            // 填充不足的部分
            var padded = new byte[size];
            Array.Copy(bytes, padded, bytes.Length);
            bytes = padded;
        }
            
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(bytes, 0, ptr, size);
            return Marshal.PtrToStructure<T>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}