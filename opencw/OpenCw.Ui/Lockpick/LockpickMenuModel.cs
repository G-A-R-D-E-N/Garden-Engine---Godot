namespace OpenCommonwealth.Services.Ui.Lockpick;

public enum LockpickMenuPhase
{
    Active,
    Unlocking,
    Closed,
}

public sealed class LockpickMenuModel
{
    private readonly ILockpickSession _session;
    private readonly LockpickOptions _options;
    private readonly HashSet<LockpickTurnKey> _turn = new();
    private readonly HashSet<LockpickCancelKey> _cancel = new();
    private double _stick;
    private bool _stickArmed;
    private double _unlock;
    private int _lastPins;
    private int _broken;

    public LockpickMenuModel(ILockpickSession session, LockpickOptions? options = null, double initialStick = 0)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _options = options ?? new LockpickOptions();
        _stickArmed = Math.Abs(initialStick) < _options.StickDeadZone;
        _lastPins = session.View.PinsLeft;
    }

    public event Action? Changed;

    public event Action<int>? PinBroken;

    public event Action<string>? AudioCue;

    public event Action<LockpickOutcome, int>? Closed;

    public LockpickMenuPhase Phase { get; private set; } = LockpickMenuPhase.Active;

    public LockpickOutcome? Outcome { get; private set; }

    public bool UsingController { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public int PinsBroken => _broken;

    public LockpickView View => _session.View;

    public bool CanExit => Phase == LockpickMenuPhase.Active;

    public string Title => View.Level.ToString().ToUpperInvariant() + " LOCK";

    public string PinsText => View.PinsLeft > 99 ? "99+ PINS LEFT" : View.PinsLeft == 1 ? "1 PIN LEFT" : $"{Math.Max(View.PinsLeft, 0)} PINS LEFT";

    public string Instruction => UsingController ? _options.ControllerInstruction : _options.MouseInstruction;

    public static LockpickMenuModel? TryOpen(ILockpickRules rules, string lockId, out string reason, LockpickOptions? options = null, double initialStick = 0)
    {
        ArgumentNullException.ThrowIfNull(rules);
        LockpickEntry entry = rules.Begin(lockId);
        if (entry.Session is null)
        {
            reason = string.IsNullOrEmpty(entry.Reason) ? (options ?? new LockpickOptions()).RefusedText : entry.Reason;
            return null;
        }
        reason = string.Empty;
        return new LockpickMenuModel(entry.Session, options, initialStick);
    }

    public void NoteDevice(bool controller)
    {
        if (UsingController == controller)
            return;
        UsingController = controller;
        Changed?.Invoke();
    }

    public void SetTurn(LockpickTurnKey key, bool down)
    {
        if (Phase != LockpickMenuPhase.Active)
            return;
        if (down)
            _turn.Add(key);
        else
            _turn.Remove(key);
    }

    public void SetStick(double x)
    {
        if (!double.IsFinite(x))
            x = 0;
        _stick = x;
        if (Math.Abs(x) < _options.StickDeadZone)
            _stickArmed = true;
    }

    public void MovePickMouse(double pixels)
    {
        if (Phase != LockpickMenuPhase.Active || !double.IsFinite(pixels) || pixels == 0)
            return;
        _session.MovePick(pixels * _options.MouseDegreesPerPixel);
    }

    public void PressCancel(LockpickCancelKey key)
    {
        if (Phase == LockpickMenuPhase.Active)
            _cancel.Add(key);
    }

    public void ReleaseCancel(LockpickCancelKey key)
    {
        if (_cancel.Remove(key) && Phase == LockpickMenuPhase.Active)
            Finish(LockpickOutcome.Cancelled);
    }

    public void Tick(double seconds)
    {
        if (Phase == LockpickMenuPhase.Closed || !double.IsFinite(seconds) || seconds <= 0)
            return;
        if (!_session.StillValid)
        {
            Finish(LockpickOutcome.Aborted);
            return;
        }
        if (Phase == LockpickMenuPhase.Active)
        {
            double stick = ShapedStick();
            if (stick != 0)
                _session.MovePick(stick * _options.StickDegreesPerSecond * seconds);
            _session.Step(_turn.Count > 0 ? 1.0 : 0.0, seconds);
        }
        else
            _session.Step(0.0, seconds);
        foreach (string cue in _session.DrainCues())
            AudioCue?.Invoke(cue);
        LockpickView view = _session.View;
        if (view.PinsLeft < _lastPins)
        {
            _broken += _lastPins - view.PinsLeft;
            _lastPins = view.PinsLeft;
            PinBroken?.Invoke(view.PinsLeft);
        }
        if (view.Phase == LockpickPhase.OutOfPins)
        {
            Message = _options.OutOfPinsText;
            Finish(LockpickOutcome.OutOfPins);
            return;
        }
        if (Phase == LockpickMenuPhase.Active && view.Phase == LockpickPhase.Solved)
        {
            Phase = LockpickMenuPhase.Unlocking;
            _unlock = 0;
            _turn.Clear();
            _cancel.Clear();
        }
        else if (Phase == LockpickMenuPhase.Unlocking)
        {
            _unlock += seconds;
            if (_unlock > _options.UnlockDelay)
            {
                Finish(LockpickOutcome.Unlocked);
                return;
            }
        }
        Changed?.Invoke();
    }

    public void Abort() => Finish(LockpickOutcome.Aborted);

    private double ShapedStick()
    {
        if (!_stickArmed || Math.Abs(_stick) < _options.StickDeadZone)
            return 0;
        return _stick * _stick * _stick;
    }

    private void Finish(LockpickOutcome outcome)
    {
        if (Phase == LockpickMenuPhase.Closed)
            return;
        Phase = LockpickMenuPhase.Closed;
        Outcome = outcome;
        _turn.Clear();
        _cancel.Clear();
        _stick = 0;
        Closed?.Invoke(outcome, _broken);
        Changed?.Invoke();
    }
}
