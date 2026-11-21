using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Interaction;

namespace OpenCommonwealth.Services.Ui.DevConsole;

// Requires: debug use only; recruit calls CompanionRoster.Recruit directly and so skips the CompanionActorScript marker gate.
public sealed class CompanionConsoleHost(IConsoleHost inner, CompanionRoster roster, Func<(string RefKey, string BaseKey)?>? nearest = null) : IConsoleHost
{
    public const string Command = "companion";
    public const string Usage = "Usage: companion status | recruit [<refKey> <baseKey>] | dismiss";

    public ConsoleResult Execute(string line, ConsoleTarget? target)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !string.Equals(parts[0], Command, StringComparison.OrdinalIgnoreCase)) return inner.Execute(line, target);
        switch (parts.Length > 1 ? parts[1].ToLowerInvariant() : "")
        {
            case "status" when parts.Length == 2:
                return ConsoleResult.Info(Status());
            case "recruit" when parts.Length == 2:
                return nearest?.Invoke() is { } near ? Recruit(near.RefKey, near.BaseKey) : ConsoleResult.Error("No nearby NPC. " + Usage);
            case "recruit" when parts.Length == 4:
                return Recruit(parts[2], parts[3]);
            case "dismiss" when parts.Length == 2:
                return roster.Current is { } held && roster.Dismiss(held.RefKey)
                    ? ConsoleResult.Info("Dismissed " + held.RefKey + ". " + Status())
                    : ConsoleResult.Error("No companion to dismiss.");
            default:
                return ConsoleResult.Error(Usage);
        }
    }

    public IReadOnlyList<string> Complete(string prefix) =>
        Command.StartsWith(prefix ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            ? inner.Complete(prefix ?? string.Empty).Append(Command).Order(StringComparer.Ordinal).ToList()
            : inner.Complete(prefix ?? string.Empty);

    public ConsoleTarget? Pick(double x, double y) => inner.Pick(x, y);

    private ConsoleResult Recruit(string refKey, string baseKey) => roster.Recruit(refKey, baseKey)
        ? ConsoleResult.Info("Recruited without the script gate. " + Status())
        : ConsoleResult.Error("Not recruited: disallowed or already the companion. " + Status());

    private string Status() => roster.Current is { } entry
        ? $"Companion {entry.RefKey} (base {entry.BaseKey}): {entry.State}, {entry.Distance}, {entry.Stance}."
        : "No companion.";
}
