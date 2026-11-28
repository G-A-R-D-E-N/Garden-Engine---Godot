namespace OpenCommonwealth.Services.Ui.MainMenu;

public enum PendingSceneVerdict
{
    None,
    Forget,
    Free,
}

public sealed class PendingSceneLease
{
    private ulong _instanceId;
    private bool _inFlight;

    public bool IsArmed => _instanceId != 0;

    public ulong PendingId => _instanceId;

    public bool ChangeInFlight => _inFlight;

    public void HandToEngine(ulong instanceId)
    {
        _instanceId = instanceId;
        _inFlight = instanceId != 0;
    }

    public void Adopted()
    {
        _instanceId = 0;
        _inFlight = false;
    }

    public void Settled()
    {
        _inFlight = false;
    }

    public PendingSceneVerdict ReleaseOrphan(bool instanceValid, bool insideTree)
    {
        if (_instanceId == 0) return PendingSceneVerdict.None;
        if (_inFlight && instanceValid) return PendingSceneVerdict.None;
        return Resolve(instanceValid && !insideTree);
    }

    public PendingSceneVerdict ReleaseOnExit(bool instanceValid, bool insideTree) =>
        _inFlight ? PendingSceneVerdict.None : ReleaseOrphan(instanceValid, insideTree);

    private PendingSceneVerdict Resolve(bool orphan)
    {
        _instanceId = 0;
        _inFlight = false;
        return orphan ? PendingSceneVerdict.Free : PendingSceneVerdict.Forget;
    }
}