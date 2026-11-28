using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Dialogue;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Ui.Dialogs;
using Radicle.Interop;
using NameKind = Radicle.Interop.Native.NameKind;
using Xunit;
using FormKey = Radicle.Interop.FormKey;

namespace OpenCw.Ui.Tests;

public sealed class DialogueResultTests
{
    static readonly FormKey Npc = new("Fallout4.esm", 0x19FD9);
    static readonly FormKey Quest = new("Fallout4.esm", 0x400);
    static readonly FormKey Scene = new("Fallout4.esm", 0x500);

    static FormKey Key(uint id) => new("Fallout4.esm", id);

    static DialogueInfoData Info(uint id, string text, string? prompt = null, bool player = false, InfoResult? result = null, FormKey? startScene = null, ushort? flags = null)
    {
        var responses = new List<DialogueResponse> { new(null, Key(0x900), 1, 0, 0, 0, 0, new DialogueText(NameKind.Inline, text, 0), null, null) };
        var info = new DialogueInfo(flags, null, null, null, null, null, null, null, startScene, new DialogueText(NameKind.Absent, null, 0), responses, []);
        return new DialogueInfoData(Key(id), info, [text], prompt, [], Quest, player, result);
    }

    static DialogueTopicData Topic(uint id, string? name, byte category, params DialogueInfoData[] infos) => new(Key(id), name, category, "", 0, infos);

    sealed class Recorder : IDialogueEffects
    {
        public List<string> Ran { get; } = new();

        public IReadOnlyList<EffectOutcome> Run(DialogueInfoData info, bool end)
        {
            Ran.Add($"{info.Key.LocalId:X}:{(end ? "end" : "begin")}");
            return [new EffectOutcome(end ? "end" : "begin", EffectStatus.Applied, "recorded")];
        }
    }

    sealed class Throwing(bool end = true, bool begin = true) : IDialogueEffects
    {
        public bool Attempted { get; private set; }

        public IReadOnlyList<EffectOutcome> Run(DialogueInfoData info, bool isEnd)
        {
            Attempted |= isEnd;
            return (isEnd ? end : begin) ? throw new InvalidOperationException("effect broke") : [];
        }
    }

    sealed class ThrowingStarter : ISceneStarter
    {
        public EffectOutcome Start(FormKey scene) => throw new InvalidOperationException("scene broke");
    }

    sealed class Starter : ISceneStarter
    {
        public List<FormKey> Started { get; } = new();

        public EffectOutcome Start(FormKey scene)
        {
            Started.Add(scene);
            return new EffectOutcome("scene-start", EffectStatus.Applied, "started");
        }
    }

    sealed class QuietWorld : ISceneWorld
    {
        public string? ResolveActor(string quest, int alias) => null;
        public SpeechResult Speak(string context, string topic, string? actor) => new(SpeechStatus.Spoken, 1, "");
        public void StopSpeech(string context, string actor) { }
        public PackageResult BeginPackage(string context, string package, string? actor) => new(false, "absent");
        public void EndPackage(string package, string? actor) { }
        public bool SetStage(string quest, int stage) => true;
    }

    static SceneDef QueuedScene(string key) => new(key, "000400:Fallout4.esm", [], [], null, null);

    [Fact]
    public void ADialogueResultThatStartsASceneQueuesItOnTheSceneRuntime()
    {
        var runtime = new SceneRuntime(key => key == "000500:Fallout4.esm" ? QueuedScene(key) : null, new QuietWorld());
        var records = new Fake([Topic(2, "Hello", 7, Info(1, "Hi.", startScene: Scene)) with { Subtype = "GREE" }], new SceneData(Scene, []));
        var conversation = new DialogueConversation(Npc, records, scenes: new SceneRuntimeStarter(runtime));

        conversation.NodeStarted(conversation.First.Id);

        Assert.True(runtime.IsPlaying("000500:Fallout4.esm"));
        Assert.Contains(conversation.EffectLog, e => e.Outcome is { Effect: "scene-start", Status: EffectStatus.Applied });
    }

    [Fact]
    public void ASceneTheRuntimeDoesNotKnowIsARefusedEffectNotAStartedScene()
    {
        var runtime = new SceneRuntime(_ => null, new QuietWorld());

        var outcome = new SceneRuntimeStarter(runtime).Start(Scene);

        Assert.Equal(EffectStatus.Refused, outcome.Status);
        Assert.False(runtime.IsPlaying("000500:Fallout4.esm"));
        Assert.Contains("000500:Fallout4.esm", outcome.Detail);
    }

