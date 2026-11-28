namespace OpenCommonwealth.Services;

public sealed class BoundedLru<TKey, TValue> where TKey : notnull
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new();
    private int _sessionId = -1;

    public BoundedLru(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(comparer);
    }

    public int Count { get { lock (_gate) return _map.Count; } }

    public bool TryGet(TKey key, out TValue value) => TryGet(0, key, out value);

    public bool TryGet(int sessionId, TKey key, out TValue value)
    {
        lock (_gate)
        {
            EnsureSessionLocked(sessionId);
            if (!_map.TryGetValue(key, out var node)) { value = default!; return false; }
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(TKey key, TValue value) => Set(0, key, value);

    public void Set(int sessionId, TKey key, TValue value)
    {
        lock (_gate)
        {
            EnsureSessionLocked(sessionId);
            if (_map.TryGetValue(key, out var node)) _order.Remove(node);
            else if (_map.Count >= _capacity)
            {
                _map.Remove(_order.Last!.Value.Key);
                _order.RemoveLast();
            }
            _map[key] = _order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _order.Clear();
            _sessionId = -1;
        }
    }

    private void EnsureSessionLocked(int sessionId)
    {
        if (_sessionId == sessionId) return;
        _map.Clear();
        _order.Clear();
        _sessionId = sessionId;
    }
}
