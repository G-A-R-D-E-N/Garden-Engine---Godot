using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;
using RadicleConditionFunctions = Radicle.Interop.ConditionFunctions;

namespace OpenCommonwealth.Services.Ui.Workbench;

public interface IRecipeFacts
{
    double? Level { get; }

    bool? HasPerk(FormKey perk);

    bool? HasKeyword(FormKey keyword);

    double? Global(FormKey global);

    int ItemCount(FormKey item);
}

// Constraint: fails closed like LockGate; only Verdict True lists a recipe, unknown inputs never pass.
public static class RecipeConditionGate
{
    private const byte FlagUseGlobal = 0x04;
    private const byte FlagsNeedingMoreContext = 0x02 | 0x08 | 0x10;
    public const uint RunOnSubject = 0;
    public const uint RunOnTarget = 1;
    private const uint RunOnReference = 2;
    private const uint PlayerReference = 0x14;

    private static readonly ConditionInput Unknown = new(false, 0, false, 0, 0, 0);
    private static readonly ushort? GetItemCount = RadicleConditionFunctions.Id("GetItemCount");
    private static readonly ushort? HasPerk = RadicleConditionFunctions.Id("HasPerk");
    private static readonly ushort? GetLevel = RadicleConditionFunctions.Id("GetLevel");
    private static readonly ushort? HasKeyword = RadicleConditionFunctions.Id("HasKeyword");
    private static readonly ushort? GetGlobalValue = RadicleConditionFunctions.Id("GetGlobalValue");

    public static bool Passes(IReadOnlyList<RecordCondition>? conditions, IRecipeFacts? facts) =>
        Evaluate(conditions, facts) == RuleVerdict.True;

    public static RuleVerdict Evaluate(IReadOnlyList<RecordCondition>? conditions, IRecipeFacts? facts) =>
        conditions is null ? RuleVerdict.Unevaluable : Verdict(conditions, facts, RunOnSubject);

    // Requires: playerRunOn is the run-on value that addresses the player (Subject for a recipe, Target for a terminal).
    public static RuleVerdict Verdict(IReadOnlyList<RecordCondition> conditions, IRecipeFacts? facts, uint playerRunOn)
    {
        if (conditions.Count == 0) return RuleVerdict.True;
        if (facts is null) return RuleVerdict.Unevaluable;
        var rules = conditions.Select(c => new ConditionRule(c.Row.FlagsByte, c.Row.ComparisonRaw, c.Row.Function, c.Row.RunOn, c.Row.Reference)).ToArray();
        var values = conditions.Select(c => Value(c, facts, playerRunOn)).ToArray();
        try
        {
            return PackageRules.EvaluateConditions(rules, values).Verdict;
        }
        catch (RadicleException)
        {
            return RuleVerdict.Unevaluable;
        }
    }

    private static ConditionInput Value(RecordCondition c, IRecipeFacts facts, uint playerRunOn)
    {
        var row = c.Row;
        var onPlayer = row.RunOn == playerRunOn || (row.RunOn == RunOnReference && row.Reference == PlayerReference);
        if (!onPlayer || (row.FlagsByte & FlagsNeedingMoreContext) != 0) return Unknown;
        var function = row.Function;
        double? value = null;
        if (function == GetLevel) value = facts.Level;
        else if (function == HasPerk) value = c.Param1 is { } perk ? Bool(facts.HasPerk(perk)) : null;
        else if (function == HasKeyword) value = c.Param1 is { } keyword ? Bool(facts.HasKeyword(keyword)) : null;
        else if (function == GetItemCount) value = c.Param1 is { } item ? facts.ItemCount(item) : null;
        else if (function == GetGlobalValue) value = c.Param1 is { } global ? facts.Global(global) : null;
        if (value is not { } known) return Unknown;
        double globalValue = 0;
        var globalKnown = false;
        if ((row.FlagsByte & FlagUseGlobal) != 0)
        {
            if (c.Global is not { } comparison || facts.Global(comparison) is not { } resolved) return Unknown;
            globalKnown = true;
            globalValue = Narrow(resolved);
        }
        return new ConditionInput(true, Narrow(known), globalKnown, globalValue, row.RunOn, row.Reference);
    }

    private static double? Bool(bool? value) => value is { } v ? (v ? 1.0 : 0.0) : null;

    private static double Narrow(double value) => (double)(float)value;
}
