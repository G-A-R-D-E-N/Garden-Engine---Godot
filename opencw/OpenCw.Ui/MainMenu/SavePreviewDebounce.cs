using System;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public sealed class SavePreviewDebounce
{
    public const double DelaySeconds = 0.2;

    private string? _pending;
    private double _wait;

    public string? ShownId { get; private set; }

    public void Select(string? id)
    {
        if (id == ShownId && _pending is null)
            return;
        ShownId = null;
        _pending = id;
        _wait = 0;
    }

    public bool Tick(double dt)
    {
        if (_pending is null || !double.IsFinite(dt) || dt <= 0)
            return false;
        _wait += dt;
        return _wait >= DelaySeconds && Flush();
    }

    public bool Flush()
    {
        if (_pending is null)
            return false;
        ShownId = _pending;
        _pending = null;
        return true;
    }
}
