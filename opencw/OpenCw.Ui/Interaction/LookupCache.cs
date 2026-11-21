using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread only; a failed read is never stored, so only a returned value (null included) is a confirmed answer.
public sealed class LookupCache<T>(Func<Exception, bool> isTransient, Action<string, Exception>? onFailure = null) where T : class
{
    private readonly Dictionary<string, T?> _items = new(StringComparer.Ordinal);

    public int Failures { get; private set; }

    public void Clear() => _items.Clear();

    public T? Get(string key, Func<T?> read)
    {
        if (_items.TryGetValue(key, out var known)) return known;
        try { return _items[key] = read(); }
        catch (Exception ex) when (isTransient(ex))
        {
            Failures++;
            onFailure?.Invoke(key, ex);
            return null;
        }
    }
}
