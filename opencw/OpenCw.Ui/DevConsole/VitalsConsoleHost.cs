using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.DevConsole;

// Requires: main thread only; unknown commands go to inner when given; use goes through the same inventory Use path as the Pip-Boy, so a result printed here is what the player would get.
public sealed class VitalsConsoleHost(PlayerVitals vitals, PlayerItems items, Func<string, InventoryActionResult> use, IRecordReader reader, IConsoleHost? inner = null) : IConsoleHost
{
    private static readonly string[] CommandNames = { "give", "hurt", "rads", "tick", "use", "vitals" };

    public ConsoleResult Execute(string line, ConsoleTarget? target)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ConsoleResult.Error("Empty command.");
        var args = parts[1..];
        return parts[0].ToLowerInvariant() switch
        {
            "vitals" => ConsoleResult.Info(Describe()),
            "give" => Give(args),
            "use" => Use(args),
            "hurt" => Amount(args, positive: true) is { } n ? Changed(() => vitals.AddHealth(-n)) : ConsoleResult.Error("Usage: hurt <amount>"),
            "rads" => Amount(args, positive: false) is { } n ? Changed(() => vitals.AddRads(n)) : ConsoleResult.Error("Usage: rads <amount>"),
            "tick" => Amount(args, positive: true) is { } n ? Changed(() => vitals.Tick(n)) : ConsoleResult.Error("Usage: tick <seconds>"),
            _ => inner?.Execute(line, target) ?? ConsoleResult.Error("Unknown command: " + parts[0]),
        };
    }

    public IReadOnlyList<string> Complete(string prefix) =>
        CommandNames.Where(name => name.StartsWith(prefix ?? string.Empty, StringComparison.OrdinalIgnoreCase)).Concat(inner?.Complete(prefix ?? string.Empty) ?? []).Distinct().Order(StringComparer.Ordinal).ToList();

    public ConsoleTarget? Pick(double x, double y) => inner?.Pick(x, y);

    private ConsoleResult Changed(Action change)
    {
        change();
        return ConsoleResult.Info(Describe());
    }

    private ConsoleResult Give(string[] args)
    {
        if (args.Length is < 1 or > 2 || Resolve(args[0]) is not { } item) return ConsoleResult.Error("Usage: give <item> [count]; the item must be a record key or editor id.");
        var count = 1;
        if (args.Length == 2 && (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 1)) return ConsoleResult.Error("Usage: give <item> [count]");
        items.Add(item, count);
        return ConsoleResult.Info(string.Create(CultureInfo.InvariantCulture, $"Gave {count} {Label(item)}; carrying {items.CountOf(item)}."));
    }

    private ConsoleResult Use(string[] args)
    {
        if (args.Length != 1 || Resolve(args[0]) is not { } item) return ConsoleResult.Error("Usage: use <item>; the item must be a record key or editor id.");
        var result = use(item);
        var lines = new List<ConsoleLine> { new(result.Done ? ConsoleSeverity.Info : ConsoleSeverity.Warning, string.Create(CultureInfo.InvariantCulture, $"{Label(item)}: {result.Message}; carrying {items.CountOf(item)}.")) };
        lines.AddRange(result.Skipped.Select(skipped => new ConsoleLine(ConsoleSeverity.Warning, "not applied: " + skipped)));
        lines.Add(new ConsoleLine(ConsoleSeverity.Info, Describe()));
        return new ConsoleResult(lines);
    }

    private string? Resolve(string name) =>
        (RecordKey.TryParse(name, out _, out _) ? name : reader.FindEditorId(name)) is { } key && reader.Signature(key) is not null ? key : null;

    private string Label(string key) => reader.Name(key) ?? reader.EditorId(key) ?? key;

    private static double? Amount(string[] args, bool positive) =>
        args.Length == 1 && double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && (!positive || value > 0) ? value : null;

    private string Describe()
    {
        var buffs = vitals.Buffs.Count == 0 ? "none" : string.Join(", ", vitals.Buffs.Select(buff => string.Create(CultureInfo.InvariantCulture, $"{buff.ActorValue} {buff.Amount:+0.##;-0.##} ({buff.Left:0.##}s)")));
        return string.Create(CultureInfo.InvariantCulture,
            $"Health {vitals.Health:0.##}/{vitals.MaxHealth:0.##} (pending {vitals.PendingHealth:0.##}); AP {vitals.ActionPoints:0.##}/{vitals.MaxActionPoints:0.##}; Rads {vitals.Rads:0.##} (pending {vitals.PendingRads:0.##}); buffs: {buffs}");
    }
}
