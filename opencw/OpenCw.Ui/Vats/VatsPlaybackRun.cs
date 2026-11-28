namespace OpenCommonwealth.Services.Ui.Vats;

// Requires: main thread; the owner calls Tick every frame while the menu is open.
public sealed class VatsPlaybackRun
{
    private readonly VatsMenuModel _model;
    private readonly IVatsShotSink _sink;
    private readonly Func<double> _roll;
    private readonly double _interval;
    private VatsPlayback? _playback;

    public VatsPlaybackRun(VatsMenuModel model, IVatsShotSink sink, Func<double> roll, double interval = VatsAssumptions.ShotIntervalSeconds)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(roll);
        _model = model;
        _sink = sink;
        _roll = roll;
        _interval = interval;
        model.Accepted += queue =>
        {
            _playback = new VatsPlayback(queue, model.Snapshot, sink, roll, interval);
            _playback.ShotResolved += model.ReportShot;
        };
        model.PlaybackAbortRequested += () => _playback?.Abort();
        model.Closed += _ => _playback = null;
    }

    // Invariant: the model is always closed once the playback ends or a shot throws (Executed, or Aborted after an abort request), or the modal menu would stay in Playback.
    public void Tick(double delta)
    {
        if (_playback is not { } playback)
            return;
        bool done;
        try { done = playback.Advance(delta); }
        catch
        {
            _playback = null;
            _model.Finish();
            throw;
        }
        if (!done)
            return;
        _playback = null;
        if (playback.Aborted)
            _model.Abort();
        else
            _model.Finish();
    }
}
