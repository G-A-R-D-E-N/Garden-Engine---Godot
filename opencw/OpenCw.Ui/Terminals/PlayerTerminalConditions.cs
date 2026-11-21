using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;
using OpenCommonwealth.Services.Ui.Workbench;

namespace OpenCommonwealth.Services.Ui.Terminals;

// Constraint: a terminal runs its conditions with the terminal as subject and the player as target, so Target addresses the player; inputs the facts cannot supply stay Unevaluable.
public sealed class PlayerTerminalConditions(IRecipeFacts facts) : ITerminalConditions
{
    public RuleVerdict Evaluate(IReadOnlyList<RecordCondition> conditions) =>
        RecipeConditionGate.Verdict(conditions, facts, RecipeConditionGate.RunOnTarget);
}
