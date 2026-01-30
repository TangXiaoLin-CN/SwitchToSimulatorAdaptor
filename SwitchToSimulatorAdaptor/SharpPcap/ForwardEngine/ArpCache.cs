namespace SwitchToSimulatorAdaptor.ForwardEngine;

public class ArpCache
{
    private readonly record struct ArpEntry(byte[] Mac, DateTime ExpireAt);

    private readonly Dictionary<uint, ArpEntry> _cache = new();
    private readonly TimeSpan _ttl;
    private readonly int _maxSize;
    private readonly object _lock = new();

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

    public ArpCache(TimeSpan? ttl = null, int maxSize = AppSetting.ArpCacheSize)
    {
        _ttl = ttl ?? TimeSpan.FromSeconds(AppSetting.ArpTtlSeconds);
        _maxSize = maxSize;
    }

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

    public bool HasIp(ReadOnlySpan<byte> ip)
    {
        var key = ByteHelper.IpToUint(ip);
        lock (_lock)
        {
            return _cache.TryGetValue(key, out var entry) && entry.ExpireAt > DateTime.UtcNow;
        }
    }

    public void Set(ReadOnlySpan<byte> mac, ReadOnlySpan<byte> ip)
    {
        if (ip[0] == 0 && ip[1] == 0 && ip[2] == 0 && ip[3] == 0) return;

        var key = ByteHelper.IpToUint(ip);
        var macArray = mac.Slice(0, 6).ToArray();

        lock (_lock)
        {
            if (_cache.Count >= _maxSize && !_cache.ContainsKey(key))
            {
                RemoveOldestEntry();
            }
            
            _cache[key] = new ArpEntry(macArray, DateTime.UtcNow + _ttl);
        }
    }

    public bool Remove(ReadOnlySpan<byte> ip)
    {
        var key = ByteHelper.IpToUint(ip);
        lock (_lock)
        {
            return _cache.Remove(key);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }

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

    private void RemoveOldestEntry()
    {
        if (_cache.Count == 0) return;

        var oldest = _cache.MinBy(kvp => kvp.Value.ExpireAt);
        _cache.Remove(oldest.Key);
    }
}