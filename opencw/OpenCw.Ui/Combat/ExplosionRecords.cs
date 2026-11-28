using System;
using System.Globalization;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Combat;

public sealed record ExplosionInfo(float Damage, float Force, float InnerRadius, float OuterRadius, uint Flags);

public static class ExplosionRecords
{
    // Requires: explosionKey is an EXPL record key; an unreadable or non-numeric field makes the explosion null, never a guessed default.
    public static ExplosionInfo? Read(IRecordReader reader, string explosionKey)
    {
        if (reader.Signature(explosionKey) != "EXPL") return null;
        if (!Float(reader.Scalar(explosionKey, "EXPL\\Damage"), out var damage)
            || !Float(reader.Scalar(explosionKey, "EXPL\\Force"), out var force)
            || !Float(reader.Scalar(explosionKey, "EXPL\\InnerRadius"), out var inner)
            || !Float(reader.Scalar(explosionKey, "EXPL\\OuterRadius"), out var outer)
            || !Unsigned(reader.Scalar(explosionKey, "EXPL\\Flags"), out var flags)) return null;
        return new ExplosionInfo(damage, force, inner, outer, flags);
    }

    // Requires: placedObject is the single-door read of the EXPL placed-object link and is called for an EXPL record only; a link that is not a HAZD record gives null.
    public static string? PlacedHazard(IRecordReader reader, Func<string, string?> placedObject, string explosionKey)
    {
        if (reader.Signature(explosionKey) != "EXPL") return null;
        return placedObject(explosionKey) is { } key && reader.Signature(key) == "HAZD" ? key : null;
    }

    private static bool Float(string? text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    private static bool Unsigned(string? text, out uint value) =>
        uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
