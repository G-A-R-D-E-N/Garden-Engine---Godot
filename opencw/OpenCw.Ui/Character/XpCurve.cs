using System;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Character;

// Invariant: the XP gap from level L to L + 1 is Base + Bump * (L - 1), Base is above 0 and Bump is not negative, so every level has a finite positive gap.
public sealed record XpCurve(int Base, int Bump)
{
    public const string BaseSetting = "iXPBase";
    public const string BumpSetting = "iXPBumpBase";
    public const int MaxLevel = ushort.MaxValue;
    public const int KillXpPerLevel = 5;

    public static readonly XpCurve Default = new(200, 75);

    // Constraint: a missing, unreadable or out-of-range setting falls back to the Default pair as a whole, never to one half of it.
    public static XpCurve Read(IRecordReader reader) => Read(reader, out _);

    // Invariant: failed is true only when a record read failed or threw; an absent or invalid setting is a settled Default and not a failure.
    public static XpCurve Read(IRecordReader reader, out bool failed)
    {
        var before = reader.FailureCount;
        try
        {
            var xpBase = RecordCrimeFacts.Integer(reader, BaseSetting);
            var bump = RecordCrimeFacts.Integer(reader, BumpSetting);
            failed = reader.FailureCount != before;
            return !failed && xpBase is > 0 && bump is >= 0 ? new XpCurve(xpBase.Value, bump.Value) : Default;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failed = true;
            return Default;
        }
    }

    public long TotalFor(int level)
    {
        var steps = Math.Clamp(level, 1, MaxLevel) - 1L;
        return Base * steps + Bump * steps * (steps - 1) / 2;
    }

    public int LevelAt(long xp)
    {
        var level = 1;
        while (level < MaxLevel && TotalFor(level + 1) <= xp) level++;
        return level;
    }

    // Invariant: 0 at the first point of a level and below 1 inside it; 0 at the top level, which has no next level to fill toward.
    public double Progress(long xp, int level)
    {
        if (level >= MaxLevel) return 0;
        var from = TotalFor(level);
        return Math.Clamp((xp - from) / (double)(TotalFor(level + 1) - from), 0, 1);
    }

    public static long KillXp(int victimLevel) => Math.Max(victimLevel, 1) * (long)KillXpPerLevel;

    // Constraint: a victim whose level is unresolved awards nothing, because its level is not known and KillXp(1) would pay a made-up 5.
    public static long? KillXpOf(int victimLevel, string? levelUnresolved) => levelUnresolved is null ? KillXp(victimLevel) : null;
}
