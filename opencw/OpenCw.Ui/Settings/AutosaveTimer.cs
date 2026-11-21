using System;

namespace OpenCommonwealth.Services.Ui.Settings;

public sealed class AutosaveTimer
{
    private double _elapsed;

    public bool Tick(double seconds, int minutes, bool enabled, bool idle)
    {
        if (!enabled)
        {
            _elapsed = 0;
            return false;
        }
        if (!idle || !double.IsFinite(seconds) || seconds <= 0) return false;
        _elapsed += seconds;
        if (_elapsed < Math.Max(1, minutes) * 60.0) return false;
        _elapsed = 0;
        return true;
    }
}
