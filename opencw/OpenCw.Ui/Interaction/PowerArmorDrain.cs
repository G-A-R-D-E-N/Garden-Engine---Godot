using System;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Interaction;

public enum PowerArmorDrainKind { Running, ActionPoint, Jump, MeleeAttack, ImpactLand }

public sealed record PowerArmorDrainRates(
    double PerSecondRunning = 0,
    double PerActionPoint = 0,
    double PerJump = 0,
    double PerMeleeAttack = 0,
    double PerImpactLand = 0)
{
    public double Of(PowerArmorDrainKind kind) => kind switch
    {
        PowerArmorDrainKind.Running => PerSecondRunning,
        PowerArmorDrainKind.ActionPoint => PerActionPoint,
        PowerArmorDrainKind.Jump => PerJump,
        PowerArmorDrainKind.MeleeAttack => PerMeleeAttack,
        PowerArmorDrainKind.ImpactLand => PerImpactLand,
        _ => 0,
    };

    public static PowerArmorDrainRates Read(IRecordReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new(
            PickpocketSettings.Value(reader, "fPowerArmorPowerDrainPerSecondRunning", 0),
            PickpocketSettings.Value(reader, "fPowerArmorPowerDrainPerActionPoint", 0),
            PickpocketSettings.Value(reader, "fPowerArmorPowerDrainPerJump", 0),
            PickpocketSettings.Value(reader, "fPowerArmorPowerDrainPerMeleeAttack", 0),
            PickpocketSettings.Value(reader, "fPowerArmorPowerDrainPerImpactLand", 0));
    }
}

// Requires: Capacity in the same units as the rates (battery units); a non-positive capacity disables the drain.
public sealed record PowerArmorDrain(PowerArmorDrainRates Rates, double Capacity, double DamageRate = 1)
{
    public const string CapacitySetting = "fNewBatteryCapacity";

    // Constraint: must equal the CK exe default of fNewBatteryCapacity (see code notes).
    public const float DefaultCapacity = 100;

    public static PowerArmorDrain Read(IRecordReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new(PowerArmorDrainRates.Read(reader), PickpocketSettings.Value(reader, CapacitySetting, DefaultCapacity));
    }
}
