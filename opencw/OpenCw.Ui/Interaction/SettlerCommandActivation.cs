using System;
using OpenCommonwealth.Services.Ui.Workshop;

namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread only; handles a target only while a non-companion settler is in the command state and the target is a workshop item (work object, bed or any other item, which only ends the state).
public sealed class SettlerCommandActivation(SettlerCommandSession session, Func<ActivationTarget, CommandTarget?> targetOf, Action<CommandResult>? reported = null)
    : IActivationHandler
{
    public bool Handles(ActivationTarget target) => session.Commanding && targetOf(target) is not null;

    public ActivationResult Activate(ActivationTarget target)
    {
        if (targetOf(target) is not { } command) return new ActivationResult(ActivationStatus.Unhandled);
        var result = session.Give(command);
        reported?.Invoke(result);
        return result.Outcome switch
        {
            CommandOutcome.Assigned or CommandOutcome.Ended => new ActivationResult(ActivationStatus.Handled, result.Outcome.ToString()),
            CommandOutcome.Refused => new ActivationResult(ActivationStatus.Blocked, "the settler assignment was refused", true),
            _ => new ActivationResult(ActivationStatus.Unhandled),
        };
    }

    public static bool IsSettlerOutcome(ActivationResult result) => result.Detail is nameof(CommandOutcome.Assigned) or nameof(CommandOutcome.Ended);
}
