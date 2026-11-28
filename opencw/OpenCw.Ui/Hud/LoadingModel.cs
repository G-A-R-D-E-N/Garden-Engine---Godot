using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Hud;

public enum LoadingPhase
{
    Idle,
    Loading,
    Finished,
    Cancelled,
}

public sealed class LoadingModel
{
    private readonly double _minDisplay;
    private readonly double _tipSeconds;
    private readonly List<string> _tips = new();
    private int _tipIndex;
    private double _tipTime;
    private Action? _onFinished;

    public LoadingModel(double minDisplaySeconds = 1.5, double tipSeconds = 6)
    {
        _minDisplay = double.IsFinite(minDisplaySeconds) ? Math.Max(0, minDisplaySeconds) : 0;
        _tipSeconds = double.IsFinite(tipSeconds) ? Math.Max(0, tipSeconds) : 0;
    }

    public LoadingPhase Phase { get; private set; }

    public bool IsVisible => Phase == LoadingPhase.Loading;

    public bool WorkComplete { get; private set; }

    public string Title { get; private set; } = "";

    public double Progress { get; private set; }

    public double Elapsed { get; private set; }

    public string Tip => _tips.Count == 0 ? "" : _tips[_tipIndex];

    public int TipNumber => _tips.Count == 0 ? 0 : _tipIndex + 1;

    public void Begin(string? title, IReadOnlyList<string>? tips = null, int firstTip = 0, Action? onFinished = null)
    {
        _tips.Clear();
        if (tips is not null)
            foreach (var tip in tips)
                if (!string.IsNullOrWhiteSpace(tip)) _tips.Add(tip.Trim());
        _tipIndex = _tips.Count == 0 ? 0 : ((firstTip % _tips.Count) + _tips.Count) % _tips.Count;
        _tipTime = 0;
        Title = title?.Trim() ?? "";
        Progress = 0;
        Elapsed = 0;
        WorkComplete = false;
        _onFinished = onFinished;
        Phase = LoadingPhase.Loading;
    }

    public void SetProgress(double fraction)
    {
        if (Phase != LoadingPhase.Loading || double.IsNaN(fraction)) return;
        Progress = Math.Max(Progress, Math.Clamp(fraction, 0, 1));
    }

    public void Complete()
    {
        if (Phase != LoadingPhase.Loading) return;
        WorkComplete = true;
        Progress = 1;
        TryFinish();
    }

    public bool Cancel()
    {
        if (Phase != LoadingPhase.Loading) return false;
        Phase = LoadingPhase.Cancelled;
        _onFinished = null;
        return true;
    }

    public void NextTip()
    {
        if (_tips.Count == 0) return;
        _tipIndex = (_tipIndex + 1) % _tips.Count;
        _tipTime = 0;
    }

    public void Tick(double dt)
    {
        if (Phase != LoadingPhase.Loading || !double.IsFinite(dt) || dt <= 0) return;
        Elapsed += dt;
        if (_tipSeconds > 0 && _tips.Count > 1)
        {
            _tipTime += dt;
            int steps = (int)(_tipTime / _tipSeconds);
            _tipTime -= steps * _tipSeconds;
            _tipIndex = (_tipIndex + steps) % _tips.Count;
        }
        TryFinish();
    }

    private void TryFinish()
    {
        if (!WorkComplete || Elapsed < _minDisplay) return;
        Phase = LoadingPhase.Finished;
        var callback = _onFinished;
        _onFinished = null;
        callback?.Invoke();
    }
}
