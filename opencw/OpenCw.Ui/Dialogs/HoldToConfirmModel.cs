namespace OpenCommonwealth.Services.Ui.Dialogs;

public enum HoldMode
{
    Hold,
    DoublePress,
}

public enum HoldState
{
    Idle,
    Holding,
    Armed,
    Completed,
}

public sealed class HoldToConfirmModel
{
    private double _elapsed;

    public HoldToConfirmModel(double holdSeconds = 1.5, HoldMode mode = HoldMode.Hold, double doublePressWindowSeconds = 1.5)
    {
        if (!double.IsFinite(holdSeconds) || holdSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(holdSeconds));
        if (!double.IsFinite(doublePressWindowSeconds) || doublePressWindowSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(doublePressWindowSeconds));
        HoldSeconds = holdSeconds;
        DoublePressWindowSeconds = doublePressWindowSeconds;
        Mode = mode;
    }

    public double HoldSeconds { get; }
    public double DoublePressWindowSeconds { get; }
    public HoldMode Mode { get; private set; }
    public HoldState State { get; private set; }
    public bool IsCompleted => State == HoldState.Completed;

    public double Progress => State switch
    {
        HoldState.Holding => Math.Min(_elapsed / HoldSeconds, 1.0),
        HoldState.Armed => 0.5,
        HoldState.Completed => 1.0,
        _ => 0.0,
    };

    public void SetMode(HoldMode mode)
    {
        if (State == HoldState.Completed)
            return;
        Mode = mode;
        Reset();
    }

    public void Reset()
    {
        if (State == HoldState.Completed)
            return;
        State = HoldState.Idle;
        _elapsed = 0;
    }

    public bool Press()
    {
        if (State == HoldState.Completed)
            return false;
        if (Mode == HoldMode.Hold)
        {
            if (State == HoldState.Idle)
            {
                State = HoldState.Holding;
                _elapsed = 0;
            }
            return false;
        }
        if (State == HoldState.Armed)
        {
            State = HoldState.Completed;
            return true;
        }
        State = HoldState.Armed;
        _elapsed = 0;
        return false;
    }

    public void Release()
    {
        if (Mode == HoldMode.Hold && State == HoldState.Holding)
            Reset();
    }

    public bool Tick(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
            return false;
        if (State == HoldState.Holding)
        {
            _elapsed += deltaSeconds;
            if (_elapsed < HoldSeconds)
                return false;
            _elapsed = HoldSeconds;
            State = HoldState.Completed;
            return true;
        }
        if (State == HoldState.Armed)
        {
            _elapsed += deltaSeconds;
            if (_elapsed >= DoublePressWindowSeconds)
                Reset();
        }
        return false;
    }
}