    sealed class Fake(IReadOnlyList<DialogueTopicData> topics, SceneData? scene = null) : IDialogueRecords
    {
        public bool LastPhaseOpen { get; set; } = true;

        public Func<DialogueInfoData, bool>? Gate { get; set; }

        public string SpeakerName(FormKey npc) => "MacCready";

        public FormKey? VoiceType(FormKey npc) => null;

        public IReadOnlyList<DialogueTopicData> Topics(FormKey npc) => topics;

        public bool Eligible(FormKey npc, DialogueInfoData info) => Gate?.Invoke(info) ?? true;

        public SceneData? SceneOf(FormKey key) => key == Scene ? scene : null;

        public bool PhaseOpen(FormKey npc, FormKey? reference, SceneData data, ScenePhaseData phase) => phase.Name != "Last" || LastPhaseOpen;
    }

    static (Fake Records, Recorder Effects, Starter Starter, DialogueConversation Conversation) Meet(bool lastOpen = true, ISceneStarter? host = null, ushort? beginReplyFlags = null, ushort? beginPlayerFlags = null)
    {
        var yes = Topic(10, null, 2, Info(11, "You've got a deal.", "Yes", true));
        var no = Topic(12, null, 2, Info(13, "No way.", "No", true));
        var yesReply = Topic(14, null, 2, Info(15, "Lead on.", result: new InfoResult(null, 30, false)));
        var noReply = Topic(16, null, 2, Info(17, "Sticks and stones.", result: new InfoResult(null, 20, false)));
        var begin = Topic(18, null, 2, Info(19, "Opening line.", "Interested", true, startScene: beginPlayerFlags is null ? null : Scene, flags: beginPlayerFlags));
        var beginReply = Topic(20, null, 2, Info(21, "Joking?", result: new InfoResult(null, null, false), flags: beginReplyFlags));
        var phases = new List<ScenePhaseData>
        {
            new("Begin", [], [new SceneOption(SceneBranch.Positive, begin, beginReply)], null),
            new("Last", [], [new SceneOption(SceneBranch.Positive, yes, yesReply), new SceneOption(SceneBranch.Negative, no, noReply)], null),
        };
        var greeting = Info(1, "Hey, you.", result: new InfoResult(null, null, false, "Begin"), startScene: Scene);
        var records = new Fake([Topic(2, "Hello", 7, greeting) with { Subtype = "GREE" }], new SceneData(Scene, phases)) { LastPhaseOpen = lastOpen };
        var effects = new Recorder();
        var starter = new Starter();
        return (records, effects, starter, new DialogueConversation(Npc, records, effects: effects, scenes: host ?? starter));
    }

    static DialogueNode Pick(DialogueConversation conversation, DialogueNode node, string text) =>
        conversation.Next(node.Id, node.Choices.Single(c => c.Text == text).Slot!.Value)!;

    static DialogueNode ToFirstChoice(DialogueConversation conversation, DialogueNode node)
    {
        while (node.Choices.Count == 1 && node.Choices[0].Text == "Continue")
            node = conversation.Next(node.Id, node.Choices[0].Slot!.Value)!;
        return node;
    }

    static DialogueNode ToFinalChoice(DialogueConversation conversation)
    {
        var menu = ToFirstChoice(conversation, conversation.First);
        return ToFirstChoice(conversation, Pick(conversation, menu, "Interested"));
    }

    [Fact]
    public void TheGreetingStartsItsSceneExactlyOnce()
    {
        var (_, _, starter, conversation) = Meet();
        Assert.Empty(starter.Started);
        conversation.NodeStarted(conversation.First.Id);
        Assert.Equal([Scene], starter.Started);
        conversation.NodeStarted(conversation.First.Id);
        _ = ToFirstChoice(conversation, conversation.First);
        Assert.Single(starter.Started);
    }

    [Fact]
    public void AfterTheGreetingThePlayerGetsTheScenePhaseMenu()
    {
        var (_, _, _, conversation) = Meet();
        var menu = ToFirstChoice(conversation, conversation.First);
        Assert.Equal(["Interested"], menu.Choices.Select(c => c.Text));
    }

