using System.Runtime.ExceptionServices;

namespace OpenCommonwealth.Services.Papyrus.Save;

public enum LoadStage { Quests, Scripts, State, Actors }

public sealed record LoadIssue(LoadStage Stage, string Owner, string Reason, Exception? Error = null)
{
    public string Line => $"{Stage.ToString().ToLowerInvariant()} stage, {Owner}: {Reason}";
}

public sealed record LoadResult(IReadOnlyList<string> Ran, IReadOnlyList<LoadIssue> Failures, IReadOnlyList<LoadIssue> Skipped, IReadOnlyList<LoadIssue> Warnings)
{
    public bool Succeeded => Failures.Count == 0;

    public bool Failed(LoadStage stage) => Failures.Any(issue => issue.Stage == stage);

    public void ThrowFirst()
    {
        if (Failures.Count > 0 && Failures[0].Error is { } error) ExceptionDispatchInfo.Capture(error).Throw();
    }
}

public sealed class LoadTransaction
{
    private sealed record Step(LoadStage Stage, string Owner, Action Run, Action<Exception>? OnFailure, LoadStage[] Needs);

    private readonly List<Step> _steps = new();
    private readonly List<LoadIssue> _warnings = new();

    // Requires: a step needs only its own or an earlier stage; a step is skipped, not run, when a stage it needs has a failure before it.
    public LoadTransaction Add(LoadStage stage, string owner, Action run, Action<Exception>? onFailure = null, params LoadStage[] needs)
    {
        if (needs.Any(need => need > stage)) throw new ArgumentException($"{owner} cannot need a stage after {stage}", nameof(needs));
        _steps.Add(new Step(stage, owner, run, onFailure, needs));
        return this;
    }

    public void Warn(LoadStage stage, string owner, string reason) => _warnings.Add(new LoadIssue(stage, owner, reason));

    // Invariant: steps run once, in stage order then insertion order; one failing step is recorded and never stops a step that does not need it.
    public LoadResult Run()
    {
        var ran = new List<string>();
        var failures = new List<LoadIssue>();
        var skipped = new List<LoadIssue>();
        foreach (var step in _steps.OrderBy(step => step.Stage))
        {
            var blocker = failures.FirstOrDefault(failure => step.Needs.Contains(failure.Stage));
            if (blocker is not null)
            {
                skipped.Add(new LoadIssue(step.Stage, step.Owner, $"skipped, the {blocker.Stage.ToString().ToLowerInvariant()} stage failed at {blocker.Owner}"));
                continue;
            }
            ran.Add($"{step.Stage}:{step.Owner}");
            try
            {
                step.Run();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failures.Add(new LoadIssue(step.Stage, step.Owner, error.Message, error));
                if (step.OnFailure is null) continue;
                try { step.OnFailure(error); }
                catch (Exception undo) when (undo is not OutOfMemoryException) { error.Data[UndoFailureKey] = undo.Message; }
            }
        }
        return new LoadResult(ran, failures, skipped, _warnings.ToArray());
    }

    public const string UndoFailureKey = "loadUndo";
}
