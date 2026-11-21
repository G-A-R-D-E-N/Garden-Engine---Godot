using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth;
using OpenCommonwealth.Services.Dialogue;
using Radicle.Interop;
using ConditionFunctions = OpenCommonwealth.ConditionFunctions;
using QuestState = OpenCommonwealth.Services.Papyrus.Quest.QuestState;
using Xunit;

namespace OpenCw.Ui.Tests;

// Constraint: both typed reads must hand PackageRules.EvaluateConditions a row it decides exactly as the facts say; a wrong run-on or comparison-operator mapping, and an unknown fact collapsed to False, must fail here.
public sealed class ConditionParityTests
{
    private const int Equal = 0;
    private const int GreaterThan = 2;
    private const int GreaterOrEqual = 3;
    private const int LessThan = 4;

    private static readonly FormKey Self = Key(0x1A);
    private static readonly FormKey Race = Key(0x2B);
    private static readonly FormKey Faction = Key(0x3C);
    private static readonly FormKey Keyword = Key(0x4D);
    private static readonly FormKey Global = Key(0x5E);
    private static readonly FormKey Quest = Key(0x6F);
    private static readonly FormKey Voice = Key(0x7A);
    private static readonly FormKey Player = Key(0x7);

    private static FormKey Key(uint id) => new("Fallout4.esm", id);

    private static ConditionRule Row(ushort function, int comparison, float value, byte flags = 0, uint runOn = 0, uint reference = 0) =>
        new((byte)((comparison << 5) | flags), BitConverter.SingleToUInt32Bits(value), function, runOn, reference);

    private static RuleVerdict Decide(ConditionRule rule, ConditionInput value) =>
        PackageRules.EvaluateConditions(new[] { rule }, new[] { value }).Verdict;

    private static ConditionInput PackageRead(ConditionRule rule, ConditionSubject subject, FormKey? param = null, FormKey? comparisonGlobal = null) =>
        PackageConditionValues.Value(rule, param, comparisonGlobal, subject);

    private static ConditionSubject Subject(double? level = 12, bool? dead = false, double hour = 13.1,
        Func<FormKey, double?>? global = null, Func<FormKey, int?>? stage = null, Func<FormKey, int, bool?>? stageDone = null) =>
        new(Self, Race, new Dictionary<FormKey, int> { [Faction] = 2 }, new HashSet<FormKey> { Keyword },
            level, dead, hour, global ?? (key => key == Global ? 2.5 : null), stage, stageDone);

    private static ConditionSubject PlayerSubject() =>
        new(Player, null, new Dictionary<FormKey, int>(), null, null, false, 13.1, _ => null);

    [Fact]
    public void Package_rows_decide_the_same_way_under_every_comparison_operator()
    {
        var subject = Subject(level: 12);

        RuleVerdict Level(int comparison, float value)
        {
            var rule = Row(ConditionFunctions.GetLevel, comparison, value);
            return Decide(rule, PackageRead(rule, subject));
        }

        Assert.Equal(RuleVerdict.True, Level(Equal, 12));
        Assert.Equal(RuleVerdict.False, Level(Equal, 11));
        Assert.Equal(RuleVerdict.True, Level(GreaterThan, 11));
        Assert.Equal(RuleVerdict.False, Level(GreaterThan, 12));
        Assert.Equal(RuleVerdict.True, Level(GreaterOrEqual, 12));
        Assert.Equal(RuleVerdict.False, Level(GreaterOrEqual, 13));
        Assert.Equal(RuleVerdict.True, Level(LessThan, 20));
        Assert.Equal(RuleVerdict.False, Level(LessThan, 12));
    }

    [Fact]
    public void A_fact_the_actor_does_not_have_is_unevaluable_where_a_failed_comparison_is_false()
    {
        var rule = Row(ConditionFunctions.GetLevel, Equal, 12);

        var missing = PackageRead(rule, Subject(level: null));
        Assert.False(missing.Known);
        Assert.Equal(RuleVerdict.Unevaluable, Decide(rule, missing));

        var failed = PackageRead(rule, Subject(level: 11));
        Assert.True(failed.Known);
        Assert.Equal(RuleVerdict.False, Decide(rule, failed));
    }

    [Fact]
    public void A_row_that_does_not_run_on_the_subject_stays_unevaluable_even_when_the_fact_is_known()
    {
        var rule = Row(ConditionFunctions.GetLevel, Equal, 12, runOn: 1, reference: 9);
        var value = PackageRead(rule, Subject(level: 12));
        Assert.False(value.Known);
        Assert.Equal(RuleVerdict.Unevaluable, Decide(rule, value));
    }

