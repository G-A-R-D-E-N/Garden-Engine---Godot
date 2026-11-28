namespace OpenCommonwealth.Services.Ui.Lockpick;

public enum LockLevel
{
    Novice,
    Advanced,
    Expert,
    Master,
}

public enum LockpickPhase
{
    Active,
    Solved,
    OutOfPins,
}

public enum LockpickOutcome
{
    Unlocked,
    Cancelled,
    OutOfPins,
    Aborted,
}

public enum LockpickTurnKey
{
    W,
    A,
    S,
    D,
    PadA,
}

public enum LockpickCancelKey
{
    Escape,
    PadB,
}

public readonly record struct LockpickView(
    LockLevel Level,
    double PickAngle,
    double TurnAngle,
    double Condition,
    int PinsLeft,
    bool Stressed,
    LockpickPhase Phase);

public interface ILockpickSession
{
    LockpickView View { get; }

    bool StillValid { get; }

    void MovePick(double degrees);

    void Step(double turnInput, double seconds);

    IReadOnlyList<string> DrainCues();
}

public sealed record LockpickEntry(string Reason, ILockpickSession? Session)
{
    public bool IsReady => Session is not null;
}

public interface ILockpickRules
{
    LockpickEntry Begin(string lockId);
}

public sealed record LockpickOptions
{
    public double MouseDegreesPerPixel { get; init; } = 0.25;
    public double StickDeadZone { get; init; } = 0.15;
    public double StickDegreesPerSecond { get; init; } = 400;
    public double UnlockDelay { get; init; } = 1.5;
    public string MouseInstruction { get; init; } = "Move the mouse to aim the pick. Hold W, A, S or D to turn the lock.";
    public string ControllerInstruction { get; init; } = "Move the left stick to aim the pick. Hold A to turn the lock.";
    public string OutOfPinsText { get; init; } = "Out of bobby pins.";
    public string RefusedText { get; init; } = "This lock cannot be picked.";
}
