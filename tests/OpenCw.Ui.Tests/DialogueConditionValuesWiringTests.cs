using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth;
using OpenCommonwealth.Services.Dialogue;
using OpenCommonwealth.Services.Papyrus.LiveState;
using Radicle.Interop;
using ConditionFunctions = OpenCommonwealth.ConditionFunctions;
using Record = Radicle.Interop.Record;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class DialogueConditionValuesWiringTests
{
    static FormKey Key(uint id) => new("Fallout4.esm", id);

    static readonly FormKey Preston = Key(0x19FD9);
    static readonly FormKey Player = Key(0x7);
    static readonly Record SourceA = new(new IntPtr(1), null!);
    static readonly Record SourceB = new(new IntPtr(2), null!);

    delegate IReadOnlyList<ConditionInput> Reader(Record record, IReadOnlyList<RadicleCondition> rows, IConditionLiveState state);

    static ConditionInput Known(double value) => new(true, value, false, 0, 0, 0);

    static RadicleCondition Row(ushort function) => new(0, 1065353216, function, 0, 0, 0, 0, 0, 0, null, null);

    static InfoCondition Cond(ushort function, ConditionRunOn runOn, Record? source, FormKey? param = null, int rawParam1 = 0) =>
        new(new ConditionRule(0, 1065353216, function, (uint)runOn, 0), param, null, 0, false, null, rawParam1, source, source is null ? null : Row(function));

    static LiveSubject On(ConditionRunOn runOn) => new((uint)runOn, null, 0, 0);

    static ActorFacts Npc() => new(Preston, null, new Dictionary<FormKey, int>(), null, null, false);

    static ActorFacts Pc(double? level = null) => new(Player, null, new Dictionary<FormKey, int>(), null, level, false);

    static DialogueSource Source(Func<FormKey, double?>? global = null, Func<FormKey, double?>? actorValue = null, Func<ActorFacts>? player = null) =>
        new(Npc(), 12, player ?? (() => Pc()), null, global ?? (_ => null), actorValue);

    static IReadOnlyList<ConditionInput> Run(Reader read, IReadOnlyList<InfoCondition> conditions, DialogueSource? source = null,
        Action<Record, RadicleException>? fault = null, LiveStateStore? store = null) =>
        DialogueConditionValues.ValuesFrom((r, rows, state) => read(r, rows, state), store ?? new LiveStateStore(), conditions, false, source ?? Source(), fault);

    [Fact]
    public void Rows_are_read_once_per_source_record_in_list_order_and_a_row_without_a_source_stays_unknown()
    {
        var calls = new List<(Record Record, int Rows)>();
        var conditions = new[]
        {
            Cond(ConditionFunctions.GetLevel, ConditionRunOn.Subject, SourceA),
            Cond(ConditionFunctions.GetLevel, ConditionRunOn.Subject, SourceB),
            Cond(ConditionFunctions.GetLevel, ConditionRunOn.Subject, SourceA),
            Cond(ConditionFunctions.GetLevel, ConditionRunOn.Subject, null),
        };

        var values = Run((record, rows, state) =>
        {
            calls.Add((record, rows.Count));
            return rows.Select((row, i) => Known((record == SourceA ? 100 : 200) + i)).ToList();
        }, conditions);

        Assert.Equal(new[] { (SourceA, 2), (SourceB, 1) }, calls);
        Assert.Equal(100, values[0].Value);
        Assert.Equal(200, values[1].Value);
        Assert.Equal(101, values[2].Value);
        Assert.False(values[3].Known);
    }

    [Fact]
    public void The_player_subject_globals_and_actor_values_are_read_once_before_the_environment_is_called()
    {
        var glob = Key(0x40);
        var actorValue = Key(0x50);
        int playerCalls = 0, globalCalls = 0, valueCalls = 0;
        var seen = new List<(double?, double?, double?)>();
        var conditions = new[]
        {
            Cond(ConditionFunctions.GetGlobalValue, ConditionRunOn.Subject, SourceA, glob),
            Cond(ConditionFunctions.GetLevel, ConditionRunOn.Target, SourceA),
            Cond(ConditionFunctions.GetValue, ConditionRunOn.Subject, SourceA, actorValue),
        };

        Run((record, rows, state) =>
        {
            Assert.Equal((1, 1, 1), (playerCalls, globalCalls, valueCalls));
            seen.Add((state.GlobalValue(glob), state.Level(On(ConditionRunOn.Target)), state.ActorValue(On(ConditionRunOn.Subject), actorValue)));
            seen.Add((state.GlobalValue(glob), state.Level(On(ConditionRunOn.Target)), state.ActorValue(On(ConditionRunOn.Subject), actorValue)));
            return rows.Select(row => Known(0)).ToList();
        }, conditions,
            Source(global: _ => { globalCalls++; return 2.5; }, actorValue: _ => { valueCalls++; return 9; }, player: () => { playerCalls++; return Pc(level: 3); }));

        Assert.Equal(new (double?, double?, double?)[] { (2.5, 3, 9), (2.5, 3, 9) }, seen);
        Assert.Equal((1, 1, 1), (playerCalls, globalCalls, valueCalls));
    }

    [Fact]
    public void The_player_is_not_read_when_no_row_needs_the_player_facts()
    {
        var playerCalls = 0;

        Run((record, rows, state) => rows.Select(row => Known(1)).ToList(), [Cond(ConditionFunctions.GetLevel, ConditionRunOn.Subject, SourceA)],
            Source(player: () => { playerCalls++; return Pc(); }));

        Assert.Equal(0, playerCalls);
    }

    [Fact]
    public void A_fact_on_an_undecidable_run_on_is_reset_to_unknown_and_a_live_function_keeps_the_value_radicle_read()
    {
        var conditions = new[]
        {
            Cond(ConditionFunctions.GetLevel, ConditionRunOn.QuestAlias, SourceA),
            Cond(ConditionFunctions.GetIsSex, ConditionRunOn.Subject, SourceA, rawParam1: 1),
            Cond(ConditionFunctions.GetIsSex, ConditionRunOn.QuestAlias, SourceA, rawParam1: 1),
        };

        var values = Run((record, rows, state) => rows.Select(row => Known(5)).ToList(), conditions);

        Assert.False(values[0].Known);
        Assert.True(values[1].Known);
        Assert.Equal(5.0, values[1].Value);
        Assert.False(values[2].Known);
    }

    [Fact]
    public void The_live_facts_radicle_asks_are_put_in_the_store_before_the_call()
    {
        var npcRef = Key(0x2000);
        var quest = Key(0x400);
        var asked = new List<(int? Sex, int? Items, bool? Alias, int? PlayerSex, int? PlayerItems)>();
        var aliases = new RecordingAliases();
        var store = new LiveStateStore { Aliases = aliases };
        var conditions = new[]
        {
            Cond(ConditionFunctions.GetIsSex, ConditionRunOn.Subject, SourceA, rawParam1: 1),
            Cond(ConditionFunctions.GetItemCount, ConditionRunOn.Target, SourceA, Key(0xF)),
            Cond(ConditionFunctions.GetIsAliasRef, ConditionRunOn.Subject, SourceA, rawParam1: 2),
            Cond(ConditionFunctions.GetIsSex, ConditionRunOn.Target, SourceA, rawParam1: 0),
        };
        var source = new DialogueSource(Npc(), 12, () => Pc() with { Items = item => item == Key(0xF) ? 4 : 0 }, Key(0x14), _ => null,
            NpcSex: () => 1, NpcRef: npcRef, Quest: quest, PlayerSex: () => 0);

        Run((record, rows, state) =>
        {
            asked.Add((state.SexOf(On(ConditionRunOn.Subject)), state.ItemCount(On(ConditionRunOn.Subject), Key(0xF)),
                state.IsAliasRef(On(ConditionRunOn.Subject), null, 2), state.SexOf(On(ConditionRunOn.Target)), state.ItemCount(On(ConditionRunOn.Target), Key(0xF))));
            return rows.Select(row => Known(1)).ToList();
        }, conditions, source, store: store);

        Assert.Equal(((int?)1, (int?)null, (bool?)true, (int?)0, (int?)4), Assert.Single(asked));
        Assert.Equal((quest, 2, npcRef, (FormKey?)Preston), aliases.Asked);
    }

    [Fact]
    public void The_npc_sex_is_read_only_for_a_sex_row_on_the_npc()
    {
        var sexReads = 0;
        var source = new DialogueSource(Npc(), 12, () => Pc(), null, _ => null, NpcSex: () => { sexReads++; return 1; });

        Run((record, rows, state) => rows.Select(row => Known(1)).ToList(), [Cond(ConditionFunctions.GetLevel, ConditionRunOn.Subject, SourceA)], source);
        Run((record, rows, state) => rows.Select(row => Known(1)).ToList(), [Cond(ConditionFunctions.GetIsSex, ConditionRunOn.Target, SourceA, rawParam1: 1)], source);
        Assert.Equal(0, sexReads);

        Run((record, rows, state) => rows.Select(row => Known(1)).ToList(), [Cond(ConditionFunctions.GetIsSex, ConditionRunOn.Subject, SourceA, rawParam1: 1)], source);
        Assert.Equal(1, sexReads);
    }

    [Fact]
    public void The_alias_source_is_primed_before_the_call_and_only_for_an_alias_row()
    {
        var aliases = new RecordingAliases();
        var store = new LiveStateStore { Aliases = aliases };
        var primedAtCall = new List<int>();

        Run((record, rows, state) => { primedAtCall.Add(aliases.Primed); return rows.Select(row => Known(1)).ToList(); },
            [Cond(ConditionFunctions.GetLevel, ConditionRunOn.Subject, SourceA)], store: store);
        Run((record, rows, state) => { primedAtCall.Add(aliases.Primed); return rows.Select(row => Known(1)).ToList(); },
            [Cond(ConditionFunctions.GetIsAliasRef, ConditionRunOn.Subject, SourceA, rawParam1: 2)], store: store);

        Assert.Equal(new[] { 0, 1 }, primedAtCall.ToArray());
    }

    [Fact]
    public void A_sex_read_that_fails_leaves_the_sex_unknown_and_does_not_escape()
    {
        var source = new DialogueSource(Npc(), 12, () => Pc(), null, _ => null, NpcSex: () => throw new RadicleException(default, "sex"), PlayerSex: () => throw new RadicleException(default, "sex"));
        int? seen = 5;

        Run((record, rows, state) => { seen = state.SexOf(On(ConditionRunOn.Subject)); return rows.Select(row => Known(1)).ToList(); },
            [Cond(ConditionFunctions.GetIsSex, ConditionRunOn.Subject, SourceA, rawParam1: 1), Cond(ConditionFunctions.GetIsSex, ConditionRunOn.Target, SourceA, rawParam1: 1)], source);

        Assert.Null(seen);
    }

    sealed class RecordingAliases : IAliasState
    {
        public int Primed;

        public void Prime() => Primed++;

        public (FormKey Quest, int Alias, FormKey Reference, FormKey? Base)? Asked;

        public bool? IsAliasRef(FormKey quest, int alias, FormKey reference, FormKey? baseForm)
        {
            Asked = (quest, alias, reference, baseForm);
            return true;
        }
    }

    [Fact]
    public void A_failing_source_record_leaves_only_its_rows_unknown_and_is_reported_once()
    {
        var faults = new List<(Record, string)>();
        var conditions = new[]
        {
            Cond(ConditionFunctions.GetLevel, ConditionRunOn.Subject, SourceA),
            Cond(ConditionFunctions.GetLevel, ConditionRunOn.Subject, SourceB),
            Cond(ConditionFunctions.GetLevel, ConditionRunOn.Subject, SourceA),
        };

        var values = Run((record, rows, state) => record == SourceA
            ? throw new RadicleException(default, "bad record")
            : rows.Select(row => Known(4)).ToList(), conditions, fault: (record, error) => faults.Add((record, error.Message)));

        Assert.False(values[0].Known);
        Assert.True(values[1].Known);
        Assert.False(values[2].Known);
        Assert.Equal(new[] { (SourceA, "bad record") }, faults);
    }
}
