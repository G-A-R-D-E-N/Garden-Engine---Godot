using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Hud;

public enum ToastKind
{
    Info,
    Success,
    Discovery,
    Quest,
    Warning,
    Error,
}

public sealed record ToastOptions
{
    public int MaxVisible { get; init; } = 3;
    public int MaxPending { get; init; } = 12;
    public double FadeIn { get; init; } = 0.2;
    public double FadeOut { get; init; } = 0.35;
    public double HoldBase { get; init; } = 1.5;
    public double HoldPerChar { get; init; } = 0.06;
    public double MinHold { get; init; } = 2.5;
    public double MaxHold { get; init; } = 8;
}

// Enter is the fade-in progress 0..1, for a slide animation. Alpha covers fade-in and fade-out.
public readonly record struct ToastView(int Id, string Text, ToastKind Kind, int Count, double Alpha, double Enter)
{
    public string DisplayText => Count > 1 ? $"{Text} (x{Count})" : Text;
}

public sealed class ToastQueue
{
    private enum Phase
    {
        FadeIn,
        Hold,
        FadeOut,
        Done,
    }

    private sealed class Toast
    {
        public int Id;
        public string Text = "";
        public ToastKind Kind;
        public int Priority;
        public int Count = 1;
        public double Hold;
        public Phase Phase = Phase.FadeIn;
        public double Time;
    }

    private readonly ToastOptions _options;
    private readonly List<Toast> _visible = new();
    private readonly List<Toast> _pending = new();
    private readonly List<ToastView> _view = new();
    private int _nextId = 1;

    public ToastQueue(ToastOptions? options = null)
    {
        _options = options ?? new ToastOptions();
        if (_options.MaxVisible < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxVisible must be at least 1.");
        if (_options.MaxPending < 0) throw new ArgumentOutOfRangeException(nameof(options), "MaxPending cannot be negative.");
    }

    public IReadOnlyList<ToastView> Visible => _view;

    public int PendingCount => _pending.Count;

    public static int PriorityOf(ToastKind kind) => kind switch
    {
        ToastKind.Error => 5,
        ToastKind.Warning => 4,
        ToastKind.Quest => 3,
        ToastKind.Discovery => 2,
        ToastKind.Success => 1,
        _ => 0,
    };

    public void Show(string text) => Show(text, ToastKind.Info);

    public void Show(string text, ToastKind kind) => Enqueue(text, kind);

    public int Enqueue(string text, ToastKind kind, int? priority = null, double? holdSeconds = null)
    {
        string trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0) return -1;
        int rank = priority ?? PriorityOf(kind);

        var existing = Find(_visible, trimmed, kind) ?? Find(_pending, trimmed, kind);
        if (existing is not null)
        {
            existing.Count++;
            existing.Priority = Math.Max(existing.Priority, rank);
            if (existing.Phase is Phase.Hold or Phase.FadeOut)
            {
                existing.Phase = Phase.Hold;
                existing.Time = 0;
            }
            Settle();
            return existing.Id;
        }

        var toast = new Toast { Id = _nextId++, Text = trimmed, Kind = kind, Priority = rank, Hold = HoldFor(trimmed, kind, holdSeconds) };
        _pending.Add(toast);
        Settle();
        return toast.Id;
    }

    public void Dismiss(int id)
    {
        if (_pending.RemoveAll(t => t.Id == id) > 0) Rebuild();
        foreach (var toast in _visible)
            if (toast.Id == id && toast.Phase != Phase.FadeOut) StartFadeOut(toast);
        Rebuild();
    }

    public void Clear()
    {
        _visible.Clear();
        _pending.Clear();
        Rebuild();
    }

    public void Tick(double dt)
    {
        if (dt > 0)
        {
            for (int i = _visible.Count - 1; i >= 0; i--)
            {
                Advance(_visible[i], dt);
                if (_visible[i].Phase == Phase.Done) _visible.RemoveAt(i);
            }
        }
        Promote();
        Rebuild();
    }

