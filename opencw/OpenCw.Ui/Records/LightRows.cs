using System;
using System.Globalization;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

public sealed record LightRow(
    uint Radius, byte Red, byte Green, byte Blue, uint Flags, float FalloffExponent, float Fov, float NearClip,
    float FlickerPeriod, float FlickerIntensity, float FlickerMovement, float Constant, float Scalar, float? Exponent, float Fade, string? Gobo, bool RandomAnimStart);

public static class LightRows
{
    public const string RadiusPath = "LIGH\\Radius";
    public const string RedPath = "LIGH\\ColorRed";
    public const string GreenPath = "LIGH\\ColorGreen";
    public const string BluePath = "LIGH\\ColorBlue";
    public const string FlagsPath = "LIGH\\Flags";
    public const string FalloffExponentPath = "LIGH\\FalloffExponent";
    public const string FovPath = "LIGH\\FOV";
    public const string NearClipPath = "LIGH\\NearClip";
    public const string FlickerPeriodPath = "LIGH\\FlickerPeriod";
    public const string FlickerIntensityPath = "LIGH\\FlickerIntensityAmplitude";
    public const string FlickerMovementPath = "LIGH\\FlickerMovementAmplitude";
    public const string ConstantPath = "LIGH\\Constant";
    public const string ScalarPath = "LIGH\\Scalar";
    public const string ExponentPath = "LIGH\\Exponent";
    public const string FadePath = "LIGH\\FadeValue";
    public const string GoboPath = "LIGH\\Gobo";
    public const string RandomAnimStartPath = "LIGH\\RandomAnimStart";

    // Invariant: an absent, unreadable, non-finite or unparsable row reads as the typed default for absent DATA; Exponent stays null so the caller applies the engine curve default.
    public static LightRow Read(Func<string, string?> row, Func<string, bool> flag) => new(
        Whole(row, RadiusPath, 0u), Byte(row, RedPath), Byte(row, GreenPath), Byte(row, BluePath), Whole(row, FlagsPath, 0u),
        Real(row, FalloffExponentPath) ?? 1f, Real(row, FovPath) ?? 0f, Real(row, NearClipPath) ?? 0f,
        Real(row, FlickerPeriodPath) ?? 0f, Real(row, FlickerIntensityPath) ?? 0f, Real(row, FlickerMovementPath) ?? 0f,
        Real(row, ConstantPath) ?? 0f, Real(row, ScalarPath) ?? 1f, Real(row, ExponentPath), Real(row, FadePath) ?? 1f, Text(row, GoboPath),
        Flag(flag, RandomAnimStartPath));

    // Invariant: Radius is a DATA row, so a LIGH with no DATA subrecord (or an unreadable row) has no Radius text.
    public static bool HasData(Func<string, string?> row) => Text(row, RadiusPath) != null;

    private static string? Text(Func<string, string?> row, string path)
    {
        try
        {
            return row(path);
        }
        catch (RadicleException)
        {
            return null;
        }
    }

    private static bool Flag(Func<string, bool> flag, string path)
    {
        try
        {
            return flag(path);
        }
        catch (RadicleException)
        {
            return false;
        }
    }

    private static uint Whole(Func<string, string?> row, string path, uint fallback) =>
        uint.TryParse(Text(row, path), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static byte Byte(Func<string, string?> row, string path) =>
        byte.TryParse(Text(row, path), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : (byte)0;

    private static float? Real(Func<string, string?> row, string path) =>
        float.TryParse(Text(row, path), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value) ? value : null;
}
