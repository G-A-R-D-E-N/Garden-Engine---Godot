using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Terminals;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Radicle.Interop;
using Xunit;
using RadicleConditionFunctions = Radicle.Interop.ConditionFunctions;

namespace OpenCw.Ui.Tests;

public class PlayerTerminalConditionsTests
{
    static RecordCondition Level(float value, uint runOn, uint reference = 0) =>
        new(new RadicleCondition(0x60, BitConverter.SingleToUInt32Bits(value), RadicleConditionFunctions.Id("GetLevel")!.Value, 0, 0, 0, runOn, reference, 0, null, null),
            null, null, null, null);

    static PlayerTerminalConditions At(int level) => new(new PlayerRecipeFacts(new PlayerItems(), () => level));

    [Theory]
    [InlineData(4, RuleVerdict.False)]
    [InlineData(5, RuleVerdict.True)]
    public void A_condition_on_the_target_is_evaluated_for_the_player(int level, RuleVerdict expected) =>
        Assert.Equal(expected, At(level).Evaluate(new[] { Level(5f, RecipeConditionGate.RunOnTarget) }));

    [Fact]
    public void A_condition_on_the_terminal_itself_is_unevaluable_not_answered_with_the_player_level()
    {
        Assert.Equal(RuleVerdict.Unevaluable, At(9).Evaluate(new[] { Level(5f, RecipeConditionGate.RunOnSubject) }));
    }

    [Fact]
    public void A_condition_naming_the_player_reference_is_evaluated_for_the_player()
    {
        Assert.Equal(RuleVerdict.True, At(9).Evaluate(new[] { Level(5f, 2, 0x14) }));
    }
}
