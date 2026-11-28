using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Workbench;

public sealed record BenchSettings
{
    public static readonly BenchSettings Default = new();

    public string IgnoreDataObject { get; init; } = "IgnoreWorkbenchDataKeyword_DO";

    public string PieceKeywordObject { get; init; } = "PowerArmorArmorKeyword_DO";

    public string ChemKeywordObject { get; init; } = "WorkbenchChemlabKeyword_DO";

    public string CookingKeywordObject { get; init; } = "WorkbenchCookingKeyword_DO";

    public string ChemLoopObject { get; init; } = "CraftingLoopSound_Chemlab_DO";

    public string CookingLoopObject { get; init; } = "CraftingLoopSound_Cooking_DO";

    public string ExamineLoopObject { get; init; } = "CraftingLoopSound_Examine_DO";

    public bool PlayCraftSound { get; init; } = true;

    public int MaxComponentSounds { get; init; } = 1;
}

public static class BenchRules
{
    // Requires: keywords are formatted record keys (RecordKey.Format); an unresolved default object never matches.
    public static int? EffectiveType(IRecordReader reader, int? benchType, IReadOnlyCollection<string> keywords, bool isPowerArmorFrame, BenchSettings? settings = null)
    {
        var s = settings ?? BenchSettings.Default;
        if (HasKeyword(reader, keywords, s.IgnoreDataObject)) return null;
        return benchType == BenchKinds.PowerArmor && isPowerArmorFrame ? null : benchType;
    }

    // Invariant: a type that needs a piece keyword and cannot resolve it returns "", which no item carries, so the bench lists nothing.
    public static string? PieceKeyword(IRecordReader reader, int? effectiveType, BenchSettings? settings = null) =>
        effectiveType == BenchKinds.PowerArmor ? DefaultObjectLink(reader, (settings ?? BenchSettings.Default).PieceKeywordObject) ?? string.Empty : null;

    public static string? Notice(int? effectiveType) => effectiveType == BenchKinds.RobotMod ? BenchKinds.RobotNotice : null;

    public static bool HasKeyword(IRecordReader reader, IReadOnlyCollection<string> keywords, string defaultObject) =>
        DefaultObjectLink(reader, defaultObject) is { } keyword && keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase);

    public static string? DefaultObjectLink(IRecordReader reader, string defaultObject) =>
        reader.FindEditorId(defaultObject, "DFOB") is { } dfob ? RecordRows.Link(reader, dfob, "DFOB\\Object") : null;
}
