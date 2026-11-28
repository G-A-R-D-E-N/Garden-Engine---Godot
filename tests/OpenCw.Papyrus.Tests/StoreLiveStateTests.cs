using OpenCommonwealth;
using OpenCommonwealth.Services.Dialogue;
using OpenCommonwealth.Services.Papyrus.LiveState;
using OpenCommonwealth.Services.Papyrus.Quest;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class StoreLiveStateTests
{
    static FormKey Key(uint id) => new("Fallout4.esm", id);

    static LiveSubject On(ConditionRunOn runOn = ConditionRunOn.Subject) => new((uint)runOn, null, 0, 0);

    static StoreLiveState Subjects(LiveStateStore store, IReadOnlyDictionary<FormKey, IReadOnlyList<FormKey>>? lists = null) =>
        new(store, who => who.RunOn == (uint)ConditionRunOn.Subject ? "self" : who.RunOn == (uint)ConditionRunOn.Target ? "other" : null, lists);

    static LiveStateStore With(ActorFacts facts)
    {
        var store = new LiveStateStore();
        store.PutActor("self", facts);
        return store;
    }

    sealed class Quests(Func<FormKey, QuestState> state, Func<FormKey, int> stage, Func<FormKey, int, bool> done) : IQuestState
    {
        public int Reads;

        public Func<FormKey, bool> Known = _ => true;

        public bool IsKnown(FormKey quest) => Known(quest);

        public QuestState StateOf(FormKey quest) { Reads++; return state(quest); }

        public int StageOf(FormKey quest) { Reads++; return stage(quest); }

        public bool StageDone(FormKey quest, int stage) { Reads++; return done(quest, stage); }
    }

    [Fact]
    public void Actor_facts_answer_from_the_actor_the_run_on_resolves_to()
    {
        var facts = new ActorFacts(Key(1), Key(2), new Dictionary<FormKey, int> { [Key(3)] = 2 }, new HashSet<FormKey> { Key(4) }, 7, true, Key(5));
        var store = With(facts);
        store.HourOfDay = 13;
        var state = Subjects(store);
        Assert.Equal(13, state.CurrentTime());
        Assert.True(state.IsDead(On()));
        Assert.Equal(7, state.Level(On()));
        Assert.True(state.IsId(On(), Key(1)));
        Assert.False(state.IsId(On(), Key(9)));
        Assert.True(state.IsRace(On(), Key(2)));
        Assert.True(state.InFaction(On(), Key(3)));
        Assert.False(state.InFaction(On(), Key(9)));
        Assert.Equal(2, state.FactionRank(On(), Key(3)));
        Assert.Equal(-1, state.FactionRank(On(), Key(9)));
        Assert.True(state.HasKeyword(On(), Key(4)));
        Assert.False(state.HasKeyword(On(), Key(9)));
        Assert.True(state.IsVoiceType(On(), Key(5)));
        Assert.False(state.IsVoiceType(On(), Key(9)));
    }

    [Fact]
    public void A_fact_the_store_does_not_hold_is_unknown_and_never_false()
    {
        var state = Subjects(With(new ActorFacts(null, null, null, null, null, null)));
        Assert.Null(state.CurrentTime());
        Assert.Null(state.IsDead(On()));
        Assert.Null(state.Level(On()));
        Assert.Null(state.IsId(On(), Key(1)));
        Assert.Null(state.IsRace(On(), Key(1)));
        Assert.Null(state.InFaction(On(), Key(1)));
        Assert.Null(state.FactionRank(On(), Key(1)));
        Assert.Null(state.HasKeyword(On(), Key(1)));
        Assert.Null(state.IsVoiceType(On(), Key(1)));
        Assert.Null(state.ActorValue(On(), Key(1)));
        Assert.Null(state.GlobalValue(Key(1)));
        Assert.Null(state.QuestStage(Key(1)));
        Assert.Null(state.QuestStageDone(Key(1), 10));
        Assert.Null(state.QuestRunning(Key(1)));
        Assert.Null(state.QuestCompleted(Key(1)));
    }

    [Theory]
    [InlineData(ConditionRunOn.Reference)]
    [InlineData(ConditionRunOn.CombatTarget)]
    [InlineData(ConditionRunOn.QuestAlias)]
    public void A_run_on_no_actor_stands_for_is_unknown(ConditionRunOn runOn)
    {
        var values = new Dictionary<FormKey, double?> { [Key(6)] = 5 };
        var state = Subjects(With(new ActorFacts(Key(1), Key(2), new Dictionary<FormKey, int> { [Key(3)] = 2 }, new HashSet<FormKey> { Key(4) }, 7, true, Key(5), values)));
        Assert.Null(state.IsDead(On(runOn)));
        Assert.Null(state.Level(On(runOn)));
        Assert.Null(state.IsId(On(runOn), Key(1)));
        Assert.Null(state.IsRace(On(runOn), Key(2)));
        Assert.Null(state.InFaction(On(runOn), Key(3)));
        Assert.Null(state.FactionRank(On(runOn), Key(3)));
        Assert.Null(state.HasKeyword(On(runOn), Key(4)));
        Assert.Null(state.IsVoiceType(On(runOn), Key(5)));
        Assert.Null(state.ActorValue(On(runOn), Key(6)));
    }

    [Fact]
    public void Two_actors_in_the_store_answer_separately()
    {
        var store = With(new ActorFacts(Key(1), null, null, null, 3, false));
        store.PutActor("other", new ActorFacts(Key(2), null, null, null, 40, true));
        var state = Subjects(store);
        Assert.Equal(3, state.Level(On(ConditionRunOn.Subject)));
        Assert.Equal(40, state.Level(On(ConditionRunOn.Target)));
        Assert.False(state.IsDead(On(ConditionRunOn.Subject)));
        Assert.True(state.IsDead(On(ConditionRunOn.Target)));
    }

    [Fact]
    public void A_form_list_matches_when_any_member_does_and_is_unknown_when_a_member_cannot_be_decided()
    {
        var list = Key(50);
        var lists = new Dictionary<FormKey, IReadOnlyList<FormKey>> { [list] = new[] { Key(8), Key(9) } };
        Assert.True(Subjects(With(new ActorFacts(Key(9), Key(9), null, null, null, null, Key(9))), lists).IsId(On(), list));
        Assert.False(Subjects(With(new ActorFacts(Key(1), Key(1), null, null, null, null, Key(1))), lists).IsRace(On(), list));
        Assert.Null(Subjects(With(new ActorFacts(Key(1), null, null, null, null, null)), lists).IsRace(On(), list));
        Assert.Null(Subjects(With(new ActorFacts(Key(1), null, null, null, null, null)), new Dictionary<FormKey, IReadOnlyList<FormKey>> { [list] = Array.Empty<FormKey>() }).IsId(On(), list));
    }

    [Fact]
    public void An_actor_value_comes_from_the_stored_values_only()
    {
        var values = new Dictionary<FormKey, double?> { [Key(6)] = 5, [Key(7)] = null };
        var state = Subjects(With(new ActorFacts(Key(1), null, null, null, null, null, null, values)));
        Assert.Equal(5, state.ActorValue(On(), Key(6)));
        Assert.Null(state.ActorValue(On(), Key(7)));
        Assert.Null(state.ActorValue(On(), Key(8)));
    }

    [Fact]
    public void A_runtime_global_wins_over_the_primed_record_value_and_an_unset_overlay_falls_back_to_it()
    {
        var store = new LiveStateStore();
        var reads = 0;
        store.PrimeGlobals(new[] { Key(5), Key(6) }, key => { reads++; return key == Key(5) ? 0.5 : null; });
        var state = Subjects(store);
        Assert.Equal(2, reads);
        Assert.Equal(0.5, state.GlobalValue(Key(5)));
        Assert.Null(state.GlobalValue(Key(6)));
        Assert.Null(state.GlobalValue(Key(8)));
        store.GlobalOverlay = key => key == Key(5) ? 3 : null;
        Assert.Equal(3, state.GlobalValue(Key(5)));
        Assert.Null(state.GlobalValue(Key(6)));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void Quest_facts_follow_the_bound_quest_source_and_stay_unknown_without_one()
    {
        var store = new LiveStateStore();
        var state = Subjects(store);
        var quests = new Quests(quest => quest == Key(6) ? QuestState.Running : QuestState.Completed, quest => quest == Key(6) ? 30 : 100, (quest, stage) => stage <= 30);
        Assert.Null(state.QuestRunning(Key(6)));
        store.Quests = quests;
        Assert.Equal(30, state.QuestStage(Key(6)));
        Assert.True(state.QuestStageDone(Key(6), 20));
        Assert.False(state.QuestStageDone(Key(6), 40));
        Assert.True(state.QuestRunning(Key(6)));
        Assert.False(state.QuestCompleted(Key(6)));
        Assert.False(state.QuestRunning(Key(7)));
        Assert.True(state.QuestCompleted(Key(7)));
        store.Quests = null;
        Assert.Null(state.QuestStage(Key(6)));
    }

    [Fact]
    public void A_known_only_state_answers_unknown_for_a_quest_the_source_does_not_know()
    {
        var store = new LiveStateStore { Quests = new Quests(_ => QuestState.NotStarted, _ => 0, (_, _) => false) { Known = quest => quest == Key(6) } };
        var state = new StoreLiveState(store, _ => null, knownQuestsOnly: true);
        Assert.Equal(0, state.QuestStage(Key(6)));
        Assert.False(state.QuestStageDone(Key(6), 10));
        Assert.False(state.QuestRunning(Key(6)));
        Assert.False(state.QuestCompleted(Key(6)));
        Assert.Null(state.QuestStage(Key(7)));
        Assert.Null(state.QuestStageDone(Key(7), 10));
        Assert.Null(state.QuestRunning(Key(7)));
        Assert.Null(state.QuestCompleted(Key(7)));
    }

    [Fact]
    public void Without_the_known_only_flag_an_unknown_quest_still_answers_the_source_default()
    {
        var store = new LiveStateStore { Quests = new Quests(_ => QuestState.NotStarted, _ => 0, (_, _) => false) { Known = _ => false } };
        var state = Subjects(store);
        Assert.Equal(0, state.QuestStage(Key(7)));
        Assert.False(state.QuestStageDone(Key(7), 10));
        Assert.False(state.QuestRunning(Key(7)));
    }

    [Fact]
    public void ClearQuestsIf_clears_only_the_binding_the_caller_owns()
    {
        var mine = new Quests(_ => QuestState.Running, _ => 1, (_, _) => true);
        var newer = new Quests(_ => QuestState.Running, _ => 2, (_, _) => true);
        var store = new LiveStateStore { Quests = newer };

        store.ClearQuestsIf(mine);
        Assert.Same(newer, store.Quests);
        store.ClearQuestsIf(null);
        Assert.Same(newer, store.Quests);
        store.ClearQuestsIf(newer);
        Assert.Null(store.Quests);
    }

    [Fact]
    public void Reset_drops_actors_primed_globals_and_the_hour_but_keeps_the_bound_sources()
    {
        var store = With(new ActorFacts(Key(1), null, null, null, 3, false));
        store.HourOfDay = 9;
        store.PrimeGlobals(new[] { Key(5) }, _ => 0.5);
        var quests = new Quests(_ => QuestState.Running, _ => 10, (_, _) => true);
        store.Quests = quests;
        store.GlobalOverlay = key => key == Key(7) ? 1 : null;

        store.Reset();

        var state = Subjects(store);
        Assert.Null(state.Level(On()));
        Assert.Null(state.CurrentTime());
        Assert.Null(state.GlobalValue(Key(5)));
        Assert.Equal(1, state.GlobalValue(Key(7)));
        Assert.True(state.QuestRunning(Key(1)));
    }

    [Fact]
    public void A_query_never_reads_a_source_it_was_not_asked_about()
    {
        var quests = new Quests(_ => QuestState.Running, _ => 10, (_, _) => true);
        var store = With(new ActorFacts(Key(1), null, null, null, 3, false));
        store.Quests = quests;
        var state = Subjects(store);
        state.Level(On());
        state.IsDead(On());
        state.GlobalValue(Key(5));
        Assert.Equal(0, quests.Reads);
        state.QuestStage(Key(1));
        Assert.Equal(1, quests.Reads);
    }

    [Fact]
    public void The_trace_is_silent_until_a_sink_is_set_and_then_names_each_answer()
    {
        var store = With(new ActorFacts(Key(1), null, null, null, 3, null));
        var state = Subjects(store);
        Assert.False(store.Trace.Enabled);
        Assert.Equal(3, state.Level(On()));

        var lines = new List<(string, string)>();
        store.Trace.Sink = (category, message) => lines.Add((category, message));
        Assert.Equal(3, state.Level(On()));
        Assert.Null(state.IsDead(On()));
        Assert.Null(state.GlobalValue(Key(5)));

        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.Equal("Condition", line.Item1));
        Assert.Contains("Level", lines[0].Item2);
        Assert.EndsWith("-> 3", lines[0].Item2);
        Assert.Contains("IsDead", lines[1].Item2);
        Assert.EndsWith("-> unknown", lines[1].Item2);
        Assert.Contains("GlobalValue", lines[2].Item2);
    }

    [Fact]
    public void A_throwing_sink_does_not_change_the_answer_and_its_exception_is_kept()
    {
        var store = With(new ActorFacts(Key(1), null, null, null, 3, null));
        store.Trace.Sink = (_, _) => throw new InvalidOperationException("sink");
        Assert.Equal(3, Subjects(store).Level(On()));
        Assert.Equal("sink", store.Trace.SinkFailure!.Message);
    }

    sealed class Aliases(Func<FormKey, int, FormKey, FormKey?, bool?> answer) : IAliasState
    {
        public readonly List<(FormKey Quest, int Alias, FormKey Reference, FormKey? Base)> Asked = new();

        public bool? IsAliasRef(FormKey quest, int alias, FormKey reference, FormKey? baseForm)
        {
            Asked.Add((quest, alias, reference, baseForm));
            return answer(quest, alias, reference, baseForm);
        }
    }

    [Fact]
    public void Sex_comes_from_the_actor_facts_and_a_missing_sex_or_actor_is_unknown()
    {
        var state = Subjects(With(new ActorFacts(Key(1), null, null, null, null, null, Sex: 1)));
        Assert.Equal(1, state.SexOf(On()));
        Assert.Null(state.SexOf(On(ConditionRunOn.Target)));
        Assert.Null(Subjects(With(new ActorFacts(Key(1), null, null, null, null, null))).SexOf(On()));
    }

    [Fact]
    public void Item_count_asks_the_actors_inventory_and_an_actor_without_one_is_unknown()
    {
        var state = Subjects(With(new ActorFacts(Key(1), null, null, null, null, null, Items: item => item == Key(9) ? 3 : 0)));
        Assert.Equal(3, state.ItemCount(On(), Key(9)));
        Assert.Equal(0, state.ItemCount(On(), Key(8)));
        Assert.Null(state.ItemCount(On(ConditionRunOn.Target), Key(9)));
        Assert.Null(Subjects(With(new ActorFacts(Key(1), null, null, null, null, null))).ItemCount(On(), Key(9)));
    }

    [Fact]
    public void An_alias_ref_compares_the_quest_and_alias_with_the_subjects_reference_and_base()
    {
        var aliases = new Aliases((quest, alias, reference, _) => quest == Key(7) && alias == 3 && reference == Key(20));
        var store = With(new ActorFacts(Key(1), null, null, null, null, null, Ref: Key(20)));
        store.Aliases = aliases;
        var state = Subjects(store);

        Assert.True(state.IsAliasRef(On(), Key(7), 3));
        Assert.False(state.IsAliasRef(On(), Key(7), 4));
        Assert.Equal((Key(7), 3, Key(20), (FormKey?)Key(1)), aliases.Asked[0]);
    }

    [Fact]
    public void An_alias_ref_with_no_quest_reference_actor_or_alias_source_is_unknown_and_never_false()
    {
        var aliases = new Aliases((_, _, _, _) => true);
        var store = With(new ActorFacts(Key(1), null, null, null, null, null, Ref: Key(20)));
        store.Aliases = aliases;
        var state = Subjects(store);
        Assert.Null(state.IsAliasRef(On(), null, 3));
        Assert.Null(state.IsAliasRef(On(ConditionRunOn.Target), Key(7), 3));
        Assert.Empty(aliases.Asked);

        var noRef = With(new ActorFacts(Key(1), null, null, null, null, null));
        noRef.Aliases = aliases;
        Assert.Null(Subjects(noRef).IsAliasRef(On(), Key(7), 3));
        Assert.Null(Subjects(With(new ActorFacts(Key(1), null, null, null, null, null, Ref: Key(20)))).IsAliasRef(On(), Key(7), 3));
        Assert.Empty(aliases.Asked);
    }

    [Fact]
    public void An_alias_ref_for_a_row_that_names_no_quest_uses_the_quest_the_host_is_evaluating_for()
    {
        var aliases = new Aliases((quest, _, _, _) => quest == Key(7));
        var store = With(new ActorFacts(Key(1), null, null, null, null, null, Ref: Key(20)));
        store.Aliases = aliases;
        var state = new StoreLiveState(store, _ => "self", contextQuest: Key(7));
        Assert.True(state.IsAliasRef(On(), null, 3));
        Assert.False(state.IsAliasRef(On(), Key(8), 3));
        Assert.Null(new StoreLiveState(store, _ => "self").IsAliasRef(On(), null, 3));
    }
}
