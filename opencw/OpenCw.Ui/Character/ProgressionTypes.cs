using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Character;

public enum PerkChartMode { Browse, Spend }

public enum PerkChartKind { Attribute, Perk }

public enum UnmetKind { Level, Attribute, Perk, Other }

public enum ChooseResult { Ok, NoPoints, Locked, AlreadyMax, Unknown }

public sealed record UnmetRequirement(UnmetKind Kind, int Needed, int Have, string Name);

public sealed record PerkRankInfo(string Description, int RequiredLevel = 0, (int Column, int Amount)? RequiredAttribute = null,
                                  IReadOnlyList<UnmetRequirement>? Unmet = null, int Cost = 1)
{
    public IReadOnlyList<UnmetRequirement> UnmetOrEmpty => Unmet ?? Array.Empty<UnmetRequirement>();
}

public sealed record PerkChartEntry(string Id, string Name, PerkChartKind Kind, int? Column, int CurrentRank, int MaxRank,
                                    IReadOnlyList<PerkRankInfo> Ranks, bool Available);

public sealed record PerkChartSnapshot(int Revision, int Level, int PerkPoints, IReadOnlyList<int> Attributes,
                                       IReadOnlyList<PerkChartEntry> Entries);

public sealed record ProgressionSnapshot(PerkChartSnapshot Chart, double XpIntoLevel, double XpForNextLevel, int? LevelCap = null)
{
    public int Revision => Chart.Revision;

    public int Level => Chart.Level;

    public int PerkPoints => Chart.PerkPoints;

    public bool IsValid => Chart.Level >= 0 && Chart.PerkPoints is >= 0 and <= ProgressionCurve.MaxPerkPoints && Chart.Attributes.Count == 7;
}

public interface IProgressionService
{
    ProgressionSnapshot Snapshot { get; }

    event Action? Changed;

    ChooseResult TryChoose(string entryId, int rank);
}

public static class ProgressionCurve
{
    public const int MaxPerkPoints = 255;
}
