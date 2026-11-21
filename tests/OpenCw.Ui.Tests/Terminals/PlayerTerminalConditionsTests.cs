using OpenCommonwealth.Services.Ui.Character;
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

    static RecordCondition HasPerk(FormKey perk) =>
        new(new RadicleCondition(0, BitConverter.SingleToUInt32Bits(1f), RadicleConditionFunctions.Id("HasPerk")!.Value, 0, 0, 0, RecipeConditionGate.RunOnTarget, 0, 0, null, null),
            null, perk, null, null);

    [Fact]
    public void A_perk_condition_is_true_for_an_owned_perk_false_for_a_missing_one_and_unknown_without_a_perk_store()
    {
        var perk = FakeRecordReader.Key("000F01:Fallout4.esm");
        var perks = new PlayerPerks();
        var rows = new[] { HasPerk(perk) };
        Assert.Equal(RuleVerdict.False, new PlayerTerminalConditions(new PlayerRecipeFacts(new PlayerItems(), null, perks)).Evaluate(rows));
        perks.Add(RecordKey.Format(perk));
        Assert.Equal(RuleVerdict.True, new PlayerTerminalConditions(new PlayerRecipeFacts(new PlayerItems(), null, perks)).Evaluate(rows));
        Assert.Equal(RuleVerdict.Unevaluable, new PlayerTerminalConditions(new PlayerRecipeFacts(new PlayerItems())).Evaluate(rows));
    }
}
