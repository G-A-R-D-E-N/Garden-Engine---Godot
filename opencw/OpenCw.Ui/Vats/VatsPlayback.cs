namespace OpenCommonwealth.Services.Ui.Vats;

// Requires: main thread; the snapshot is the one taken at Accept (the menu refreshes it every frame), Advance is driven by frame time.
public sealed class VatsPlayback(IReadOnlyList<VatsQueued> queue, VatsSnapshot snapshot, IVatsShotSink sink, Func<double> roll, double interval)
{
    private readonly IReadOnlyList<VatsQueued> _queue = queue.ToArray();
    private int _next;
    private double _wait;

    public event Action<VatsShot>? ShotResolved;

    public bool Aborted { get; private set; }

    public bool Done => Aborted || (_next >= _queue.Count && _wait <= 0);

    // Invariant: the first shot resolves on the first call, each later one after interval seconds, the playback is done interval seconds after the last; an aborted playback resolves nothing more.
    public bool Advance(double delta)
    {
        if (Done)
            return true;
        _wait -= Math.Max(0, delta);
        if (_wait <= 0 && _next < _queue.Count)
        {
            var shot = VatsExecution.Resolve(_queue[_next++], snapshot, sink, roll);
            _wait = shot.Result == VatsShotResult.Skipped ? 0 : Math.Max(0, interval);
            ShotResolved?.Invoke(shot);
        }
        return Done;
    }

    public void Abort() => Aborted = true;
}
