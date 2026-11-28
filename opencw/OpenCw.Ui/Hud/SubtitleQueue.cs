using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Hud;

public readonly record struct SubtitleView(string Speaker, IReadOnlyList<string> Lines, double Alpha);

public sealed class SubtitleQueue
{
    public const int MaxPending = 32;

    private sealed class Entry
    {
        public string Speaker = "";
        public IReadOnlyList<string> Lines = Array.Empty<string>();
        public double Duration;
        public double Elapsed;
    }

    private readonly Func<string, double> _measure;
    private readonly int _maxLines;
    private readonly int _maxActive;
    private readonly List<Entry> _active = new();
    private readonly List<Entry> _pending = new();
    private readonly List<SubtitleView> _view = new();
    private bool _enabled = true;
    private double _maxWidth;

    public SubtitleQueue(Func<string, double> measureWidth, double maxWidth, int maxLines = 2, int maxActive = 2)
    {
        ArgumentNullException.ThrowIfNull(measureWidth);
        if (maxLines < 1) throw new ArgumentOutOfRangeException(nameof(maxLines));
        if (maxActive < 1) throw new ArgumentOutOfRangeException(nameof(maxActive));
        _measure = measureWidth;
        _maxLines = maxLines;
        _maxActive = maxActive;
        MaxWidth = maxWidth;
    }

    public double MaxWidth
    {
        get => _maxWidth;
        set
        {
            if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            _maxWidth = value;
        }
    }

    public double CharsPerSecond { get; set; } = 15;

    public double MinSeconds { get; set; } = 1.2;

    public double FadeSeconds { get; set; } = 0.15;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) Clear();
        }
    }

    public IReadOnlyList<SubtitleView> Visible => _view;

    public int PendingCount => _pending.Count;

    public void Add(string text, string? speaker = null, double? seconds = null)
    {
        if (!_enabled) return;
        var lines = TextWrap.Wrap(text, MaxWidth, _measure);
        if (lines.Count == 0) return;

        int totalChars = 0;
        foreach (var line in lines) totalChars += line.Length;
        double total = seconds is { } given && double.IsFinite(given) && given > 0
            ? given
            : Math.Max(MinSeconds, totalChars / Math.Max(1e-6, CharsPerSecond));

        for (int start = 0; start < lines.Count; start += _maxLines)
        {
            int count = Math.Min(_maxLines, lines.Count - start);
            var page = new string[count];
            int pageChars = 0;
            for (int i = 0; i < count; i++)
            {
                page[i] = lines[start + i];
                pageChars += page[i].Length;
            }
            double share = totalChars > 0 ? (double)pageChars / totalChars : 1;
            _pending.Add(new Entry { Speaker = speaker?.Trim() ?? "", Lines = page, Duration = Math.Max(0.5, total * share) });
        }
        Promote();
        if (_pending.Count > MaxPending) _pending.RemoveRange(0, _pending.Count - MaxPending);
        Rebuild();
    }

    public void Clear()
    {
        _active.Clear();
        _pending.Clear();
        Rebuild();
    }

    public void Tick(double dt)
    {
        if (double.IsFinite(dt) && dt > 0)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                _active[i].Elapsed += dt;
                if (_active[i].Elapsed >= _active[i].Duration) _active.RemoveAt(i);
            }
        }
        Promote();
        Rebuild();
    }

    private void Promote()
    {
        for (int i = 0; i < _pending.Count && _active.Count < _maxActive;)
        {
            var entry = _pending[i];
            if (_active.Exists(a => a.Speaker == entry.Speaker))
            {
                i++;
                continue;
            }
            _active.Add(entry);
            _pending.RemoveAt(i);
        }
    }

    private double AlphaOf(Entry entry)
    {
        if (!(FadeSeconds > 0)) return 1;
        return Math.Clamp(Math.Min(entry.Elapsed, entry.Duration - entry.Elapsed) / FadeSeconds, 0, 1);
    }

    private void Rebuild()
    {
        _view.Clear();
        foreach (var entry in _active) _view.Add(new SubtitleView(entry.Speaker, entry.Lines, AlphaOf(entry)));
    }
}
