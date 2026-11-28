using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Combat;

public sealed record HazardVictim(Vector3 Position, bool IsPlayer, IEffectTarget Target);

public sealed record HazardSpawn(int? Id, IReadOnlyList<string> Skipped);

// Requires: main thread only; positions are metres and the caller passes the FO4 units per metre; the radius unit is the one named constant below, UNVERIFIED (CodeNotes:Garden Engine/history/docs/code-notes/opencw-interact-traps.md).
public sealed class HazardField
{
    public const double RadiusUnitFo4Units = 1.0;
    public const uint AffectsPlayerOnly = 0x01;
    public const int Capacity = 64;

    private sealed record Live(HazardInfo Info, Vector3 Position, HazardPlan PerTick);

    private sealed class Group
    {
        public HazardSchedule Schedule { get; } = new();
        public Dictionary<int, Live> Hazards { get; } = [];
    }

    private readonly IMagicEffectRecords _records;
    private readonly double _unitsPerMeter;
    private readonly Dictionary<string, Group> _groups = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly EffectApplier _applier = new();

    public HazardField(IMagicEffectRecords records, double unitsPerMeter)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (!double.IsFinite(unitsPerMeter) || unitsPerMeter <= 0) throw new ArgumentOutOfRangeException(nameof(unitsPerMeter));
        _records = records;
        _unitsPerMeter = unitsPerMeter;
    }

    public int Count => _groups.Values.Sum(group => group.Hazards.Count);

    public double RadiusMeters(HazardInfo info) =>
        !float.IsFinite(info.Radius) || info.Radius <= 0 ? 0.0 : info.Radius * RadiusUnitFo4Units / _unitsPerMeter;

    // Invariant: effect eligibility is judged once here, so Advance never reads a record; a hazard with no applicable effect is not spawned.
    public HazardSpawn Spawn(string baseKey, HazardInfo info, Vector3 position, IReadOnlyList<ConsumableEffect> effects)
    {
        var perTick = HazardDamage.Plan(info, 1, effects, _records);
        if (RadiusMeters(info) <= 0 || perTick.Specs.Count == 0 || Count >= Capacity || string.IsNullOrWhiteSpace(baseKey)) return new HazardSpawn(null, perTick.Skipped);
        var existing = _groups.TryGetValue(baseKey, out var found) ? found : null;
        var target = existing ?? new Group();
        if (target.Schedule.Add(info) is not { } id) return new HazardSpawn(null, perTick.Skipped);
        if (existing is null) _groups[baseKey] = target;
        target.Hazards[id] = new Live(info, position, perTick);
        Prune(target);
        return new HazardSpawn(id, perTick.Skipped);
    }

    // Invariant: victimsOf is called at most once per Advance and only when a tick is due; ticks apply before expiry is pruned, because the schedule drops an ended hazard in the same Advance that reports its last ticks.
    public int Advance(double seconds, Func<IReadOnlyList<HazardVictim>> victimsOf)
    {
        var accepted = 0;
        IReadOnlyList<HazardVictim>? victims = null;
        foreach (var group in _groups.Values.ToList())
        {
            foreach (var tick in group.Schedule.Advance(seconds))
            {
                if (!group.Hazards.TryGetValue(tick.Id, out var live)) continue;
                var reach = RadiusMeters(live.Info);
                if (reach <= 0) continue;
                var plan = new HazardPlan(live.PerTick.Specs.Select(spec => spec with { Amount = spec.Amount * tick.Count }).ToList(), live.PerTick.Skipped);
                victims ??= victimsOf();
                foreach (var victim in victims)
                {
                    if ((live.Info.Flags & AffectsPlayerOnly) != 0 && !victim.IsPlayer) continue;
                    if (Vector3.DistanceSquared(victim.Position, live.Position) > reach * reach) continue;
                    accepted += HazardDamage.Apply(_applier, victim.Target, plan);
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
