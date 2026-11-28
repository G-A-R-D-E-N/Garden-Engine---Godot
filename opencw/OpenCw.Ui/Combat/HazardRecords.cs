using System.Globalization;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Combat;

public sealed record HazardInfo(float Radius, float Lifetime, float TargetInterval, uint Limit, uint Flags, string? SpellKey);

public static class HazardRecords
{
    // Requires: hazardKey is a HAZD record key; an unreadable or non-numeric numeric field makes the hazard null, never a guessed default, and a missing spell link keeps it.
    public static HazardInfo? Read(IRecordReader reader, string hazardKey)
    {
        if (reader.Signature(hazardKey) != "HAZD") return null;
        if (!Float(reader.Scalar(hazardKey, "HAZD\\Radius"), out var radius)
            || !Float(reader.Scalar(hazardKey, "HAZD\\Lifetime"), out var lifetime)
            || !Float(reader.Scalar(hazardKey, "HAZD\\TargetInterval"), out var interval)
            || !Unsigned(reader.Scalar(hazardKey, "HAZD\\Limit"), out var limit)
            || !Unsigned(reader.Scalar(hazardKey, "HAZD\\Flags"), out var flags)) return null;
        return new HazardInfo(radius, lifetime, interval, limit, flags, RecordRows.Link(reader, hazardKey, "HAZD\\Spell"));
    }

    private static bool Float(string? text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    private static bool Unsigned(string? text, out uint value) =>
        uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
