using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Dialogs;

public sealed class SessionKeyedCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _values;
    private int _sessionId;
    private bool _initialized;

    public SessionKeyedCache(IEqualityComparer<TKey>? comparer = null) =>
        _values = new Dictionary<TKey, TValue>(comparer ?? EqualityComparer<TKey>.Default);

    public int Count => _values.Count;

    public bool SyncSession(int sessionId)
    {
        if (_initialized && sessionId == _sessionId) return false;
        _values.Clear();
        _sessionId = sessionId;
        _initialized = true;
        return true;
    }

    public bool TryGet(int sessionId, TKey key, out TValue value)
    {
        SyncSession(sessionId);
        if (_values.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }
        value = default!;
        return false;
    }

    public void Set(int sessionId, TKey key, TValue value)
    {
        SyncSession(sessionId);
        _values[key] = value;
    }

    public TValue GetOrResolve(
        int sessionId,
        TKey key,
        System.Func<int> currentSession,
        System.Func<(bool Cacheable, TValue Value)> resolve)
    {
        if (TryGet(sessionId, key, out var cached)) return cached;
        var (cacheable, value) = resolve();
        if (cacheable && currentSession() == sessionId) Set(sessionId, key, value);
        return value;
    }

    public void Clear() => _values.Clear();
}
