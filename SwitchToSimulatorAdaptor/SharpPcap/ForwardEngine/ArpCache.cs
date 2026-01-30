namespace SwitchToSimulatorAdaptor.ForwardEngine;

/// <summary>
/// ARP 缓存
/// 来源: switch-lan-play/src/arp.c
/// </summary>
public class ArpCache
{
    private readonly record struct ArpEntry(byte[] Mac, DateTime ExpireAt);

    private readonly Dictionary<uint, ArpEntry> _cache = new();
    private readonly TimeSpan _ttl;
    private readonly int _maxSize;
    private readonly object _lock = new();

    /// <summary>
    /// 创建 ARP 缓存
    /// </summary>
    /// <param name="ttl">条目生存时间</param>
    /// <param name="maxSize">最大条目数</param>
    public ArpCache(TimeSpan? ttl = null, int maxSize = 100)
    {
        _ttl = ttl ?? TimeSpan.FromSeconds(30);
        _maxSize = maxSize;
    }

    /// <summary>
    /// 尝试获取 IP 对应的 MAC 地址
    /// </summary>
    public bool TryGetMac(ReadOnlySpan<byte> ip, out byte[] mac)
    {
        var key = ByteHelper.IpToUint(ip);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry) && entry.ExpireAt > DateTime.UtcNow)
            {
                mac = entry.Mac;
                return true;
            }
        }
        mac = Array.Empty<byte>();
        return false;
    }

    /// <summary>
    /// 检查是否存在指定 IP
    /// </summary>
    public bool HasIp(ReadOnlySpan<byte> ip)
    {
        var key = ByteHelper.IpToUint(ip);
        lock (_lock)
        {
            return _cache.TryGetValue(key, out var entry) && entry.ExpireAt > DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 设置 IP 到 MAC 的映射
    /// </summary>
    public void Set(ReadOnlySpan<byte> mac, ReadOnlySpan<byte> ip)
    {
        // 忽略全零 IP
        if (ip[0] == 0 && ip[1] == 0 && ip[2] == 0 && ip[3] == 0)
            return;

        var key = ByteHelper.IpToUint(ip);
        var macArray = mac.Slice(0, 6).ToArray();

        lock (_lock)
        {
            // 如果缓存已满，删除最旧的条目
            if (_cache.Count >= _maxSize && !_cache.ContainsKey(key))
            {
                RemoveOldestEntry();
            }

            _cache[key] = new ArpEntry(macArray, DateTime.UtcNow + _ttl);
        }
    }

    /// <summary>
    /// 删除指定 IP 的缓存
    /// </summary>
    public bool Remove(ReadOnlySpan<byte> ip)
    {
        var key = ByteHelper.IpToUint(ip);
        lock (_lock)
        {
            return _cache.Remove(key);
        }
    }

    /// <summary>
    /// 清空缓存
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }

    /// <summary>
    /// 获取所有有效的条目
    /// </summary>
    public List<(byte[] Ip, byte[] Mac)> GetAllValid()
    {
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            return _cache
                .Where(kvp => kvp.Value.ExpireAt > now)
                .Select(kvp => (ByteHelper.UintToIp(kvp.Key), kvp.Value.Mac))
                .ToList();
        }
    }

    /// <summary>
    /// 获取缓存条目数量
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _cache.Count;
            }
        }
    }

    /// <summary>
    /// 清理过期条目
    /// </summary>
    public int CleanupExpired()
    {
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            var expired = _cache
                .Where(kvp => kvp.Value.ExpireAt <= now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expired)
            {
                _cache.Remove(key);
            }

            return expired.Count;
        }
    }

    /// <summary>
    /// 删除最旧的条目
    /// </summary>
    private void RemoveOldestEntry()
    {
        if (_cache.Count == 0)
            return;

        var oldest = _cache.MinBy(kvp => kvp.Value.ExpireAt);
        _cache.Remove(oldest.Key);
    }
}