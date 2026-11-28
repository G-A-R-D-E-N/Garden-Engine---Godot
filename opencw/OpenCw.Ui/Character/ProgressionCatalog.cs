using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed record ProgressionAttributeDef(string Id, string Name, int MaxRank = 10);

public sealed record ProgressionPerkRankDef(string Description, int RequiredLevel = 0, int RequiredAttribute = 0, int Cost = 1);

public sealed record ProgressionPerkDef(string Id, string Name, int? Column, IReadOnlyList<ProgressionPerkRankDef> Ranks,
                                        string? PrerequisitePerkId = null, string? PrerequisitePerkName = null, int PrerequisiteRank = 1);

public sealed record ProgressionXpFormula(double BaseXp = 200, double BumpXp = 75)
{
    public double XpForNextLevel(int level)
    {
        var baseXp = double.IsFinite(BaseXp) && BaseXp > 0 ? BaseXp : 200;
        var bump = double.IsFinite(BumpXp) && BumpXp >= 0 ? BumpXp : 75;
        var steps = Math.Max(1, level) - 1;
        var xp = steps * bump + baseXp;
        return double.IsFinite(xp) && xp > 0 ? xp : baseXp;
    }
}

public sealed record ProgressionCatalog(IReadOnlyList<ProgressionAttributeDef> Attributes, IReadOnlyList<ProgressionPerkDef> Perks,
                                        ProgressionXpFormula? XpFormula = null, int? LevelCap = null)
{
    public const int SpecialAttributeCount = 7;

    private static readonly ProgressionAttributeDef[] DefaultSpecial =
    {
        new("attr0", "Strength", 10),
        new("attr1", "Perception", 10),
        new("attr2", "Endurance", 10),
        new("attr3", "Charisma", 10),
        new("attr4", "Intelligence", 10),
        new("attr5", "Agility", 10),
        new("attr6", "Luck", 10),
    };

    public static IReadOnlyList<ProgressionAttributeDef> DefaultSpecialAttributes() => DefaultSpecial;

    public ProgressionAttributeDef AttributeAt(int column)
    {
        var index = Math.Clamp(column, 0, SpecialAttributeCount - 1);
        return Attributes is not null && index < Attributes.Count ? Attributes[index] : DefaultSpecial[index];
    }

    public int AttributeMaxRank(int column) => Math.Max(1, AttributeAt(column).MaxRank);
}
