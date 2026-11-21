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

public sealed class DialogueConversationEdgeTests
{
    static readonly FormKey Npc = new("Fallout4.esm", 0x19FD9);
    static readonly FormKey NpcVoice = new("Fallout4.esm", 0x1234);

    static FormKey Key(uint id) => new("Fallout4.esm", id);

    static DialogueInfoData Info(uint id, string?[] texts, string? prompt = null, FormKey? previous = null)
    {
        var responses = texts.Select((text, i) => new DialogueResponse(null, Key(0x900 + (uint)i), (byte)(i + 1), 0, 0, 0, 0,
            new DialogueText(NameKind.Inline, text, 0), null, null)).ToList();
        var info = new DialogueInfo(null, null, null, null, previous, null, null, null, null,
            new DialogueText(NameKind.Absent, null, 0), responses, []);
        return new DialogueInfoData(Key(id), info, texts, prompt, []);
    }

    static DialogueTopicData Topic(uint id, string? name, byte category, string subtype, float priority, params DialogueInfoData[] infos) =>
        new(Key(id), name, category, subtype, priority, infos);

    static DialogueTopicData Player(uint id, string name, float priority, string line = "Line.") =>
        Topic(id, name, DialogueConversation.PlayerCategory, "CUST", priority, Info(id * 0x10, [line]));

    sealed class Fake(params DialogueTopicData[] topics) : IDialogueRecords
    {
        public HashSet<FormKey> Rejected { get; } = new();

        public List<FormKey> Asked { get; } = new();

        public FormKey? Voice { get; set; } = NpcVoice;

        public string SpeakerName(FormKey npc) => "Preston";

        public FormKey? VoiceType(FormKey npc) => Voice;

        public IReadOnlyList<DialogueTopicData> Topics(FormKey npc) => topics;

        public bool Eligible(FormKey npc, DialogueInfoData info)
        {
            Asked.Add(info.Key);
            return !Rejected.Contains(info.Key);
        }
    }

    static string Texts(IEnumerable<DialogueChoice> choices) => string.Join("|", choices.Select(c => c.Text));

    [Fact]
    public void Responses_with_blank_text_are_skipped_but_keep_their_response_index_and_number()
    {
        var conversation = new DialogueConversation(Npc, new Fake(Topic(1, "Hello", 7, "GREE", 0, Info(10, ["One", "", "Three"]))));

        var one = conversation.First;
        var three = conversation.Next(one.Id, 0)!;

        Assert.Equal("One", one.Line);
        Assert.Equal(1, one.Voice!.ResponseNumber);
        Assert.Equal("Three", three.Line);
        Assert.Equal(3, three.Voice!.ResponseNumber);
        Assert.Equal("Continue", Assert.Single(one.Choices).Text);
    }

    [Fact]
    public void Texts_and_responses_of_different_lengths_use_the_shorter_and_whitespace_is_not_spoken()
    {
        var shortTexts = Info(10, ["a", "b"]) with { Texts = new string?[] { "a" } };
        var longTexts = Info(20, ["c", "d"]) with { Texts = new string?[] { "c", "d", "e" } };
        var blankOnly = Info(30, ["x", "y"]) with { Texts = new string?[] { "  ", null } };

        var a = new DialogueConversation(Npc, new Fake(Topic(1, "G", 7, "GREE", 0, shortTexts))).First;
        var c = new DialogueConversation(Npc, new Fake(Topic(1, "G", 7, "GREE", 0, longTexts)));
        var x = new DialogueConversation(Npc, new Fake(Topic(1, "G", 7, "GREE", 0, blankOnly))).First;

        Assert.Equal(("a", "Goodbye"), (a.Line, Texts(a.Choices)));
        var d = c.Next(c.First.Id, 0)!;
        Assert.Equal(("d", "Goodbye"), (d.Line, Texts(d.Choices)));
        Assert.Equal(("", "Goodbye"), (x.Line, Texts(x.Choices)));
    }

