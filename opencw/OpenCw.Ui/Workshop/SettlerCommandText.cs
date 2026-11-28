using System;

namespace OpenCommonwealth.Services.Ui.Workshop;

// Requires: nameOf returns null for a settler it cannot name; every text here names a settler by a display name, never by its key.
public static class SettlerCommandText
{
    public const string AssignedText = "Settler assigned.";
    public const string RefusedText = "That can't be assigned.";
    public const string UnknownSettlerText = "another settler";

    public static string Unassigned(string? name) =>
        (string.IsNullOrWhiteSpace(name) ? "Another settler" : name) + " was unassigned.";

    public static string? Toast(CommandResult result, Func<string, string?> nameOf)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(nameOf);
        return result.Outcome switch
        {
            CommandOutcome.Assigned => result.Displaced is { } displaced ? AssignedText + " " + Unassigned(nameOf(displaced)) : AssignedText,
            CommandOutcome.Refused => RefusedText,
            _ => null,
        };
    }
}
