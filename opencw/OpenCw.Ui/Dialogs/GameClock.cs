namespace OpenCommonwealth.Services.Ui.Dialogs;

public readonly record struct GameClock
{
    public const int MinutesPerDay = 1440;

    public int Day { get; }
    public int MinuteOfDay { get; }

    public GameClock(int day, int minuteOfDay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(day);
        ArgumentOutOfRangeException.ThrowIfNegative(minuteOfDay);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minuteOfDay, MinutesPerDay);
        Day = day;
        MinuteOfDay = minuteOfDay;
    }

    public int Hour => MinuteOfDay / 60;
    public int Minute => MinuteOfDay % 60;

    public GameClock AddMinutes(long minutes)
    {
        long total = Day * (long)MinutesPerDay + MinuteOfDay + minutes;
        ArgumentOutOfRangeException.ThrowIfNegative(total, nameof(minutes));
        return new GameClock(checked((int)(total / MinutesPerDay)), (int)(total % MinutesPerDay));
    }

    public GameClock AddHours(int hours) => AddMinutes(hours * 60L);

    public int HoursUntil(int hourOfDay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hourOfDay);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hourOfDay, 23);
        int minutes = (hourOfDay * 60 - MinuteOfDay + MinutesPerDay) % MinutesPerDay;
        if (minutes == 0)
            minutes = MinutesPerDay;
        return (minutes + 59) / 60;
    }

    public string Format24() => $"{Hour:00}:{Minute:00}";

    // Constraint: a non-finite or negative total clamps to day 0, 00:00; the minute is floored so it never reaches 1440.
    public static GameClock FromTotalHours(double totalHours)
    {
        if (!double.IsFinite(totalHours) || totalHours < 0) return new GameClock(0, 0);
        var minutes = (long)Math.Min(Math.Floor(totalHours * 60.0), int.MaxValue / 2.0);
        return new GameClock((int)(minutes / MinutesPerDay), (int)(minutes % MinutesPerDay));
    }
}

public sealed record TimeBand(string Name, int StartHour);

public sealed class TimeBands
{
    public static TimeBands Default { get; } = new(new[]
    {
        new TimeBand("Night", 0),
        new TimeBand("Dawn", 5),
        new TimeBand("Morning", 8),
        new TimeBand("Afternoon", 12),
        new TimeBand("Evening", 17),
        new TimeBand("Night", 21),
    });

    private readonly TimeBand[] _bands;

    public TimeBands(IEnumerable<TimeBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        _bands = bands.ToArray();
        if (_bands.Length == 0 || _bands[0].StartHour != 0)
            throw new ArgumentException("Bands must be non-empty and the first must start at hour 0.", nameof(bands));
        for (int i = 0; i < _bands.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(_bands[i].Name) || _bands[i].StartHour is < 0 or > 23 ||
                (i > 0 && _bands[i].StartHour <= _bands[i - 1].StartHour))
                throw new ArgumentException("Bands need names and strictly ascending start hours in 0..23.", nameof(bands));
        }
    }

    public string NameAt(GameClock clock)
    {
        string name = _bands[0].Name;
        foreach (TimeBand band in _bands)
        {
            if (band.StartHour > clock.Hour)
                break;
            name = band.Name;
        }
        return name;
    }
}