    [Fact]
    public void AcceptingRunsTheOnEndStageOfTheReplyExactlyOnce()
    {
        var (_, effects, _, conversation) = Meet();
        var final = ToFinalChoice(conversation);
        Assert.Equal(["Yes", "No"], final.Choices.Select(c => c.Text));
        var said = Pick(conversation, final, "Yes");
        Assert.Equal("You've got a deal.", said.Line);
        var npcReply = conversation.Next(said.Id, 0)!;
        Assert.Equal("Lead on.", npcReply.Line);
        Assert.DoesNotContain("F:end", effects.Ran);
        conversation.NodeFinished(npcReply.Id);
        Assert.Equal(1, effects.Ran.Count(r => r == "F:end"));
        conversation.NodeFinished(npcReply.Id);
        conversation.Next(npcReply.Id, npcReply.Choices[0].Slot!.Value);
        Assert.Equal(1, effects.Ran.Count(r => r == "F:end"));
        Assert.DoesNotContain("11:end", effects.Ran);
    }

    [Fact]
    public void DecliningRunsTheDeclineStageAndNotTheAcceptStage()
    {
        var (_, effects, _, conversation) = Meet();
        var npcReply = conversation.Next(Pick(conversation, ToFinalChoice(conversation), "No").Id, 0)!;
        conversation.Next(npcReply.Id, npcReply.Choices[0].Slot!.Value);
        Assert.Contains("11:end", effects.Ran);
        Assert.DoesNotContain("F:end", effects.Ran);
    }

    static DialogueSession Drive(DialogueConversation conversation, DialogueNode first)
    {
        var session = new DialogueSession(first);
        session.NodeStarted += node => conversation.NodeStarted(node.Id);
        session.NodeFinished += node => conversation.NodeFinished(node.Id);
        conversation.NodeStarted(first.Id);
        return session;
    }

