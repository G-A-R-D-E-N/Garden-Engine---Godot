using System;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Interaction;

// Constraint: thrust values are FO4 units per second of vertical speed; fuel capacity, refill rate and Enabled are UNVERIFIED defaults (see code notes).
public sealed record PowerArmorJetpackSettings(
    bool Enabled = true,
    double ThrustInitial = 350,
    double ThrustSustained = 85,
    double TimeToSustained = 0.6,
    double DrainInitial = 45,
    double DrainSustained = 16,
    double MinFuelRequired = 3,
    bool InitialOnlyOnTakeoff = true,
    double FuelCapacity = 100,
    double RefillPerSecond = 25);

// Constraint: all values are UNVERIFIED defaults (see code notes); distances are metres, speeds metres per second.
public sealed record PowerArmorStompSettings(
    bool Enabled = true,
    double MinFallSpeed = 6.5,
    double Radius = 3,
    double DamagePerSpeed = 5);

// Constraint: defaults of the exe game settings come from the Creation Kit and Fallout4.exe static tables; the rest are UNVERIFIED named defaults.
public sealed record PowerArmorSettings
{
    public static readonly PowerArmorSettings Default = new();

    public double PartDamageMultiplier { get; init; } = 1;

    public bool PowerLossWalkOnly { get; init; } = true;

    public PowerArmorJetpackSettings Jetpack { get; init; } = new();

    public PowerArmorStompSettings Stomp { get; init; } = new();

    public static PowerArmorSettings Read(IRecordReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var d = new PowerArmorJetpackSettings();
        double V(string name, double fallback) => Math.Round(PickpocketSettings.Value(reader, name, (float)fallback), 6);
        return new()
        {
            PartDamageMultiplier = V("fPowerArmorPCArmorDamageMultiplier", 1),
            Jetpack = d with
            {
                ThrustInitial = V("fJetpackThrustInitial", d.ThrustInitial),
                ThrustSustained = V("fJetpackThrustSustained", d.ThrustSustained),
                TimeToSustained = V("fJetpackTimeToSustained", d.TimeToSustained),
                DrainInitial = V("fJetpackDrainInital", d.DrainInitial),
                DrainSustained = V("fJetpackDrainSustained", d.DrainSustained),
                MinFuelRequired = V("fJetpackMinFuelRequired", d.MinFuelRequired),
                InitialOnlyOnTakeoff = V("bJetpackInitialThrustOnlyOnTakeoff", 1) != 0,
            },
        };
    }
}
