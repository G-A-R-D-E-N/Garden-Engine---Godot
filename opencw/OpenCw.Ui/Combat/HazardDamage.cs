using System.Collections.Generic;
using System.Globalization;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Combat;

public sealed record HazardPlan(IReadOnlyList<EffectSpec> Specs, IReadOnlyList<string> Skipped);

// Requires: a hazard spell magnitude is damage per second, UNVERIFIED (CodeNotes:Garden Engine/history/docs/code-notes/opencw-interact-traps.md); nothing here reads or sizes the hazard radius.
public static class HazardDamage
{
    // Invariant: each effect is judged alone, so an unsupported one is skipped with a reason and never guessed; the effect's own duration is ignored.
    public static HazardPlan Plan(HazardInfo hazard, int ticks, IReadOnlyList<ConsumableEffect> effects, IMagicEffectRecords records)
    {
        var specs = new List<EffectSpec>();
        var skipped = new List<string>();
        if (ticks <= 0 || !double.IsFinite(hazard.TargetInterval) || hazard.TargetInterval <= 0) return new HazardPlan(specs, ["no ticks"]);
        foreach (var effect in effects)
        {
            if (Refusal(effect, records) is { } reason) skipped.Add($"{records.Label(effect.EffectId)}: {reason}");
            else specs.Add(new EffectSpec(VitalsEffects.HealthValue, EffectKind.Damage, effect.Magnitude * hazard.TargetInterval * ticks));
        }
        return new HazardPlan(specs, skipped);
    }

    public static int Apply(EffectApplier applier, IEffectTarget target, HazardPlan plan)
    {
        var accepted = 0;
        foreach (var spec in plan.Specs)
            if (applier.Apply(target, spec).Accepted) accepted++;
        return accepted;
    }

    private static string? Refusal(ConsumableEffect effect, IMagicEffectRecords records)
    {
        if (effect.Conditional) return "conditional effect";
        if (!double.IsFinite(effect.Magnitude) || effect.Magnitude <= 0) return "magnitude is not a positive number";
        if (records.Of(effect.EffectId) is not { } info) return "magic effect unreadable";
        if (info.Conditions != 0) return "magic effect has its own conditions";
        if (info.Archetype != VitalsEffects.ValueModifier) return string.Create(CultureInfo.InvariantCulture, $"archetype {info.Archetype} not evidenced");
        if ((info.Flags & (VitalsEffects.HostileFlag | VitalsEffects.DetrimentalFlag)) == 0) return "not hostile or detrimental";
        return info.ActorValue == VitalsEffects.HealthValue ? null : $"actor value {info.ActorValue ?? "none"} not tracked";
    }
}
