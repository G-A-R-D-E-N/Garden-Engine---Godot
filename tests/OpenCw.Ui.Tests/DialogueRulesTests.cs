using System;
using OpenCommonwealth;
using System.Collections.Generic;
using OpenCommonwealth.Services.Dialogue;
using System.Linq;
using Radicle.Interop;
using ConditionFunctions = OpenCommonwealth.ConditionFunctions;
using ParamKind = Radicle.Interop.Native.ConditionParamKind;
using QuestState = OpenCommonwealth.Services.Papyrus.Quest.QuestState;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class DialogueRulesTests
{
    [Theory]
    [InlineData("Caps You Promised?", null, "Caps You Promised?")]
    [InlineData("Sarcastic", "", "Sarcastic")]
    [InlineData("Let's Get Started", "SomeEdid", "Let's Get Started")]
    [InlineData("***PLEASE MAKE AOT_DN131_1 SAY ONCE***", null, null)]
    [InlineData("DN121MinRadiantIntro", "DN121MinRadiantIntro", null)]
    [InlineData("dn121minradiantintro", "DN121MinRadiantIntro", null)]
    [InlineData("CA_CustomEvent_CompanionCrippled_Head_Gage", "CA_Event_CompanionCrippled_Head_Gage", null)]
    [InlineData("MinRecruit04RadiantSettlementIntro", null, null)]
    [InlineData("CIS_ENL_BunkerHillTopic", null, null)]
    [InlineData("Goodbye", null, "Goodbye")]
    [InlineData("", null, null)]
    [InlineData("   ", "X", null)]
    [InlineData(null, "X", null)]
    public void Menu_text_that_is_an_editor_label_or_a_developer_note_is_dropped(string? name, string? editorId, string? expected) =>
        Assert.Equal(expected, DialogueRules.MenuName(name, editorId));

    static InfoCondition Cond(ushort function) => new(new ConditionRule(0, 0, function, 0, 0), null, null);

    [Fact]
    public void A_group_member_takes_its_head_conditions_before_its_own_and_the_head_speaker_when_it_has_none()
    {
        var head = new[] { Cond(59), Cond(72) };
        var npc = new FormKey("Fallout4.esm", 0x19FD9);
        var (conditions, speaker) = DialogueRules.WithGroupHead([Cond(14)], null, head, npc);
        Assert.Equal(new ushort[] { 59, 72, 14 }, conditions.Select(c => c.Rule.Function).ToArray());
        Assert.Equal(npc, speaker);
    }

    [Fact]
    public void A_member_keeps_its_own_speaker_and_an_ungrouped_info_is_unchanged()
    {
        var own = new FormKey("Fallout4.esm", 0x1);
        var conditions = new[] { Cond(14) };
        Assert.Equal(own, DialogueRules.WithGroupHead(conditions, own, [Cond(59)], new FormKey("Fallout4.esm", 0x2)).Speaker);
        var (same, speaker) = DialogueRules.WithGroupHead(conditions, null, null, null);
        Assert.Same(conditions, same);
        Assert.Null(speaker);
    }

    static readonly FormKey VoiceA = new("Fallout4.esm", 0xA1);
    static readonly FormKey VoiceB = new("Fallout4.esm", 0xB2);

    [Theory]
    [InlineData((ushort)72, true)]
    [InlineData((ushort)69, true)]
    [InlineData((ushort)426, true)]
    [InlineData((ushort)59, false)]
    [InlineData((ushort)74, false)]
    public void Only_id_race_and_voice_type_conditions_address_an_info_to_an_npc(ushort function, bool addresses) =>
        Assert.Equal(addresses, DialogueRules.AddressesBy(function));

    [Fact]
    public void A_voice_type_condition_is_one_for_the_matching_voice_and_zero_for_another()
    {
        var rule = new ConditionRule(0, 1, ConditionFunctions.GetIsVoiceType, 0, 0);
        var match = DialogueRules.VoiceTypeInput(rule, VoiceA, VoiceA);
        var other = DialogueRules.VoiceTypeInput(rule, VoiceA, VoiceB);
        Assert.True(match.Known);
        Assert.Equal(1.0, match.Value);
        Assert.True(other.Known);
        Assert.Equal(0.0, other.Value);
    }

    [Fact]
    public void A_voice_type_condition_is_unknown_without_a_voice_or_with_a_non_subject_target_or_a_global_comparison()
    {
        var plain = new ConditionRule(0, 1, ConditionFunctions.GetIsVoiceType, 0, 0);
        Assert.False(DialogueRules.VoiceTypeInput(plain, VoiceA, null).Known);
        Assert.False(DialogueRules.VoiceTypeInput(plain, null, VoiceA).Known);
        Assert.False(DialogueRules.VoiceTypeInput(plain with { RunOn = 1 }, VoiceA, VoiceA).Known);
        Assert.False(DialogueRules.VoiceTypeInput(plain with { FlagsByte = ConditionFunctions.FlagUseGlobal }, VoiceA, VoiceA).Known);
    }

    sealed record Snap(QuestState State, int Stage = 0, int[]? Done = null);

    sealed class FakeQuests(Dictionary<FormKey, Snap> map) : IQuestState
    {
        public QuestState StateOf(FormKey quest) => map.TryGetValue(quest, out var s) ? s.State : QuestState.NotStarted;

        public int StageOf(FormKey quest) => map.TryGetValue(quest, out var s) ? s.Stage : 0;

        public bool StageDone(FormKey quest, int stage) => map.TryGetValue(quest, out var s) && s.Done is { } done && done.Contains(stage);
    }

    static readonly FormKey QuestA = new("Fallout4.esm", 0x100);
    static readonly FormKey QuestB = new("Fallout4.esm", 0x200);
    static readonly FormKey QuestC = new("Fallout4.esm", 0x300);

    static readonly FakeQuests Quests = new(new()
    {
        [QuestA] = new Snap(QuestState.NotStarted),
        [QuestB] = new Snap(QuestState.Running, 40, [10, 40]),
        [QuestC] = new Snap(QuestState.Running),
    });

    static ConditionInput Quest(ushort function, FormKey? quest, int stage = 0, IQuestState? state = null, byte flags = 0) =>
        DialogueRules.QuestInput(new ConditionRule(flags, 1, function, 0, 0), quest, stage, null, state ?? Quests, _ => null);

    [Fact]
    public void An_unstarted_quest_is_not_running_not_completed_at_stage_zero_with_no_stage_done()
    {
        foreach (var function in new[] { ConditionFunctions.GetQuestRunning, ConditionFunctions.GetQuestCompleted, ConditionFunctions.GetStage })
        {
            var input = Quest(function, QuestA);
            Assert.True(input.Known);
            Assert.Equal(0.0, input.Value);
        }
        var done = Quest(ConditionFunctions.GetStageDone, QuestA, 10);
        Assert.True(done.Known);
        Assert.Equal(0.0, done.Value);
    }

    [Fact]
    public void A_running_quest_answers_its_stage_and_every_stage_done_and_a_stage_never_set_is_zero_not_unknown()
    {
        Assert.Equal(1.0, Quest(ConditionFunctions.GetQuestRunning, QuestB).Value);
        Assert.Equal(40.0, Quest(ConditionFunctions.GetStage, QuestB).Value);
        Assert.Equal(1.0, Quest(ConditionFunctions.GetStageDone, QuestB, 40).Value);
        Assert.Equal(1.0, Quest(ConditionFunctions.GetStageDone, QuestB, 10).Value);
        Assert.Equal(0.0, Quest(ConditionFunctions.GetStageDone, QuestB, 20).Value);
        Assert.True(Quest(ConditionFunctions.GetStageDone, QuestB, 20).Known);
        Assert.Equal(1.0, Quest(ConditionFunctions.GetQuestRunning, QuestC).Value);
        Assert.Equal(0.0, Quest(ConditionFunctions.GetStage, QuestC).Value);
        Assert.True(Quest(ConditionFunctions.GetStage, QuestC).Known);
        Assert.Equal(0.0, Quest(ConditionFunctions.GetStageDone, QuestC, 10).Value);
    }

    [Fact]
    public void A_completed_quest_answers_completed_and_not_running()
    {
        var state = new FakeQuests(new() { [QuestA] = new Snap(QuestState.Completed, 200, [200]) });
        Assert.Equal(1.0, Quest(ConditionFunctions.GetQuestCompleted, QuestA, state: state).Value);
        Assert.Equal(0.0, Quest(ConditionFunctions.GetQuestRunning, QuestA, state: state).Value);
        Assert.Equal(200.0, Quest(ConditionFunctions.GetStage, QuestA, state: state).Value);
    }

    [Fact]
    public void A_quest_condition_is_unknown_without_a_state_or_a_quest_or_with_pack_data_flags_and_a_quest_the_state_never_saw_is_not_started()
    {
        Assert.False(DialogueRules.QuestInput(new ConditionRule(0, 1, ConditionFunctions.GetStage, 0, 0), QuestA, 0, null, null, _ => null).Known);
        Assert.False(Quest(ConditionFunctions.GetStage, null).Known);
        Assert.Equal(0.0, Quest(ConditionFunctions.GetStage, new FormKey("Fallout4.esm", 0x999)).Value);
        Assert.Equal(0.0, Quest(ConditionFunctions.GetQuestRunning, new FormKey("Fallout4.esm", 0x999)).Value);
        Assert.False(Quest(ConditionFunctions.GetStage, QuestA, flags: 0x08).Known);
        Assert.False(Quest(14, QuestA).Known);
    }

    [Fact]
    public void A_quest_condition_compared_against_a_global_carries_the_global_value()
    {
        var glob = new FormKey("Fallout4.esm", 0x400);
        var input = DialogueRules.QuestInput(new ConditionRule(ConditionFunctions.FlagUseGlobal, 7, ConditionFunctions.GetStage, 0, 0), QuestB, 0, glob, Quests, k => k == glob ? 40.0 : null);
        Assert.True(input.GlobalKnown);
        Assert.Equal(40.0, input.GlobalValue);
    }

    [Fact]
    public void Every_function_whose_first_parameter_is_a_quest_is_a_quest_function()
    {
        Assert.Equal(new[] { true, true, true, true, false, true, true },
            new ushort[] { 56, 58, 59, 543, 74, 629, 664 }.Select(ConditionFunctions.IsQuestFunction).ToArray());
    }

    [Theory]
    [InlineData(0u, false, false, ConditionTarget.Npc)]
    [InlineData(1u, false, false, ConditionTarget.Player)]
    [InlineData(0u, false, true, ConditionTarget.Player)]
    [InlineData(1u, false, true, ConditionTarget.Npc)]
    [InlineData(2u, true, false, ConditionTarget.Player)]
    [InlineData(2u, true, true, ConditionTarget.Player)]
    [InlineData(2u, false, false, ConditionTarget.Unknown)]
    [InlineData(5u, true, false, ConditionTarget.Unknown)]
    [InlineData(3u, false, true, ConditionTarget.Unknown)]
    public void A_condition_runs_on_the_npc_or_the_player_depending_on_who_speaks_the_line(uint runOn, bool referenceIsPlayer, bool playerSpoken, ConditionTarget expected) =>
        Assert.Equal(expected, DialogueRules.TargetOf(runOn, referenceIsPlayer, playerSpoken));

    [Fact]
    public void Rewriting_a_rule_to_the_subject_keeps_everything_but_the_run_on()
    {
        var rule = new ConditionRule(0x10, 7, 71, 1, 9);
        var subject = DialogueRules.AsSubject(rule);
        Assert.Equal(0u, subject.RunOn);
        Assert.Equal(rule with { RunOn = 0 }, subject);
    }

    [Fact]
    public void A_topic_is_available_only_while_its_quest_runs_and_nothing_is_gated_without_a_state_or_a_quest()
    {
        Assert.True(DialogueRules.QuestAllows(null, QuestA));
        Assert.True(DialogueRules.QuestAllows(Quests, null));
        Assert.False(DialogueRules.QuestAllows(Quests, QuestA));
        Assert.True(DialogueRules.QuestAllows(Quests, QuestB));
        Assert.True(DialogueRules.QuestAllows(Quests, QuestC));
        Assert.False(DialogueRules.QuestAllows(Quests, new FormKey("Fallout4.esm", 0x999)));
        var done = new FakeQuests(new() { [QuestA] = new Snap(QuestState.Completed, 200), [QuestB] = new Snap(QuestState.NotStarted, 10) });
        Assert.False(DialogueRules.QuestAllows(done, QuestA));
        Assert.False(DialogueRules.QuestAllows(done, QuestB));
    }

    static DialogueCondition Raw(ushort function, int param1 = 0, int param2 = 0, uint runOn = 0, uint reference = 0, byte flags = 0, uint comparison = 0) =>
        new(flags, comparison, function, 0, param1, param2, runOn, reference, 0, null, null);

    static FormKey Key(uint id) => new("Fallout4.esm", id);

    static RadicleConditionParam Slot(ParamKind kind, uint id) =>
        new(kind, null, (int)id, id, null, kind == ParamKind.Form && id != 0 ? (FormKey?)Key(id) : null, false);

    static RadicleTypedCondition Typed(DialogueCondition c, ParamKind param1 = ParamKind.Form) => new(null,
        Slot(param1, (uint)c.Param1), Slot(ParamKind.Integer, (uint)c.Param2),
        (c.FlagsByte & ConditionFunctions.FlagUseGlobal) != 0 ? Slot(ParamKind.Form, c.ComparisonRaw) : Slot(ParamKind.Float, c.ComparisonRaw),
        c.RunOn == 2 ? Slot(ParamKind.Form, c.Reference) : Slot(ParamKind.None, c.Reference));

    static InfoCondition From(DialogueCondition c, ParamKind param1 = ParamKind.Form, Func<FormKey, IReadOnlyList<FormKey>?>? lists = null, FormKey? playerRef = null) =>
        DialogueRules.ConditionFrom(c, Typed(c, param1), lists ?? (_ => null), playerRef);

    [Fact]
    public void A_condition_resolves_its_form_parameter_only_for_functions_that_take_one()
    {
        Assert.Equal(Key(0x55), From(Raw(72, 0x55)).Param);
        Assert.Null(From(Raw(14, 0x55), ParamKind.Other).Param);
        Assert.Equal(Key(0x55), From(Raw(ConditionFunctions.GetIsVoiceType, 0x55)).Param);
        Assert.Equal(Key(0x55), From(Raw(ConditionFunctions.GetStageDone, 0x55, 20)).Param);
        Assert.Equal(20, From(Raw(ConditionFunctions.GetStageDone, 0x55, 20)).Param2);
        Assert.Null(From(Raw(72, 0)).Param);
    }

    // Constraint: Regression: an alias or package-data switched reference slot must not be read as a FormID, and one unresolved FormID must leave the condition without a key.
    [Fact]
    public void A_parameter_the_export_reports_as_an_index_or_as_unresolved_carries_no_form_key()
    {
        var aliased = Raw(ConditionFunctions.GetIsVoiceType, 3, flags: 0x02);
        var indexed = Typed(aliased, ParamKind.Index) with { Param1 = Slot(ParamKind.Index, 3) with { Form = Key(3) } };
        var aliasedCondition = DialogueRules.ConditionFrom(aliased, indexed, _ => new[] { Key(3) }, null);
        Assert.Null(aliasedCondition.Param);
        Assert.Null(aliasedCondition.Members);

        var outside = Raw(72, 0x01000800);
        var unresolved = Typed(outside) with { Param1 = Slot(ParamKind.Form, 0) with { FormUnresolved = true } };
        Assert.Null(DialogueRules.ConditionFrom(outside, unresolved, _ => null, null).Param);
    }

    [Fact]
    public void A_global_comparison_is_resolved_only_when_the_use_global_flag_is_set()
    {
        Assert.Equal(Key(0x77), From(Raw(74, 0x55, flags: ConditionFunctions.FlagUseGlobal, comparison: 0x77)).ComparisonGlobal);
        Assert.Null(From(Raw(74, 0x55, comparison: 0x77)).ComparisonGlobal);
    }

    [Fact]
    public void A_voice_type_list_parameter_expands_to_its_members_and_a_plain_record_does_not()
    {
        var list = Key(0x100);
        var members = new[] { VoiceA, VoiceB };
        IReadOnlyList<FormKey>? Lists(FormKey k) => k == list ? members : null;
        Assert.Equal(members, From(Raw(ConditionFunctions.GetIsVoiceType, 0x100), lists: Lists).Members);
        Assert.Equal(members, From(Raw(ConditionFunctions.GetIsRace, 0x100), lists: Lists).Members);
        Assert.Equal(members, From(Raw(ConditionFunctions.GetIsID, 0x100), lists: Lists).Members);
        Assert.Null(From(Raw(ConditionFunctions.GetIsVoiceType, 0x200), lists: Lists).Members);
        Assert.Null(From(Raw(ConditionFunctions.GetStage, 0x100), lists: Lists).Members);
    }

    [Fact]
    public void A_reference_condition_is_on_the_player_only_when_it_names_the_player_reference()
    {
        var playerRef = Key(0x14);
        Assert.True(From(Raw(71, 0x55, runOn: 2, reference: 0x14), playerRef: playerRef).ReferenceIsPlayer);
        Assert.False(From(Raw(71, 0x55, runOn: 2, reference: 0x15), playerRef: playerRef).ReferenceIsPlayer);
        Assert.False(From(Raw(71, 0x55, runOn: 2, reference: 0x14), playerRef: null).ReferenceIsPlayer);
        Assert.False(From(Raw(71, 0x55, runOn: 1, reference: 0x14), playerRef: playerRef).ReferenceIsPlayer);
    }

    [Fact]
    public void An_info_is_owned_by_its_speaker_and_by_every_member_of_a_listed_address_or_the_single_address()
    {
        var npc = Key(1);
        var list = Key(0x100);
        var single = From(Raw(ConditionFunctions.GetIsVoiceType, 0x200));
        var listed = From(Raw(ConditionFunctions.GetIsVoiceType, 0x100), lists: k => k == list ? new[] { VoiceA, VoiceB } : null);
        var other = From(Raw(ConditionFunctions.GetStage, 0x300), ParamKind.Other);
        var owners = DialogueRules.OwnersOf(npc, new[] { single, listed, other }).ToHashSet();
        Assert.Equal(new HashSet<FormKey> { npc, Key(0x200), VoiceA, VoiceB }, owners);
        Assert.DoesNotContain(list, owners);
    }

    [Fact]
    public void A_listed_address_matches_when_any_member_does_and_is_unknown_only_when_no_member_matches_and_one_is_unknown()
    {
        ConditionInput K(double v) => new(true, v, false, 0, 0, 0);
        var none = new ConditionInput(false, 0, false, 0, 0, 0);
        Assert.Equal(1.0, DialogueRules.AnyOf([K(0), K(1)]).Value);
        Assert.Equal(1.0, DialogueRules.AnyOf([none, K(1)]).Value);
        Assert.True(DialogueRules.AnyOf([K(0), K(0)]).Known);
        Assert.Equal(0.0, DialogueRules.AnyOf([K(0), K(0)]).Value);
        Assert.False(DialogueRules.AnyOf([K(0), none]).Known);
        Assert.False(DialogueRules.AnyOf([]).Known);
    }

    static DialogueInfoData InfoWith(FormKey? speaker, FormKey? quest) =>
        new(QuestA, new DialogueInfo(null, null, null, null, null, null, null, speaker, null, new DialogueText(Radicle.Interop.Native.NameKind.Absent, null, 0), [], []), [], null, [], quest);

    [Fact]
    public void An_info_is_admitted_for_its_speaker_and_while_its_quest_runs()
    {
        var npc = Key(1);
        Assert.True(DialogueRules.Admits(npc, InfoWith(null, null), Quests));
        Assert.True(DialogueRules.Admits(npc, InfoWith(npc, QuestB), Quests));
        Assert.False(DialogueRules.Admits(npc, InfoWith(Key(2), QuestB), Quests));
        Assert.False(DialogueRules.Admits(npc, InfoWith(npc, QuestA), Quests));
        Assert.True(DialogueRules.Admits(npc, InfoWith(npc, QuestA), null));
    }
}
