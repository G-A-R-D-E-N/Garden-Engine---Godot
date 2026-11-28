using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public interface IPipBoyStatusSource
{
    double? Health { get; }

    double? MaxHealth { get; }

    int? Level { get; }

    double? LevelProgress { get; }

    double? ActionPoints { get; }

    double? MaxActionPoints { get; }

    double? CarryWeight { get; }

    double? CarryCapacity { get; }

    long? Caps { get; }

    DateTime? WorldTime { get; }

    string? Location { get; }
}

public static class PipBoyFooter
{
    public const string Unknown = "--";
    public const long CapsCeiling = 1_000_000;

    public static IReadOnlyList<string> Segments(PipBoyTab tab, IPipBoyStatusSource? source)
    {
        if (source is null) return tab == PipBoyTab.Radio ? Array.Empty<string>() : new[] { Unknown };
        return tab switch
        {
            PipBoyTab.Stats => [Health(source), Level(source), ActionPoints(source)],
            PipBoyTab.Inventory => [Weight(source), Caps(source)],
            PipBoyTab.Data => [Date(source.WorldTime), Clock(source.WorldTime)],
            PipBoyTab.Map => MapSegments(source),
            _ => [],
        };
    }

    public static string Text(PipBoyTab tab, IPipBoyStatusSource? source) => string.Join("    ", Segments(tab, source));

    private static IReadOnlyList<string> MapSegments(IPipBoyStatusSource source)
    {
        var segments = new List<string> { Date(source.WorldTime), Clock(source.WorldTime) };
        if (!string.IsNullOrWhiteSpace(source.Location)) segments.Add(source.Location!);
        return segments;
    }

    private static string Health(IPipBoyStatusSource source)
    {
        var current = source.Health is { } health && double.IsFinite(health) ? Whole(Math.Max(health, 0)) : Unknown;
        return "HP " + current + "/" + Whole(source.MaxHealth);
    }

    private static string Level(IPipBoyStatusSource source)
    {
        if (source.Level is not { } level) return "LEVEL " + Unknown;
        var text = "LEVEL " + level.ToString(CultureInfo.InvariantCulture);
        if (source.LevelProgress is { } progress && double.IsFinite(progress))
            text += " " + Whole(Math.Clamp(progress, 0, 1) * 100 + 1e-9) + "%";
        return text;
    }

    private static string ActionPoints(IPipBoyStatusSource source)
    {
        if (source.MaxActionPoints is not { } max || !double.IsFinite(max) || max <= 0) return "AP --/--";
        return "AP " + Whole(source.ActionPoints) + "/" + Whole(max);
    }

    private static string Weight(IPipBoyStatusSource source)
    {
        var text = "WT " + Whole(source.CarryWeight);
        return source.CarryCapacity is { } capacity && double.IsFinite(capacity) ? text + "/" + Whole(capacity) : text;
    }

    private static string Caps(IPipBoyStatusSource source) =>
        "CAPS " + (source.Caps is not { } caps ? Unknown
            : caps >= CapsCeiling ? CapsCeiling.ToString(CultureInfo.InvariantCulture) + "+"
            : Math.Max(caps, 0).ToString(CultureInfo.InvariantCulture));

    private static string Date(DateTime? time) =>
        time is { } value ? value.ToString("MM'.'dd'.'yyyy", CultureInfo.InvariantCulture) : "--.--.----";

    private static string Clock(DateTime? time)
    {
        if (time is not { } value) return "--:-- --";
        var hour = value.Hour % 12 == 0 ? 12 : value.Hour % 12;
        return string.Create(CultureInfo.InvariantCulture, $"{hour}:{value.Minute:00} {(value.Hour < 12 ? "AM" : "PM")}");
    }

    private static string Whole(double? value) =>
        value is { } number && double.IsFinite(number)
            ? Math.Floor(number).ToString("0", CultureInfo.InvariantCulture)
            : Unknown;
}