    [Fact]
    public void The_typed_value_and_the_comparison_global_are_narrowed_to_the_float_the_evaluator_compares()
    {
        var subject = Subject(hour: 13.1, global: key => key == Global ? 13.1 : 2.5);

        var plain = Row(ConditionFunctions.GetCurrentTime, Equal, 13.1f);
        var hour = PackageRead(plain, subject);
        Assert.Equal((double)13.1f, hour.Value);
        Assert.NotEqual(13.1, hour.Value);
        Assert.Equal(RuleVerdict.True, Decide(plain, hour));

        var withGlobal = Row(ConditionFunctions.GetCurrentTime, Equal, 0, ConditionFunctions.FlagUseGlobal);
        var global = PackageRead(withGlobal, subject, comparisonGlobal: Global);
        Assert.True(global.GlobalKnown);
        Assert.Equal((double)13.1f, global.GlobalValue);
        Assert.Equal(RuleVerdict.True, Decide(withGlobal, global));
    }

    [Fact]
    public void Faction_and_keyword_rows_decide_from_the_subject_facts()
    {
        var subject = Subject();

        RuleVerdict Of(ushort function, int comparison, float value, FormKey param)
        {
            var rule = Row(function, comparison, value);
            return Decide(rule, PackageRead(rule, subject, param));
        }

        Assert.Equal(RuleVerdict.True, Of(ConditionFunctions.GetInFaction, Equal, 1, Faction));
        Assert.Equal(RuleVerdict.False, Of(ConditionFunctions.GetInFaction, Equal, 1, Key(0x3D)));
        Assert.Equal(RuleVerdict.True, Of(ConditionFunctions.GetFactionRank, Equal, -1, Key(0x3D)));
        Assert.Equal(RuleVerdict.True, Of(ConditionFunctions.GetFactionRank, GreaterOrEqual, 2, Faction));
        Assert.Equal(RuleVerdict.True, Of(ConditionFunctions.HasKeyword, Equal, 1, Keyword));
        Assert.Equal(RuleVerdict.False, Of(ConditionFunctions.HasKeyword, Equal, 1, Key(0x4E)));
    }

    [Fact]
    public void Both_reads_decide_a_quest_stage_row_the_same_way_the_evaluator_does()
    {
        var rule = Row(ConditionFunctions.GetStage, Equal, 20);
        var quests = new Stages(QuestState.Running, 20);
        var package = PackageRead(rule, Subject(stage: key => key == Quest ? 20 : 0), Quest);
        var dialogue = DialogueConditionValues.Value(new InfoCondition(rule, Quest, null), false, Subject(), PlayerSubject, Voice, quests);
        Assert.Equal(RuleVerdict.True, Decide(rule, package));
        Assert.Equal(RuleVerdict.True, Decide(rule, dialogue));

        var other = Row(ConditionFunctions.GetStage, Equal, 21);
        var packageFalse = PackageRead(other, Subject(stage: key => key == Quest ? 20 : 0), Quest);
        var dialogueFalse = DialogueConditionValues.Value(new InfoCondition(other, Quest, null), false, Subject(), PlayerSubject, Voice, quests);
        Assert.Equal(RuleVerdict.False, Decide(other, packageFalse));
        Assert.Equal(RuleVerdict.False, Decide(other, dialogueFalse));

        var noQuestState = DialogueConditionValues.Value(new InfoCondition(rule, Quest, null), false, Subject(), PlayerSubject, Voice, null);
        Assert.Equal(RuleVerdict.Unevaluable, Decide(rule, noQuestState));
    }

    [Fact]
    public void A_dialogue_row_keeps_its_own_run_on_and_the_evaluator_decides_it_only_then()
    {
        var onPlayer = Row(ConditionFunctions.GetIsID, Equal, 1, runOn: 1, reference: 9);
        var value = DialogueConditionValues.Value(new InfoCondition(onPlayer, Player, null), false, Subject(), PlayerSubject, Voice, null);
        Assert.True(value.Known);
        Assert.Equal(1u, value.ResolvedRunOn);
        Assert.Equal(9u, value.ResolvedReference);
        Assert.Equal(RuleVerdict.True, Decide(onPlayer, value));
        Assert.Equal(RuleVerdict.Unevaluable, Decide(onPlayer, value with { ResolvedRunOn = 0 }));

        var onSubject = Row(ConditionFunctions.GetIsID, Equal, 1);
        Assert.Equal(RuleVerdict.True, Decide(onSubject, DialogueConditionValues.Value(new InfoCondition(onSubject, Self, null), false, Subject(), PlayerSubject, Voice, null)));
        Assert.Equal(RuleVerdict.True, Decide(onSubject, DialogueConditionValues.Value(new InfoCondition(onSubject, Player, null), true, Subject(), PlayerSubject, Voice, null)));
        Assert.Equal(RuleVerdict.False, Decide(onSubject, DialogueConditionValues.Value(new InfoCondition(onSubject, Player, null), false, Subject(), PlayerSubject, Voice, null)));
    }

