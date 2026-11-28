using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.DevConsole;

public sealed class InMemoryConsoleHost : IConsoleHost
{
    private static readonly string[] CommandNames = { "echo", "get", "listrefs", "prid", "set" };

    private readonly List<(ConsoleTarget Target, double X, double Y, double Radius)> _targets = new();
    private readonly Dictionary<string, double> _values = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Line, ConsoleTarget? Target)> Executed { get; } = new();

    public IReadOnlyDictionary<string, double> Values => _values;

    public InMemoryConsoleHost AddTarget(ConsoleTarget target, double x, double y, double radius)
    {
        _targets.Add((target, x, y, radius));
        return this;
    }

    public ConsoleResult Execute(string line, ConsoleTarget? target)
    {
        Executed.Add((line, target));
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ConsoleResult.Error("Empty command.");
        var args = parts[1..];
        switch (parts[0].ToLowerInvariant())
        {
            case "echo":
                return ConsoleResult.Info(string.Join(' ', args));
            case "get":
                return Get(args);
            case "set":
                return Set(args);
            case "prid":
                return Select(args);
            case "listrefs":
                return new ConsoleResult(_targets.Select(t => new ConsoleLine(ConsoleSeverity.Info, t.Target.Id + " " + t.Target.Name)).ToList());
            default:
                return ConsoleResult.Error("Unknown command: " + parts[0]);
        }
    }

    public IReadOnlyList<string> Complete(string prefix) =>
        CommandNames.Where(name => name.StartsWith(prefix ?? string.Empty, StringComparison.OrdinalIgnoreCase)).ToList();

    public ConsoleTarget? Pick(double x, double y)
    {
        foreach (var (target, cx, cy, radius) in _targets)
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                return target;
        return null;
    }

    private ConsoleResult Get(string[] args)
    {
        if (args.Length != 1) return ConsoleResult.Error("Usage: get <name>");
        return _values.TryGetValue(args[0], out var value)
            ? ConsoleResult.Info(args[0] + " = " + value.ToString(CultureInfo.InvariantCulture))
            : ConsoleResult.Error("No value named " + args[0]);
    }

    private ConsoleResult Set(string[] args)
    {
        if (args.Length != 2 || !double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            return ConsoleResult.Error("Usage: set <name> <number>");
        _values[args[0]] = value;
        return ConsoleResult.Info(args[0] + " set to " + value.ToString(CultureInfo.InvariantCulture));
    }

    private ConsoleResult Select(string[] args)
    {
        if (args.Length != 1) return ConsoleResult.Error("Usage: prid <id>");
        var found = _targets.Select(t => t.Target).FirstOrDefault(t => string.Equals(t.Id, args[0], StringComparison.OrdinalIgnoreCase));
        return found is null ? ConsoleResult.Error("No reference " + args[0]) : ConsoleResult.Info("Selected " + found.Name, found);
    }
}
