using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Workbench;

public static class BenchSounds
{
    private const string SoundSignature = "SNDR";
    private const string CraftingSoundSubrecord = "CUSD";

    // Constraint: returns SNDR editor ids in play order (craft sound first, then component use sounds, no repeats); every id was read from a record link, so an unresolved link is silent.
    public static IReadOnlyList<string> Resolve(IRecordReader reader, int? effectiveType, IReadOnlyCollection<string> benchKeywords, IEnumerable<string> componentIds, BenchSettings? settings = null)
    {
        var s = settings ?? BenchSettings.Default;
        var ids = new List<string>();
        if (s.PlayCraftSound && LoopObject(reader, effectiveType, benchKeywords, s) is { } loop && SoundOf(reader, BenchRules.DefaultObjectLink(reader, loop)) is { } craft) ids.Add(craft);
        foreach (var component in componentIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(Math.Max(0, s.MaxComponentSounds)))
            if (reader.FormLinks(component, CraftingSoundSubrecord).Select(link => SoundOf(reader, link)).OfType<string>().FirstOrDefault() is { } use && !ids.Contains(use)) ids.Add(use);
        return ids;
    }

    private static string? LoopObject(IRecordReader reader, int? effectiveType, IReadOnlyCollection<string> benchKeywords, BenchSettings s) =>
        BenchRules.HasKeyword(reader, benchKeywords, s.ChemKeywordObject) ? s.ChemLoopObject
        : BenchRules.HasKeyword(reader, benchKeywords, s.CookingKeywordObject) ? s.CookingLoopObject
        : effectiveType is BenchKinds.Weapons or BenchKinds.Armor or BenchKinds.PowerArmor ? s.ExamineLoopObject
        : null;

    private static string? SoundOf(IRecordReader reader, string? id) =>
        id is not null && reader.Signature(id) == SoundSignature ? reader.EditorId(id) : null;
}
