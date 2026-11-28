using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.Lockpick;

public interface IPinStock
{
    int Count { get; }
    bool Take();
}

public sealed class PlayerPinStock(PlayerItems items, string key) : IPinStock
{
    public int Count => items.CountOf(key);

    public bool Take() => items.Remove(key, 1);
}

// Requires: main thread only; one session per lock attempt.
public sealed class BobbyPinSession : ILockpickSession
{
    public const double MaxTurn = 90;
    public const double TurnSpeed = 120;
    public const double ReleaseSpeed = 240;
    public const double StressDrain = 60;
    public const double SweetWindow = 1.5;

    private readonly IPinStock _pins;
    private readonly double _sweet;
    private readonly double _tolerance;
    private readonly List<string> _cues = new();
    private double _pick;
    private double _turn;
    private double _condition = 100;
    private bool _stressed;
    private bool _turnHeld;
    private LockpickPhase _phase = LockpickPhase.Active;

    public BobbyPinSession(LockLevel level, IPinStock pins, double sweetAngle)
    {
        ArgumentNullException.ThrowIfNull(pins);
        Level = level;
        _pins = pins;
        _sweet = Math.Clamp(sweetAngle, -MaxTurn, MaxTurn);
        _tolerance = Tolerance(level);
    }

    public LockLevel Level { get; }

    public LockpickView View => new(Level, _pick, _turn, _condition, _pins.Count, _stressed, _phase);

    public bool StillValid => true;

    public static double Tolerance(LockLevel level) => level switch
    {
        LockLevel.Novice => 40,
        LockLevel.Advanced => 30,
        LockLevel.Expert => 20,
        _ => 12,
    };

    public void MovePick(double degrees)
    {
        if (_phase == LockpickPhase.Active && double.IsFinite(degrees)) _pick = Math.Clamp(_pick + degrees, -MaxTurn, MaxTurn);
    }

    public void Step(double turnInput, double seconds)
    {
        if (_phase != LockpickPhase.Active) return;
        if (turnInput <= 0)
        {
            _turnHeld = false;
            _stressed = false;
            _turn = Math.Max(0, _turn - ReleaseSpeed * seconds);
            return;
        }
        var allowed = MaxTurn * (1 - Math.Min(Math.Max(0, Math.Abs(_pick - _sweet) - SweetWindow) / _tolerance, 1));
        if (!_turnHeld) _cues.Add("cylinder_turn");
        _turnHeld = true;
        _turn = Math.Min(MaxTurn, _turn + TurnSpeed * seconds);
        _stressed = _turn > allowed;
        if (_stressed)
        {
            _turn = allowed;
            _condition -= StressDrain * seconds;
            if (_condition <= 0) BreakPin();
        }
        else if (_turn >= MaxTurn)
        {
            _phase = LockpickPhase.Solved;
            _cues.Add("unlock");
        }
    }

    public IReadOnlyList<string> DrainCues()
    {
        var cues = _cues.ToArray();
        _cues.Clear();
        return cues;
    }

    private void BreakPin()
    {
        _cues.Add("pin_break");
        _pins.Take();
        _condition = 100;
        _turn = 0;
        _stressed = false;
        if (_pins.Count <= 0) _phase = LockpickPhase.OutOfPins;
    }
}

public sealed class BobbyPinSessions(IPinStock? pins) : ILockpickSessions
{
    public const string EditorId = "Lockpick";
    public const string Signature = "MISC";
    public const double SweetRange = 60;

    public ILockpickSession? Start(string lockId, LockLevel level) =>
        pins is { Count: > 0 } ? new BobbyPinSession(level, pins, SweetAngle(lockId)) : null;

    public static double SweetAngle(string lockId)
    {
        var hash = 2166136261u;
        foreach (var c in lockId.ToLowerInvariant()) hash = (hash ^ c) * 16777619u;
        return hash % 10001 / 10000.0 * 2 * SweetRange - SweetRange;
    }
}
