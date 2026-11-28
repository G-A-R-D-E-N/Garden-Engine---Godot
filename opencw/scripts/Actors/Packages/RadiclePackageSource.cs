using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Lighting;
using OpenCommonwealth.Services.Papyrus.LiveState;
using Radicle.Interop;

namespace OpenCommonwealth;

public sealed class RadiclePackageSource : IPackageSource
{
    public static readonly RadiclePackageSource Instance = new();

    private sealed class Entry(FormKey key, Record record, Package data, PackageCandidateInput input, string? leaf, string procedure, RadicleCondition[] rows, FormKey[] globals)
    {
        public FormKey Key { get; } = key;
        public Record Record { get; } = record;
        public Package Data { get; } = data;
        public PackageCandidateInput Input { get; } = input;
        public string? Leaf { get; } = leaf;
        public string Procedure { get; } = procedure;
        public RadicleCondition[] Rows { get; } = rows;
        public FormKey[] Globals { get; } = globals;
        public ActivePackage? Resolved { get; set; }
    }

    // constraint: these static caches are main thread only
    private static readonly Dictionary<FormKey, Entry?> Entries = new();
    private static readonly Dictionary<FormKey, List<Entry>> Lists = new();
    private static readonly HashSet<string> Noted = new();
    private static readonly LoadSession.EnvStamp EnvStamp = new();

    public ActivePackage? Active(ActorIdentity actor, GameClock clock)
    {
        if (!GameLoader.HasLoadOrder) return null;
        RadicleEnvironment env;
        try { env = GameLoader.Records.Environment; }
        catch (InvalidOperationException e) when (e is not RadicleException) { return null; }
        if (!GameLoader.TryParseKey(actor.Node.GetMeta(NpcPuppetSpawner.ResolvedMeta, actor.BaseKey).AsString(), out var npc)) return null;
        return LoadSession.TryRead(env, () => Select(env, actor, npc, clock), out var active) ? active : null;
    }

    // Requires: a package forced by a scene bypasses the actor's package list and conditions; null when the record is not a readable PACK.
    public ActivePackage? Forced(string package)
    {
        if (!GameLoader.HasLoadOrder || !GameLoader.TryParseKey(package, out var key)) return null;
        RadicleEnvironment env;
        try { env = GameLoader.Records.Environment; }
        catch (InvalidOperationException e) when (e is not RadicleException) { return null; }
        return LoadSession.TryRead(env, () =>
        {
            Refresh(env);
            if (!Entries.TryGetValue(key, out var entry)) Entries[key] = entry = Load(env, key);
            return entry is null ? null : entry.Resolved ??= Resolve(entry);
        }, out var active) ? active : null;
    }

    private static void Refresh(RadicleEnvironment env)
    {
        if (!EnvStamp.Changed(env)) return;
        Entries.Clear();
        Lists.Clear();
        Noted.Clear();
    }

    private static ActivePackage? Select(RadicleEnvironment env, ActorIdentity actor, FormKey npc, GameClock clock)
    {
        Refresh(env);
        var list = CandidatesOf(env, npc);
        if (list.Count == 0)
        {
            Note($"{actor.RefKey}|empty", () => $"{actor.RefKey}: no package candidates for {npc} (empty or unreadable AI package list); nothing selected.");
            return null;
        }
        Prime(actor, npc, clock);
        var inputs = list.Select(e => WithValues(env, e, actor.RefKey)).ToArray();
        var choice = PackageRules.SelectPackage(inputs, PackageRecordMap.ClockOf(clock));
        var outcomes = string.Join(", ", Enumerable.Range(0, list.Count).Select(i => $"{list[i].Key}={choice.Reasons[i]}"));
        Note($"{actor.RefKey}|outcomes|{choice.State}|{outcomes}", () => $"{actor.RefKey}: condition outcome {choice.State}: {outcomes}.");
        if (choice.State == SelectionState.Undecided)
        {
            NoteUndecided(actor, list[choice.BlockingIndex], inputs[choice.BlockingIndex], choice.Reasons[choice.BlockingIndex]);
            return null;
        }
        if (choice.State != SelectionState.Selected)
        {
            Note($"{actor.RefKey}|state|{choice.State}", () => $"{actor.RefKey}: selection state {choice.State} among {list.Count} candidate(s); nothing selected.");
            return null;
        }
        var entry = list[choice.SelectedIndex];
        return entry.Resolved ??= Resolve(entry);
    }