    [Fact]
    public void A_voice_type_row_is_read_from_the_speaker_only_when_it_runs_on_the_subject()
    {
        var rule = Row(ConditionFunctions.GetIsVoiceType, Equal, 1);
        var match = DialogueConditionValues.Value(new InfoCondition(rule, Voice, null), false, Subject(), PlayerSubject, Voice, null);
        Assert.Equal(RuleVerdict.True, Decide(rule, match));

        var offTarget = rule with { RunOn = 1 };
        var unknown = DialogueConditionValues.Value(new InfoCondition(offTarget, Voice, null), false, Subject(), PlayerSubject, Voice, null);
        Assert.False(unknown.Known);
        Assert.Equal(RuleVerdict.Unevaluable, Decide(offTarget, unknown));
    }

    [Fact]
    public void An_or_group_of_dialogue_members_is_true_when_any_member_matches_and_unevaluable_without_a_subject_base()
    {
        var rule = Row(ConditionFunctions.GetIsID, Equal, 1);
        RuleVerdict Of(ConditionSubject npc, params FormKey[] members) =>
            Decide(rule, DialogueConditionValues.Value(new InfoCondition(rule, null, null, Members: members), false, npc, PlayerSubject, Voice, null));

        Assert.Equal(RuleVerdict.True, Of(Subject(), Key(0x99), Self));
        Assert.Equal(RuleVerdict.False, Of(Subject(), Key(0x99), Key(0x98)));
        Assert.Equal(RuleVerdict.Unevaluable, Of(Subject() with { Base = null }, Key(0x99), Self));
    }

    [Fact]
    public void An_info_is_eligible_only_when_every_condition_is_true_and_an_undecidable_one_is_counted()
    {
        var atLeastTen = new InfoCondition(Row(ConditionFunctions.GetLevel, GreaterOrEqual, 10), null, null);
        var overFifty = new InfoCondition(Row(ConditionFunctions.GetLevel, GreaterThan, 50), null, null);
        var inFaction = new InfoCondition(Row(ConditionFunctions.GetInFaction, Equal, 1), Faction, null);

        Assert.Equal((true, (ushort?)null), DialogueConditionValues.Verdict([], false, Subject(), PlayerSubject, Voice, null));
        Assert.Equal((true, (ushort?)null), DialogueConditionValues.Verdict([atLeastTen, inFaction], false, Subject(), PlayerSubject, Voice, null));
        Assert.Equal((false, (ushort?)null), DialogueConditionValues.Verdict([atLeastTen, overFifty], false, Subject(), PlayerSubject, Voice, null));
        Assert.Equal((false, (ushort?)ConditionFunctions.GetLevel), DialogueConditionValues.Verdict([atLeastTen], false, Subject(level: null), PlayerSubject, Voice, null));
    }

    [Fact]
    public void A_faction_the_player_joined_at_runtime_decides_a_condition_that_runs_on_the_player()
    {
        var onPlayer = new InfoCondition(Row(ConditionFunctions.GetInFaction, Equal, 1, runOn: 1), Faction, null);
        var joined = new OpenCommonwealth.Services.Ui.Character.PlayerFactions();
        ConditionSubject PlayerWith(OpenCommonwealth.Services.Ui.Character.PlayerFactions overlay) =>
            new(Player, null, overlay.Merge(new Dictionary<FormKey, int>()), null, null, false, 13.1, _ => null);

        Assert.False(DialogueConditionValues.Verdict([onPlayer], false, Subject(), () => PlayerWith(joined), Voice, null).Eligible);

        joined.SetRank("00003C:Fallout4.esm", 1);
        Assert.True(DialogueConditionValues.Verdict([onPlayer], false, Subject(), () => PlayerWith(joined), Voice, null).Eligible);
    }

    private sealed class Stages(QuestState state, int stage, params int[] done) : IQuestState
    {
        public QuestState StateOf(FormKey quest) => state;

        public int StageOf(FormKey quest) => stage;

        public bool StageDone(FormKey quest, int asked) => done.Contains(asked);
    }
}
