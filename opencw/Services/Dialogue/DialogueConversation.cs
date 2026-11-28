using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Dialogs;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Dialogue;

public interface ISceneStarter
{
    EffectOutcome Start(FormKey scene);

    bool RunsScene(FormKey scene) => false;

    EffectOutcome StartHosted(FormKey scene) => Start(scene);

    void Phase(FormKey scene, int phase)
    {
    }

    void End(FormKey scene)
    {
    }

    void Cancel(FormKey scene)
    {
    }
}

public sealed class DialogueConversation
{
    public const int ExitSlot = 1;
    public const int TradeSlot = 3;
    public const byte PlayerCategory = 0;
    public const byte MiscCategory = 7;
    public const ushort EndRunningSceneFlag = 0x40;
    public const string GreetingCode = "GREE";
    public const string HelloCode = "HELO";
    public const string PlayerSpeakerName = "Player";
    public const string BarterText = "Barter";
    public const string CommandText = "Give orders";

    private static readonly int[] TopicSlots = [0, 2, 3];

    private readonly FormKey _npc;
    private readonly IDialogueRecords _records;
    private readonly bool _offersTrade;
    private readonly bool _offersCommand;
    private readonly string _speaker;
    private readonly FormKey? _voiceType;
    private List<DialogueTopicData> _topics;
    private readonly HashSet<FormKey> _said = new();
    private readonly Dictionary<string, Func<int, DialogueNode?>> _next = new();
    private readonly IDialogueEffects? _effects;
    private readonly ISceneStarter? _scenes;
    private readonly FormKey? _reference;
    private readonly HashSet<string> _ran = new();
    private readonly HashSet<FormKey> _hosted = new();
    private readonly Dictionary<string, Action> _hostFinished = new();
    private readonly Dictionary<string, DialogueInfoData> _pendingBegin = new();
    private readonly Dictionary<string, (DialogueInfoData Info, MenuHolder Holder)> _pendingEnd = new();
    private int _serial;

    private sealed record Option(DialogueTopicData Topic, DialogueInfoData Info, string Text);

    private sealed record Menu(IReadOnlyList<DialogueChoice> Choices, Func<int, DialogueNode?> Pick, (FormKey Scene, int Phase)? Enters = null, FormKey? EndsScene = null);

    private sealed class MenuHolder(Func<Menu> factory)
    {
        public Menu Current { get; private set; } = factory();

        public void Refresh() => Current = factory();
    }

    public DialogueConversation(FormKey npc, IDialogueRecords records, Func<bool>? offersTrade = null, Func<bool>? offersCommand = null,
        IDialogueEffects? effects = null, ISceneStarter? scenes = null, FormKey? reference = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        _npc = npc;
        _effects = effects;
        _scenes = scenes;
        _reference = reference;
        _records = records;
        _offersTrade = offersTrade?.Invoke() ?? false;
        _offersCommand = offersCommand?.Invoke() ?? false;
        _speaker = records.SpeakerName(npc);
        _voiceType = records.VoiceType(npc);
        _topics = records.Topics(npc).ToList();
        Trace?.Invoke($"npc={npc} speaker='{_speaker}' voiceType={(_voiceType is { } voice ? voice.ToString() : "none")} indexedTopics={_topics.Count} offersTrade={_offersTrade} offersCommand={_offersCommand}");
        First = Opening();
    }

    public DialogueNode First { get; }

    public List<(FormKey Info, EffectOutcome Outcome)> EffectLog { get; } = new();

    public event Action<FormKey, EffectOutcome>? EffectReported;

    public DialogueNode? Next(string nodeId, int slot)
    {
        NodeFinished(nodeId);
        return _next.TryGetValue(nodeId, out var pick) ? pick(slot) : null;
    }

    // Invariant: an INFO's on-begin result runs once, when its first node is shown, never while the conversation is only being built.
    public void NodeStarted(string nodeId)
    {
        if (!_pendingBegin.Remove(nodeId, out var info))
            return;
        Run(info, false);
        if (info.Info.StartScene is { } sceneKey && _ran.Add($"scene:{sceneKey}"))
            Log(info.Key, StartScene(sceneKey));
    }

    private EffectOutcome StartScene(FormKey scene)
    {
        if (_scenes is null)
            return new EffectOutcome("scene-start", EffectStatus.Refused, $"no scene starter is wired, so scene {scene} was not started");
        try
        {
            bool hosted = !_scenes.RunsScene(scene) && _records.SceneOf(scene) is not null;
            var outcome = hosted ? _scenes.StartHosted(scene) : _scenes.Start(scene);
            if (hosted && outcome.Status == EffectStatus.Applied)
                _hosted.Add(scene);
            return outcome;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Failed("scene-start", ex);
        }
    }