    [Fact]
    public void Four_player_topics_show_the_three_highest_priority_in_the_topic_slots_and_goodbye_in_slot_one()
    {
        var first = new DialogueConversation(Npc, new Fake(Player(1, "A", 4), Player(2, "B", 3), Player(3, "C", 2), Player(4, "D", 1))).First;

        Assert.Equal("A|B|C|Goodbye", Texts(first.Choices));
        Assert.Equal(new int?[] { 0, 2, 3, 1 }, first.Choices.Select(c => c.Slot).ToArray());
        Assert.Equal(DialogueChoiceKind.Exit, first.Choices[3].Kind);
        Assert.All(first.Choices, c => Assert.False(c.Used));
    }

    [Fact]
    public void Barter_is_offered_only_when_the_delegate_says_so_and_goes_just_before_goodbye()
    {
        var records = new Fake(Player(1, "A", 2), Player(2, "B", 1));

        var without = new DialogueConversation(Npc, records, () => false).First;
        var absent = new DialogueConversation(Npc, records).First;
        var with = new DialogueConversation(Npc, records, () => true).First;

        Assert.Equal("A|B|Goodbye", Texts(without.Choices));
        Assert.Equal("A|B|Goodbye", Texts(absent.Choices));
        Assert.Equal("A|B|Barter|Goodbye", Texts(with.Choices));
        Assert.Equal(DialogueChoiceKind.Trade, with.Choices[2].Kind);
        Assert.Equal(DialogueChoiceKind.Exit, with.Choices[^1].Kind);
    }

    [Fact]
    public void Barter_takes_the_last_topic_slot_so_every_slot_is_unique_and_goodbye_is_never_cut()
    {
        var first = new DialogueConversation(Npc, new Fake(Player(1, "A", 4), Player(2, "B", 3), Player(3, "C", 2), Player(4, "D", 1)), () => true).First;

        Assert.Equal("A|B|Barter|Goodbye", Texts(first.Choices));
        Assert.Equal(new int?[] { 0, 2, DialogueConversation.TradeSlot, DialogueConversation.ExitSlot }, first.Choices.Select(c => c.Slot).ToArray());
        Assert.Equal(first.Choices.Count, first.Choices.Select(c => c.Slot).Distinct().Count());
        Assert.True(first.Choices.Count <= DialogueNode.MaxChoices);
    }

    [Fact]
    public void Barter_stays_in_the_hub_after_a_topic_is_spoken_and_picking_its_slot_speaks_nothing()
    {
        var conversation = new DialogueConversation(Npc, new Fake(Player(1, "A", 2), Player(2, "B", 1)), () => true);

        var after = conversation.Next(conversation.First.Id, 0)!;

        Assert.Equal("B|A|Barter|Goodbye", Texts(after.Choices));
        Assert.Null(conversation.Next(after.Id, DialogueConversation.TradeSlot));
    }

    [Fact]
    public void Selecting_barter_closes_the_dialogue_with_the_trade_outcome()
    {
        var conversation = new DialogueConversation(Npc, new Fake(Player(1, "A", 1)), () => true);
        var session = new DialogueSession(conversation.First);
        DialogueOutcome? outcome = null;
        session.Ended += o => outcome = o;
        for (int i = 0; i < 200 && session.State != DialogueState.Choosing; i++) session.Tick(0.1);

        Assert.True(session.Choose(DialogueConversation.TradeSlot));
        for (int i = 0; i < 50 && outcome is null; i++) session.Tick(0.1);

        Assert.Equal(DialogueOutcome.Trade, outcome);
    }

    [Fact]
    public void Give_orders_is_offered_only_when_the_delegate_says_so_and_takes_the_last_topic_slot()
    {
        var records = new Fake(Player(1, "A", 4), Player(2, "B", 3), Player(3, "C", 2), Player(4, "D", 1));

        var without = new DialogueConversation(Npc, records, null, () => false).First;
        var with = new DialogueConversation(Npc, records, null, () => true).First;

        Assert.Equal("A|B|C|Goodbye", Texts(without.Choices));
        Assert.Equal("A|B|Give orders|Goodbye", Texts(with.Choices));
        Assert.Equal(new int?[] { 0, 2, 3, 1 }, with.Choices.Select(c => c.Slot).ToArray());
        Assert.Equal(DialogueChoiceKind.Command, with.Choices[2].Kind);
    }

