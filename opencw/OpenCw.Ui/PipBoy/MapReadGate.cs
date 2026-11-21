namespace OpenCommonwealth.Services.Ui.PipBoy;

// Requires: main thread only; a kept read lasts until the identity changes or Reset, a failed read is retried on every requested call and a bounded number of background calls.
public sealed class MapReadGate
{
    public const int MaxBackgroundFailures = 3;

    private string? _identity;
    private bool _loaded;
    private int _failures;

    public bool ShouldRead(string identity, bool requested)
    {
        if (identity != _identity)
        {
            _identity = identity;
            _loaded = false;
            _failures = 0;
            return true;
        }
        return !_loaded && (requested || _failures < MaxBackgroundFailures);
    }

    public void Report(bool loaded)
    {
        _loaded = loaded;
        _failures = loaded ? 0 : _failures + 1;
    }

    public void Reset() => _identity = null;
}