    private static EffectOutcome Failed(string effect, Exception ex) => new(effect, EffectStatus.Failed, $"{ex.GetType().Name}: {ex.Message}");

    // Invariant: an INFO's on-end result runs once, when its last node finishes or the player picks from it, never when the dialogue is cancelled before that node finished.
    public void NodeFinished(string nodeId)
    {
        if (_pendingEnd.Remove(nodeId, out var pending))
        {
            Run(pending.Info, true);
            try
            {
                pending.Holder.Refresh();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log(pending.Info.Key, Failed("menu-rebuild", ex));
            }
        }
        if (_hostFinished.Remove(nodeId, out var host))
            host();
    }

    // Invariant: a scene this conversation started is entered phase by phase and ended from node finish, never from a menu factory, which also runs while the conversation is built.
    private void Hosting(Menu menu, FormKey info)
    {
        if (_scenes is null)
            return;
        try
        {
            if (menu.Enters is { } enters && _hosted.Contains(enters.Scene))
                _scenes.Phase(enters.Scene, enters.Phase);
            if (menu.EndsScene is { } ended && _hosted.Remove(ended))
                _scenes.End(ended);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log(info, Failed("scene-host", ex));
        }
    }

    public void Close()
    {
        if (_scenes is null)
            return;
        foreach (var scene in _hosted.ToArray())
        {
            _hosted.Remove(scene);
            try
            {
                _scenes.Cancel(scene);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log(_npc, Failed("scene-cancel", ex));
            }
        }
    }

    private void Run(DialogueInfoData info, bool end)
    {
        if (_effects is null || info.Result is not { Any: true } || !_ran.Add($"{info.Key}:{(end ? "end" : "begin")}"))
            return;
        IReadOnlyList<EffectOutcome> outcomes;
        try
        {
            outcomes = _effects.Run(info, end);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            outcomes = [Failed(end ? "info-end" : "info-begin", ex)];
        }
        foreach (var outcome in outcomes)
            Log(info.Key, outcome);
    }

