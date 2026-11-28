namespace OpenCommonwealth.Services;

public static class WorldScale
{
    public const double UnitsPerMeter = 69.99125;
    public const float Fo4UnitsPerMeter = (float)UnitsPerMeter;

    // 1 Godot unit = 1 real meter. Multiply raw FO4 distances (positions, vertices, ranges) only --
    // never dimensionless values (an object's own scale field, UVs, directions). See LIGHTMAP_BAKING_NOTES.md.
    public const float Fo4ToGodot = 1f / Fo4UnitsPerMeter;
}
