using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Hud;

// Alpha 0 is clear, 1 is full black. Steps run in order; a fade rate is 1/duration so a partial fade takes proportionally less.
public sealed class FadeController
{
    private enum StepKind
    {
        ToBlack,
        Hold,
        ToClear,
    }

    private sealed class Step
    {
        public StepKind Kind;
        public double Duration;
        public double Remaining;
        public Action? Done;
    }

    private readonly Queue<Step> _steps = new();

    public FadeController(double initialAlpha = 0)
    {
        Alpha = double.IsFinite(initialAlpha) ? Math.Clamp(initialAlpha, 0, 1) : 0;
    }

    public double Alpha { get; private set; }

    public bool IsBlack => Alpha >= 1;

    public bool IsClear => Alpha <= 0;

    public bool IsBusy => _steps.Count > 0;

    public void FadeOut(double seconds, Action? onBlack = null) => Add(StepKind.ToBlack, seconds, onBlack);

    public void Hold(double seconds, Action? onDone = null) => Add(StepKind.Hold, seconds, onDone);

    public void FadeIn(double seconds, Action? onClear = null) => Add(StepKind.ToClear, seconds, onClear);

    public void Blink(double fadeOut, double hold, double fadeIn, Action? onBlack = null, Action? onClear = null)
    {
        FadeOut(fadeOut, onBlack);
        Hold(hold);
        FadeIn(fadeIn, onClear);
    }

    public void Cut(bool black)
    {
        _steps.Clear();
        Alpha = black ? 1 : 0;
    }

    public void Cancel() => _steps.Clear();

    public void Tick(double dt)
    {
        double left = double.IsFinite(dt) ? Math.Max(0, dt) : 0;
        while (_steps.Count > 0)
        {
            var step = _steps.Peek();
            if (!Consume(step, ref left)) return;
            _steps.Dequeue();
            step.Done?.Invoke();
        }
    }

    private void Add(StepKind kind, double seconds, Action? done)
    {
        double duration = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        _steps.Enqueue(new Step { Kind = kind, Duration = duration, Remaining = duration, Done = done });
    }

    private bool Consume(Step step, ref double left)
    {
        if (step.Kind == StepKind.Hold)
        {
            if (step.Remaining > left)
            {
                step.Remaining -= left;
                left = 0;
                return false;
            }
            left -= step.Remaining;
            step.Remaining = 0;
            return true;
        }

        double target = step.Kind == StepKind.ToBlack ? 1 : 0;
        if (step.Duration <= 0)
        {
            Alpha = target;
            return true;
        }
        double need = Math.Abs(target - Alpha) * step.Duration;
        if (need <= left)
        {
            Alpha = target;
            left -= need;
            return true;
        }
        Alpha += Math.Sign(target - Alpha) * left / step.Duration;
        left = 0;
        return false;
    }
}
