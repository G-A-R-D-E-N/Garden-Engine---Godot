using System;

namespace OpenCommonwealth.Services.Ui.Settings;

public sealed class RevertCountdown
{
    public bool IsActive { get; private set; }

    public double Remaining { get; private set; }

    public int WholeSecondsLeft => IsActive ? (int)Math.Ceiling(Remaining) : 0;

    public void Start(double seconds)
    {
        Remaining = Math.Max(seconds, 0);
        IsActive = true;
    }

    public void Stop()
    {
        IsActive = false;
        Remaining = 0;
    }

    public bool Tick(double deltaSeconds)
    {
        if (!IsActive || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return false;
        Remaining -= deltaSeconds;
        if (Remaining > 0) return false;
        Stop();
        return true;
    }
}
