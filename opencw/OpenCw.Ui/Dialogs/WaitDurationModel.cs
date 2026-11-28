namespace OpenCommonwealth.Services.Ui.Dialogs;

public enum WaitMode
{
    Wait,
    Sleep,
}

public sealed class WaitPreset
{
    public string Label { get; }
    public int Hours { get; }
    public int? UntilHour { get; }

    private WaitPreset(string label, int hours, int? untilHour)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Label = label;
        Hours = hours;
        UntilHour = untilHour;
    }

    public static WaitPreset ForHours(string label, int hours)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hours, 1);
        return new WaitPreset(label, hours, null);
    }

    public static WaitPreset Until(string label, int hourOfDay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hourOfDay);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hourOfDay, 23);
        return new WaitPreset(label, 0, hourOfDay);
    }
}

public sealed record ModeBlock(WaitMode Mode, string State, string Message);

public readonly record struct WaitRequest(WaitMode Mode, int Hours);

public sealed record WaitOptions
{
    public static IReadOnlyList<WaitPreset> DefaultPresets { get; } = new[]
    {
        WaitPreset.ForHours("1 hour", 1),
        WaitPreset.ForHours("3 hours", 3),
        WaitPreset.ForHours("6 hours", 6),
        WaitPreset.ForHours("12 hours", 12),
        WaitPreset.Until("Until morning", 6),
        WaitPreset.Until("Until evening", 18),
    };

    public int MinHours { get; init; } = 1;
    public int MaxHours { get; init; } = 24;
    public int Step { get; init; } = 1;
    public int DefaultHours { get; init; } = 1;
    public IReadOnlyList<WaitPreset> Presets { get; init; } = DefaultPresets;
    public IReadOnlyList<ModeBlock> Blocks { get; init; } = Array.Empty<ModeBlock>();
    public TimeBands Bands { get; init; } = TimeBands.Default;
}

public sealed class WaitDurationModel
{
    private readonly WaitOptions _options;
    private readonly HashSet<string> _states = new(StringComparer.Ordinal);

    public WaitDurationModel(GameClock now, WaitMode mode = WaitMode.Wait, WaitOptions? options = null)
    {
        _options = options ?? new WaitOptions();
        if (_options.MinHours < 1 || _options.MaxHours < _options.MinHours || _options.Step < 1)
            throw new ArgumentException("Hour range must satisfy 1 <= min <= max and step >= 1.", nameof(options));
        ArgumentNullException.ThrowIfNull(_options.Presets);
        ArgumentNullException.ThrowIfNull(_options.Blocks);
        ArgumentNullException.ThrowIfNull(_options.Bands);
        Now = now;
        Mode = mode;
        Hours = Snap(_options.DefaultHours);
    }

    public GameClock Now { get; private set; }
    public WaitMode Mode { get; private set; }
    public int Hours { get; private set; }
    public DialogOutcome Outcome { get; private set; }
    public IReadOnlyList<WaitPreset> Presets => _options.Presets;
    public int MinHours => _options.MinHours;
    public int MaxHours => LastValue;
    public int Step => _options.Step;

    private int LastValue => _options.MinHours + (_options.MaxHours - _options.MinHours) / _options.Step * _options.Step;

    public GameClock ResultTime => Now.AddHours(Hours);
    public int DaysAhead => ResultTime.Day - Now.Day;
    public string ResultTimeOfDay => _options.Bands.NameAt(ResultTime);
    public string? BlockMessage => BlockMessageFor(Mode);
    public bool CanConfirm => Outcome == DialogOutcome.Pending && BlockMessage is null;

    public string? BlockMessageFor(WaitMode mode)
    {
        foreach (ModeBlock block in _options.Blocks)
            if (block.Mode == mode && _states.Contains(block.State))
                return block.Message;
        return null;
    }

    public bool SetHours(int hours)
    {
        if (Outcome != DialogOutcome.Pending)
            return false;
        int snapped = Snap(hours);
        if (snapped == Hours)
            return false;
        Hours = snapped;
        return true;
    }

    public bool Adjust(int steps) =>
        SetHours((int)Math.Clamp(Hours + steps * (long)_options.Step, int.MinValue, int.MaxValue));

    public bool SetToMinimum() => SetHours(_options.MinHours);

    public bool SetToMaximum() => SetHours(LastValue);

    public int ResolvePreset(int index)
    {
        if (index < 0 || index >= Presets.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        WaitPreset preset = Presets[index];
        return Snap(preset.UntilHour is { } hour ? Now.HoursUntil(hour) : preset.Hours);
    }

    public bool ApplyPreset(int index) =>
        index >= 0 && index < Presets.Count && SetHours(ResolvePreset(index));

    public void SetMode(WaitMode mode)
    {
        if (Outcome == DialogOutcome.Pending)
            Mode = mode;
    }

    public void SetActiveStates(IEnumerable<string> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        _states.Clear();
        foreach (string state in states)
            _states.Add(state);
    }

    public void UpdateNow(GameClock now)
    {
        if (Outcome == DialogOutcome.Pending)
            Now = now;
    }

    public WaitRequest? Confirm()
    {
        if (!CanConfirm)
            return null;
        Outcome = DialogOutcome.Confirmed;
        return new WaitRequest(Mode, Hours);
    }

    public void Cancel()
    {
        if (Outcome == DialogOutcome.Pending)
            Outcome = DialogOutcome.Cancelled;
    }

    private int Snap(int hours)
    {
        int clamped = Math.Clamp(hours, _options.MinHours, LastValue);
        int snapped = _options.MinHours + (clamped - _options.MinHours + _options.Step / 2) / _options.Step * _options.Step;
        return Math.Min(snapped, LastValue);
    }
}
