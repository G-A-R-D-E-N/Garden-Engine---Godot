using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Combat;

namespace OpenCommonwealth.Services.Ui.PipBoy;

// Requires: main thread; the buff list stores no source name, so every line is attributed to the generic Consumable source.
public static class LiveBuffEffects
{
    public const string SourceName = "Consumable";
    public const int Group = 1;

    public static IReadOnlyList<EffectSource> Sources(PlayerVitals? vitals)
    {
        if (vitals is null || vitals.Buffs.Count == 0) return [];
        return [new EffectSource(Group, SourceName, vitals.Buffs.Select(buff => new EffectLine(NameOf(buff.ActorValue), buff.Amount, false, buff.Left)).ToArray())];
    }

    public static IReadOnlyList<string> HudLines(PlayerVitals? vitals)
    {
        if (vitals is null) return [];
        return vitals.Buffs.Select(buff => string.Create(CultureInfo.InvariantCulture, $"{NameOf(buff.ActorValue)} {buff.Amount:+0.##;-0.##} {Math.Ceiling(buff.Left):0}s")).ToArray();
    }

    public static IReadOnlyList<SpecialEntry> WithModifiers(IReadOnlyList<SpecialEntry> rows, PlayerVitals? vitals) =>
        vitals is null || vitals.Buffs.Count == 0 ? rows : rows.Select(row => row with { Modifier = vitals.Buff(row.Id) }).ToArray();

    public static string NameOf(string actorValue) => actorValue switch
    {
        VitalsEffects.HealthValue => "Max Health",
        VitalsEffects.ActionPointsValue => "Action Points",
        _ => actorValue,
    };
}