    static void Tick(DialogueSession session, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.01)
            session.Tick(0.01);
    }

    static DialogueNode ReplyAfterYes(DialogueConversation conversation) =>
        conversation.Next(Pick(conversation, ToFinalChoice(conversation), "Yes").Id, 0)!;

    [Fact]
    public void ACancelledReplyThatNeverFinishedRunsNoEndResult()
    {
        var (_, effects, _, conversation) = Meet();
        var reply = ReplyAfterYes(conversation);
        Assert.Equal("Lead on.", reply.Line);
        var session = Drive(conversation, reply);
        Assert.Equal(DialogueState.Speaking, session.State);
        Tick(session, 0.5);
        session.Abort();
        Tick(session, 3);
        Assert.DoesNotContain("F:end", effects.Ran);
    }

    [Fact]
    public void AReplyThatFinishesRunsItsEndResultOnceThroughTheSessionEvent()
    {
        var (_, effects, _, conversation) = Meet();
        var reply = ReplyAfterYes(conversation);
        var session = Drive(conversation, reply);
        Tick(session, 0.5);
        Assert.DoesNotContain("F:end", effects.Ran);
        Tick(session, 2);
        Assert.Equal(DialogueState.Choosing, session.State);
        Assert.Equal(1, effects.Ran.Count(r => r == "F:end"));
        Assert.True(session.Choose(session.Node.Choices[0].Slot!.Value));
        Tick(session, 1);
        Assert.Equal(1, effects.Ran.Count(r => r == "F:end"));
    }

    [Fact]
    public void APhaseWithFalseStartConditionsIsSkipped()
    {
        var (_, _, _, conversation) = Meet(lastOpen: false);
        var final = ToFinalChoice(conversation);
        Assert.Equal("Goodbye", final.Choices.Single(c => c.Kind == DialogueChoiceKind.Exit).Text);
        Assert.DoesNotContain(final.Choices, c => c.Text is "Yes" or "No");
    }

    [Fact]
    public void WithoutASceneStarterTheSceneIsReportedNotStarted()
    {
        var (records, _, _, _) = Meet();
        var conversation = new DialogueConversation(Npc, records);
        Assert.Empty(conversation.EffectLog);
        conversation.NodeStarted(conversation.First.Id);
        Assert.Contains(conversation.EffectLog, e => e.Outcome is { Effect: "scene-start", Status: EffectStatus.Refused });
    }

    [Fact]
    public void AnInfoWithOnlyAFragmentRunsItsBeginAndEndEffectsEachExactlyOnce()
    {
        var vmad = new InfoResult(null, null, true, null, new OpenCommonwealth.Services.Papyrus.Vmad.InfoVmadParse("F_Info", [], [new OpenCommonwealth.Services.Papyrus.Vmad.InfoFragmentRow(true, "F_Info", "Fragment_End")], null));
        var records = new Fake([Topic(2, "Hello", 7, Info(1, "Hey, you.", result: vmad)) with { Subtype = "GREE" }]);
        var effects = new Recorder();
        var conversation = new DialogueConversation(Npc, records, effects: effects);
        var first = conversation.First!;
        Assert.Empty(effects.Ran);
        conversation.NodeStarted(first.Id);
        conversation.NodeStarted(first.Id);
        Assert.Equal(["1:begin"], effects.Ran);
        conversation.NodeFinished(first.Id);
        conversation.NodeFinished(first.Id);
        conversation.Next(first.Id, first.Choices[0].Slot!.Value);
        Assert.Equal(["1:begin", "1:end"], effects.Ran);
    }

    [Fact]
    public void ABuiltButNeverShownConversationRunsNoBeginEffect()
    {
        var result = new InfoResult(30, null, false);
        var records = new Fake([Topic(2, "Hello", 7, Info(1, "Hey, you.", result: result)) with { Subtype = "GREE" }]);
        var effects = new Recorder();
        _ = new DialogueConversation(Npc, records, effects: effects);
        Assert.Empty(effects.Ran);
    }

    [Fact]
    public void ThePlayerMenuAfterAnInfoIsRebuiltAfterItsEndResultRan()
    {
        var effects = new Recorder();
        var greeting = Info(1, "Hi.", result: new InfoResult(null, 5, false));
        var ask = Topic(3, "Ask", 0, Info(4, "Answer.", "Ask"));
        var records = new Fake([Topic(2, "Hello", 7, greeting) with { Subtype = "GREE" }, ask]) { Gate = i => i.Key.LocalId != 4 || effects.Ran.Contains("1:end") };
        var conversation = new DialogueConversation(Npc, records, effects: effects);
        var first = conversation.First;
        Assert.DoesNotContain(first.Choices, c => c.Text == "Ask");
        conversation.NodeFinished(first.Id);
        var after = first.ChoicesAfterFinish!();
        var asked = Assert.Single(after, c => c.Text == "Ask");
        Assert.Equal("Answer.", conversation.Next(first.Id, asked.Slot!.Value)!.Line);
    }

    [Fact]
    public void QuestTopicsAddedDuringConversationAppearAfterTheCurrentLineFinishes()
    {
        var greeting = Info(1, "Hi.", result: new InfoResult(null, 5, false));
        var topics = new List<DialogueTopicData> { Topic(2, "Hello", 7, greeting) with { Subtype = "GREE" } };
        var conversation = new DialogueConversation(Npc, new Fake(topics), effects: new Recorder());
        var first = conversation.First;
        Assert.DoesNotContain(first.Choices, c => c.Text == "New quest");

        topics.Add(Topic(3, "New quest", 0, Info(4, "The quest started.", "New quest")));
        conversation.NodeFinished(first.Id);

        var choice = Assert.Single(first.ChoicesAfterFinish!(), c => c.Text == "New quest");
        Assert.Equal("The quest started.", conversation.Next(first.Id, choice.Slot!.Value)!.Line);
    }

    [Fact]
    public void QuestTopicsAddedDuringAPlainLineAppearWhenItFinishes()
    {
        var topics = new List<DialogueTopicData> { Topic(2, "Hello", 7, Info(1, "Hi.")) with { Subtype = "GREE" } };
        var conversation = new DialogueConversation(Npc, new Fake(topics));
        var first = conversation.First;
        topics.Add(Topic(3, "New quest", 0, Info(4, "The quest started.", "New quest")));

        conversation.NodeFinished(first.Id);

        Assert.Contains(first.ChoicesAfterFinish!(), c => c.Text == "New quest");
    }

    static DialogueConversation Greeting(IDialogueEffects effects, Func<DialogueInfoData, bool>? gate = null)
    {
        var records = new Fake([Topic(2, "Hello", 7, Info(1, "Hi.", result: new InfoResult(30, 5, false))) with { Subtype = "GREE" }, Topic(3, "Ask", 0, Info(4, "Answer.", "Ask"))]) { Gate = gate };
        return new DialogueConversation(Npc, records, effects: effects);
    }

    [Fact]
    public void AnEffectThatThrowsAtBeginIsAFailedOutcomeAndTheSceneStillStarts()
    {
        var starter = new Starter();
        var records = new Fake([Topic(2, "Hello", 7, Info(1, "Hi.", result: new InfoResult(30, null, false), startScene: Scene)) with { Subtype = "GREE" }], new SceneData(Scene, []));
        var conversation = new DialogueConversation(Npc, records, effects: new Throwing(), scenes: starter);
        conversation.NodeStarted(conversation.First.Id);
        var failed = Assert.Single(conversation.EffectLog, e => e.Outcome.Status == EffectStatus.Failed);
        Assert.Equal("info-begin", failed.Outcome.Effect);
        Assert.Contains("effect broke", failed.Outcome.Detail);
        Assert.Equal([Scene], starter.Started);
        conversation.NodeStarted(conversation.First.Id);
        Assert.Single(conversation.EffectLog, e => e.Outcome.Status == EffectStatus.Failed);
    }

    [Fact]
    public void AnEffectThatThrowsAtEndIsFailedOnceAndTheMenuIsStillRebuilt()
    {
        var effects = new Throwing(begin: false);
        var conversation = Greeting(effects, i => i.Key.LocalId != 4 || effects.Attempted);
        var first = conversation.First;
        Assert.DoesNotContain(first.Choices, c => c.Text == "Ask");
        conversation.NodeFinished(first.Id);
        conversation.NodeFinished(first.Id);
        Assert.Equal("info-end", Assert.Single(conversation.EffectLog, e => e.Outcome.Status == EffectStatus.Failed).Outcome.Effect);
        Assert.Contains(first.ChoicesAfterFinish!(), c => c.Text == "Ask");
    }

    [Fact]
    public void ASceneStarterThatThrowsIsAFailedOutcome()
    {
        var records = new Fake([Topic(2, "Hello", 7, Info(1, "Hi.", result: new InfoResult(null, null, false), startScene: Scene)) with { Subtype = "GREE" }], new SceneData(Scene, []));
        var conversation = new DialogueConversation(Npc, records, scenes: new ThrowingStarter());
        conversation.NodeStarted(conversation.First.Id);
        var outcome = Assert.Single(conversation.EffectLog).Outcome;
        Assert.Equal((EffectStatus.Failed, "scene-start"), (outcome.Status, outcome.Effect));
        Assert.Contains("scene broke", outcome.Detail);
    }

    [Fact]
    public void AMenuRebuildThatThrowsIsReportedAndTheOldMenuStillPicks()
    {
        bool broken = false;
        var records = new Fake([Topic(2, "Hello", 7, Info(1, "Hi.", result: new InfoResult(null, 5, false))) with { Subtype = "GREE" }, Topic(3, "Ask", 0, Info(4, "Answer.", "Ask"))])
        {
            Gate = _ => broken ? throw new InvalidOperationException("records gone") : true,
        };
        var conversation = new DialogueConversation(Npc, records, effects: new Recorder());
        var first = conversation.First;
        broken = true;
        conversation.NodeFinished(first.Id);
        var outcome = Assert.Single(conversation.EffectLog, e => e.Outcome.Status == EffectStatus.Failed).Outcome;
        Assert.Equal("menu-rebuild", outcome.Effect);
        Assert.Contains("records gone", outcome.Detail);
        broken = false;
        Assert.Equal("Answer.", conversation.Next(first.Id, first.Choices.Single(c => c.Text == "Ask").Slot!.Value)!.Line);
    }

    [Fact]
    public void AnEffectSinkThatThrowsNeitherEscapesNorLosesTheLog()
    {
        var effects = new Recorder();
        var conversation = Greeting(effects);
        conversation.EffectReported += (_, _) => throw new InvalidOperationException("sink broke");
        conversation.NodeStarted(conversation.First.Id);
        conversation.NodeFinished(conversation.First.Id);
        Assert.Equal(2, conversation.EffectLog.Count);
        Assert.Equal(["1:begin", "1:end"], effects.Ran);
    }

    [Fact]
    public void ASceneWhoseOptionTopicsHaveNoInfoFallsBackToTheTopicMenu()
    {
        var empty = Topic(30, null, 2);
        var phases = new List<ScenePhaseData> { new("Begin", [], [new SceneOption(SceneBranch.Positive, empty, Topic(31, null, 2))], empty) };
        var greeting = Info(1, "Hey.", result: new InfoResult(null, null, false, "Begin"), startScene: Scene);
        var records = new Fake([Topic(2, "Hello", 7, greeting) with { Subtype = "GREE" }, Topic(3, "Ask", 0, Info(4, "Answer.", "Ask"))], new SceneData(Scene, phases));
        var conversation = new DialogueConversation(Npc, records, effects: new Recorder());
        var menu = ToFirstChoice(conversation, conversation.First);
        Assert.Contains(menu.Choices, c => c.Text == "Ask");
        Assert.Contains(menu.Choices, c => c.Kind == DialogueChoiceKind.Exit);
    }

    [Fact]
    public void EveryOutcomeIsReportedToTheEffectSink()
    {
        var (_, _, _, conversation) = Meet();
        var seen = new List<(FormKey Info, EffectStatus Status)>();
        conversation.EffectReported += (info, outcome) => seen.Add((info, outcome.Status));
        conversation.NodeStarted(conversation.First.Id);
        Assert.Equal([(Key(1), EffectStatus.Applied)], seen);
        var refused = new DialogueConversation(Npc, Meet().Records);
        var reported = new List<EffectStatus>();
        refused.EffectReported += (_, outcome) => reported.Add(outcome.Status);
        refused.NodeStarted(refused.First.Id);
        Assert.Equal([EffectStatus.Refused], reported);
    }

    sealed class HostStarter : ISceneStarter
    {
        public List<string> Calls { get; } = new();

        public EffectOutcome Start(FormKey scene)
        {
            Calls.Add("start");
            return new EffectOutcome("scene-start", EffectStatus.Applied, "started");
        }

        public EffectOutcome StartHosted(FormKey scene)
        {
            Calls.Add("hosted");
            return new EffectOutcome("scene-start", EffectStatus.Applied, "hosted");
        }

        public void Phase(FormKey scene, int phase) => Calls.Add($"phase {phase}");

        public void End(FormKey scene) => Calls.Add("end");

        public void Cancel(FormKey scene) => Calls.Add("cancel");
    }

    static DialogueNode Finish(DialogueConversation conversation, DialogueNode node)
    {
        conversation.NodeStarted(node.Id);
        conversation.NodeFinished(node.Id);
        return node;
    }

    [Fact]
    public void AHostedSceneEntersEachPhaseWhenItsMenuIsShownAndEndsAfterTheLastReply()
    {
        var host = new HostStarter();
        var (_, _, _, conversation) = Meet(host: host);
        Assert.Empty(host.Calls);

        var greeting = Finish(conversation, conversation.First);
        Assert.Equal(["hosted", "phase 0"], host.Calls);
        var said = Finish(conversation, Pick(conversation, greeting, "Interested"));
        Assert.Equal(["hosted", "phase 0"], host.Calls);
        var reply = Finish(conversation, conversation.Next(said.Id, 0)!);
        Assert.Equal(["hosted", "phase 0", "phase 1"], host.Calls);
        Assert.Equal(["Yes", "No"], reply.Choices.Select(c => c.Text));

        var yes = Finish(conversation, Pick(conversation, reply, "Yes"));
        var lead = Finish(conversation, conversation.Next(yes.Id, 0)!);

        Assert.Equal("Lead on.", lead.Line);
        Assert.Equal(["hosted", "phase 0", "phase 1", "end"], host.Calls);
        conversation.Close();
        Assert.DoesNotContain("cancel", host.Calls);
    }

    [Fact]
    public void AReplyFlaggedEndRunningSceneEndsTheSceneAndReturnsToTheHubNotTheNextPhase()
    {
        var host = new HostStarter();
        var (_, _, _, conversation) = Meet(host: host, beginReplyFlags: DialogueConversation.EndRunningSceneFlag);
        var greeting = Finish(conversation, conversation.First);
        var said = Finish(conversation, Pick(conversation, greeting, "Interested"));
        var reply = conversation.Next(said.Id, 0)!;
        Assert.Equal("Joking?", reply.Line);

        Finish(conversation, reply);

        Assert.Equal(["hosted", "phase 0", "end"], host.Calls);
        Assert.DoesNotContain(reply.ChoicesAfterFinish!(), c => c.Text is "Yes" or "No");
        Assert.Contains(reply.ChoicesAfterFinish!(), c => c.Kind == DialogueChoiceKind.Exit);
    }

    [Fact]
    public void ClosingTheConversationCancelsAHostedSceneOnceAndBuildingItStartsNothing()
    {
        var host = new HostStarter();
        var (_, _, _, conversation) = Meet(host: host);
        Assert.Empty(host.Calls);
        conversation.Close();
        Assert.Empty(host.Calls);

        conversation.NodeStarted(conversation.First.Id);
        conversation.Close();
        conversation.Close();

        Assert.Equal(["hosted", "cancel"], host.Calls);
    }

    sealed class StageWorld : ISceneWorld
    {
        public List<int> Stages { get; } = new();

        public string? ResolveActor(string quest, int alias) => null;
        public SpeechResult Speak(string context, string topic, string? actor) => new(SpeechStatus.NoLine, 0, "");
        public void StopSpeech(string context, string actor) { }
        public PackageResult BeginPackage(string context, string package, string? actor) => new(false, "absent");
        public void EndPackage(string package, string? actor) { }

        public bool SetStage(string quest, int stage)
        {
            Stages.Add(stage);
            return true;
        }
    }

    static SceneRuntime HostedRuntime(StageWorld world) => new(key => key != "000500:Fallout4.esm" ? null : new SceneDef(key, "000400:Fallout4.esm",
        [new(0, "Begin", 1, 2), new(1, "Last", 3, 4)], [new("PlayerDialogue#0", SceneActionKind.PlayerDialogue, 1, 0, 0, false, null, 0)], 9, 10, null, null,
        "action PlayerDialogue#0 is a player dialogue action, which only the dialogue menu runs"), world);

    [Fact]
    public void AGreetingStartsAPlayerDialogueSceneOnTheRuntimeAndTheMenuWalkCompletesIt()
    {
        var world = new StageWorld();
        var runtime = HostedRuntime(world);
        Assert.Equal(SceneStartOutcome.Refused, runtime.Start("000500:Fallout4.esm").Outcome);
        var (_, _, _, conversation) = Meet(host: new SceneRuntimeStarter(runtime));

        var greeting = Finish(conversation, conversation.First);
        runtime.Tick(0.25);
        Assert.True(runtime.IsPlaying("000500:Fallout4.esm"));
        var said = Finish(conversation, Pick(conversation, greeting, "Interested"));
        var reply = Finish(conversation, conversation.Next(said.Id, 0)!);
        var yes = Finish(conversation, Pick(conversation, reply, "Yes"));
        Finish(conversation, conversation.Next(yes.Id, 0)!);
        runtime.Tick(0.25);

        Assert.Equal([9, 1, 2, 3, 4, 10], world.Stages);
        Assert.False(runtime.IsPlaying("000500:Fallout4.esm"));
        Assert.True(runtime.ReportOf("000500:Fallout4.esm")!.EndStageSet);
    }

    [Fact]
    public void LeavingTheConversationMidSceneStopsTheRunWithoutItsEndStageAndTheSceneCanStartAgain()
    {
        var world = new StageWorld();
        var runtime = HostedRuntime(world);
        var (_, _, _, conversation) = Meet(host: new SceneRuntimeStarter(runtime));
        Finish(conversation, conversation.First);
        runtime.Tick(0.25);

        conversation.Close();

        Assert.False(runtime.IsPlaying("000500:Fallout4.esm"));
        Assert.True(runtime.ReportOf("000500:Fallout4.esm")!.Stopped);
        Assert.DoesNotContain(10, world.Stages);
        var (_, _, _, again) = Meet(host: new SceneRuntimeStarter(runtime));
        again.NodeStarted(again.First.Id);
        Assert.True(runtime.IsPlaying("000500:Fallout4.esm"));
    }

    sealed class SpeakWorld : ISceneWorld
    {
        public List<string> Spoken { get; } = new();

        public string? ResolveActor(string quest, int alias) => "A1:Fallout4.esm";

        public SpeechResult Speak(string context, string topic, string? actor)
        {
            Spoken.Add(topic);
            return new SpeechResult(SpeechStatus.Spoken, 1, "");
        }

        public void StopSpeech(string context, string actor) { }
        public PackageResult BeginPackage(string context, string package, string? actor) => new(false, "absent");
        public void EndPackage(string package, string? actor) { }
        public bool SetStage(string quest, int stage) => true;
    }

    [Fact]
    public void ASceneWithOnlyNpcActionsStartedFromAGreetingStillSpeaksItsLinesAndSurvivesClose()
    {
        var world = new SpeakWorld();
        var runtime = new SceneRuntime(key => key != "000500:Fallout4.esm" ? null : new SceneDef(key, "000400:Fallout4.esm", [new(0, "Only", null, null)],
            [new("Dialogue#0", SceneActionKind.Dialogue, 1, 0, 0, false, "000777:Fallout4.esm", 0)], null, 10), world);
        var (_, _, _, conversation) = Meet(host: new SceneRuntimeStarter(runtime));

        var greeting = Finish(conversation, conversation.First);
        runtime.Tick(0.25);
        conversation.Close();
        runtime.Tick(2);

        Assert.Equal(["000777:Fallout4.esm"], world.Spoken);
        Assert.False(runtime.IsPlaying("000500:Fallout4.esm"));
        Assert.True(runtime.ReportOf("000500:Fallout4.esm")!.Completed);
        Assert.False(runtime.ReportOf("000500:Fallout4.esm")!.Stopped);
        Assert.NotNull(greeting);
    }

    [Fact]
    public void APlayerLineFlaggedEndRunningSceneEndsTheSceneEvenWhenItAlsoNamesTheScene()
    {
        var host = new HostStarter();
        var (_, _, _, conversation) = Meet(host: host, beginPlayerFlags: DialogueConversation.EndRunningSceneFlag);
        var greeting = Finish(conversation, conversation.First);

        var said = Finish(conversation, Pick(conversation, greeting, "Interested"));

        Assert.Equal(["hosted", "phase 0", "end"], host.Calls);
        Assert.DoesNotContain(said.ChoicesAfterFinish!(), c => c.Text == "Continue");
        Assert.Contains(said.ChoicesAfterFinish!(), c => c.Kind == DialogueChoiceKind.Exit);
    }

    [Fact]
    public void AnNpcTopicLineFlaggedEndRunningSceneEndsTheSceneAndDoesNotGoOnToTheNextPhase()
    {
        var host = new HostStarter();
        var bye = Topic(30, null, 2, Info(31, "Bye.", flags: DialogueConversation.EndRunningSceneFlag));
        var after = Topic(32, null, 2, Info(33, "More.", "More", true));
        var phases = new List<ScenePhaseData>
        {
            new("Only", [], [], bye),
            new("Next", [], [new SceneOption(SceneBranch.Positive, after, after)], null),
        };
        var greeting = Info(1, "Hey, you.", result: new InfoResult(null, null, false, "Only"), startScene: Scene);
        var records = new Fake([Topic(2, "Hello", 7, greeting) with { Subtype = "GREE" }], new SceneData(Scene, phases));
        var conversation = new DialogueConversation(Npc, records, scenes: host);

        var first = Finish(conversation, conversation.First);
        var line = Finish(conversation, conversation.Next(first.Id, 0)!);

        Assert.Equal("Bye.", line.Line);
        Assert.Equal(["hosted", "phase 0", "end"], host.Calls);
        Assert.DoesNotContain(line.ChoicesAfterFinish!(), c => c.Text == "More");
    }

    [Fact]
    public void AnNpcOnlySceneIsSpokenOnceByTheRuntimeAndTheConversationOffersNoSecondCopyOfItsTopic()
    {
        var world = new SpeakWorld();
        var runtime = new SceneRuntime(key => key != "000500:Fallout4.esm" ? null : new SceneDef(key, "000400:Fallout4.esm", [new(0, "Only", null, null)],
            [new("Dialogue#0", SceneActionKind.Dialogue, 1, 0, 0, false, "000777:Fallout4.esm", 0)], null, 10), world);
        var topic = Topic(40, null, 2, Info(41, "Said by the scene."));
        var records = new Fake([Topic(2, "Hello", 7, Info(1, "Hey, you.", startScene: Scene)) with { Subtype = "GREE" }],
            new SceneData(Scene, [new ScenePhaseData("Only", [], [], topic)]));
        var conversation = new DialogueConversation(Npc, records, scenes: new SceneRuntimeStarter(runtime));

        var first = Finish(conversation, conversation.First);
        runtime.Tick(0.25);
        runtime.Tick(2);

        Assert.Equal(["000777:Fallout4.esm"], world.Spoken);
        Assert.DoesNotContain(first.ChoicesAfterFinish!(), c => c.Text == "Continue");
        Assert.Contains(first.ChoicesAfterFinish!(), c => c.Kind == DialogueChoiceKind.Exit);
    }
}
