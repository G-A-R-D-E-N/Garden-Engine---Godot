using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Character;

public interface ICharacterDataProvider
{
    IReadOnlyList<StatDef> Stats { get; }

    int StatPoints { get; }

    bool RequireAllStatPointsSpent { get; }

    IReadOnlyList<StatPreset> StatPresets { get; }

    IReadOnlyList<PerkDef> Perks { get; }

    IPerkRequirementChecker PerkRequirements { get; }

    IReadOnlyList<AppearanceControl> AppearanceControls { get; }

    IReadOnlyList<DerivedStat> DerivedStats { get; }
}

public sealed class OwnedPerkRequirementChecker : IPerkRequirementChecker
{
    private const string Prefix = "own:";

    public bool IsMet(string requirement, IReadOnlyDictionary<string, int> perkRanks) =>
        requirement.StartsWith(Prefix, System.StringComparison.Ordinal)
        && perkRanks.TryGetValue(requirement.Substring(Prefix.Length), out var rank)
        && rank > 0;
}

public sealed class GenericCharacterData : ICharacterDataProvider
{
    public IReadOnlyList<StatDef> Stats { get; } =
        Enumerable.Range(1, 7).Select(i => new StatDef($"s{i}", $"Stat {i}", 1, 10)).ToArray();

    public int StatPoints => 5;

    public bool RequireAllStatPointsSpent => true;

    public IReadOnlyList<StatPreset> StatPresets { get; } = new[]
    {
        new StatPreset("Even", new Dictionary<string, int> { ["s1"] = 2, ["s2"] = 2, ["s3"] = 2, ["s4"] = 2, ["s5"] = 2 }),
        new StatPreset("Focused", new Dictionary<string, int> { ["s1"] = 6 }),
    };

    public IReadOnlyList<PerkDef> Perks { get; } = new[]
    {
        new PerkDef("p1", "Perk 1", "Group A", new[] { new PerkRank("Rank one of perk 1."), new PerkRank("Rank two of perk 1.") }),
        new PerkDef("p2", "Perk 2", "Group A", new[] { new PerkRank("Perk 2 text.", new[] { "own:p1" }) }),
        new PerkDef("p3", "Perk 3", "Group B", new[] { new PerkRank("Perk 3 text.") }),
    };

    public IPerkRequirementChecker PerkRequirements { get; } = new OwnedPerkRequirementChecker();

    public IReadOnlyList<AppearanceControl> AppearanceControls { get; } = new[]
    {
        AppearanceControl.Slider("sl1", "Left width", "Shape", 0, 1, 0.5, 0.1, "sl2"),
        AppearanceControl.Slider("sl2", "Right width", "Shape", 0, 1, 0.5, 0.1),
        AppearanceControl.Slider("sl3", "Height", "Shape", 0, 100, 50, 1),
        AppearanceControl.Choice("ch1", "Style", "Style", new[] { "Option 1", "Option 2", "Option 3" }),
    };

    public IReadOnlyList<DerivedStat> DerivedStats { get; } = new[]
    {
        new DerivedStat("d1", "Total of 1 and 2", v => v["s1"] + v["s2"]),
        new DerivedStat("d2", "Double of 3", v => v["s3"] * 2),
    };
}
