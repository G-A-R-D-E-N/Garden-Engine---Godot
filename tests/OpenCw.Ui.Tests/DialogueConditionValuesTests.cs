using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth;
using OpenCommonwealth.Services.Dialogue;
using OpenCommonwealth.Services.Papyrus.LiveState;
using Radicle.Interop;
using ConditionFunctions = OpenCommonwealth.ConditionFunctions;
using QuestState = OpenCommonwealth.Services.Papyrus.Quest.QuestState;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class DialogueConditionValuesTests
{
    static FormKey Key(uint id) => new("Fallout4.esm", id);

    static readonly FormKey Preston = Key(0x19FD9);
    static readonly FormKey Player = Key(0x7);
    static readonly FormKey PlayerRef = Key(0x14);
    static readonly FormKey Faction = Key(0x300);
    static readonly FormKey VoiceA = Key(0xA1);
    static readonly FormKey VoiceB = Key(0xB2);
    static readonly FormKey VoiceC = Key(0xC3);
    static readonly FormKey RaceA = Key(0xD1);

    static ActorFacts Npc(double? level = null) => new(Preston, RaceA, new Dictionary<FormKey, int>(), null, level, false);

    static ActorFacts Pc() => new(Player, null, new Dictionary<FormKey, int> { [Faction] = 0 }, null, null, false);

    static DialogueSource Source() => new(Npc(), 12, Pc, PlayerRef, _ => null);

    static LiveSubject On(ConditionRunOn runOn, FormKey? reference = null) => new((uint)runOn, reference, 0, 0);

    sealed class Quests(QuestState state, int stage = 0, params int[] done) : IQuestState
    {
        public QuestState StateOf(FormKey quest) => state;

        public int StageOf(FormKey quest) => stage;

        public bool StageDone(FormKey quest, int asked) => done.Contains(asked);
    }

    static IConditionLiveState State(bool playerSpoken = false, FormKey? voice = null, IQuestState? quests = null, ActorFacts? npc = null,
        IReadOnlyDictionary<FormKey, IReadOnlyList<FormKey>>? lists = null, IReadOnlyDictionary<FormKey, double?>? globals = null,
        IReadOnlyDictionary<FormKey, double?>? actorValues = null)
    {
        var store = new LiveStateStore { Quests = quests, HourOfDay = 12 };
        var facts = (npc ?? Npc()) with { Voice = voice, Values = actorValues };
        var key = DialogueConditionValues.NpcKey(facts);
        store.PutActor(key, facts);
        store.PutActor(LiveStateStore.PlayerKey, Pc());
        if (globals is not null) store.PrimeGlobals(globals.Keys, global => globals[global]);
        return DialogueConditionValues.StateFor(store, playerSpoken, PlayerRef, key, lists);
    }

    static IReadOnlyDictionary<FormKey, IReadOnlyList<FormKey>> List(FormKey list, params FormKey[] members) =>
        new Dictionary<FormKey, IReadOnlyList<FormKey>> { [list] = members };

    [Fact]
    public void An_npc_line_asks_the_npc_for_run_on_subject_and_the_player_for_run_on_target()
    {
        var state = State();
        Assert.True(state.IsId(On(ConditionRunOn.Subject), Preston));
        Assert.False(state.IsId(On(ConditionRunOn.Subject), Player));
        Assert.True(state.InFaction(On(ConditionRunOn.Target), Faction));
        Assert.False(state.InFaction(On(ConditionRunOn.Subject), Faction));
    }

    [Fact]
    public void A_player_line_swaps_the_roles_so_run_on_subject_is_the_player_and_target_is_the_npc()
    {
        var state = State(playerSpoken: true);
        Assert.True(state.IsId(On(ConditionRunOn.Subject), Player));
        Assert.True(state.IsId(On(ConditionRunOn.Target), Preston));
        Assert.False(state.IsId(On(ConditionRunOn.Subject), Preston));
    }

    [Fact]
    public void A_voice_type_list_matches_any_member_and_a_missing_voice_stays_unknown()
    {
        var list = Key(0x100);
        Assert.True(State(voice: VoiceB, lists: List(list, VoiceA, VoiceB)).IsVoiceType(On(ConditionRunOn.Subject), list));
        Assert.False(State(voice: VoiceC, lists: List(list, VoiceA, VoiceB)).IsVoiceType(On(ConditionRunOn.Subject), list));
        Assert.Null(State(voice: null, lists: List(list, VoiceA, VoiceB)).IsVoiceType(On(ConditionRunOn.Subject), list));
        Assert.Null(State(voice: VoiceA).IsVoiceType(On(ConditionRunOn.Target), VoiceA));
        Assert.True(State(voice: VoiceA).IsVoiceType(On(ConditionRunOn.Subject), VoiceA));
    }

    [Fact]
    public void A_race_list_and_an_id_list_match_any_member_and_an_empty_list_is_unknown()
    {
        var list = Key(0x100);
        Assert.True(State(lists: List(list, Key(0xD0), RaceA)).IsRace(On(ConditionRunOn.Subject), list));
        Assert.False(State(lists: List(list, Key(0xD0))).IsRace(On(ConditionRunOn.Subject), list));
        Assert.True(State(lists: List(list, Key(0x5), Preston)).IsId(On(ConditionRunOn.Subject), list));
        Assert.Null(State(lists: List(list)).IsId(On(ConditionRunOn.Subject), list));
    }

    [Fact]
    public void A_condition_on_the_player_reference_reads_the_player_and_any_other_target_is_unknown()
    {
        var state = State();
        Assert.True(state.InFaction(On(ConditionRunOn.Reference, PlayerRef), Faction));
        Assert.Null(state.InFaction(On(ConditionRunOn.Reference, Key(0x99)), Faction));
        Assert.Null(state.InFaction(On(ConditionRunOn.Reference), Faction));
        Assert.Null(state.IsId(On(ConditionRunOn.QuestAlias), Preston));
    }

    [Fact]
    public void A_quest_condition_reads_the_quest_state_and_is_unknown_without_one()
    {
        var quest = Key(0x500);
        Assert.True(State(quests: new Quests(QuestState.Running)).QuestRunning(quest));
        Assert.False(State(quests: new Quests(QuestState.NotStarted)).QuestRunning(quest));
        Assert.True(State(quests: new Quests(QuestState.Completed)).QuestCompleted(quest));
        Assert.False(State(quests: new Quests(QuestState.Running)).QuestCompleted(quest));
        Assert.Null(State().QuestRunning(quest));
        Assert.Null(State().QuestCompleted(quest));
        Assert.Null(State().QuestStage(quest));
        Assert.Null(State().QuestStageDone(quest, 1));
    }

    [Fact]
    public void A_running_quest_with_a_recorded_stage_answers_the_stage_and_each_stage_done()
    {
        var asked = State(quests: new Quests(QuestState.Running, 20, 10, 20));
        Assert.Equal(20, asked.QuestStage(Key(0x500)));
        Assert.True(asked.QuestStageDone(Key(0x500), 10));
        Assert.False(asked.QuestStageDone(Key(0x500), 15));
    }

    [Fact]
    public void Level_time_globals_and_actor_values_come_from_the_subjects_and_the_prefetched_maps()
    {
        var state = State(npc: Npc(level: 7), globals: new Dictionary<FormKey, double?> { [Key(0x40)] = 2.5 },
            actorValues: new Dictionary<FormKey, double?> { [Key(0x50)] = 9 });
        Assert.Equal(7, state.Level(On(ConditionRunOn.Subject)));
        Assert.Null(State().Level(On(ConditionRunOn.Subject)));
        Assert.Equal(12, state.CurrentTime());
        Assert.Equal(2.5, state.GlobalValue(Key(0x40)));
        Assert.Null(state.GlobalValue(Key(0x41)));
        Assert.Equal(9, state.ActorValue(On(ConditionRunOn.Subject), Key(0x50)));
        Assert.Null(state.ActorValue(On(ConditionRunOn.Target), Key(0x50)));
    }

    sealed class Aliases(bool? answer) : IAliasState
    {
        public (FormKey Quest, int Alias, FormKey Reference, FormKey? Base)? Asked;

        public bool? IsAliasRef(FormKey quest, int alias, FormKey reference, FormKey? baseForm)
        {
            Asked = (quest, alias, reference, baseForm);
            return answer;
        }
    }

    [Fact]
    public void Sex_and_item_count_are_asked_of_the_actor_the_run_on_resolves_to_and_the_player_is_not_the_npc()
    {
        var store = new LiveStateStore();
        var npc = Npc() with { Sex = 1 };
        var key = DialogueConditionValues.NpcKey(npc);
        store.PutActor(key, npc);
        store.PutActor(LiveStateStore.PlayerKey, Pc() with { Sex = 0, Items = item => item == Key(0xF) ? 3 : 0 });
        var state = DialogueConditionValues.StateFor(store, false, PlayerRef, key);

        Assert.Equal(1, state.SexOf(On(ConditionRunOn.Subject)));
        Assert.Equal(0, state.SexOf(On(ConditionRunOn.Reference, PlayerRef)));
        Assert.Null(state.ItemCount(On(ConditionRunOn.Subject), Key(0xF)));
        Assert.Equal(3, state.ItemCount(On(ConditionRunOn.Reference, PlayerRef), Key(0xF)));
        Assert.Null(state.SexOf(On(ConditionRunOn.QuestAlias)));
    }

    [Fact]
    public void An_alias_fill_is_asked_with_the_reference_of_the_resolved_actor_and_the_quest_the_info_is_evaluated_for()
    {
        var store = new LiveStateStore();
        var aliases = new Aliases(true);
        store.Aliases = aliases;
        var npcRef = Key(0x2000);
        var npc = Npc() with { Ref = npcRef };
        var key = DialogueConditionValues.NpcKey(npc);
        store.PutActor(key, npc);
        var state = DialogueConditionValues.StateFor(store, false, PlayerRef, key, quest: Key(0x400));

        Assert.True(state.IsAliasRef(On(ConditionRunOn.Subject), null, 2));
        Assert.Equal((Key(0x400), 2, npcRef, (FormKey?)Preston), aliases.Asked);
        Assert.Null(DialogueConditionValues.StateFor(store, false, PlayerRef, key).IsAliasRef(On(ConditionRunOn.Subject), null, 2));
    }
}
