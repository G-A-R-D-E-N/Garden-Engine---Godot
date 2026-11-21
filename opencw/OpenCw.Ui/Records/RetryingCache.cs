using System;
using System.Collections.Concurrent;

namespace OpenCommonwealth.Services.Ui.Records;

// Requires: read is idempotent; thread-safe. A complete value is kept; an incomplete one is read again on the next call, up to attempts reads, then the last value is kept so a permanent gap does not repeat the read for ever.
public sealed class RetryingCache<TKey, TValue>(Func<TKey, TValue> read, Func<TValue, bool> complete, int attempts = 3)
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _kept = new();
    private readonly ConcurrentDictionary<TKey, int> _reads = new();

    public TValue Get(TKey key)
    {
        if (_kept.TryGetValue(key, out var hit)) return hit;
        var value = read(key);
        if (complete(value) || _reads.AddOrUpdate(key, 1, (_, n) => n + 1) >= attempts) _kept[key] = value;
        return value;
    }

    public void Clear()
    {
        _kept.Clear();
        _reads.Clear();
    }
}
