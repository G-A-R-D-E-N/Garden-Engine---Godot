using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Dialogue;
using OpenCommonwealth.Services.Ui.Dialogs;
using Radicle.Interop;
using NameKind = Radicle.Interop.Native.NameKind;
using Xunit;
using FormKey = Radicle.Interop.FormKey;

namespace OpenCw.Ui.Tests;

public sealed class DialogueConversationTests
{
    static readonly FormKey Npc = new("Fallout4.esm", 0x19FD9);
    static readonly FormKey Voice = new("Fallout4.esm", 0x1234);

    static FormKey Key(uint id) => new("Fallout4.esm", id);

    static DialogueInfoData Info(uint id, string[] texts, string? prompt = null, FormKey? previous = null, FormKey? speaker = null, bool playerSpoken = false)
    {
        var responses = texts.Select((text, i) => new DialogueResponse(null, Key(0x900 + (uint)i), (byte)(i + 1), 0, 0, 0, 0,
            new DialogueText(NameKind.Inline, text, 0), null, null)).ToList();
        var info = new DialogueInfo(null, null, null, null, previous, null, null, speaker, null,
            new DialogueText(NameKind.Absent, null, 0), responses, []);
        return new DialogueInfoData(Key(id), info, texts, prompt, [], PlayerSpoken: playerSpoken);
    }

    static DialogueTopicData Topic(uint id, string? name, byte category, string subtype, float priority, params DialogueInfoData[] infos) =>
        new(Key(id), name, category, subtype, priority, infos);

    sealed class Fake(params DialogueTopicData[] topics) : IDialogueRecords
    {
        public HashSet<FormKey> Rejected { get; } = new();

        public string SpeakerName(FormKey npc) => "Preston";

        public FormKey? VoiceType(FormKey npc) => Voice;

        public IReadOnlyList<DialogueTopicData> Topics(FormKey npc) => topics;

        public bool Eligible(FormKey npc, DialogueInfoData info) => !Rejected.Contains(info.Key);
    }

    static string Texts(IEnumerable<DialogueChoice> choices) => string.Join("|", choices.Select(c => c.Text));

    [Fact]
    public void GreetingFirstThenHubOrderedByPriority()
    {
        var records = new Fake(
            Topic(1, "Hello", 7, "GREE", 0, Info(10, ["Welcome, General."])),
            Topic(2, "Settlements", 0, "CUST", 1, Info(20, ["Sanctuary needs help."], prompt: "Any work?")),
            Topic(3, "Rumors", 0, "CUST", 5, Info(30, ["Heard things."])));
        var first = new DialogueConversation(Npc, records).First;
        Assert.Equal("Preston", first.SpeakerName);
        Assert.Equal("Welcome, General.", first.Line);
        Assert.Equal("Rumors|Any work?|Goodbye", Texts(first.Choices));
        Assert.Equal(DialogueConversation.ExitSlot, first.Choices.Single(c => c.Kind == DialogueChoiceKind.Exit).Slot);
        Assert.All(first.Choices.Where(c => c.Kind != DialogueChoiceKind.Exit), c => Assert.NotEqual(DialogueConversation.ExitSlot, c.Slot));
    }

    [Fact]
    public void NodeCarriesVoiceRef()
    {
        var first = new DialogueConversation(Npc, new Fake(Topic(1, "Hello", 7, "GREE", 0, Info(10, ["Hi."])))).First;
        var voice = first.Voice!;
        Assert.Equal(Key(10), voice.Info);
        Assert.Equal(1, voice.ResponseNumber);
        Assert.Equal(Voice, voice.VoiceType);
    }

    [Fact]
    public void NodeCarriesTheSharedInfoThatOwnsTheVoiceFile()
    {
        var shared = Info(10, ["Hi."]);
        shared = shared with { Info = shared.Info with { SharedInfo = Key(0x77) } };
        var voice = new DialogueConversation(Npc, new Fake(Topic(1, "Hello", 7, "GREE", 0, shared))).First.Voice!;
        Assert.Equal(Key(0x77), voice.SharedInfo);
        Assert.Equal(Key(10), voice.Info);
    }

    [Fact]
    public void HelloIsUsedWhenThereIsNoGreeting()
    {
        var first = new DialogueConversation(Npc, new Fake(Topic(1, "Hi", 7, "HELO", 0, Info(10, ["Hello there."])))).First;
        Assert.Equal("Hello there.", first.Line);
    }

    [Fact]
    public void NoGreetingStartsOnASilentHub()
    {
        var first = new DialogueConversation(Npc, new Fake(Topic(2, "Settlements", 0, "CUST", 1, Info(20, ["Line."])))).First;
        Assert.Equal("", first.Line);
        Assert.Equal("Settlements|Goodbye", Texts(first.Choices));
    }

    [Fact]
    public void PickingATopicSpeaksItsResponsesInOrderThenReturnsToTheHubWithItUsed()
    {
        var conversation = new DialogueConversation(Npc, new Fake(
            Topic(2, "Settlements", 0, "CUST", 5, Info(20, ["One.", "Two."])),
            Topic(3, "Rumors", 0, "CUST", 1, Info(30, ["Heard things."]))));
        var hub = conversation.First;
        var slot = hub.Choices.First(c => c.Text == "Settlements").Slot!.Value;

        var one = conversation.Next(hub.Id, slot)!;
        Assert.Equal("One.", one.Line);
        Assert.Equal("Continue", Assert.Single(one.Choices).Text);
        var two = conversation.Next(one.Id, 0)!;
        Assert.Equal("Two.", two.Line);
        Assert.Equal(2, two.Voice!.ResponseNumber);
        Assert.Equal("Rumors|Settlements|Goodbye", Texts(two.Choices));
        Assert.True(two.Choices.Single(c => c.Text == "Settlements").Used);
        Assert.False(two.Choices.Single(c => c.Text == "Rumors").Used);
    }