    private void Log(FormKey info, EffectOutcome outcome)
    {
        EffectLog.Add((info, outcome));
        Trace?.Invoke($"effect info={info} {outcome}");
        try
        {
            EffectReported?.Invoke(info, outcome);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Trace?.Invoke($"effect sink threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool Ok(DialogueInfoData info) => _records.Eligible(_npc, _reference, info);

    public static IReadOnlyList<DialogueInfoData> InChainOrder(IReadOnlyList<DialogueInfoData> infos)
    {
        var inTopic = infos.Select(i => i.Key).ToHashSet();
        var placed = new HashSet<FormKey>();
        var result = new List<DialogueInfoData>(infos.Count);
        var rest = infos.ToList();
        while (rest.Count > 0)
        {
            var ready = rest.Where(i => i.Info.PreviousInfo is not { } prev || !inTopic.Contains(prev) || placed.Contains(prev)).ToList();
            if (ready.Count == 0)
                ready = rest.ToList();
            foreach (var info in ready)
            {
                placed.Add(info.Key);
                result.Add(info);
                rest.Remove(info);
            }
        }
        return result;
    }

    private DialogueNode Opening()
    {
        var greeting = Greeting(GreetingCode) ?? Greeting(HelloCode);
        return greeting is null ? Silent(Hub()) : Speak(greeting, Hub);
    }

    private DialogueInfoData? Greeting(string code) =>
        Ranked(t => t.Category == MiscCategory && t.Subtype == code).Select(t => FirstSpoken(t)).FirstOrDefault(i => i is not null);

    private IEnumerable<DialogueTopicData> Ranked(Func<DialogueTopicData, bool> keep) =>
        _topics.Where(keep).Select((t, i) => (t, i)).OrderByDescending(x => x.t.Priority).ThenBy(x => x.i).Select(x => x.t);

    public static Action<string>? Trace { get; set; }

    private DialogueInfoData? FirstSpoken(DialogueTopicData topic)
    {
        DialogueInfoData? found = null;
        int rejected = 0;
        var samples = new List<string>();
        foreach (var info in InChainOrder(topic.Infos))
        {
            if (Spoken(info).Count == 0)
                continue;
            if (Ok(info))
            {
                found = info;
                break;
            }
            rejected++;
            if (Trace is not null && samples.Count < 12)
                samples.Add($"{info.Key}: {_records.WhyNot(_npc, _reference, info)}");
        }
        Trace?.Invoke($"topic {topic.Key} cat={topic.Category} sub={topic.Subtype} name='{topic.Name}' infos={topic.Infos.Count} eligible={(found is null ? "none" : found.Key.ToString())} rejected={rejected}{(samples.Count == 0 ? "" : " rejections=[" + string.Join("; ", samples) + "]")}");
        return found;
    }

    private static IReadOnlyList<int> Spoken(DialogueInfoData info) =>
        Enumerable.Range(0, Math.Min(info.Info.Responses.Count, info.Texts.Count)).Where(i => !string.IsNullOrWhiteSpace(info.Texts[i])).ToList();

    private Menu Hub()
    {
        _topics = _records.Topics(_npc).ToList();
        bool trade = _offersTrade, command = _offersCommand;
        int extras = (trade ? 1 : 0) + (command ? 1 : 0);
        var options = Ranked(t => t.Category == PlayerCategory)
            .Select(t => (Topic: t, Info: FirstSpoken(t)))
            .Select(x => (x.Topic, x.Info, Text: string.IsNullOrWhiteSpace(x.Info?.Prompt) ? x.Topic.Name : x.Info!.Prompt))
            .Where(x => x.Info is not null && !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => new Option(x.Topic, x.Info!, x.Text!))
            .OrderBy(o => _said.Contains(o.Topic.Key))
            .Take(TopicSlots.Length - extras)
            .ToList();
        var choices = options.Select((o, i) => new DialogueChoice(o.Text, Used: _said.Contains(o.Topic.Key), Slot: TopicSlots[i])).ToList();
        if (command)
            choices.Add(new DialogueChoice(CommandText, Kind: DialogueChoiceKind.Command, Slot: TopicSlots[^extras]));
        if (trade)
            choices.Add(new DialogueChoice(BarterText, Kind: DialogueChoiceKind.Trade, Slot: TradeSlot));
        choices.Add(new DialogueChoice("Goodbye", Kind: DialogueChoiceKind.Exit, Slot: ExitSlot));
        return new Menu(choices, slot =>
        {
            int index = Array.IndexOf(TopicSlots, slot);
            if (index < 0 || index >= options.Count)
                return null;
            _said.Add(options[index].Topic.Key);
            return Speak(options[index].Info, Hub);
        });
    }

    private DialogueNode Silent(Menu menu)
    {
        string id = NextId("hub");
        _next[id] = menu.Pick;
        if (_scenes is not null && (menu.Enters is not null || menu.EndsScene is not null))
            _hostFinished[id] = () => Hosting(menu, _npc);
        return new DialogueNode(id, _speaker, "", menu.Choices, 0, true);
    }

    private DialogueNode Speak(DialogueInfoData info, Func<Menu> after)
    {
        var spoken = Spoken(info);
        Trace?.Invoke($"say info={info.Key} quest={info.Quest} startScene={info.Info.StartScene} lines={spoken.Count} result={info.Result}");
        Func<Menu> factory = info.Info.StartScene is { } sceneKey && !EndsRunningScene(info) && !RunsItself(sceneKey) ? () => SceneMenu(info, sceneKey) ?? after() with { EndsScene = sceneKey } : after;
        var holder = new MenuHolder(factory);
        string speaker = info.PlayerSpoken ? PlayerSpeakerName : _speaker;
        var voiceType = info.PlayerSpoken ? null : _voiceType;
        var nodes = new List<DialogueNode>(spoken.Count);
        for (int n = 0; n < spoken.Count; n++)
        {
            var response = info.Info.Responses[spoken[n]];
            string id = NextId(info.Key.LocalId.ToString("X8"));
            bool last = n == spoken.Count - 1;
            var choices = last ? holder.Current.Choices : [new DialogueChoice("Continue", Slot: 0)];
            nodes.Add(new DialogueNode(id, speaker, info.Texts[spoken[n]]!, choices, 0, true, true)
            {
                Voice = new VoiceRef(info.Key, response.Number, voiceType, info.PlayerSpoken, info.Info.SharedInfo),
                ChoicesAfterFinish = last ? () => holder.Current.Choices : null,
            });
            if (n > 0)
            {
                var self = nodes[n];
                _next[nodes[n - 1].Id] = _ => self;
            }
        }
        _next[nodes[^1].Id] = slot => holder.Current.Pick(slot);
        if (_scenes is not null)
            _hostFinished[nodes[^1].Id] = () => Hosting(holder.Current, info.Key);
        _pendingEnd[nodes[^1].Id] = (info, holder);
        if (_effects is not null && info.Result is { Any: true })
        {
            _pendingBegin[nodes[0].Id] = info;
        }
        else if (info.Info.StartScene is not null)
            _pendingBegin[nodes[0].Id] = info;
        return nodes[0];
    }

    private static readonly SceneBranch[] BranchOrder = [SceneBranch.Positive, SceneBranch.Negative, SceneBranch.Neutral, SceneBranch.Question];

    private Menu? SceneMenu(DialogueInfoData info, FormKey sceneKey)
    {
        if (_records.SceneOf(sceneKey) is not { } scene)
        {
            Trace?.Invoke($"scene {sceneKey} has no readable player dialogue; falling back to the topic menu");
            return null;
        }
        int start = info.Result?.StartScenePhase is { } name ? PhaseIndex(scene, name) : 0;
        return ScenePhaseMenu(scene, start < 0 ? 0 : start);
    }

    private static int PhaseIndex(SceneData scene, string name) =>
        scene.Phases.ToList().FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    // Requires: phases run in index order and a phase with false start conditions is skipped; advance past the NAM0 jump is UNVERIFIED.
    private Menu? ScenePhaseMenu(SceneData scene, int from)
    {
        for (int p = from; p < scene.Phases.Count; p++)
        {
            var phase = scene.Phases[p];
            if (!_records.PhaseOpen(_npc, _reference, scene, phase))
            {
                Trace?.Invoke($"scene {scene.Key} phase {p} '{phase.Name}' skipped: start conditions are not true");
                continue;
            }
            int index = p;
            var shown = phase.Options
                .Select(o => (Option: o, Line: FirstSpoken(o.Player)))
                .Where(x => x.Line is not null)
                .OrderBy(x => Array.IndexOf(BranchOrder, x.Option.Branch))
                .ToList();
            if (shown.Count > 0)
            {
                var choices = shown.Select(x => new DialogueChoice(string.IsNullOrWhiteSpace(x.Line!.Prompt) ? x.Option.Player.Name ?? x.Line.Key.ToString() : x.Line.Prompt, Slot: Array.IndexOf(BranchOrder, x.Option.Branch))).ToList();
                return new Menu(choices, slot =>
                {
                    var picked = shown.FirstOrDefault(x => Array.IndexOf(BranchOrder, x.Option.Branch) == slot);
                    return picked.Line is null ? null : Speak(picked.Line, () => EndsRunningScene(picked.Line) ? Ended(scene) : new Menu([new DialogueChoice("Continue", Slot: 0)], _ => SceneReply(scene, index, picked.Option)));
                }, (scene.Key, index));
            }
            if (phase.NpcTopic is { } topic && FirstSpoken(topic) is { } line)
                return new Menu([new DialogueChoice("Continue", Slot: 0)], _ => Speak(line, () => EndsRunningScene(line) ? Ended(scene) : Onward(scene, index + 1)), (scene.Key, index));
        }
        Trace?.Invoke($"scene {scene.Key} has no further phase with dialogue from phase {from}");
        return null;
    }

    private bool RunsItself(FormKey scene) => _scenes?.RunsScene(scene) ?? false;

    // Requires: an INFO flagged End Running Scene ends the scene and the conversation returns to the hub; completion versus stop is UNVERIFIED.
    private static bool EndsRunningScene(DialogueInfoData info) => ((info.Info.Flags ?? 0) & EndRunningSceneFlag) != 0;

    private Menu Ended(SceneData scene) => Hub() with { EndsScene = scene.Key };

    private Menu Onward(SceneData scene, int next) => ScenePhaseMenu(scene, next) ?? Ended(scene);

    private DialogueNode SceneReply(SceneData scene, int phase, SceneOption option)
    {
        var reply = FirstSpoken(option.Reply);
        int next = phase + 1;
        bool ends = reply is not null && EndsRunningScene(reply);
        if (!ends && reply?.Info.StartScene == scene.Key && reply.Result?.StartScenePhase is { } name && PhaseIndex(scene, name) is >= 0 and var jump)
            next = jump;
        Menu After() => ends ? Ended(scene) : Onward(scene, next);
        return reply is null ? Silent(After()) : Speak(reply, After);
    }

    private string NextId(string stem) => $"{stem}.{_serial++}";
}
