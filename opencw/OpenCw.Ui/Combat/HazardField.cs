using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Combat;

public sealed record HazardVictim(Vector3 Position, bool IsPlayer, IEffectTarget Target, Func<string, IEffectTarget>? Sourced = null);

public readonly record struct HazardRow(string Token, string Base, Vector3 Position, double Age, bool Enabled, string Source = "");

public sealed record HazardSpawn(int? Id, IReadOnlyList<string> Skipped)
{
    public string? PartialNotice(string baseKey) => Id is not null && Skipped.Count > 0 ? $"{baseKey} spawned without: {string.Join("; ", Skipped)}" : null;
}

// Requires: main thread only; positions are metres and the caller passes the FO4 units per metre; the radius unit is the one named constant below, UNVERIFIED (docs/code-notes/opencw-interact-traps.md).
public sealed class HazardField(IMagicEffectRecords records, double unitsPerMeter)
{
    public const double RadiusUnitFo4Units = 1.0;
    public const uint AffectsPlayerOnly = 0x01;
    public const int Capacity = 64;
    public const double MaxStepSeconds = 0.25;

    private sealed record Live(HazardInfo Info, Vector3 Position, HazardPlan PerTick, bool Enabled, string? Source);

    private sealed class Group
    {
        public HazardSchedule Schedule { get; } = new();
        public Dictionary<int, Live> Hazards { get; } = [];
    }

    private readonly Dictionary<string, Group> _groups = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly EffectApplier _applier = new();

    public int Count => _groups.Values.Sum(group => group.Hazards.Count);

    public double RadiusMeters(HazardInfo info) => info.Radius * RadiusUnitFo4Units / unitsPerMeter;

    // Invariant: effect eligibility is judged once here, so Advance never reads a record; a hazard with no applicable effect is not spawned.
    public HazardSpawn Spawn(string baseKey, HazardInfo info, Vector3 position, IReadOnlyList<ConsumableEffect> effects, bool enabled = true, double age = 0, string? source = null)
    {
        if (!float.IsFinite(info.Radius) || info.Radius < 0f) return new HazardSpawn(null, ["the radius is negative or not a number"]);
        var perTick = HazardDamage.Plan(info, 1, effects, records);
        _groups.TryGetValue(baseKey, out var existing);
        var replaces = info.Limit > 0 && existing is not null && existing.Schedule.Count >= info.Limit;
        if (perTick.Specs.Count == 0 || (Count >= Capacity && !replaces)) return new HazardSpawn(null, perTick.Skipped);
        if (existing is null) _groups[baseKey] = existing = new Group();
        var group = existing;
        if (group.Schedule.Add(info, age) is not { } id) return new HazardSpawn(null, perTick.Skipped);
        group.Hazards[id] = new Live(info, position, perTick, enabled, source);
        Prune(group);
        return new HazardSpawn(id, perTick.Skipped);
    }

    // Invariant: rows are in spawn order per base record, so a restore through Spawn keeps each record's Limit eviction order; the token is filled in by the host.
    public IReadOnlyList<(string Base, int Id, HazardRow Row)> Snapshot() =>
        _groups.SelectMany(entry => entry.Value.Hazards.OrderBy(hazard => hazard.Key).Select(hazard =>
            (entry.Key, hazard.Key, new HazardRow("", entry.Key, hazard.Value.Position, entry.Value.Schedule.AgeOf(hazard.Key), hazard.Value.Enabled, hazard.Value.Source ?? "")))).ToList();

    public bool Contains(string baseKey, int id) => _groups.TryGetValue(baseKey, out var group) && group.Hazards.ContainsKey(id);

    public bool SetEnabled(string baseKey, int id, bool enabled)
    {
        if (!_groups.TryGetValue(baseKey, out var group) || !group.Hazards.TryGetValue(id, out var live)) return false;
        group.Hazards[id] = live with { Enabled = enabled };
        return true;
    }

    // Invariant: victimsOf is called at most once per Advance and only when a tick is due; ticks apply before expiry is pruned, because the schedule drops an ended hazard in the same Advance that reports its last ticks.
    public int Advance(double seconds, Func<IReadOnlyList<HazardVictim>> victimsOf)
    {
        seconds = Math.Min(seconds, MaxStepSeconds);
        var accepted = 0;
        IReadOnlyList<HazardVictim>? victims = null;
        foreach (var group in _groups.Values.ToList())
        {
            foreach (var tick in group.Schedule.Advance(seconds))
            {
                if (!group.Hazards.TryGetValue(tick.Id, out var live) || !live.Enabled) continue;
                var plan = new HazardPlan(live.PerTick.Specs.Select(spec => spec with { Amount = spec.Amount * tick.Count }).ToList(), live.PerTick.Skipped);
                var reach = RadiusMeters(live.Info);
                victims ??= victimsOf();
                foreach (var victim in victims)
                {
                    if ((live.Info.Flags & AffectsPlayerOnly) != 0 && !victim.IsPlayer) continue;
                    if (Vector3.DistanceSquared(victim.Position, live.Position) > reach * reach) continue;
                    accepted += HazardDamage.Apply(_applier, live.Source is { } source && victim.Sourced is { } sourced ? sourced(source) : victim.Target, plan);
                }
            }
            Prune(group);
        }
        foreach (var key in _groups.Where(entry => entry.Value.Hazards.Count == 0).Select(entry => entry.Key).ToList()) _groups.Remove(key);
        return accepted;
    }

    public void Clear() => _groups.Clear();

    private static void Prune(Group group)
    {
        foreach (var id in group.Hazards.Keys.Where(id => !group.Schedule.Contains(id)).ToList()) group.Hazards.Remove(id);
    }
}