    private static PackageCandidateInput WithValues(RadicleEnvironment env, Entry entry, string actorKey)
    {
        if (entry.Rows.Length == 0) return entry.Input;
        try
        {
            var store = EngineLiveState.Store;
            store.PrimeGlobals(entry.Globals, GlobalOf);
            var state = new StoreLiveState(store, who => who.RunOn == (uint)ConditionRunOn.Subject ? actorKey : null, knownQuestsOnly: true);
            return entry.Input with { Values = env.ConditionInputs(entry.Record, entry.Rows, state) };
        }
        catch (RadicleException ex)
        {
            if (Noted.Add($"{entry.Key}|values")) GD.PrintErr($"[Package] {entry.Key}: condition values unavailable ({ex.GetType().Name}: {ex.Message}).");
            return entry.Input;
        }
    }

    private static void Prime(ActorIdentity actor, FormKey npc, GameClock clock)
    {
        var facts = FactsOf(npc);
        var state = GodotObject.IsInstanceValid(actor.Node) ? ActorCombat.Of(actor.Node)?.State : null;
        var store = EngineLiveState.Store;
        store.HourOfDay = clock.HourOfDay;
        store.PutActor(actor.RefKey, new ActorFacts(npc, facts.Race, facts.Factions, facts.Keywords, state is { LevelUnresolved: null } ? state.Level : null, state?.IsDead));
    }

    private static RadicleNpcRecords Npcs => RadicleNpcRecords.For(GameLoader.Records);

    private static List<Entry> CandidatesOf(RadicleEnvironment env, FormKey npc)
    {
        if (Lists.TryGetValue(npc, out var cached)) return cached;
        var list = new List<Entry>();
        if (env.Winner(npc) is not null)
        {
            var keys = Npcs.PackageKeys(npc);
            if (keys == null) Note($"{npc}:package-template", () => Npcs.TemplateFlags(npc) == null
                ? $"{npc}: stub, the NPC_ template flags are unreadable; the list is undecided and no package is selected."
                : $"{npc}: stub, the package list comes from an NPC_ template (AI Packages or Def Pack List flag) that cannot be followed (no link, not an NPC_, a leveled list, or deeper than 8 hops); the list is undecided and no package is selected.");
            else foreach (var key in keys) Add(env, list, key);
        }
        Lists[npc] = list;
        return list;
    }

    private static void Add(RadicleEnvironment env, List<Entry> list, FormKey key)
    {
        if (!Entries.TryGetValue(key, out var entry)) Entries[key] = entry = Load(env, key);
        if (entry != null) list.Add(entry);
    }

