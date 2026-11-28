using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Lighting;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Audio;
using OpenCommonwealth.Services.Dialogue;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Ui.Dialogs;
using OpenCommonwealth.UI.Bootstrap;
using Radicle.Interop;

namespace OpenCommonwealth;

public sealed class SceneWorld : ISceneWorld
{
    private sealed record Pending(string Context, string Actor, double Delay, DialogueNode Node);

    private readonly GameSession _session;
    private readonly List<Pending> _pending = new();
    private readonly Dictionary<string, string> _playing = new();
    private RadicleDialogueRecords? _records;
    private object? _recordsEnv;

    public SceneWorld(GameSession session)
    {
        _session = session;
        Aliases = new QuestAliasResolver(quest => RadicleSceneSource.ReadAliasFills(GameLoader.Records.Environment, quest), LiveOf, () => GameLoader.HasLoadOrder ? GameLoader.Records.Environment : null);
        session.QuestStages.QuestReset += Aliases.ClearQuest;
    }

    public QuestAliasResolver Aliases { get; }

    public static bool SameForm(string? a, string? b) =>
        a is not null && b is not null && (a == b || GameLoader.TryParseKey(a, out var left) && GameLoader.TryParseKey(b, out var right) && left == right);

    private static string? LiveOf(AliasFillDef fill)
    {
        if (fill.Specific) return ActorRegistry.Find(fill.Target)?.RefKey;
        return ActorRegistry.Npcs().FirstOrDefault(npc => (SameForm(npc.NpcKey, fill.Target) || SameForm(npc.BaseKey, fill.Target)) && GodotObject.IsInstanceValid(npc.Node)
            && NpcPuppetSpawner.IsAlive(npc.Node) && NpcPuppetSpawner.IsEnabled(npc.Node))?.RefKey;
    }

    public string? ResolveActor(string quest, int alias) => Aliases.Resolve(quest, alias) is { } key && ActorRegistry.Find(key) is not null ? key : null;

    public SpeechResult Speak(string context, string topic, string? actor)
    {
        if (actor is null || ActorRegistry.Find(actor) is not { } identity) return new SpeechResult(SpeechStatus.ActorAbsent, 0, "no live actor for the alias");
        var records = GameLoader.Records;
        var env = records.Environment;
        string npcText = identity.Node.GetMeta(NpcPuppetSpawner.ResolvedMeta, identity.BaseKey).AsString();
        if (!GameLoader.TryParseKey(topic, out var topicKey) || !GameLoader.TryParseKey(npcText, out var npc) || env.Winner(topicKey) is not { } topicRecord)
            return new SpeechResult(SpeechStatus.NoLine, 0, $"topic {topic} or actor {npcText} does not resolve");
        var layer = Records(env);
        var topicData = env.DialogueTopic(topicRecord);
        var infos = env.TopicChildren(topicRecord);
        var why = new List<string>();
        foreach (var info in infos)
        {
            var decoded = env.DialogueInfo(info);
            var rows = decoded.Conditions;
            var conditions = rows.Count == 0 ? new List<InfoCondition>()
                : env.ConditionParameters(info, rows.Select(c => ConditionFunctions.ToRadicle(c)).ToArray()).Select((typed, i) => DialogueRules.ConditionFrom(rows[i], typed, _ => null, null, info)).ToList();
            var texts = decoded.Responses.Select(r => r.Text.Kind == Radicle.Interop.Native.NameKind.Inline ? r.Text.Text
                : records.Strings.ResolveDialogueText(info, RadicleDialogueRecords.ResponseTextSignature, r.Text) is { } bytes ? GameLoader.Cp1252.GetString(bytes) : null).ToList();
            var data = new DialogueInfoData(env.Info(info).FormKey, decoded, texts, null, conditions, topicData.Quest);
            if (!layer.Eligible(npc, data)) { why.Add($"{data.Key}: {layer.WhyNot(npc, data)}"); continue; }
            var spoken = Enumerable.Range(0, texts.Count).Where(i => !string.IsNullOrWhiteSpace(texts[i])).ToList();
            if (spoken.Count == 0) { why.Add($"{data.Key}: no response text"); continue; }
            return Play(context, identity, npc, data, spoken, layer.SpeakerName(npc), layer.VoiceType(npc));
        }
        return new SpeechResult(SpeechStatus.NoLine, 0, infos.Count == 0 ? "the topic has no INFO" : "no INFO is eligible: " + string.Join("; ", why));
    }

