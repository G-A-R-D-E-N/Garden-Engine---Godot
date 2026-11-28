using System.Collections.Generic;

namespace OpenCommonwealth.Session;

public sealed class SessionStampedLookup<TKey, TValue> where TKey : notnull
{
    private int _sessionId = -1;
    private readonly Dictionary<TKey, TValue?> _values;

    public SessionStampedLookup(IEqualityComparer<TKey>? comparer = null) =>
        _values = new Dictionary<TKey, TValue?>(comparer ?? EqualityComparer<TKey>.Default);

    public int EnterSession(int sessionId)
    {
        if (_sessionId != sessionId)
        {
            _values.Clear();
            _sessionId = sessionId;
        }
        return sessionId;
    }

    public bool TryGet(TKey key, out TValue? value) => _values.TryGetValue(key, out value);

    public void Store(TKey key, TValue? value, int sessionAtEntry, int sessionNow, bool persist)
    {
        if (persist && sessionAtEntry == sessionNow)
            _values[key] = value;
    }
}