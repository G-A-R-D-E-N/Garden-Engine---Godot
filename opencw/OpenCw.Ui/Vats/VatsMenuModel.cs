namespace OpenCommonwealth.Services.Ui.Vats;

public sealed class VatsMenuModel
{
    public const double ConeHalfAngleDegrees = 35;
    public const double AlignmentWeight = 2.0;
    public const int MaxBrackets = 4;

    private readonly IVatsTargets _targets;
    private readonly List<VatsQueued> _queue = new();
    private readonly List<VatsShot> _shots = new();
    private VatsSnapshot _snapshot;
    private string? _focusId;
    private string? _partId;
    private bool _begun;
    private bool _slow;
    private bool _armed;
    private bool _criticalRequested;
    private bool _abortRequested;

    public VatsMenuModel(IVatsTargets targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        _targets = targets;
        _snapshot = targets.Snapshot();
        string? initial = targets.InitialTargetId;
        VatsTarget? first = initial is null ? null : _snapshot.Targets.FirstOrDefault(t => t.Alive && t.Id == initial);
        _focusId = first?.Id;
        _partId = first?.Parts.FirstOrDefault()?.Id;
    }

    public event Action? Changed;

    public event Action<string>? FocusChanged;

    public event Action<bool>? SlowTimeRequested;

    public event Action<IReadOnlyList<VatsQueued>>? Accepted;

    public event Action? CriticalRequested;

    public event Action? PlaybackAbortRequested;

    public event Action<string>? AudioCue;

    public event Action<VatsOutcome>? Closed;

    public VatsPhase Phase { get; private set; } = VatsPhase.Targeting;

    public VatsOutcome? Outcome { get; private set; }

    public bool IsArmed => _armed;

    public string? CriticalNotice { get; private set; }

    public VatsSnapshot Snapshot => _snapshot;

    public IReadOnlyList<VatsQueued> Queue => _queue;

    public IReadOnlyList<VatsShot> Shots => _shots;

    public int QueuedCost => _queue.Sum(q => q.Cost);

    public int MaxQueue => Math.Max(1, _targets.MaxQueue);

    public int ActionPointPreview => Phase == VatsPhase.Targeting ? Math.Max(0, _snapshot.ActionPoints - QueuedCost) : _snapshot.ActionPoints;

    public VatsTarget? Focus => _focusId is null ? null : _snapshot.Targets.FirstOrDefault(t => t.Id == _focusId);

    public VatsPart? SelectedPart => Focus?.Parts.FirstOrDefault(p => p.Id == _partId);

    public bool AbortEnabled => !_abortRequested;

    public bool CriticalEnabled => Phase == VatsPhase.Playback && _snapshot.CriticalReady && !_criticalRequested;

    public IReadOnlyList<(string Label, bool Enabled)> HintLabels
    {
        get
        {
            if (Phase == VatsPhase.Playback)
                return new[] { ("CRITICAL", CriticalEnabled), ("ABORT", AbortEnabled) };
            var hints = new List<(string, bool)> { ("SELECT", true), ("ACCEPT", _queue.Count > 0), ("RETURN", true) };
            if ((Focus?.Parts.Count ?? 0) > 1)
                hints.Add(("BODY PART", true));
            if (_snapshot.Targets.Count(t => t.Alive) > 1)
                hints.Add(("CYCLE TARGETS", true));
            return hints;
        }
    }

    public static int DisplayChance(int chance) => Math.Clamp(chance, 0, 100);

    public int QueuedOn(string targetId, string partId) => _queue.Count(q => q.TargetId == targetId && q.PartId == partId);

    public void Begin()
    {
        if (_begun || Phase == VatsPhase.Closed)
            return;
        _begun = true;
        if (_focusId is null)
        {
            Close(VatsOutcome.Cancelled);
            return;
        }
        _slow = true;
        SlowTimeRequested?.Invoke(true);
        Cue("enter");
        FocusChanged?.Invoke(_focusId);
        Raise();
    }

    public void Arm() => _armed = true;

    public void Refresh()
    {
        if (Phase == VatsPhase.Closed)
            return;
        _snapshot = _targets.Snapshot();
        if (!_snapshot.StillValid)
        {
            Close(VatsOutcome.Interrupted);
            return;
        }
        if (Phase == VatsPhase.Targeting && !Reconcile())
            return;
        Raise();
    }

    public bool Handle(VatsCommand command)
    {
        if (Phase == VatsPhase.Closed)
            return false;
        if (!_armed)
            return true;
        Refresh();
        if (Phase == VatsPhase.Closed)
            return true;
        if (Phase == VatsPhase.Playback)
        {
            if (command == VatsCommand.Critical)
                RequestCritical();
            else if (command == VatsCommand.Return)
                RequestAbort();
            return true;
        }
        switch (command)
        {
            case VatsCommand.PartUp:
                MovePart(-1);
                break;
            case VatsCommand.PartDown:
                MovePart(1);
                break;
            case VatsCommand.Queue:
                QueueSelected();
                break;
            case VatsCommand.CycleNext:
                Cycle(1);
                break;
            case VatsCommand.CyclePrevious:
                Cycle(-1);
                break;
            case VatsCommand.Return:
                Return();
                break;
            case VatsCommand.Accept:
                Accept();
                break;
        }
        return true;
    }

    public bool SelectPart(string partId)
    {
        if (!_armed || Phase != VatsPhase.Targeting || Focus?.Parts.Any(p => p.Id == partId) != true)
            return false;
        if (_partId != partId)
        {
            _partId = partId;
            Cue("select");
            Raise();
        }
        return true;
    }

