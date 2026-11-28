using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Ui.Gallery;

public sealed class SamplePlayerStatus : IPlayerStatusSource, IPipBoyStatusSource
{
    private static readonly string[] SpecialNames =
        ["Stat A", "Stat B", "Stat C", "Stat D", "Stat E", "Stat F", "Stat G"];

    public string? PlayerName => "Character A";

    public double? Health => 62;

    public double? MaxHealth => 100;

    public double? PendingHealthGain => 20;

    public double? Radiation => 0.25;

    public IReadOnlyList<DamageTotal>? DamageTotals { get; } = [new DamageTotal("Kind 1", 18), new DamageTotal("Kind 2", 4)];

    public IReadOnlyList<DamageTotal>? ResistTotals { get; } = [new DamageTotal("Kind 1", 12)];

    public AidSupply? Stimpak => new("Item A", 3);

    public AidSupply? RadAway => new("Item B", 0);

    public IReadOnlyList<EffectSource> Effects { get; } =
    [
        new EffectSource(1, "Source A", [new EffectLine("Effect 1", 2), new EffectLine("Effect 2", -10, true)]),
        new EffectSource(2, "Source B", [new EffectLine("Effect 3", 1, false, 60)]),
    ];

    public IReadOnlyList<SpecialEntry> Special { get; } = BuildSpecial();

    public IReadOnlyList<OwnedPerk> Perks { get; } =
    [
        new OwnedPerk("perk_a", "Perk A", 2, ["Sample rank one text.", "Sample rank two text.", "Sample rank three text."]),
        new OwnedPerk("perk_b", "Perk B", 1, ["Sample single rank text."]),
    ];

    public bool ReadOnly => false;

    public int? Level => 7;

    public double? LevelProgress => 0.5;

    public double? ActionPoints => 70;

    public double? MaxActionPoints => 90;

    public double? CarryWeight => 64.5;

    public double? CarryCapacity => GallerySamples.SampleWeightCapacity;

    public long? Caps => 215;

    public DateTime? WorldTime => new(2026, 3, 14, 15, 9, 0);

    public string? Location => "Town B";

    public LimbCondition? Limb(LimbKind limb) => limb switch
    {
        LimbKind.LeftLeg => new LimbCondition(0, true),
        LimbKind.RightArm => new LimbCondition(0.55),
        _ => new LimbCondition(1),
    };

    private static SpecialEntry[] BuildSpecial()
    {
        var entries = new SpecialEntry[SpecialNames.Length];
        for (var i = 0; i < entries.Length; i++)
            entries[i] = new SpecialEntry("special_" + i, SpecialNames[i], 3 + i, i == 2 ? 1 : 0, "Sample description " + (i + 1) + ".");
        return entries;
    }
}