    [Fact]
    public void ExitAndUnknownSlotsHaveNoNextNode()
    {
        var conversation = new DialogueConversation(Npc, new Fake(Topic(2, "Settlements", 0, "CUST", 1, Info(20, ["Line."]))));
        Assert.Null(conversation.Next(conversation.First.Id, DialogueConversation.ExitSlot));
        Assert.Null(conversation.Next(conversation.First.Id, 3));
        Assert.Null(conversation.Next("missing", 0));
    }

    [Fact]
    public void IneligibleInfosAreSkippedAndTopicsWithoutAnyAreHidden()
    {
        var records = new Fake(
            Topic(2, "Settlements", 0, "CUST", 1, Info(20, ["Blocked."]), Info(21, ["Allowed."])),
            Topic(3, "Rumors", 0, "CUST", 1, Info(30, ["Blocked too."])));
        records.Rejected.Add(Key(20));
        records.Rejected.Add(Key(30));
        var conversation = new DialogueConversation(Npc, records);
        Assert.Equal("Settlements|Goodbye", Texts(conversation.First.Choices));
        Assert.Equal("Allowed.", conversation.Next(conversation.First.Id, conversation.First.Choices[0].Slot!.Value)!.Line);
    }

    [Fact]
    public void InfosWithoutSpokenTextAreNotOffered()
    {
        var conversation = new DialogueConversation(Npc, new Fake(Topic(2, "Silent", 0, "CUST", 1, Info(20, [""]))));
        Assert.Equal("Goodbye", Texts(conversation.First.Choices));
    }

    [Fact]
    public void InChainOrderFollowsPreviousInfoAndKeepsCycles()
    {
        var a = Info(1, ["a"], previous: Key(2));
        var b = Info(2, ["b"], previous: Key(3));
        var c = Info(3, ["c"]);
        Assert.Equal([Key(3), Key(2), Key(1)], DialogueConversation.InChainOrder([a, b, c]).Select(i => i.Key));
        var x = Info(7, ["x"], previous: Key(8));
        var y = Info(8, ["y"], previous: Key(7));
        Assert.Equal(2, DialogueConversation.InChainOrder([x, y]).Count);
    }

    [Fact]
    public void ConversationRunsThroughASessionToTheExit()
    {
        var conversation = new DialogueConversation(Npc, new Fake(
            Topic(1, "Hello", 7, "GREE", 0, Info(10, ["Welcome."])),
            Topic(2, "Settlements", 0, "CUST", 1, Info(20, ["Sanctuary."]))));
        var session = new DialogueSession(conversation.First);
        session.ChoiceSelected += (id, slot) =>
        {
            if (conversation.Next(id, slot) is { } next) session.Advance(next);
        };
        var lines = new List<string> { session.Node.Line };
        DialogueOutcome? outcome = null;
        session.Ended += o => outcome = o;
        void Settle()
        {
            for (int i = 0; i < 200 && session.State is not (DialogueState.Choosing or DialogueState.Ended); i++)
                session.Tick(0.1);
        }
        Settle();
        Assert.True(session.Choose(Enumerable.Range(0, DialogueSession.SlotCount).First(i => session.Slot(i).Choice?.Text == "Settlements")));
        Settle();
        lines.Add(session.Node.Line);
        Settle();
        Assert.True(session.Choose(DialogueConversation.ExitSlot));
        for (int i = 0; i < 20 && outcome is null; i++) session.Tick(0.1);
        Assert.Equal(["Welcome.", "Sanctuary."], lines);
        Assert.Equal(DialogueOutcome.Left, outcome);
    }

    [Fact]
    public void ANpcLineIsAttributedToTheNpcWithItsVoiceType()
    {
        var first = new DialogueConversation(Npc, new Fake(Topic(1, "Hello", 7, "GREE", 0, Info(10, ["Hi."])))).First;
        Assert.Equal("Preston", first.SpeakerName);
        Assert.False(first.Voice!.PlayerLine);
        Assert.Equal(Voice, first.Voice.VoiceType);
    }

    [Fact]
    public void APlayerSpokenLineIsAttributedToThePlayerAndNeverCarriesTheNpcVoiceType()
    {
        var conversation = new DialogueConversation(Npc, new Fake(Topic(2, "Feeling", 0, "CUST", 1, Info(20, ["How are you feeling?", "Second."], prompt: "How are you feeling?", playerSpoken: true))));
        var hub = conversation.First;
        var one = conversation.Next(hub.Id, hub.Choices.First(c => c.Text == "How are you feeling?").Slot!.Value)!;
        Assert.Equal(DialogueConversation.PlayerSpeakerName, one.SpeakerName);
        Assert.True(one.Voice!.PlayerLine);
        Assert.Null(one.Voice.VoiceType);
        var two = conversation.Next(one.Id, 0)!;
        Assert.Equal(DialogueConversation.PlayerSpeakerName, two.SpeakerName);
        Assert.True(two.Voice!.PlayerLine);
        Assert.Null(two.Voice.VoiceType);
        Assert.Equal("Preston", hub.SpeakerName);
    }
}
