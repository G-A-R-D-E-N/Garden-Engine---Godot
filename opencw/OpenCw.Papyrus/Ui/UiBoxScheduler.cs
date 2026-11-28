using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenCommonwealth.Services.Papyrus.Ui;

/// <summary>One box a script or the engine wants shown; identity matters, so this is a class.</summary>
public sealed class UiBoxRequest
{
    public UiBoxRequest(string text, Action<int>? answer = null, CancellationToken cancelled = default)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Answer = answer;
        Cancelled = cancelled;
    }

    public string Text { get; }

    public Action<int>? Answer { get; }

    public CancellationToken Cancelled { get; }
}

/// <summary>
/// Shows boxes one at a time in arrival order. Enqueue is safe from any thread; Pump, Answered and
/// NoteClosed belong to the thread that owns the box control.
/// </summary>
public sealed class UiBoxScheduler
{
    public const int MaxQueued = 64;

    private readonly object _gate = new();
    private readonly LinkedList<UiBoxRequest> _pending = new();
    private readonly Func<UiBoxRequest, bool> _present;
    private readonly Action _close;
    private UiBoxRequest? _active;
    private bool _closedSeen;
    private bool _stopped;
    private int _dropped;

    // Requires: present returns false (and shows nothing) while the box control is busy; close is safe to call when nothing is open.
    public UiBoxScheduler(Func<UiBoxRequest, bool> present, Action close)
    {
        _present = present ?? throw new ArgumentNullException(nameof(present));
        _close = close ?? throw new ArgumentNullException(nameof(close));
    }

    public int Queued
    {
        get
        {
            lock (_gate) return _pending.Count;
        }
    }

    public int Dropped
    {
        get
        {
            lock (_gate) return _dropped;
        }
    }

    public UiBoxRequest? Active
    {
        get
        {
            lock (_gate) return _active;
        }
    }

    public bool Enqueue(UiBoxRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_stopped) return false;
            if (_pending.Count >= MaxQueued)
            {
                _dropped++;
                return false;
            }
            _pending.AddLast(request);
            return true;
        }
    }

    /// <summary>Closes a cancelled or dismissed active box, then shows the next waiting one.</summary>
    public void Pump()
    {
        UiBoxRequest? dismissed = null;
        var close = false;
        lock (_gate)
        {
            if (_stopped) return;
            if (_active is { } active)
            {
                if (active.Cancelled.IsCancellationRequested)
                {
                    close = true;
                    Release();
                }
                else if (_closedSeen)
                {
                    dismissed = active;
                    Release();
                }
            }
        }
        if (close) _close();
        dismissed?.Answer?.Invoke(UiServices.Dismissed);
        PresentNext();
    }

    /// <summary>The control reported a choice for this request; a stale or cancelled request is ignored.</summary>
    public void Answered(UiBoxRequest request, int choice)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, request)) return;
            Release();
        }
        if (!request.Cancelled.IsCancellationRequested) request.Answer?.Invoke(choice);
    }

    // Requires: a close with an answer reports the answer in the same call stack, so a close alone is judged on the next Pump.
    public void NoteClosed()
    {
        lock (_gate)
        {
            if (_active is not null) _closedSeen = true;
        }
    }

    /// <summary>Ends the session: drops every queued box, closes the open one, and answers nothing.</summary>
    public void Clear()
    {
        bool hadActive;
        lock (_gate)
        {
            _stopped = true;
            hadActive = _active is not null;
            Release();
            _pending.Clear();
        }
        if (hadActive) _close();
    }

    private void PresentNext()
    {
        UiBoxRequest next;
        lock (_gate)
        {
            if (_stopped || _active is not null) return;
            while (_pending.First is { } head && head.Value.Cancelled.IsCancellationRequested) _pending.RemoveFirst();
            if (_pending.First is not { } first) return;
            next = first.Value;
            _pending.RemoveFirst();
            _active = next;
            _closedSeen = false;
        }
        bool shown;
        try
        {
            shown = _present(next);
        }
        catch
        {
            Answered(next, UiServices.Dismissed);
            throw;
        }
        if (!shown) Return(next);
    }

    private void Return(UiBoxRequest request)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, request)) return;
            Release();
            if (!_stopped) _pending.AddFirst(request);
        }
    }

    private void Release()
    {
        _active = null;
        _closedSeen = false;
    }
}
