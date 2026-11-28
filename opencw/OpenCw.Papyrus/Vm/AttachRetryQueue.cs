using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Papyrus.Vm;

// Requires: Tick runs outside a native callback, because an attempt attaches and initialises scripts.
public sealed class AttachRetryQueue
{
    public const double MaxSeconds = 30;
    public const int Capacity = 256;

    private sealed record Entry(string Key, Func<string?> Attempt, double Age);

    private readonly List<Entry> _pending = new();

    public int Pending => _pending.Count;

    public int Abandoned { get; private set; }

    public int Recovered { get; private set; }

    public string? LastAbandonReason { get; private set; }

    // Invariant: an attempt returns null once attached, otherwise the reason the reference is not present yet.
    public bool Enqueue(string key, Func<string?> attempt)
    {
        if (_pending.Exists(entry => entry.Key == key)) return true;
        if (_pending.Count >= Capacity)
        {
            Abandoned++;
            LastAbandonReason = $"the attach retry queue is full, so {key} was not queued";
            return false;
        }
        _pending.Add(new Entry(key, attempt, 0));
        return true;
    }

    // Invariant: an entry that an earlier attempt removed or a Clear dropped does not run; the entry is found again by key after its attempt.
    public int Tick(double seconds)
    {
        var recovered = 0;
        foreach (var entry in _pending.ToArray())
        {
            if (!_pending.Contains(entry)) continue;
            string? reason;
            try { reason = entry.Attempt(); }
            catch (Exception failure)
            {
                if (_pending.Remove(entry)) Abandon($"{entry.Key}: {failure.Message}");
                continue;
            }
            var index = _pending.FindIndex(pending => pending.Key == entry.Key);
            if (index < 0)
            {
                if (reason is null) recovered++;
                continue;
            }
            if (reason is null)
            {
                _pending.RemoveAt(index);
                recovered++;
                continue;
            }
            var age = entry.Age + seconds;
            if (age >= MaxSeconds)
            {
                _pending.RemoveAt(index);
                Abandon($"{entry.Key}: still not present after {age:0.#} seconds: {reason}");
            }
            else _pending[index] = entry with { Age = age };
        }
        Recovered += recovered;
        return recovered;
    }

    public void Clear() => _pending.Clear();

    private void Abandon(string reason)
    {
        Abandoned++;
        LastAbandonReason = reason;
    }
}
