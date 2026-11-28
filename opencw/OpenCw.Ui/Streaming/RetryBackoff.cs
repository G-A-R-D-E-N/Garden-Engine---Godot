using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Streaming;

public sealed class RetryBackoff<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, long> _until = new();
    private readonly List<TKey> _scratch = new();

    public bool BackedOff(TKey key, long frame) => _until.TryGetValue(key, out var until) && frame < until;

    public void Fail(TKey key, long frame, int frames) => _until[key] = frame + frames;

    public void Clear(TKey key) => _until.Remove(key);

    public void Prune(ICollection<TKey> keep)
    {
        _scratch.Clear();
        foreach (var key in _until.Keys)
            if (!keep.Contains(key)) _scratch.Add(key);
        foreach (var key in _scratch) _until.Remove(key);
    }
}
