namespace OpenCommonwealth.Services.Ui.Vats;

[Flags]
public enum VatsPartFlags : byte
{
    None = 0,
    Severable = 1 << 0,
    HitReaction = 1 << 1,
    HitReactionDefault = 1 << 2,
    Explodable = 1 << 3,
    CutMeatCapSever = 1 << 4,
    OnCripple = 1 << 5,
    ExplodableAbsoluteChance = 1 << 6,
    ShowCrippleGeometry = 1 << 7,
}

public sealed record VatsBodyPartData(string? Name, string? Node, string? Target, byte PartType, float DamageMult, byte Flags, byte HealthPercent, byte ToHitChance);

public interface IBodyPartSource
{
    IReadOnlyList<VatsBodyPartData>? Parts(string actorBaseKey);
}

public sealed record VatsBodyPart(string Name, string Target, string? Node, byte PartType, int BaseHitChance, float DamageMult, int HealthPercent, VatsPartFlags Flags)
{
    public bool Severable => Flags.HasFlag(VatsPartFlags.Severable);

    public bool Explodable => Flags.HasFlag(VatsPartFlags.Explodable);

    public bool OnCripple => Flags.HasFlag(VatsPartFlags.OnCripple);
}

public static class VatsBodyParts
{
    public const int PartSlotCount = 26;

    // Invariant: the engine keeps 26 slots indexed by part type and the later part of a type replaces the earlier one (before the target check); a part also needs node data (BPND) and a non-empty VATS target (BPNT).
    public static IReadOnlyList<VatsBodyPart> Targetable(IBodyPartSource source, string actorBaseKey)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Parts(actorBaseKey) is { } rows ? Targetable(rows) : [];
    }

    public static IReadOnlyList<VatsBodyPart> Targetable(IReadOnlyList<VatsBodyPartData> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var lastOfType = new Dictionary<byte, int>();
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].PartType < PartSlotCount)
                lastOfType[rows[i].PartType] = i;
        return lastOfType.Values
            .Order()
            .Select(i => rows[i])
            .Where(r => !string.IsNullOrWhiteSpace(r.Target))
            .Select(r => new VatsBodyPart(
                string.IsNullOrWhiteSpace(r.Name) ? r.Target! : r.Name,
                r.Target!,
                r.Node,
                r.PartType,
                r.ToHitChance,
                r.DamageMult,
                r.HealthPercent,
                (VatsPartFlags)r.Flags))
            .ToArray();
    }
}
