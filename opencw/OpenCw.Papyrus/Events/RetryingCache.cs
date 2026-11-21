namespace OpenCommonwealth.Services.Papyrus.Events;

public sealed class RetryingCache<T>(long retryMilliseconds)
{
    private readonly Dictionary<string, (T Value, long RetryAt)> _items = new(StringComparer.OrdinalIgnoreCase);

    public T Get(string key, long nowMilliseconds, Func<(T Value, bool Complete)> read)
    {
        if (_items.TryGetValue(key, out var hit) && (hit.RetryAt == long.MaxValue || nowMilliseconds < hit.RetryAt)) return hit.Value;
        var (value, complete) = read();
        _items[key] = (value, complete ? long.MaxValue : nowMilliseconds + retryMilliseconds);
        return value;
    }

    public void Clear() => _items.Clear();
}