    private SpeechResult Play(string context, ActorIdentity identity, FormKey npc, DialogueInfoData data, List<int> spoken, string speaker, FormKey? voiceType)
    {
        var voice = NpcVoice.For(identity.Node);
        double at = 0;
        int missing = 0;
        foreach (var index in spoken)
        {
            var response = data.Info.Responses[index];
            var node = new DialogueNode(data.Key.ToString() + "." + response.Number, speaker, data.Texts[index]!, [], 0, true, true)
            {
                Voice = new VoiceRef(data.Key, response.Number, voiceType, false, data.Info.SharedInfo),
            };
            var voiced = voice.Prepare(node);
            bool clip = !ReferenceEquals(voiced, node);
            if (!clip) missing++;
            _pending.Add(new Pending(context, identity.RefKey, at, voiced));
            at += voiced.SpeakingMs / 1000.0;
        }
        var detail = $"info={data.Key} responses={spoken.Count} missingAudio={missing}";
        return new SpeechResult(missing == 0 ? SpeechStatus.Spoken : SpeechStatus.MissingAudio, at, detail);
    }

    public void Tick(double delta)
    {
        if (_pending.Count == 0) return;
        var fired = new HashSet<string>();
        for (int i = 0; i < _pending.Count; i++)
        {
            var pending = _pending[i] with { Delay = Math.Max(0, _pending[i].Delay - delta) };
            if (pending.Delay > 0 || !fired.Add(pending.Actor)) { _pending[i] = pending; continue; }
            _pending.RemoveAt(i--);
            try
            {
                if (ActorRegistry.Find(pending.Actor) is not { } identity || !GodotObject.IsInstanceValid(identity.Node)) continue;
                NpcVoice.For(identity.Node).Play(pending.Node);
                _playing[pending.Actor] = pending.Context;
                UiBootstrap.Instance?.Ui.Hud.Subtitles.Post(pending.Node.Line, pending.Node.SpeakerName, pending.Node.SpeakingMs / 1000.0);
            }
            catch (Exception error)
            {
                GD.PushError($"[Scene] line for {pending.Actor} failed: {error.Message}");
            }
        }
    }

    public void StopSpeech(string context, string actor)
    {
        _pending.RemoveAll(p => p.Actor == actor && p.Context == context);
        if (_playing.GetValueOrDefault(actor) != context) return;
        _playing.Remove(actor);
        StopVoice(actor);
    }

    public void ClearAll()
    {
        Aliases.ClearAll();
        Overrides.Clear();
        _pending.Clear();
        var playing = _playing.Keys.ToArray();
        _playing.Clear();
        foreach (var actor in playing) StopVoice(actor);
    }

    private static void StopVoice(string actor)
    {
        try
        {
            if (ActorRegistry.Find(actor) is { } identity && GodotObject.IsInstanceValid(identity.Node)) identity.Node.GetNodeOrNull<NpcVoice>(NpcVoice.NodeName)?.Stop();
        }
        catch (Exception error)
        {
            GD.PushError($"[Scene] stopping the voice of {actor} failed: {error.Message}");
        }
    }

    public PackageOverrideBook<ActivePackage> Overrides { get; } = new();

    // Requires: the actor has a PackageRunner; the override holds until EndPackage, ClearAll or a load clears it.
    public PackageResult BeginPackage(string context, string package, string? actor)
    {
        if (actor is null || ActorRegistry.Find(actor) is not { } identity || !GodotObject.IsInstanceValid(identity.Node) || identity.Node.GetNodeOrNull<PackageRunner>(PackageRunner.NodeName) is null)
            return new(false, "the actor has no PackageRunner");
        if (RadiclePackageSource.Instance.Forced(package) is not { } active) return new(false, $"package {package} is not a readable PACK record");
        Overrides.Begin(actor, package, active);
        return new(true, $"forced kind={active.Kind} procedure={active.Procedure}");
    }

    public void EndPackage(string package, string? actor)
    {
        if (actor is not null) Overrides.End(actor, package);
    }

    public bool SetStage(string quest, int stage) => _session.QuestStages.Apply(quest, QuestNative.SetStage, stage);

    private RadicleDialogueRecords Records(RadicleEnvironment env)
    {
        if (_records is null || !ReferenceEquals(_recordsEnv, env))
        {
            _records = new RadicleDialogueRecords(GameLoader.Records, () => EnvironmentController.Active?.Clock.HourOfDay ?? 12.0);
            _recordsEnv = env;
        }
        _records.Quests = SessionQuestState.From(_session);
        return _records;
    }
}
