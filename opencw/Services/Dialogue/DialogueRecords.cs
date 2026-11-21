using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Radicle.Interop;
using static OpenCommonwealth.ConditionFunctions;
using QuestState = OpenCommonwealth.Services.Papyrus.Quest.QuestState;

namespace OpenCommonwealth.Services.Dialogue;

public sealed record InfoCondition(ConditionRule Rule, FormKey? Param, FormKey? ComparisonGlobal, int Param2 = 0, bool ReferenceIsPlayer = false,
    IReadOnlyList<FormKey>? Members = null);

public enum ConditionTarget
{
    Unknown,
    Npc,
    Player,
}

public interface IQuestState
{
    QuestState StateOf(FormKey quest);

    int StageOf(FormKey quest);

    bool StageDone(FormKey quest, int stage);
}

public sealed record DialogueInfoData(FormKey Key, DialogueInfo Info, IReadOnlyList<string?> Texts, string? Prompt,
    IReadOnlyList<InfoCondition> Conditions, FormKey? Quest = null, bool PlayerSpoken = false);

public sealed record DialogueTopicData(FormKey Key, string? Name, byte Category, string Subtype, float Priority,
    IReadOnlyList<DialogueInfoData> Infos);

public interface IDialogueRecords
{
    string SpeakerName(FormKey npc);

    FormKey? VoiceType(FormKey npc);

    IReadOnlyList<DialogueTopicData> Topics(FormKey npc);

    bool Eligible(FormKey npc, DialogueInfoData info);
}

public static partial class DialogueRules
{
    public static bool AddressesBy(ushort function) => function == GetIsID || function == GetIsRace || function == GetIsVoiceType;

    public static IEnumerable<FormKey> OwnersOf(FormKey? speaker, IEnumerable<InfoCondition> conditions)
    {
        var owners = new HashSet<FormKey>();
        if (speaker is { } own)
            owners.Add(own);
        foreach (var c in conditions.Where(c => AddressesBy(c.Rule.Function)))
        {
            if (c.Members is { } members)
                owners.UnionWith(members);
            else if (c.Param is { } param)
                owners.Add(param);
        }
        return owners;
    }

    public static InfoCondition ConditionFrom(DialogueCondition c, RadicleTypedCondition typed,
        Func<FormKey, IReadOnlyList<FormKey>?> listMembers, FormKey? playerRef)
    {
        var rule = new ConditionRule(c.FlagsByte, c.ComparisonRaw, c.Function, c.RunOn, c.Reference);
        var param = FormOf(typed.Param1);
        var comparison = FormOf(typed.Comparison);
        var members = param is { } listed && AddressesBy(c.Function) ? listMembers(listed) : null;
        bool referenceIsPlayer = c.RunOn == 2 && FormOf(typed.Reference) is { } reference && playerRef is { } player && reference == player;
        return new InfoCondition(rule, param, comparison, c.Param2, referenceIsPlayer, members);
    }

    public static bool Admits(FormKey npc, DialogueInfoData info, IQuestState? quests) =>
        (info.Info.Speaker is not { } speaker || speaker == npc) && QuestAllows(quests, info.Quest);

    public static ConditionInput AnyOf(IReadOnlyList<ConditionInput> values)
    {
        var unknown = new ConditionInput(false, 0, false, 0, 0, 0);
        if (values.Count == 0)
            return unknown;
        if (values.Any(v => v.Known && v.Value != 0))
            return values.First(v => v.Known && v.Value != 0);
        return values.All(v => v.Known) ? values[0] with { Value = 0 } : unknown;
    }

    public static bool QuestAllows(IQuestState? state, FormKey? quest) =>
        state is null || quest is null || state.StateOf(quest.Value) == QuestState.Running;

    public static ConditionInput QuestInput(ConditionRule rule, FormKey? quest, int stage, FormKey? comparisonGlobal, IQuestState? state, Func<FormKey, double?> global)
    {
        var unknown = new ConditionInput(false, 0, false, 0, 0, 0);
        if ((rule.FlagsByte & FlagsNeedingMoreContext) != 0 || quest is not { } key || state is null)
            return unknown;
        var function = rule.Function;
        double? value = null;
        if (function == GetQuestRunning)
            value = state.StateOf(key) == QuestState.Running ? 1.0 : 0.0;
        else if (function == GetQuestCompleted)
            value = state.StateOf(key) == QuestState.Completed ? 1.0 : 0.0;
        else if (function == GetStage)
            value = state.StageOf(key);
        else if (function == GetStageDone)
            value = state.StageDone(key, stage) ? 1.0 : 0.0;
        if (value is not { } known)
            return unknown;
        bool globalKnown = false;
        double globalValue = 0;
        if ((rule.FlagsByte & FlagUseGlobal) != 0 && comparisonGlobal is { } comparison && global(comparison) is { } resolved)
        {
            globalKnown = true;
            globalValue = (double)(float)resolved;
        }
        return new ConditionInput(true, known, globalKnown, globalValue, rule.RunOn, rule.Reference);
    }

    public static ConditionTarget TargetOf(uint runOn, bool referenceIsPlayer, bool playerSpoken) => runOn switch
    {
        0 => playerSpoken ? ConditionTarget.Player : ConditionTarget.Npc,
        1 => playerSpoken ? ConditionTarget.Npc : ConditionTarget.Player,
        2 => referenceIsPlayer ? ConditionTarget.Player : ConditionTarget.Unknown,
        _ => ConditionTarget.Unknown,
    };

    public static ConditionRule AsSubject(ConditionRule rule) => rule with { RunOn = 0 };

    public static ConditionInput VoiceTypeInput(ConditionRule rule, FormKey? param, FormKey? npcVoice)
    {
        bool plain = rule.RunOn == 0 && (rule.FlagsByte & (FlagsNeedingMoreContext | FlagUseGlobal)) == 0;
        return plain && param is { } wanted && npcVoice is { } own
            ? new ConditionInput(true, wanted == own ? 1.0 : 0.0, false, 0, rule.RunOn, rule.Reference)
            : new ConditionInput(false, 0, false, 0, 0, 0);
    }

    [GeneratedRegex("[a-z][A-Z]")]
    private static partial Regex LowerThenUpper();

    public static string? MenuName(string? name, string? editorId)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("***", StringComparison.Ordinal))
            return null;
        if (editorId is { Length: > 0 } && string.Equals(name, editorId, StringComparison.OrdinalIgnoreCase))
            return null;
        return !name.Any(char.IsWhiteSpace) && (name.Contains('_') || name.Any(char.IsDigit) || LowerThenUpper().IsMatch(name)) ? null : name;
    }

    public static (IReadOnlyList<InfoCondition> Conditions, FormKey? Speaker) WithGroupHead(
        IReadOnlyList<InfoCondition> own, FormKey? speaker, IReadOnlyList<InfoCondition>? headConditions, FormKey? headSpeaker) =>
        headConditions is null ? (own, speaker) : (headConditions.Concat(own).ToList(), speaker ?? headSpeaker);
}
