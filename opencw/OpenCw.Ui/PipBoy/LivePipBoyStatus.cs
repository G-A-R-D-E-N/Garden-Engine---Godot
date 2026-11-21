using System;
using OpenCommonwealth.Services.Combat;

namespace OpenCommonwealth.Services.Ui.PipBoy;

// Constraint: reports only what its sources return; a source that is absent or returns null leaves the footer's unknown marker, and carry capacity has a source only when one is supplied.
public sealed class LivePipBoyStatus(PlayerVitals? vitals, Func<int?> level, Func<double?> actionPoints, Func<double?> maxActionPoints,
                                     Func<double?>? levelProgress = null, Func<double?>? carryWeight = null, Func<long?>? caps = null,
                                     Func<DateTime?>? worldTime = null, Func<string?>? location = null, Func<double?>? carryCapacity = null) : IPipBoyStatusSource
{
    public double? Health => vitals?.Health;

    public double? MaxHealth => vitals?.MaxHealth;

    public int? Level => level();

    public double? LevelProgress => levelProgress?.Invoke();

    public double? ActionPoints => actionPoints();

    public double? MaxActionPoints => maxActionPoints();

    public double? CarryWeight => carryWeight?.Invoke();

    public double? CarryCapacity => carryCapacity?.Invoke();

    public long? Caps => caps?.Invoke();

    public DateTime? WorldTime => worldTime?.Invoke();

    public string? Location => location?.Invoke();
}
