using System;
using System.Collections.Generic;
using Radicle.Interop;
using static OpenCommonwealth.ConditionFunctions;

namespace OpenCommonwealth;

public sealed record ConditionSubject(
    FormKey? Base,
    FormKey? Race,
    IReadOnlyDictionary<FormKey, int>? Factions,
    IReadOnlySet<FormKey>? Keywords,
    double? Level,
    bool? Dead,
    double HourOfDay,
    Func<FormKey, double?> Global,
    Func<FormKey, int?>? QuestStage = null,
    Func<FormKey, int, bool?>? QuestStageDone = null);

public static class PackageConditionValues
{
    private const uint RunOnSubject = 0;

    public static readonly ConditionInput Unknown = new(false, 0, false, 0, 0, 0);

    public static ConditionInput Value(ConditionRule rule, FormKey? param, FormKey? comparisonGlobal, ConditionSubject subject, int param2 = 0)
    {
        if (rule.RunOn != RunOnSubject || (rule.FlagsByte & FlagsNeedingMoreContext) != 0) return Unknown;
        var function = rule.Function;
        double? value = null;
        if (function == GetCurrentTime)
            value = subject.HourOfDay;
        else if (function == GetDead)
            value = Bool(subject.Dead);
        else if (function == GetLevel)
            value = subject.Level;
        else if (function == GetIsID)
            value = param is { } id && subject.Base is { } self ? Bool(self == id) : null;
        else if (function == GetIsRace)
            value = param is { } race && subject.Race is { } own ? Bool(own == race) : null;
        else if (function == GetInFaction)
            value = param is { } faction && subject.Factions is { } factions ? Bool(factions.ContainsKey(faction)) : null;
        else if (function == GetFactionRank)
            value = param is { } ranked && subject.Factions is { } rankings ? (rankings.TryGetValue(ranked, out var rank) ? rank : -1) : null;
        else if (function == HasKeyword)
            value = param is { } keyword && subject.Keywords is { } keywords ? Bool(keywords.Contains(keyword)) : null;
        else if (function == GetGlobalValue)
            value = param is { } glob ? subject.Global(glob) : null;
        else if (function == GetStageDone)
            value = param is { } done && subject.QuestStageDone is { } isDone ? Bool(isDone(done, param2)) : null;
        else if (function == GetStage)
            value = param is { } quest && subject.QuestStage is { } stageOf ? stageOf(quest) : null;
        if (value is not { } known) return Unknown;
        double globalValue = 0;
        bool globalKnown = false;
        if ((rule.FlagsByte & FlagUseGlobal) != 0 && comparisonGlobal is { } comparison && subject.Global(comparison) is { } resolved)
        {
            globalKnown = true;
            globalValue = Narrow(resolved);
        }
        return new ConditionInput(true, Narrow(known), globalKnown, globalValue, rule.RunOn, rule.Reference);
    }

    private static double? Bool(bool? value) => value is { } v ? (v ? 1.0 : 0.0) : null;

    private static double Narrow(double value) => (double)(float)value;
}
