using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.LiveState;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Dialogue;

// Requires: Global, NpcValue, NpcSex and PlayerSex read the record source and are called only before the condition call; Player is called at most once and only when a row asks the player's facts.
public sealed record DialogueSource(ActorFacts Npc, double HourOfDay, Func<ActorFacts> Player, FormKey? PlayerRef,
    Func<FormKey, double?> Global, Func<FormKey, double?>? NpcValue = null, Func<int?>? NpcSex = null, FormKey? NpcRef = null, FormKey? Quest = null, Func<int?>? PlayerSex = null);

public static class DialogueConditionValues
{
    private static readonly ConditionInput Unknown = new(false, 0, false, 0, 0, 0);

    internal static string NpcKey(ActorFacts npc) => npc.Base?.ToString() ?? "npc";

    internal static IConditionLiveState StateFor(LiveStateStore store, bool playerSpoken, FormKey? playerRef, string npcKey,
        IReadOnlyDictionary<FormKey, IReadOnlyList<FormKey>>? lists = null, FormKey? quest = null) =>
        new StoreLiveState(store, who => DialogueRules.TargetOf(who.RunOn, who.Reference is { } reference && playerRef == reference, playerSpoken) switch
        {
            ConditionTarget.Npc => npcKey,
            ConditionTarget.Player => LiveStateStore.PlayerKey,
            _ => null,
        }, lists, contextQuest: quest);

    // Invariant: a sex that cannot be read is unknown; the read happens before the condition call and a Radicle failure there must not escape.
    private static int? SexOf(Func<int?>? read)
    {
        try { return read?.Invoke(); }
        catch (RadicleException) { return null; }
    }

    // Requires: each row is read with the source record it came from; a row without Source and Row is unknown.
    public static IReadOnlyList<ConditionInput> Values(RadicleEnvironment env, LiveStateStore store, IReadOnlyList<InfoCondition> conditions, bool playerSpoken,
        DialogueSource source, Action<Radicle.Interop.Record, RadicleException>? fault = null) =>
        ValuesFrom((record, rows, state) => env.ConditionInputs(record, rows, state), store, conditions, playerSpoken, source, fault);

    internal static IReadOnlyList<ConditionInput> ValuesFrom(Func<Radicle.Interop.Record, IReadOnlyList<RadicleCondition>, IConditionLiveState, IReadOnlyList<ConditionInput>> read,
        LiveStateStore store, IReadOnlyList<InfoCondition> conditions, bool playerSpoken, DialogueSource source,
        Action<Radicle.Interop.Record, RadicleException>? fault = null)
    {
        var values = new ConditionInput[conditions.Count];
        Array.Fill(values, Unknown);
        var targets = conditions.Select(c => DialogueRules.TargetOf(c.Rule.RunOn, c.ReferenceIsPlayer, playerSpoken)).ToArray();
        bool Facts(int i) => !ConditionFunctions.IsQuestFunction(conditions[i].Rule.Function);
        bool AsksSex(ConditionTarget target) => Enumerable.Range(0, values.Length).Any(i => conditions[i].Rule.Function == ConditionFunctions.GetIsSex && targets[i] == target);
        if (Enumerable.Range(0, values.Length).Any(i => Facts(i) && targets[i] == ConditionTarget.Player))
        {
            var player = source.Player();
            store.PutActor(LiveStateStore.PlayerKey, player with { Ref = player.Ref ?? source.PlayerRef, Sex = AsksSex(ConditionTarget.Player) ? SexOf(source.PlayerSex) ?? player.Sex : player.Sex });
        }
        var npcSex = AsksSex(ConditionTarget.Npc) ? SexOf(source.NpcSex) : null;
        var globals = new HashSet<FormKey>();
        var actorValues = new Dictionary<FormKey, double?>();
        for (int i = 0; i < values.Length; i++)
        {
            var c = conditions[i];
            if (c.Rule.Function == ConditionFunctions.GetGlobalValue && c.Param is { } glob) globals.Add(glob);
            if ((c.Rule.FlagsByte & ConditionFunctions.FlagUseGlobal) != 0 && c.ComparisonGlobal is { } comparison) globals.Add(comparison);
            if (c.Rule.Function == ConditionFunctions.GetValue && c.Param is { } actorValue && Facts(i) && targets[i] == ConditionTarget.Npc
                && source.NpcValue is { } valueOf && !actorValues.ContainsKey(actorValue))
                actorValues[actorValue] = valueOf(actorValue);
        }
        store.HourOfDay = source.HourOfDay;
        store.PrimeGlobals(globals, source.Global);
        var npcKey = NpcKey(source.Npc);
        store.PutActor(npcKey, source.Npc with { Values = actorValues.Count == 0 ? source.Npc.Values : actorValues, Sex = npcSex ?? source.Npc.Sex, Ref = source.NpcRef ?? source.Npc.Ref });
        var lists = conditions.Where(c => c.Param is not null && c.Members is not null).DistinctBy(c => c.Param!.Value).ToDictionary(c => c.Param!.Value, c => c.Members!);
        if (conditions.Any(c => c.Rule.Function == ConditionFunctions.GetIsAliasRef)) store.Aliases?.Prime();
        var state = StateFor(store, playerSpoken, source.PlayerRef, npcKey, lists, source.Quest);
        foreach (var group in Enumerable.Range(0, values.Length).Where(i => conditions[i].Source is not null && conditions[i].Row is not null).GroupBy(i => conditions[i].Source!.Value))
        {
            var indexes = group.ToList();
            try
            {
                var inputs = read(group.Key, indexes.Select(i => conditions[i].Row!.Value).ToList(), state);
                for (int k = 0; k < indexes.Count; k++) values[indexes[k]] = inputs[k];
            }
            catch (RadicleException e)
            {
                fault?.Invoke(group.Key, e);
            }
        }
        for (int i = 0; i < values.Length; i++)
        {
            if (!ConditionFunctions.IsQuestFunction(conditions[i].Rule.Function) && targets[i] == ConditionTarget.Unknown) values[i] = Unknown;
        }
        return values;
    }

    // Invariant: an info with no conditions is eligible; any condition that cannot be decided makes the whole info not eligible and reports the first such function.
    public static (bool Eligible, ushort? Unevaluable) Verdict(RadicleEnvironment env, LiveStateStore store, IReadOnlyList<InfoCondition> conditions, bool playerSpoken,
        DialogueSource source, Action<Radicle.Interop.Record, RadicleException>? fault = null)
    {
        if (conditions.Count == 0)
            return (true, null);
        var outcome = PackageRules.EvaluateConditions(conditions.Select(c => c.Rule).ToArray(), Values(env, store, conditions, playerSpoken, source, fault).ToArray());
        return (outcome.Verdict == RuleVerdict.True, outcome.Verdict == RuleVerdict.Unevaluable ? (ushort?)outcome.FirstUnevaluableFunction : null);
    }
}