    public bool QueuePart(string partId)
    {
        if (!SelectPart(partId))
            return false;
        QueueSelected();
        return true;
    }

    public void ShowCriticalNotice(string kind)
    {
        CriticalNotice = kind;
        Raise();
    }

    public void ReportShot(VatsShot shot)
    {
        if (Phase != VatsPhase.Playback)
            return;
        _shots.Add(shot);
        Cue(shot.Result switch { VatsShotResult.Hit => "shot_hit", VatsShotResult.Miss => "shot_miss", _ => "shot_skipped" });
        Raise();
    }

    public void Finish() => Close(VatsOutcome.Executed);

    public void Cancel() => Close(VatsOutcome.Cancelled);

    public void Abort() => Close(VatsOutcome.Aborted);

    private bool Reconcile()
    {
        var alive = new HashSet<string>(_snapshot.Targets.Where(t => t.Alive).Select(t => t.Id));
        if (_queue.RemoveAll(q => !alive.Contains(q.TargetId)) > 0)
            Cue("unqueue");
        if (alive.Count == 0)
        {
            Close(VatsOutcome.Cancelled);
            return false;
        }
        if (_focusId is null || !alive.Contains(_focusId))
            MoveFocusToNextAlive(alive);
        else if (SelectedPart is null)
            _partId = Focus?.Parts.FirstOrDefault()?.Id;
        while (_queue.Count > 0 && QueuedCost > _snapshot.ActionPoints)
            _queue.RemoveAt(_queue.Count - 1);
        return true;
    }

    private void MoveFocusToNextAlive(HashSet<string> alive)
    {
        var targets = _snapshot.Targets;
        int current = -1;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].Id == _focusId)
                current = i;
        }
        for (int step = 1; step <= targets.Count; step++)
        {
            VatsTarget candidate = targets[(current + step + targets.Count) % targets.Count];
            if (alive.Contains(candidate.Id))
            {
                SetFocus(candidate);
                return;
            }
        }
    }

    private void SetFocus(VatsTarget target)
    {
        _focusId = target.Id;
        _partId = target.Parts.FirstOrDefault()?.Id;
        FocusChanged?.Invoke(target.Id);
    }

    private void Cycle(int direction)
    {
        var alive = _snapshot.Targets.Where(t => t.Alive).ToList();
        if (alive.Count < 2)
            return;
        int index = alive.FindIndex(t => t.Id == _focusId);
        SetFocus(alive[((index + direction) % alive.Count + alive.Count) % alive.Count]);
        Cue("select");
        Raise();
    }

    private void MovePart(int vertical)
    {
        VatsTarget? focus = Focus;
        VatsPart? selected = SelectedPart;
        if (focus is null || selected?.Screen is not { } origin)
            return;
        VatsPart? best = null;
        double bestScore = double.MaxValue;
        double halfCone = ConeHalfAngleDegrees * Math.PI / 180.0;
        double height = _snapshot.ViewHeight > 0 ? _snapshot.ViewHeight : 1.0;
        foreach (VatsPart part in focus.Parts)
        {
            if (part.Id == selected.Id || part.Screen is not { } point)
                continue;
            double dx = point.X - origin.X;
            double dy = point.Y - origin.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-9)
                continue;
            double cosine = Math.Clamp(dy * vertical / length, -1.0, 1.0);
            double angle = Math.Acos(cosine);
            if (angle > halfCone)
                continue;
            double score = length / height + angle * AlignmentWeight;
            if (score < bestScore)
            {
                bestScore = score;
                best = part;
            }
        }
        if (best is null)
            return;
        _partId = best.Id;
        Cue("select");
        Raise();
    }

    private void QueueSelected()
    {
        VatsTarget? focus = Focus;
        VatsPart? part = SelectedPart;
        if (focus is null || part is null)
            return;
        if (part.ApCost <= 0 || QueuedCost + part.ApCost > _snapshot.ActionPoints || _queue.Count >= MaxQueue)
        {
            Cue("denied");
            return;
        }
        _queue.Add(new VatsQueued(focus.Id, part.Id, part.ApCost));
        Cue("queue");
        Raise();
    }

    private void Return()
    {
        if (_queue.Count == 0)
        {
            Close(VatsOutcome.Cancelled);
            return;
        }
        _queue.RemoveAt(_queue.Count - 1);
        Cue("unqueue");
        Raise();
    }

    private void Accept()
    {
        if (_queue.Count == 0)
            return;
        Phase = VatsPhase.Playback;
        Cue("accept");
        Accepted?.Invoke(_queue.ToArray());
        Raise();
    }

    private void RequestCritical()
    {
        if (!CriticalEnabled)
            return;
        _criticalRequested = true;
        Cue("critical_fire");
        CriticalRequested?.Invoke();
        Raise();
    }

    private void RequestAbort()
    {
        if (!AbortEnabled)
            return;
        _abortRequested = true;
        Cue("abort");
        PlaybackAbortRequested?.Invoke();
        Raise();
    }

    private void Close(VatsOutcome outcome)
    {
        if (Phase == VatsPhase.Closed)
            return;
        Phase = VatsPhase.Closed;
        Outcome = outcome;
        if (_slow)
        {
            _slow = false;
            SlowTimeRequested?.Invoke(false);
        }
        Cue("exit");
        Closed?.Invoke(outcome);
        Raise();
    }

    private void Cue(string name) => AudioCue?.Invoke(name);

    private void Raise() => Changed?.Invoke();
}
