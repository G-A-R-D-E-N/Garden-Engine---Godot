using System;

namespace OpenCommonwealth.Services.Ui.Workshop;

public sealed record SettlerFacts(bool Commandable, bool InWorkshop = true, bool Companion = false);

public sealed record CommandTarget(string ObjectId, bool AllowPlayerAssignment, bool RequiresActor, bool IsBed);

public enum CommandOutcome
{
    Ignored,
    Refused,
    Ended,
    Assigned,
}

public sealed record CommandResult(CommandOutcome Outcome, string? Displaced = null);

// Requires: main thread only; mirrors the vanilla workshop command state, one settler at a time.
public sealed class SettlerCommandSession(SettlerAssignments assignments)
{
    public const double CheckSeconds = 5.0;

    private readonly SettlerAssignments _assignments = assignments ?? throw new ArgumentNullException(nameof(assignments));
    private bool _companion;
    private int _selfActivations;
    private double _elapsed;

    public string? Active { get; private set; }

    public bool Commanding => Active is not null && !_companion;

    public bool Start(string settlerId, SettlerFacts facts)
    {
        ArgumentException.ThrowIfNullOrEmpty(settlerId);
        ArgumentNullException.ThrowIfNull(facts);
        if (!facts.Commandable || !facts.InWorkshop) return false;
        Active = settlerId;
        _companion = facts.Companion;
        _selfActivations = 0;
        _elapsed = 0;
        return true;
    }

    public CommandResult Give(CommandTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(target.ObjectId);
        if (Active is not { } settlerId || _companion) return new CommandResult(CommandOutcome.Ignored);
        if (!target.AllowPlayerAssignment) return new CommandResult(CommandOutcome.Refused);
        if ((target.RequiresActor || target.IsBed) && _assignments.Conflicts(target.ObjectId, target.IsBed)) return new CommandResult(CommandOutcome.Refused);
        Cancel();
        if (!target.RequiresActor && !target.IsBed) return new CommandResult(CommandOutcome.Ended);
        return new CommandResult(CommandOutcome.Assigned, _assignments.Assign(settlerId, target.ObjectId, target.IsBed));
    }

    public bool SelfActivate()
    {
        if (Active is null) return false;
        if (++_selfActivations <= 1) return false;
        Cancel();
        return true;
    }

    public bool Tick(double seconds, bool settlerInBuildArea) => Tick(seconds, () => settlerInBuildArea);

    public bool Tick(double seconds, Func<bool> settlerInBuildArea)
    {
        ArgumentNullException.ThrowIfNull(settlerInBuildArea);
        if (Active is null || !(seconds > 0)) return false;
        _elapsed += seconds;
        if (_elapsed < CheckSeconds) return false;
        _elapsed = 0;
        if (settlerInBuildArea()) return false;
        Cancel();
        return true;
    }

    public void Cancel()
    {
        Active = null;
        _companion = false;
        _selfActivations = 0;
        _elapsed = 0;
    }
}