    [Fact]
    public void Give_orders_and_barter_together_leave_one_topic_and_keep_every_slot_unique()
    {
        var first = new DialogueConversation(Npc, new Fake(Player(1, "A", 4), Player(2, "B", 3), Player(3, "C", 2)), () => true, () => true).First;

        Assert.Equal("A|Give orders|Barter|Goodbye", Texts(first.Choices));
        Assert.Equal(new int?[] { 0, 2, DialogueConversation.TradeSlot, DialogueConversation.ExitSlot }, first.Choices.Select(c => c.Slot).ToArray());
        Assert.Equal(first.Choices.Count, first.Choices.Select(c => c.Slot).Distinct().Count());
    }

    [Fact]
    public void Selecting_give_orders_closes_the_dialogue_with_the_command_outcome()
    {
        var conversation = new DialogueConversation(Npc, new Fake(Player(1, "A", 1)), null, () => true);
        var session = new DialogueSession(conversation.First);
        DialogueOutcome? outcome = null;
        session.Ended += o => outcome = o;
        for (int i = 0; i < 200 && session.State != DialogueState.Choosing; i++) session.Tick(0.1);

        Assert.True(session.Choose(3));
        for (int i = 0; i < 50 && outcome is null; i++) session.Tick(0.1);

        Assert.Equal(DialogueOutcome.Command, outcome);
    }

    [Fact]
    public void Equal_priority_topics_keep_the_order_they_were_listed_in()
    {
        var first = new DialogueConversation(Npc, new Fake(Player(1, "First", 2), Player(2, "Second", 2), Player(3, "Third", 2))).First;

        Assert.Equal("First|Second|Third|Goodbye", Texts(first.Choices));
    }

    [Fact]
    public void A_used_topic_moves_behind_the_unused_ones_and_stays_marked_used()
    {
        var conversation = new DialogueConversation(Npc, new Fake(Player(1, "A", 3), Player(2, "B", 2), Player(3, "C", 1)));
        var hub = conversation.First;

        var after = conversation.Next(hub.Id, 2)!;

        Assert.Equal("A|C|B|Goodbye", Texts(after.Choices));
        Assert.Equal(new[] { false, false, true, false }, after.Choices.Select(c => c.Used).ToArray());
        Assert.Equal(new int?[] { 0, 2, 3, 1 }, after.Choices.Select(c => c.Slot).ToArray());
    }

    [Fact]
    public void Picking_a_used_topic_again_speaks_it_again_and_it_stays_last_among_three()
    {
        var conversation = new DialogueConversation(Npc, new Fake(Player(1, "A", 3, "Line A."), Player(2, "B", 2), Player(3, "C", 1)));
        var afterA = conversation.Next(conversation.First.Id, 0)!;

        var again = conversation.Next(afterA.Id, 3)!;

        Assert.Equal("Line A.", again.Line);
        Assert.Equal("B|C|A|Goodbye", Texts(again.Choices));
        Assert.True(again.Choices.Single(c => c.Text == "A").Used);
    }

    [Fact]
    public void With_four_topics_a_used_one_is_pushed_out_of_the_three_slots()
    {
        var conversation = new DialogueConversation(Npc, new Fake(Player(1, "A", 4), Player(2, "B", 3), Player(3, "C", 2), Player(4, "D", 1)));

        var after = conversation.Next(conversation.First.Id, 0)!;

        Assert.Equal("B|C|D|Goodbye", Texts(after.Choices));
        Assert.All(after.Choices, c => Assert.False(c.Used));
    }

