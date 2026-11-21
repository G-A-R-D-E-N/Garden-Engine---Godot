namespace OpenCommonwealth.Services.Ui.PipBoy;

public static class CarryCapacity
{
    public const double Base = 200;
    public const double PerStrength = 10;

    public static double? FromStrength(double? strength) =>
        strength is { } value && double.IsFinite(value) && value >= 0 ? Base + PerStrength * value : null;
}
