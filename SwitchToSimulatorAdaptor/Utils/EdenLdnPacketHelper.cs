using SwitchToSimulatorAdaptor.Common;
using SwitchToSimulatorAdaptor.EdenRoom;
using SwitchToSimulatorAdaptor.SwitchLdn;
using ZstdSharp;

namespace SwitchToSimulatorAdaptor.Utils;

public static class EdenLdnPacketHelper
{
    private static Compressor _compressor = new();
    private static Decompressor _decompressor = new();

    public static bool TryEncodePacket(SwitchPacket? switchPacket, out EdenLDNPacket? edenProxyPacket)
    {
        edenProxyPacket = null;
        if (switchPacket == null) return false;
        
        
        
        return true;
    }
    
    private static byte[] CompressData(byte[] data)
    {
        try
        {
            return _compressor.Wrap(data).ToArray();
        }
        catch (Exception ex)
        {
            Logger.Instance?.LogError($"[EdenProxyPacketHelper] 压缩数据失败: {ex.Message}");
            return data;
        }
    }

    private static byte[]? DecompressData(byte[] compressedData)
    {
        try
        {
            return _decompressor.Unwrap(compressedData).ToArray();
        }
        catch (Exception ex)
        {
            Logger.Instance?.LogError($"[EdenProxyPacketHelper] 解压数据失败: {ex.Message}");
            return null;
        }
    }
}