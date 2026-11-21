using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Records;

// Requires: main thread only. Keeps the most recently used values within budget bytes (the newest entry is always kept) and remembers a failed key only until retryAfter has passed on clock, so a repaired source is read again.
public sealed class SizedCache<TValue>(long budget, Func<long> clock, long retryAfter) where TValue : class
{
    private sealed record Entry(string Key, TValue? Value, long Size, long RetryAt);

    private readonly Dictionary<string, LinkedListNode<Entry>> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> _recent = new();

    public long Bytes { get; private set; }

    public int Count => _nodes.Count;

    public bool TryGet(string key, out TValue? value)
    {
        value = null;
        if (!_nodes.TryGetValue(key, out var node)) return false;
        if (node.Value.Value is null && clock() >= node.Value.RetryAt)
        {
            Remove(node);
            return false;
        }
        _recent.Remove(node);
        _recent.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    public void Add(string key, TValue value, long size) => Put(new Entry(key, value, Math.Max(0, size), 0));

    public void Fail(string key) => Put(new Entry(key, null, 0, clock() + retryAfter));

    public void Clear()
    {
        _nodes.Clear();
        _recent.Clear();
        Bytes = 0;
    }

    private void Put(Entry entry)
    {
        if (_nodes.TryGetValue(entry.Key, out var old)) Remove(old);
        _nodes[entry.Key] = _recent.AddFirst(entry);
        Bytes += entry.Size;
        while (Bytes > budget && _recent.Count > 1) Remove(_recent.Last!);
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _recent.Remove(node);
        _nodes.Remove(node.Value.Key);
        Bytes -= node.Value.Size;
    }
}
