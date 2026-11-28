using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public enum StatusSubTab
{
    Status,
    Special,
    Perks,
}

public enum LimbKind
{
    Head,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
}

public enum AidKind
{
    Stimpak,
    RadAway,
}

public sealed record LimbCondition(double Fraction, bool Crippled = false);

public sealed record DamageTotal(string Kind, double Value);

public sealed record AidSupply(string Name, int Count);

public sealed record SpecialEntry(string Id, string Name, double Value, double Modifier = 0, string Description = "");

public sealed record OwnedPerk(string Id, string Name, int Rank, IReadOnlyList<string> RankDescriptions);

public sealed record EffectLine(string Name, double Value, bool Percent = false, double Duration = 0,
                                bool CustomDescription = false);

public sealed record EffectSource(int Group, string Name, IReadOnlyList<EffectLine> Lines);

public sealed record StatusRow(string Key, string Label, string Value);

public sealed record ConditionRow(string Key, string Label, double Fraction, double PendingFraction, string Text, bool Crippled);

public sealed record AidAction(AidKind Kind, string Label, bool Enabled);

public sealed record EffectEntryView(string Source, IReadOnlyList<string> Lines);

public sealed record EffectGroupView(int Group, IReadOnlyList<EffectEntryView> Entries);

public interface IPlayerStatusSource
{
    string? PlayerName { get; }

    double? Health { get; }

    double? MaxHealth { get; }

    double? PendingHealthGain { get; }

    double? Radiation { get; }

    IReadOnlyList<DamageTotal>? DamageTotals { get; }

    IReadOnlyList<DamageTotal>? ResistTotals { get; }

    AidSupply? Stimpak { get; }

    AidSupply? RadAway { get; }

    IReadOnlyList<EffectSource> Effects { get; }

    IReadOnlyList<SpecialEntry> Special { get; }

    IReadOnlyList<OwnedPerk> Perks { get; }

    bool ReadOnly { get; }

    LimbCondition? Limb(LimbKind limb);
}
