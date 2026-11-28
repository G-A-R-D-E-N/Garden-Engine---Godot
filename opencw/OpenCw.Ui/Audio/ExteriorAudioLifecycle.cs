namespace OpenCommonwealth.Services.Audio;

public sealed class ExteriorAudioLifecycle<T> where T : class
{
    private readonly Action<T> _release;
    private T? _audio;

    public ExteriorAudioLifecycle(Action<T> release) => _release = release;

    public static void Start(Action ensurePapyrus, Action startStreaming)
    {
        ensurePapyrus();
        startStreaming();
    }

    public void Attach(Func<T?> attach) => _audio ??= attach();

    public TCell? LoadInterior<TCell>(Func<TCell?> load) where TCell : class
    {
        var cell = load();
        if (cell is not null) Clear();
        return cell;
    }

    public void Clear()
    {
        if (_audio is not { } audio) return;
        _audio = null;
        _release(audio);
    }
}
