using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Papyrus.Vmad;
using Radicle.Interop;
using static OpenCommonwealth.ConditionFunctions;
using QuestState = OpenCommonwealth.Services.Papyrus.Quest.QuestState;

namespace OpenCommonwealth.Services.Dialogue;

public sealed record InfoCondition(ConditionRule Rule, FormKey? Param, FormKey? ComparisonGlobal, int Param2 = 0, bool ReferenceIsPlayer = false,
    IReadOnlyList<FormKey>? Members = null, int RawParam1 = 0, Radicle.Interop.Record? Source = null, RadicleCondition? Row = null);

public sealed record InfoResult(int? StageOnBegin, int? StageOnEnd, bool HasVmad, string? StartScenePhase = null, InfoVmadParse? Vmad = null)
{
    public static readonly InfoResult None = new(null, null, false);

    public bool Any => StageOnBegin is not null || StageOnEnd is not null || HasVmad;
}

public enum ConditionTarget
{
    Unknown,
    Npc,
    Player,
}

public sealed record DialogueInfoData(FormKey Key, DialogueInfo Info, IReadOnlyList<string?> Texts, string? Prompt,
    IReadOnlyList<InfoCondition> Conditions, FormKey? Quest = null, bool PlayerSpoken = false, InfoResult? Result = null);

public sealed record DialogueTopicData(FormKey Key, string? Name, byte Category, string Subtype, float Priority,
    IReadOnlyList<DialogueInfoData> Infos);

public enum SceneBranch
{
    Positive,
    Negative,
    Neutral,
    Question,
}

public sealed record SceneOption(SceneBranch Branch, DialogueTopicData Player, DialogueTopicData Reply);

public sealed record ScenePhaseData(string? Name, IReadOnlyList<InfoCondition> StartConditions, IReadOnlyList<SceneOption> Options, DialogueTopicData? NpcTopic);

public sealed record SceneData(FormKey Key, IReadOnlyList<ScenePhaseData> Phases);

public interface IDialogueRecords
{
    string SpeakerName(FormKey npc);

    FormKey? VoiceType(FormKey npc);

    IReadOnlyList<DialogueTopicData> Topics(FormKey npc);

    bool Eligible(FormKey npc, DialogueInfoData info);

    string? WhyNot(FormKey npc, DialogueInfoData info) => null;

    string? ResultActions(DialogueInfoData info) => null;

    bool Eligible(FormKey npc, FormKey? reference, DialogueInfoData info) => Eligible(npc, info);

    string? WhyNot(FormKey npc, FormKey? reference, DialogueInfoData info) => WhyNot(npc, info);

    SceneData? SceneOf(FormKey scene) => null;

    bool PhaseOpen(FormKey npc, FormKey? reference, SceneData scene, ScenePhaseData phase) => true;
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

    public static bool KeepsOwnerless(FormKey? speaker, IReadOnlyList<InfoCondition> conditions, bool hasQuest) =>
        hasQuest && !OwnersOf(speaker, conditions).Any() && conditions.Any(c => c.Rule.Function == GetIsAliasRef);

    public static InfoCondition ConditionFrom(DialogueCondition c, RadicleTypedCondition typed,
        Func<FormKey, IReadOnlyList<FormKey>?> listMembers, FormKey? playerRef, Radicle.Interop.Record? source = null)
    {
        var rule = new ConditionRule(c.FlagsByte, c.ComparisonRaw, c.Function, c.RunOn, c.Reference);
        var param = FormOf(typed.Param1);
        var comparison = FormOf(typed.Comparison);
        var members = param is { } listed && AddressesBy(c.Function) ? listMembers(listed) : null;
        bool referenceIsPlayer = (ConditionRunOn)c.RunOn == ConditionRunOn.Reference && FormOf(typed.Reference) is { } reference && playerRef is { } player && reference == player;
        return new InfoCondition(rule, param, comparison, c.Param2, referenceIsPlayer, members, c.Param1, source, source is null ? null : ToRadicle(c));
    }

    public static bool Admits(FormKey npc, DialogueInfoData info, IQuestState? quests) =>
        (info.Info.Speaker is not { } speaker || speaker == npc) && QuestAllows(quests, info.Quest);

    public static bool QuestAllows(IQuestState? state, FormKey? quest) =>
        state is null || quest is null || state.StateOf(quest.Value) == QuestState.Running;

    public static ConditionTarget TargetOf(uint runOn, bool referenceIsPlayer, bool playerSpoken) => (ConditionRunOn)runOn switch
    {
        ConditionRunOn.Subject => playerSpoken ? ConditionTarget.Player : ConditionTarget.Npc,
        ConditionRunOn.Target => playerSpoken ? ConditionTarget.Npc : ConditionTarget.Player,
        ConditionRunOn.Reference => referenceIsPlayer ? ConditionTarget.Player : ConditionTarget.Unknown,
        _ => ConditionTarget.Unknown,
    };

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