    private static Entry? Load(RadicleEnvironment env, FormKey key)
    {
        try
        {
            if (env.Winner(key) is not { } record || env.Info(record).Signature != "PACK") return null;
            var package = env.Package(record);
            var rules = package.Conditions.Select(c => new ConditionRule(c.FlagsByte, c.ComparisonRaw, c.Function, c.RunOn, c.Reference)).ToArray();
            var values = rules.Select(_ => PackageConditionValues.Unknown).ToArray();
            var rows = package.Conditions.Select(c => ConditionFunctions.ToRadicle(c)).ToArray();
            var typed = rows.Length == 0 ? Array.Empty<RadicleTypedCondition>() : env.ConditionParameters(record, rows);
            var globals = Enumerable.Range(0, rows.Length).SelectMany(i => GlobalsOf(rows[i], typed[i])).Distinct().ToArray();
            return new Entry(key, record, package, new PackageCandidateInput(package.Schedule ?? PackageRules.NoSchedule, rules, values), LeafName(env, record, package), ProcedureText(env, record, package), rows, globals);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Package] {key}: skipped, the PACK did not decode ({ex.GetType().Name}: {ex.Message}).");
            return null;
        }
    }

    private static IEnumerable<FormKey> GlobalsOf(RadicleCondition row, RadicleTypedCondition typed)
    {
        if (row.Function == ConditionFunctions.GetGlobalValue && ConditionFunctions.FormOf(typed.Param1) is { } glob) yield return glob;
        if ((row.FlagsByte & ConditionFunctions.FlagUseGlobal) != 0 && ConditionFunctions.FormOf(typed.Comparison) is { } comparison) yield return comparison;
    }

    private static string? LeafName(RadicleEnvironment env, Record record, Package package)
    {
        if (PackageRecordMap.LeafNameOf(env.PackageProcedure(record)) is { } own) return own;
        return package.Counter?.TemplatePackage is { } template && env.Winner(template) is { } templateRecord
            ? PackageRecordMap.LeafNameOf(env.PackageProcedure(templateRecord))
            : null;
    }

    private static string ProcedureText(RadicleEnvironment env, Record record, Package package)
    {
        if (PackageRecordMap.ProcedureOf(env.PackageProcedure(record)) is { Length: > 0 } own) return own;
        return package.Counter?.TemplatePackage is { } template && env.Winner(template) is { } templateRecord
            ? PackageRecordMap.ProcedureOf(env.PackageProcedure(templateRecord))
            : "";
    }

    private static ActivePackage Resolve(Entry entry)
    {
        var (anchor, radius) = PackageRecordMap.LocationOf(entry.Data);
        if (anchor == null && entry.Data.Locations.Count > 0 && !PackageRecordMap.IsOwnPosition(entry.Data.Locations[0]))
            Note($"{entry.Key}:location", () => $"stub, location type {RadicleEnvironment.PackageLocationTypeName(entry.Data.Locations[0].Type) ?? entry.Data.Locations[0].Type.ToString()} of {entry.Key} is not resolved; the actor's own position stands in.");
        return new ActivePackage(entry.Key.ToString(), PackageRecordMap.KindOf(entry.Leaf), anchor, radius, entry.Procedure);
    }

    private static NpcFacts FactsOf(FormKey npc)
    {
        Note("facts:base-only", () => "stub, GetInFaction, GetFactionRank and HasKeyword answer from the base records only; factions and keywords added at runtime, perk keywords and equipped-item keywords are not seen.");
        try { return Npcs.FactsOf(npc); }
        catch (Exception ex)
        {
            GD.PrintErr($"[Package] {npc}: NPC_ condition facts unavailable ({ex.GetType().Name}: {ex.Message}).");
            return NpcFacts.Unknown;
        }
    }

    private static double? GlobalOf(FormKey key)
    {
        try { return Npcs.GlobalValue(key); }
        catch (Exception ex)
        {
            GD.PrintErr($"[Package] {key}: GLOB value unavailable ({ex.GetType().Name}: {ex.Message}).");
            return null;
        }
    }

    private static void NoteUndecided(ActorIdentity actor, Entry entry, PackageCandidateInput input, CandidateOutcome reason) =>
        Note($"{actor.RefKey}|{entry.Key}", () => reason == CandidateOutcome.ConditionUnevaluable
            ? $"{actor.RefKey}: stub, package {entry.Key} cannot be decided, condition function {PackageRules.EvaluateConditions(input.Conditions, input.Values).FirstUnevaluableFunction} has no value for this actor; no package selected."
            : $"{actor.RefKey}: stub, package {entry.Key} cannot be decided, its schedule is not evaluable; no package selected.");

    private static void Note(string once, Func<string> message)
    {
        if (Noted.Add(once)) GD.Print($"[Package] {message()}");
    }
}