    [Fact]
    public void A_topic_with_no_prompt_and_a_blank_or_missing_name_is_hidden()
    {
        var noName = Topic(1, null, 0, "CUST", 5, Info(10, ["Line."]));
        var blankName = Topic(2, "  ", 0, "CUST", 4, Info(20, ["Line."]));
        var prompted = Topic(3, null, 0, "CUST", 3, Info(30, ["Line."], prompt: "Ask me"));

        var first = new DialogueConversation(Npc, new Fake(noName, blankName, prompted)).First;

        Assert.Equal("Ask me|Goodbye", Texts(first.Choices));
        Assert.Equal(0, first.Choices[0].Slot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_prompt_falls_back_to_the_topic_name(string prompt)
    {
        var topic = Topic(1, "Name", 0, "CUST", 1, Info(10, ["Line."], prompt: prompt));

        var first = new DialogueConversation(Npc, new Fake(topic)).First;

        Assert.Equal("Name|Goodbye", Texts(first.Choices));
    }

    [Fact]
    public void The_prompt_of_the_first_spoken_info_replaces_the_topic_name()
    {
        var topic = Topic(1, "Name", 0, "CUST", 1, Info(10, [""], prompt: "Hidden prompt"), Info(11, ["Spoken."], prompt: "Shown prompt"));

        var first = new DialogueConversation(Npc, new Fake(topic)).First;

        Assert.Equal("Shown prompt|Goodbye", Texts(first.Choices));
    }

    [Fact]
    public void A_silent_hub_is_called_hub_and_response_nodes_are_named_by_info_id_with_a_shared_counter()
    {
        var silent = new DialogueConversation(Npc, new Fake(Player(1, "Ask", 1))).First;
        Assert.Equal("hub.0", silent.Id);

        var records = new Fake(
            Topic(1, "G", 7, "GREE", 0, Info(0x10, ["A", "B"])),
            Topic(2, "Ask", 0, "CUST", 1, Info(0x2B, ["C"])));
        var conversation = new DialogueConversation(Npc, records);
        var a = conversation.First;
        var b = conversation.Next(a.Id, 0)!;
        var c = conversation.Next(b.Id, 0)!;

        Assert.Equal(new[] { "00000010.0", "00000010.1", "0000002B.2" }, new[] { a.Id, b.Id, c.Id });
    }

    [Fact]
    public void A_spoken_node_can_exit_and_forces_the_line_while_the_silent_hub_can_exit_without_forcing()
    {
        var spoken = new DialogueConversation(Npc, new Fake(Topic(1, "G", 7, "GREE", 0, Info(10, ["Hi."])))).First;
        var silent = new DialogueConversation(Npc, new Fake(Player(1, "Ask", 1))).First;

        Assert.Equal((true, true, 0), (spoken.CanExit, spoken.ForceLine, spoken.LineDurationMs));
        Assert.Equal((true, false, 0), (silent.CanExit, silent.ForceLine, silent.LineDurationMs));
        Assert.Null(silent.Voice);
    }

    [Fact]
    public void A_greeting_falls_to_a_lower_priority_topic_when_the_higher_one_has_nothing_eligible()
    {
        var records = new Fake(
            Topic(1, "High", 7, "GREE", 5, Info(10, ["High line."])),
            Topic(2, "Low", 7, "GREE", 1, Info(20, ["Low line."])));
        records.Rejected.Add(Key(10));

        Assert.Equal("Low line.", new DialogueConversation(Npc, records).First.Line);
    }

    [Fact]
    public void A_greeting_with_nothing_spoken_or_eligible_falls_back_to_hello_then_to_a_silent_hub()
    {
        var blankGreeting = Topic(1, "G", 7, "GREE", 5, Info(10, [""]));
        var hello = Topic(2, "H", 7, "HELO", 0, Info(20, ["Hello line."]));
        var records = new Fake(blankGreeting, hello);

        var withHello = new DialogueConversation(Npc, records).First;
        records.Rejected.Add(Key(20));
        var silent = new DialogueConversation(Npc, records).First;

        Assert.Equal("Hello line.", withHello.Line);
        Assert.Equal(("", "Goodbye"), (silent.Line, Texts(silent.Choices)));
    }

    [Fact]
    public void A_greeting_is_always_preferred_over_hello_even_with_a_lower_priority()
    {
        var first = new DialogueConversation(Npc, new Fake(
            Topic(1, "H", 7, "HELO", 9, Info(10, ["Hello line."])),
            Topic(2, "G", 7, "GREE", 0, Info(20, ["Greeting line."])))).First;

        Assert.Equal("Greeting line.", first.Line);
    }

    [Fact]
    public void A_greeting_subtype_outside_the_misc_category_is_an_ordinary_topic()
    {
        var first = new DialogueConversation(Npc, new Fake(Topic(1, "Looks like a greeting", 0, "GREE", 0, Info(10, ["Hi."])))).First;

        Assert.Equal("", first.Line);
        Assert.Equal("Looks like a greeting|Goodbye", Texts(first.Choices));
    }

    [Fact]
    public void Topics_in_other_categories_never_reach_the_hub()
    {
        var first = new DialogueConversation(Npc, new Fake(
            Topic(1, "Combat", 3, "CUST", 9, Info(10, ["Line."])),
            Topic(2, "Misc", DialogueConversation.MiscCategory, "CUST", 9, Info(20, ["Line."])))).First;

        Assert.Equal("Goodbye", Texts(first.Choices));
    }

    [Fact]
    public void The_first_info_in_chain_order_is_the_one_that_is_spoken_and_unspoken_ones_are_not_asked_about()
    {
        var records = new Fake(Topic(1, "T", 0, "CUST", 1,
            Info(22, [""]), Info(20, ["Second."], previous: Key(21)), Info(21, ["First."])));
        var conversation = new DialogueConversation(Npc, records);

        var spoken = conversation.Next(conversation.First.Id, 0)!;

        Assert.Equal("First.", spoken.Line);
        Assert.Equal(Key(21), spoken.Voice!.Info);
        Assert.Equal([Key(21)], records.Asked.Distinct());
    }

    [Fact]
    public void A_missing_npc_voice_type_leaves_the_voice_ref_without_one()
    {
        var records = new Fake(Topic(1, "G", 7, "GREE", 0, Info(10, ["Hi."]))) { Voice = null };

        Assert.Null(new DialogueConversation(Npc, records).First.Voice!.VoiceType);
    }

    [Fact]
    public void A_hub_slot_with_no_topic_behind_it_has_no_next_node()
    {
        var conversation = new DialogueConversation(Npc, new Fake(Player(1, "A", 2), Player(2, "B", 1)));

        Assert.Null(conversation.Next(conversation.First.Id, 3));
        Assert.Null(conversation.Next(conversation.First.Id, -1));
        Assert.Equal("Line.", conversation.Next(conversation.First.Id, 2)!.Line);
    }

    [Fact]
    public void The_continue_node_between_responses_is_not_a_menu_and_only_the_last_one_has_the_hub()
    {
        var conversation = new DialogueConversation(Npc, new Fake(Topic(1, "G", 7, "GREE", 0, Info(10, ["a", "b", "c"])), Player(2, "Ask", 1)));
        var a = conversation.First;
        var b = conversation.Next(a.Id, 0)!;
        var c = conversation.Next(b.Id, 0)!;

        Assert.Equal("Continue", Assert.Single(a.Choices).Text);
        Assert.Equal("Continue", Assert.Single(b.Choices).Text);
        Assert.Equal("Ask|Goodbye", Texts(c.Choices));
        Assert.Equal(0, a.Choices[0].Slot);
    }

    [Fact]
    public void In_chain_order_a_previous_info_outside_the_topic_is_ignored_and_a_late_listed_parent_comes_first()
    {
        var outside = Info(1, ["a"], previous: Key(99));
        var plain = Info(2, ["b"]);
        var child = Info(3, ["c"], previous: Key(4));
        var parent = Info(4, ["d"]);

        Assert.Equal([Key(1), Key(2)], DialogueConversation.InChainOrder([outside, plain]).Select(i => i.Key));
        Assert.Equal([Key(2), Key(4), Key(3)], DialogueConversation.InChainOrder([child, plain, parent]).Select(i => i.Key));
        Assert.Empty(DialogueConversation.InChainOrder([]));
    }

    [Fact]
    public void An_info_that_is_its_own_previous_is_still_placed_once()
    {
        var self = Info(5, ["x"], previous: Key(5));

        Assert.Equal([Key(5)], DialogueConversation.InChainOrder([self]).Select(i => i.Key));
    }

    [Fact]
    public void A_missing_records_source_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new DialogueConversation(Npc, null!));
    }
}
