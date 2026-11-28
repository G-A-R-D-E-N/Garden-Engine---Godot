using System.Threading;

namespace OpenCommonwealth.Services;

public sealed class VoiceLimit
{
    private readonly int _capacity;
    private int _voices;

    public VoiceLimit(int capacity) => _capacity = capacity;

    public bool TryTake()
    {
        if (Interlocked.Increment(ref _voices) <= _capacity) return true;
        Interlocked.Decrement(ref _voices);
        return false;
    }

    public void Release() => Interlocked.Decrement(ref _voices);
}
