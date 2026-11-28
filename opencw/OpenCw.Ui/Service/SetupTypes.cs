using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Service;

public sealed record SetupStepInfo(string Id, string Title, string Description, bool Editable = false, bool Runs = false, int MaxLength = SetupStepInfo.DefaultMaxLength)
{
    public const int DefaultMaxLength = 260;
}

public readonly record struct SetupValidation(bool Ok, string Message);

public enum SetupItemState
{
    Written,
    Skipped,
    Error,
}

public sealed record SetupItemResult(string Item, SetupItemState State, string Message = "");

public sealed record SetupProgress(string Phase, int PhaseIndex, int PhaseCount, int Done, int Total, string Item)
{
    public double Fraction
    {
        get
        {
            if (PhaseCount <= 0) return 0;
            var inPhase = Total <= 0 ? 0 : Math.Clamp((double)Done / Total, 0, 1);
            return Math.Clamp((PhaseIndex + inPhase) / PhaseCount, 0, 1);
        }
    }
}

public interface ISetupRun
{
    SetupProgress Progress { get; }

    bool Finished { get; }

    bool Cancelled { get; }

    IReadOnlyList<SetupItemResult> Results { get; }

    bool Advance();

    void Cancel();
}

public interface ISetupSteps
{
    IReadOnlyList<SetupStepInfo> Steps { get; }

    bool IsComplete { get; }

    string Detect(string stepId);

    SetupValidation Validate(string stepId, string value);

    ISetupRun? Start(string stepId, string value);

    ServiceResult MarkComplete();
}
