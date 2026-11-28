using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Radicle.Interop;
using AliasKind = Radicle.Interop.Native.QuestAliasKind;
using FillType = Radicle.Interop.Native.QuestFillType;

namespace OpenCommonwealth.Services.Papyrus.Scenes;

public static class RadicleSceneSource
{
    private const uint LoopingFlag = 1u << 16;
    private const uint IgnoreForCompletionFlag = 1u << 19;
    private const ushort ActionDialogue = (ushort)SceneActionType.Dialogue;
    private const ushort ActionPackage = (ushort)SceneActionType.Package;
    private const ushort ActionTimer = (ushort)SceneActionType.Timer;
    private const ushort ActionPlayerDialogue = (ushort)SceneActionType.PlayerDialogue;
    private const uint SubtypeFlags = 1u << 7 | 1u << 8 | 1u << 9 | 1u << 10 | 1u << 11 | 1u << 27 | 1u << 28 | 1u << 29 | 1u << 30;

    public static bool TryParseKey(string key, out FormKey form)
    {
        form = default;
        var split = key.IndexOf(':');
        if (split <= 0 || split == key.Length - 1 || !uint.TryParse(key.AsSpan(0, split), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var local))
            return false;
        form = new FormKey(key[(split + 1)..], local);
        return true;
    }

    public static SceneDef? Read(RadicleEnvironment env, string sceneKey)
    {
        if (!TryParseKey(sceneKey, out var form) || env.Winner(form) is not { } record || env.Info(record).Signature != "SCEN") return null;
        var scene = env.Scene(record);
        var quest = scene.ParentQuest?.ToString() ?? "";
        var actions = new List<SceneActionDef>();
        var refusals = new List<string>();
        var hostRefusals = new List<string>();
        foreach (var action in scene.Actions)
            if (ReadAction(action, refusals, hostRefusals) is { } def) actions.Add(def);
        var phases = scene.Phases.Select((phase, index) => new ScenePhaseDef(index, phase.Name ?? "", Stage(phase.QuestStages?.Start), Stage(phase.QuestStages?.Completion))).ToList();
        string? condition = null;
        var phaseOnly = false;
        if (scene.Conditions.Count > 0) condition = $"the scene has {scene.Conditions.Count} start condition(s) and Garden cannot evaluate them here; ForceStart skips them";
        else if (scene.Phases.Any(phase => phase.StartConditions.Count + phase.CompletionConditions.Count > 0))
        {
            condition = "a phase has conditions and Garden cannot evaluate them here; ForceStart skips them";
            phaseOnly = scene.Phases.All(phase => phase.CompletionConditions.Count == 0);
        }
        return new SceneDef(sceneKey, quest, phases, actions, Stage(scene.QuestStages?.OnBegin), Stage(scene.QuestStages?.OnEnd),
            refusals.Count == 0 ? null : string.Join("; ", refusals), condition, hostRefusals.Count == 0 ? null : string.Join("; ", hostRefusals), phaseOnly);
    }

    internal static SceneActionDef? ReadAction(SceneAction action, List<string> refusals, List<string>? hostRefusals = null)
    {
        hostRefusals ??= new List<string>();
        var typeName = RadicleEnvironment.SceneActionTypeName(action.Type);
        var name = $"{typeName}#{action.Index}";
        var flags = action.Flags ?? 0;
        var kind = action.Type switch { ActionDialogue => SceneActionKind.Dialogue, ActionPackage => SceneActionKind.Package, ActionTimer => SceneActionKind.Timer, ActionPlayerDialogue => SceneActionKind.PlayerDialogue, _ => (SceneActionKind?)null };
        if (kind is null) { refusals.Add($"action {name} is a {typeName} action, which Garden does not run"); return null; }
        if ((flags & LoopingFlag) != 0) { refusals.Add($"action {name} loops, which Garden does not run"); return null; }
        var ignore = (flags & IgnoreForCompletionFlag) != 0;
        var start = (int)(action.StartPhase ?? 0);
        var end = (int)(action.EndPhase ?? action.StartPhase ?? 0);
        if (kind == SceneActionKind.PlayerDialogue)
        {
            if (action.Target is not PlayerDialogueActionTarget { Malformed: false }) { refusals.Add($"action {name} has an unreadable player dialogue body"); return null; }
            if ((flags & SubtypeFlags) != 0) { refusals.Add($"action {name} takes its lines from a dialogue subtype, which Garden does not run"); return null; }
            if (ignore || end != start) { refusals.Add($"action {name} is ignored for completion or spans phases, which Garden does not run for player dialogue"); return null; }
            hostRefusals.Add($"action {name} is a player dialogue action, which only the dialogue menu runs");
            return new SceneActionDef(name, kind.Value, action.ActorAlias ?? -1, start, end, false, null, 0);
        }
        string? target = null;
        var seconds = 0.0;
        if (kind == SceneActionKind.Timer)
        {
            if (action.Target is TimerActionTarget { QuestStage: not null }) { refusals.Add($"timer action {name} sets a quest stage, which Garden does not run"); return null; }
            if (action.Target is TimerActionTarget { Malformed: true }) { refusals.Add($"timer action {name} is malformed, so Garden cannot tell whether it sets a quest stage"); return null; }
            if (action.Target is TimerActionTarget { MaxSeconds: { } max }) seconds = max;
        }
        else if (action.Target is DialogueActionTarget dialogue)
            target = dialogue.Topic?.ToString();
        else if (action.Target is PackageActionTarget { Packages.Count: > 0 } package)
            target = package.Packages[0].ToString();
        if (action.Target is PackageActionTarget { Malformed: true }) { refusals.Add($"package action {name} is malformed, which Garden does not run"); return null; }
        if (kind == SceneActionKind.Package && (action.Target is PackageActionTarget { Packages.Count: > 1 } || action.Payload.Count(entry => entry.Signature == "PNAM") > 1)) { refusals.Add($"package action {name} has several packages, which Garden does not run"); return null; }
        if (kind != SceneActionKind.Timer && target is null) { refusals.Add($"action {name} has no {(kind == SceneActionKind.Dialogue ? "topic" : "package")}"); return null; }
        if (kind == SceneActionKind.Package && !ignore)
        {
            if (end == start) { refusals.Add($"package action {name} must complete the phase, and Garden has no package-finished signal"); return null; }
            hostRefusals.Add($"package action {name} spans phases without ignoring completion, and only the dialogue menu decides when its phases end");
        }
        return new SceneActionDef(name, kind.Value, action.ActorAlias ?? -1, start, end, ignore, target, seconds);
    }

    public static IReadOnlyList<AliasFillDef> ReadAliasFills(RadicleEnvironment env, string questKey)
    {
        if (!TryParseKey(questKey, out var form) || QuestStructure.ReadEnv(env, form) is not { } read) return Array.Empty<AliasFillDef>();
        return read.Structure.Aliases
            .Where(alias => alias.Kind == AliasKind.Reference && alias.Id is not null && alias.Fill.Supported && alias.Fill.Target is not null
                && alias.Fill.Type is FillType.SpecificReference or FillType.UniqueActor)
            .Select(alias => new AliasFillDef(questKey, (int)alias.Id!.Value, alias.Fill.Type == FillType.SpecificReference, alias.Fill.Target!.Value.ToString()))
            .ToList();
    }

    private static int? Stage(short? stage) => stage is >= 0 ? stage : null;
}
