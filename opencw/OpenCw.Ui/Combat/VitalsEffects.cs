using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Combat;

public sealed record MagicEffectInfo(uint Archetype, uint Flags, string? ActorValue, int Conditions = 0);

public interface IMagicEffectRecords
{
    MagicEffectInfo? Of(string effectKey);

    string Label(string effectKey) => effectKey;
}

// Requires: an unreadable MGEF field makes the effect unreadable, never a guessed default.
public sealed class RecordMagicEffects(IRecordReader reader) : IMagicEffectRecords
{
    public MagicEffectInfo? Of(string effectKey)
    {
        if (reader.Signature(effectKey) != "MGEF") return null;
        if (!Number(reader.Scalar(effectKey, "MGEF\\Archetype"), out var archetype) || !Number(reader.Scalar(effectKey, "MGEF\\Flags"), out var flags)) return null;
        if (reader.ConditionCount(effectKey) is not { } conditions) return null;
        var link = reader.Scalar(effectKey, "MGEF\\ActorValue");
        return new MagicEffectInfo(archetype, flags, string.IsNullOrEmpty(link) ? null : reader.EditorId(link), conditions);
    }

    public string Label(string effectKey) => reader.Name(effectKey) ?? reader.EditorId(effectKey) ?? effectKey;

    private static bool Number(string? text, out uint value) =>
        uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}

// Requires: main thread only; a Value Modifier duration reads magnitude as per second, a Peak Value Modifier holds a flat amount, both secondary-source supported and UNVERIFIED, see docs/code-notes/opencw-vitals.md.
public static class VitalsEffects
{
    public const uint ValueModifier = 0;
    public const uint Stimpak = 31;
    public const uint PeakValueModifier = 34;
    public const uint HostileFlag = 1u << 0;
    public const uint DetrimentalFlag = 1u << 2;
    public const string HealthValue = "Health";
    public const string ActionPointsValue = "ActionPoints";
    public const string RadsValue = "Rads";

    private static readonly HashSet<string> BuffedValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck",
        "DamageResist", "EnergyResist", "CarryWeight", HealthValue, ActionPointsValue,
    };

    // Invariant: each effect is judged alone, so an unsupported one is skipped and reported, never guessed; true when at least one effect was applied (an instant one may leave a full vital unchanged, a timed one is only queued).
    public static bool Apply(PlayerVitals vitals, IReadOnlyList<ConsumableEffect> effects, IMagicEffectRecords records)
    {
        var applied = false;
        foreach (var effect in effects)
        {
            var plan = Classify(effect, records);
            if (plan.Target is not { } target) continue;
            var total = effect.Magnitude * effect.Duration;
            if (plan.Timed == Timing.Buff) vitals.AddBuff(target, plan.Sign * effect.Magnitude, effect.Duration);
            else if (plan.Timed == Timing.Stimpak) vitals.QueueHeal(total * vitals.MaxHealth / 100, effect.Duration);
            else if (target == RadsValue && plan.Timed == Timing.Total) vitals.QueueRads(plan.Sign * total, effect.Duration);
            else if (target == RadsValue) vitals.AddRads(plan.Sign * effect.Magnitude);
            else if (plan.Timed == Timing.Total && target == HealthValue) vitals.QueueHeal(total, effect.Duration);
            else if (plan.Timed == Timing.Total) vitals.QueueActionPoints(total, effect.Duration);
            else if (target == HealthValue) vitals.AddHealth(effect.Magnitude);
            else vitals.AddActionPoints(effect.Magnitude);
            applied = true;
        }
        return applied;
    }

    public static IReadOnlyList<string> Unapplied(IReadOnlyList<ConsumableEffect> effects, IMagicEffectRecords records) =>
        effects.Count == 0
            ? ["no effects"]
            : effects.Select(effect => Classify(effect, records).Reason is { } reason ? $"{records.Label(effect.EffectId)}: {reason}" : null).OfType<string>().ToList();

    private enum Timing
    {
        Instant,
        Stimpak,
        Total,
        Buff,
    }

    private readonly record struct Plan(string? Target, string? Reason, Timing Timed = Timing.Instant, double Sign = 1);

    private static Plan Refuse(string reason) => new(null, reason);

    // Invariant: Stimpak is percent of maximum health per second for Duration seconds; Total is a Value Modifier whose magnitude per second times Duration is delivered over Duration; Buff is a flat Peak Value Modifier amount held for Duration; Sign is the change to the target (Rads rises for a detrimental effect); all secondary-source supported and UNVERIFIED (docs/code-notes/opencw-vitals.md).
    private static Plan Classify(ConsumableEffect effect, IMagicEffectRecords records)
    {
        if (effect.Conditional) return Refuse("conditional effect");
        if (!double.IsFinite(effect.Magnitude) || effect.Magnitude <= 0) return Refuse("magnitude is not a positive number");
        if (records.Of(effect.EffectId) is not { } info) return Refuse("magic effect unreadable");
        if (info.Conditions != 0) return Refuse("magic effect has its own conditions");
        var detrimental = (info.Flags & DetrimentalFlag) != 0;
        if (info.Archetype == Stimpak)
        {
            if ((info.Flags & (HostileFlag | DetrimentalFlag)) != 0) return Refuse("hostile or detrimental");
            return effect.Duration > 0 ? new Plan(HealthValue, null, Timing.Stimpak) : Refuse("stimpak effect has no duration");
        }
        if (effect.Duration < 0) return Refuse("duration is negative");
        if (info.Archetype != ValueModifier && info.Archetype != PeakValueModifier) return Refuse(string.Create(CultureInfo.InvariantCulture, $"archetype {info.Archetype} not evidenced"));
        if ((info.Flags & HostileFlag) != 0) return Refuse("hostile or detrimental");
        var timing = effect.Duration > 0 ? Timing.Total : Timing.Instant;
        if (info.Archetype == PeakValueModifier)
        {
            if (effect.Duration <= 0) return Refuse("peak modifier has no duration");
            if (info.ActorValue is null || !BuffedValues.Contains(info.ActorValue)) return Refuse($"actor value {info.ActorValue ?? "none"} not tracked");
            if (detrimental && (info.ActorValue == HealthValue || info.ActorValue == ActionPointsValue)) return Refuse("hostile or detrimental");
            return new Plan(info.ActorValue, null, Timing.Buff, detrimental ? -1 : 1);
        }
        if (info.ActorValue == RadsValue) return new Plan(RadsValue, null, timing, detrimental ? 1 : -1);
        if (detrimental) return Refuse("hostile or detrimental");
        if (info.ActorValue == HealthValue || info.ActorValue == ActionPointsValue) return new Plan(info.ActorValue, null, timing);
        return Refuse($"actor value {info.ActorValue ?? "none"} not tracked");
    }
}
