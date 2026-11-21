using System;
using System.Collections.Generic;
using System.Linq;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Dialogue;

public static class DialogueConditionValues
{
    public static ConditionInput Value(InfoCondition c, bool playerSpoken, ConditionSubject npc, Func<ConditionSubject> player, FormKey? npcVoice, IQuestState? quests)
    {
        if (ConditionFunctions.IsQuestFunction(c.Rule.Function))
            return DialogueRules.QuestInput(c.Rule, c.Param, c.Param2, c.ComparisonGlobal, quests, npc.Global);
        var target = DialogueRules.TargetOf(c.Rule.RunOn, c.ReferenceIsPlayer, playerSpoken);
        if (target == ConditionTarget.Unknown)
            return PackageConditionValues.Unknown;
        var rule = DialogueRules.AsSubject(c.Rule);
        var subject = target == ConditionTarget.Npc ? npc : player();
        ConditionInput One(FormKey? param) => c.Rule.Function == ConditionFunctions.GetIsVoiceType
            ? (target == ConditionTarget.Npc ? DialogueRules.VoiceTypeInput(rule, param, npcVoice) : PackageConditionValues.Unknown)
            : PackageConditionValues.Value(rule, param, c.ComparisonGlobal, subject);
        var value = c.Members is { } members ? DialogueRules.AnyOf(members.Select(m => One(m)).ToList()) : One(c.Param);
        return value.Known ? value with { ResolvedRunOn = c.Rule.RunOn, ResolvedReference = c.Rule.Reference } : value;
    }

    // Invariant: an info with no conditions is eligible; any condition that cannot be decided makes the whole info not eligible and reports the first such function.
    public static (bool Eligible, ushort? Unevaluable) Verdict(IReadOnlyList<InfoCondition> conditions, bool playerSpoken, ConditionSubject npc, Func<ConditionSubject> player, FormKey? npcVoice, IQuestState? quests)
    {
        if (conditions.Count == 0)
            return (true, null);
        var outcome = PackageRules.EvaluateConditions(conditions.Select(c => c.Rule).ToArray(), conditions.Select(c => Value(c, playerSpoken, npc, player, npcVoice, quests)).ToArray());
        return (outcome.Verdict == RuleVerdict.True, outcome.Verdict == RuleVerdict.Unevaluable ? (ushort?)outcome.FirstUnevaluableFunction : null);
    }
}