    private static Toast? Find(List<Toast> list, string text, ToastKind kind)
    {
        foreach (var toast in list)
            if (toast.Kind == kind && toast.Phase != Phase.Done && string.Equals(toast.Text, text, StringComparison.Ordinal)) return toast;
        return null;
    }

    private double HoldFor(string text, ToastKind kind, double? explicitHold)
    {
        if (explicitHold is { } given && double.IsFinite(given) && given > 0) return given;
        double hold = _options.HoldBase + _options.HoldPerChar * text.Length;
        if (kind is ToastKind.Warning or ToastKind.Error) hold *= 1.5;
        return Math.Clamp(hold, _options.MinHold, Math.Max(_options.MinHold, _options.MaxHold));
    }

    private void TrimPending()
    {
        while (_pending.Count > _options.MaxPending && _pending.Count > 0)
        {
            int worst = 0;
            for (int i = 1; i < _pending.Count; i++)
                if (_pending[i].Priority < _pending[worst].Priority) worst = i;
            _pending.RemoveAt(worst);
        }
    }

    private void Settle()
    {
        Promote();
        TrimPending();
        Preempt();
        Rebuild();
    }

    private void Promote()
    {
        while (_visible.Count < _options.MaxVisible && _pending.Count > 0)
        {
            int best = 0;
            for (int i = 1; i < _pending.Count; i++)
                if (_pending[i].Priority > _pending[best].Priority) best = i;
            _visible.Add(_pending[best]);
            _pending.RemoveAt(best);
        }
    }

    private void Preempt()
    {
        if (_pending.Count == 0 || _visible.Count < _options.MaxVisible) return;
        Toast? victim = null;
        int fading = 0;
        foreach (var toast in _visible)
        {
            if (toast.Phase == Phase.FadeOut) fading++;
            else if (victim is null || toast.Priority < victim.Priority) victim = toast;
        }
        if (victim is null) return;

        int outranking = 0;
        foreach (var toast in _pending)
            if (toast.Priority > victim.Priority) outranking++;
        if (outranking > fading) StartFadeOut(victim);
    }

    private void StartFadeOut(Toast toast)
    {
        double alpha = AlphaOf(toast);
        toast.Phase = Phase.FadeOut;
        toast.Time = _options.FadeOut * (1 - alpha);
    }

    private void Advance(Toast toast, double dt)
    {
        toast.Time += dt;
        while (true)
        {
            switch (toast.Phase)
            {
                case Phase.FadeIn:
                    if (toast.Time < _options.FadeIn) return;
                    toast.Time -= _options.FadeIn;
                    toast.Phase = Phase.Hold;
                    break;
                case Phase.Hold:
                    if (toast.Time < toast.Hold) return;
                    toast.Time -= toast.Hold;
                    toast.Phase = Phase.FadeOut;
                    break;
                case Phase.FadeOut:
                    if (toast.Time < _options.FadeOut) return;
                    toast.Phase = Phase.Done;
                    return;
                default:
                    return;
            }
        }
    }

    private double AlphaOf(Toast toast) => toast.Phase switch
    {
        Phase.FadeIn => _options.FadeIn > 0 ? Math.Min(1, toast.Time / _options.FadeIn) : 1,
        Phase.FadeOut => _options.FadeOut > 0 ? Math.Max(0, 1 - toast.Time / _options.FadeOut) : 0,
        Phase.Done => 0,
        _ => 1,
    };

    private double EnterOf(Toast toast) => toast.Phase == Phase.FadeIn
        ? (_options.FadeIn > 0 ? Math.Min(1, toast.Time / _options.FadeIn) : 1)
        : 1;

    private void Rebuild()
    {
        _view.Clear();
        foreach (var toast in _visible)
            _view.Add(new ToastView(toast.Id, toast.Text, toast.Kind, toast.Count, AlphaOf(toast), EnterOf(toast)));
    }
}
