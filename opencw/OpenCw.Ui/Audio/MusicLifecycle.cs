namespace OpenCommonwealth.Services.Audio;

public sealed class MusicLifecycle<T> where T : class
{
    private readonly int _count;
    private readonly Func<int, T?> _play;
    private readonly Action<T, Action> _subscribe;
    private readonly Action<T, Action> _unsubscribe;
    private readonly Action<T> _release;
    private readonly Action? _exhausted;
    private T? _current;
    private int _index;

    public MusicLifecycle(int count, Func<int, T?> play, Action<T, Action> subscribe, Action<T, Action> unsubscribe, Action<T> release, Action? exhausted = null)
    {
        _count = count;
        _play = play;
        _subscribe = subscribe;
        _unsubscribe = unsubscribe;
        _release = release;
        _exhausted = exhausted;
    }

    public void Start(int index)
    {
        Stop();
        _index = MusicTracks.PlayFirstAvailable(_count, index, i => (_current = _play(i)) is not null);
        if (_current is { } current) _subscribe(current, Finished);
        else if (_count > 0) _exhausted?.Invoke();
    }

    public void Stop()
    {
        if (_current is not { } current) return;
        _current = null;
        _unsubscribe(current, Finished);
        _release(current);
    }

    private void Finished() => Start(MusicTracks.Advance(_count, _index));
}
