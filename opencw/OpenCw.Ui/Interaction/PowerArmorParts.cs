using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Hud;

namespace OpenCommonwealth.Services.Interaction;

public static class PowerArmorParts
{
    // Constraint: UNVERIFIED default (see code notes).
    public const double FullCondition = 1;

    public static readonly IReadOnlyDictionary<string, PowerArmorPartId> Keywords = new Dictionary<string, PowerArmorPartId>(StringComparer.OrdinalIgnoreCase)
    {
        ["dn_PowerArmor_Helmet"] = PowerArmorPartId.Head,
        ["dn_PowerArmor_Torso"] = PowerArmorPartId.Torso,
        ["dn_PowerArmor_LeftArm"] = PowerArmorPartId.LeftArm,
        ["dn_PowerArmor_RightArm"] = PowerArmorPartId.RightArm,
        ["dn_PowerArmor_LeftLeg"] = PowerArmorPartId.LeftLeg,
        ["dn_PowerArmor_RightLeg"] = PowerArmorPartId.RightLeg,
    };

    public static IReadOnlyList<PowerArmorPartId> Present(IEnumerable<IEnumerable<string>> pieceKeywordNames)
    {
        ArgumentNullException.ThrowIfNull(pieceKeywordNames);
        var present = new List<PowerArmorPartId>();
        foreach (var piece in pieceKeywordNames)
            if (PartOf(piece) is { } part && !present.Contains(part)) present.Add(part);
        return present;
    }

    public static PowerArmorPartId? PartOf(IEnumerable<string> keywordEditorIds)
    {
        ArgumentNullException.ThrowIfNull(keywordEditorIds);
        foreach (var id in keywordEditorIds)
            if (id is not null && Keywords.TryGetValue(id, out var part)) return part;
        return null;
    }
}
