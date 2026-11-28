using System;
using System.Collections.Concurrent;
using Radicle.Interop;

namespace OpenCommonwealth;

// Constraint: ids and function facts come from Radicle's condition table; tests/OpenCw.Ui.Tests and tests/OpenCw.Papyrus.Tests link this file next to DialogueRecords.cs.
public static class ConditionFunctions
{
    public const byte FlagUseGlobal = 0x04;
    public const byte FlagsNeedingMoreContext = 0x02 | 0x08 | 0x10;

    public static readonly ushort GetCurrentTime = Id(nameof(GetCurrentTime));
    public static readonly ushort GetDead = Id(nameof(GetDead));
    public static readonly ushort GetQuestRunning = Id(nameof(GetQuestRunning));
    public static readonly ushort GetStage = Id(nameof(GetStage));
    public static readonly ushort GetStageDone = Id(nameof(GetStageDone));
    public static readonly ushort GetIsRace = Id(nameof(GetIsRace));
    public static readonly ushort GetInFaction = Id(nameof(GetInFaction));
    public static readonly ushort GetIsID = Id(nameof(GetIsID));
    public static readonly ushort GetFactionRank = Id(nameof(GetFactionRank));
    public static readonly ushort GetGlobalValue = Id(nameof(GetGlobalValue));
    public static readonly ushort GetLevel = Id(nameof(GetLevel));
    public static readonly ushort GetIsVoiceType = Id(nameof(GetIsVoiceType));
    public static readonly ushort GetQuestCompleted = Id(nameof(GetQuestCompleted));
    public static readonly ushort HasKeyword = Id(nameof(HasKeyword));
    public static readonly ushort GetValue = Id(nameof(GetValue));
    public static readonly ushort GetIsAliasRef = Id(nameof(GetIsAliasRef));
    public static readonly ushort GetIsSex = Id(nameof(GetIsSex));
    public static readonly ushort GetItemCount = Id(nameof(GetItemCount));

    private static readonly ConcurrentDictionary<ushort, ConditionFunctionInfo?> Infos = new();

    public static bool IsQuestFunction(ushort function) => Info(function)?.FirstParamIsQuest ?? false;

    // Constraint: a slot must yield a key only when the typed export reports Form; an alias or package-data switched slot comes back Index and has none.
    public static FormKey? FormOf(RadicleConditionParam param) =>
        param.Kind == Radicle.Interop.Native.ConditionParamKind.Form ? param.Form : null;

    public static RadicleCondition ToRadicle(DialogueCondition c) => new(c.FlagsByte, c.ComparisonRaw, c.Function, c.Unknown2,
        c.Param1, c.Param2, c.RunOn, c.Reference, c.Unknown3, c.Parameter1String, c.Parameter2String);

    public static RadicleCondition ToRadicle(BehaviorCondition c) => new(c.FlagsByte, c.ComparisonRaw, c.Function, c.Unknown2,
        c.Param1, c.Param2, c.RunOn, c.Reference, c.Unknown3, c.Parameter1String, c.Parameter2String);

    private static ushort Id(string name) =>
        Radicle.Interop.ConditionFunctions.Id(name)
        ?? throw new InvalidOperationException($"Radicle's condition table has no {name}.");

    private static ConditionFunctionInfo? Info(ushort function) =>
        Infos.GetOrAdd(function, f => Radicle.Interop.ConditionFunctions.Info(f));
}
