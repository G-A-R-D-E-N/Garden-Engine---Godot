using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

public static class PerkEntryPointRows
{
    public const string EffectsPath = "PERK\\Effects";
    public const string TabRunOnPath = "PERK\\TabRunOn";
    private const byte EntryPointEffectType = 2;
    private const byte SingleFloatParameterType = 1;

    // Invariant: only type 2 effect sections are returned, in record order; the effect DATA rows are entry point bytes only for that type, and the tab running index counts the tabs of every earlier effect, whatever its type.
    // Constraint: null when the winner is missing, is not a PERK or has an unreadable row; a Radicle condition read failure propagates as RadicleException.
    public static PerkEntryPointsRecord? Read(RadicleEnvironment env, FormKey perk)
    {
        if (env.Winner(perk) is not { } record || env.Info(record) is not { Signature: "PERK" } info) return null;
        var effects = new List<PerkEntryPointEffect>();
        var count = env.TryListSize(record, EffectsPath, out var size) ? size : 0;
        var tabsBefore = 0;
        for (var i = 0; i < count; i++)
        {
            var scope = $"{EffectsPath}[{i}]\\";
            var tabCount = env.TryListSize(record, scope + "Tabs", out var tabs) ? tabs : 0;
            if (Number(env, record, "PERK\\EffectType", i) is not { } type) return null;
            if (type == EntryPointEffectType)
            {
                if (Number(env, record, "PERK\\EffectRank", i) is not { } rank
                    || Number(env, record, "PERK\\EffectPriority", i) is not { } priority
                    || Number(env, record, "PERK\\EffectEntryPoint", i) is not { } entryPoint
                    || Number(env, record, "PERK\\EffectFunction", i) is not { } function)
                    return null;
                byte? parameterType = Hex(env, record, scope + "ParameterTypes") is { Length: 1 } one ? one[0] : null;
                float? value = parameterType == SingleFloatParameterType && Hex(env, record, scope + "ParameterData") is { Length: 4 } data
                    ? BinaryPrimitives.ReadSingleLittleEndian(data) : null;
                var tabRows = new List<PerkEntryPointTab>();
                for (var tab = 0; tab < tabCount; tab++)
                {
                    if (Number(env, record, TabRunOnPath, tabsBefore + tab) is not { } runOn) return null;
                    tabRows.Add(new PerkEntryPointTab((sbyte)runOn, TabConditions(env, record, tabsBefore + tab)));
                }
                effects.Add(new PerkEntryPointEffect((byte)rank, (byte)priority, (byte)entryPoint, (byte)function, parameterType, value, tabRows));
            }
            tabsBefore += tabCount;
        }
        return new PerkEntryPointsRecord(info.FormKey, effects);
    }

    private static RecipeCondition[] TabConditions(RadicleEnvironment env, Record record, int tab)
    {
        var rows = env.Conditions(record, $"Tabs[{tab}].Conditions");
        var typed = env.ConditionParameters(record, rows);
        return rows.Select((row, i) =>
            new RecipeCondition(row, typed[i].Comparison.Form, typed[i].Param1.Form, typed[i].Param2.Form, typed[i].Reference.Form)).ToArray();
    }

    private static int? Number(RadicleEnvironment env, Record record, string path, int index) =>
        int.TryParse(env.GetField(record, path, index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static byte[]? Hex(RadicleEnvironment env, Record record, string path) =>
        env.GetField(record, path) is { Length: > 0 } text ? Convert.FromHexString(text) : null;
}
