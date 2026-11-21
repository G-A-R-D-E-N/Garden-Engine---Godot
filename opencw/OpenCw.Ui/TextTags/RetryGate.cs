namespace OpenCommonwealth.Services.Ui.TextTags;

// Requires: main thread only; after a failure the gate stays closed until the delay passes, the delay doubles on each further failure up to the cap and resets on success.
public sealed class RetryGate(Func<double> seconds, double firstDelaySeconds = 5, double maxDelaySeconds = 60)
{
    private double _delay = firstDelaySeconds;
    private double? _openAt;

    public bool Open => _openAt is not { } at || seconds() >= at;

    public void Failed()
    {
        _openAt = seconds() + _delay;
        _delay = Math.Min(_delay * 2, maxDelaySeconds);
    }

    public void Succeeded()
    {
        _openAt = null;
        _delay = firstDelaySeconds;
    }
}
