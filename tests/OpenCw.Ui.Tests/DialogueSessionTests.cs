using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Dialogs;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class DialogueSessionTests
{
    static DialogueChoice[] Four() => new[]
    {
        new DialogueChoice("Zero"), new DialogueChoice("One"), new DialogueChoice("Two"), new DialogueChoice("Three"),
    };

    static IEnumerable<DialogueSlotView> Views(DialogueSession s) => Enumerable.Range(0, DialogueSession.SlotCount).Select(s.Slot);

    static DialogueNode Node(string id = "n1", string line = "Hello", int ms = 0, bool exit = false, bool force = false,
        IReadOnlyList<DialogueChoice>? choices = null) =>
        new(id, "Speaker", line, choices ?? Four(), ms, exit, force);

    sealed class Probe
    {
        public readonly List<string> Cues = new();
        public readonly List<(string Id, int Slot)> Selected = new();
        public readonly List<DialogueOutcome> Outcomes = new();
        public int Skips;
        public int ChallengeDone;

        public Probe(DialogueSession session)
        {
            session.CueRequested += Cues.Add;
            session.ChoiceSelected += (id, slot) => Selected.Add((id, slot));
            session.Ended += Outcomes.Add;
            session.SkipRequested += () => Skips++;
            session.ChallengeResultDone += () => ChallengeDone++;
        }
    }

    static void Run(DialogueSession s, double seconds)
    {
        for (double t = 0; t < seconds - 1e-9; t += 0.01)
            s.Tick(0.01);
    }

    [Fact]
    public void A_line_starts_speaking_with_hidden_slots_then_shows_four_after_the_duration()
    {
        var s = new DialogueSession(Node(ms: 1000));
        Assert.Equal(DialogueState.Speaking, s.State);
        Assert.All(Views(s), v => Assert.False(v.Visible));
        Assert.False(s.Choose(0));
        Assert.Equal(DialogueState.Speaking, s.State);

        Run(s, 0.9);
        Assert.Equal(DialogueState.Speaking, s.State);
        Run(s, 0.15);

        Assert.Equal(DialogueState.Choosing, s.State);
        Assert.All(Views(s), v => Assert.True(v.Visible));
        Assert.Equal(0, s.FocusedSlot);
    }

    [Fact]
    public void Zero_duration_estimates_sixty_ms_per_character_with_a_1500_ms_floor()
    {
        Assert.Equal(6000, Node(line: new string('a', 100)).SpeakingMs);
        Assert.Equal(1500, Node(line: "short").SpeakingMs);
        Assert.Equal(0, Node(line: "").SpeakingMs);
        Assert.Equal(250, Node(line: "x", ms: 250).SpeakingMs);

        var s = new DialogueSession(Node(line: new string('a', 100)));
        Run(s, 5.9);
        Assert.Equal(DialogueState.Speaking, s.State);
        Run(s, 0.2);
        Assert.Equal(DialogueState.Choosing, s.State);
    }

    [Fact]
    public void A_silent_node_skips_speaking_and_a_node_with_nothing_finishes()
    {
        Assert.Equal(DialogueState.Choosing, new DialogueSession(Node(line: "")).State);

        var empty = new DialogueSession(Node(line: "", choices: Array.Empty<DialogueChoice>()));
        var probe = new Probe(empty);
        Assert.Equal(DialogueState.Closing, empty.State);
        Run(empty, 0.3);
        Assert.Equal(new[] { DialogueOutcome.Finished }, probe.Outcomes);
    }

    [Fact]
    public void Skip_raises_once_and_advances_at_once()
    {
        var t = new DialogueSession(Node());
        var probe = new Probe(t);
        Assert.True(t.Skip());
        Assert.False(t.Skip());
        Assert.Equal(1, probe.Skips);
        Assert.Equal(DialogueState.Choosing, t.State);
        Assert.Equal(new[] { DialogueSession.AcceptCue }, probe.Cues);
    }

    [Fact]
    public void Choosing_is_ignored_while_speaking_and_requests_one_error_cue()
    {
        var s = new DialogueSession(Node());
        var probe = new Probe(s);

        Assert.False(s.Choose(1));

        Assert.Equal(new[] { DialogueSession.ErrorCue }, probe.Cues);
        Assert.Empty(probe.Selected);
    }

    [Fact]
    public void Each_slot_selects_its_index_and_selection_is_raised_once_after_the_150_ms_fade()
    {
        for (int slot = 0; slot < 4; slot++)
        {
            var s = new DialogueSession(Node(id: "dlg", line: ""));
            var probe = new Probe(s);
            Assert.True(s.Choose(slot));
            Assert.False(s.Choose((slot + 1) % 4));
            Assert.Equal(DialogueState.Resolving, s.State);
            Run(s, 0.1);
            Assert.Empty(probe.Selected);
            Run(s, 0.1);
            Run(s, 1);
            Assert.Equal(new[] { ("dlg", slot) }, probe.Selected);
            Assert.Equal(DialogueState.Waiting, s.State);
        }
    }

    [Fact]
    public void A_double_press_selects_once_and_the_second_press_gives_no_cue()
    {
        var s = new DialogueSession(Node(line: ""));
        var probe = new Probe(s);

        Assert.True(s.Choose(2));
        Assert.False(s.Choose(2));

        Assert.Equal(new[] { DialogueSession.AcceptCue }, probe.Cues);
    }

    [Fact]
    public void Slots_fade_in_over_200_ms_and_others_fade_out_while_the_pick_holds_then_fades()
    {
        var s = new DialogueSession(Node(line: ""));
        Run(s, 0.1);
        Assert.InRange(s.Slot(0).Alpha, 0.45, 0.55);
        Run(s, 0.15);
        Assert.Equal(1.0, s.Slot(0).Alpha, 6);

        s.Choose(1);
        Run(s, 0.16);
        Assert.Equal(0.0, s.Slot(0).Alpha, 6);
        Assert.Equal(1.0, s.Slot(1).Alpha, 6);
        Run(s, 0.3);
        Assert.InRange(s.Slot(1).Alpha, 0.01, 0.99);
        Run(s, 0.3);
        Assert.Equal(0.0, s.Slot(1).Alpha, 6);
        Assert.Equal("One", s.EchoText);
    }

    [Fact]
    public void Used_and_disabled_choices_are_dimmed_and_a_disabled_choice_cannot_be_chosen_or_focused()
    {
        var choices = new[]
        {
            new DialogueChoice("Off", Enabled: false),
            new DialogueChoice("Used", Used: true),
            new DialogueChoice("Plain"),
        };
        var s = new DialogueSession(Node(line: "", choices: choices));
        var probe = new Probe(s);
        Run(s, 0.3);

        Assert.Equal(DialogueSession.DisabledAlpha, s.Slot(0).Alpha, 6);
        Assert.Equal(DialogueSession.UsedAlpha, s.Slot(1).Alpha, 6);
        Assert.Equal(1, s.FocusedSlot);
        Assert.False(s.Slot(0).Enabled);
        Assert.False(s.Choose(0));
        Assert.Equal(DialogueState.Choosing, s.State);
        Assert.Equal(new[] { DialogueSession.ErrorCue }, probe.Cues);
    }

    [Fact]
    public void An_empty_slot_cannot_be_chosen_and_hidden_slots_stay_hidden()
    {
        var s = new DialogueSession(Node(line: "", choices: new[] { new DialogueChoice("Only") }));
        var probe = new Probe(s);

        Assert.False(s.Choose(3));

        Assert.True(s.Slot(0).Visible);
        Assert.False(s.Slot(3).Visible);
        Assert.Equal(new[] { DialogueSession.ErrorCue }, probe.Cues);
    }

    [Fact]
    public void Explicit_slots_are_honoured_and_extra_choices_are_dropped()
    {
        var choices = new[]
        {
            new DialogueChoice("First", Slot: 3),
            new DialogueChoice("Second"),
            new DialogueChoice("Third"),
            new DialogueChoice("Fourth"),
            new DialogueChoice("Fifth"),
        };
        var s = new DialogueSession(Node(line: "", choices: choices));

        Assert.Equal("First", s.Slot(3).Choice?.Text);
        Assert.Equal("Second", s.Slot(1).Choice?.Text);
        Assert.Equal("Third", s.Slot(2).Choice?.Text);
        Assert.Equal("Fourth", s.Slot(0).Choice?.Text);
        Assert.DoesNotContain(Views(s), v => v.Choice?.Text == "Fifth");
    }

    [Fact]
    public void Cancel_leaves_only_when_the_node_allows_it_otherwise_one_error_cue()
    {
        var stay = new DialogueSession(Node(exit: false));
        var stayProbe = new Probe(stay);
        Assert.False(stay.Cancel());
        Assert.Equal(DialogueState.Speaking, stay.State);
        Assert.Equal(new[] { DialogueSession.ErrorCue }, stayProbe.Cues);

        var leave = new DialogueSession(Node(exit: true));
        var leaveProbe = new Probe(leave);
        Assert.True(leave.Cancel());
        Run(leave, 0.3);
        Assert.Equal(new[] { DialogueOutcome.Left }, leaveProbe.Outcomes);
    }

    [Fact]
    public void Pad_B_selects_a_visible_slot_one_choice_otherwise_it_cancels()
    {
        var withOne = new DialogueSession(Node(line: "", exit: true));
        Assert.True(withOne.CancelFromPadB());
        Assert.Equal(DialogueState.Resolving, withOne.State);
        Assert.Equal("One", withOne.EchoText);

        var emptyOne = new DialogueSession(Node(line: "", exit: true, choices: new[] { new DialogueChoice("A", Slot: 0) }));
        Assert.True(emptyOne.CancelFromPadB());
        Assert.Equal(DialogueState.Closing, emptyOne.State);

        var disabledOne = new DialogueSession(Node(line: "", exit: true,
            choices: new[] { new DialogueChoice("A"), new DialogueChoice("B", Enabled: false) }));
        Assert.True(disabledOne.CancelFromPadB());
        Assert.Equal(DialogueState.Closing, disabledOne.State);

        var speaking = new DialogueSession(Node(exit: true));
        Assert.True(speaking.CancelFromPadB());
        Assert.Equal(DialogueState.Closing, speaking.State);
    }

    [Fact]
    public void Exit_and_trade_choices_end_with_left_and_trade_after_the_selection_is_raised()
    {
        foreach (var (kind, expected) in new[] { (DialogueChoiceKind.Exit, DialogueOutcome.Left), (DialogueChoiceKind.Trade, DialogueOutcome.Trade) })
        {
            var s = new DialogueSession(Node(line: "", choices: new[] { new DialogueChoice("Go", Kind: kind) }));
            var probe = new Probe(s);
            s.Choose(0);
            Run(s, 0.2);
            Assert.Single(probe.Selected);
            Assert.Empty(probe.Outcomes);
            Run(s, 0.3);
            Assert.Equal(new[] { expected }, probe.Outcomes);
        }
    }

    [Fact]
    public void The_outcome_is_raised_once_after_a_200_ms_fade_and_later_calls_are_ignored()
    {
        var s = new DialogueSession(Node(line: ""));
        var probe = new Probe(s);
        s.Finish();
        Run(s, 0.1);
        Assert.Empty(probe.Outcomes);
        Assert.InRange(s.PanelAlpha, 0.01, 0.99);
        Run(s, 0.2);

        Assert.Equal(new[] { DialogueOutcome.Finished }, probe.Outcomes);
        Assert.False(s.Finish());
        Assert.False(s.Advance(Node()));
        s.Abort();
        Run(s, 1);
        Assert.Single(probe.Outcomes);
        Assert.Equal(DialogueState.Ended, s.State);
    }

    [Fact]
    public void Advance_starts_the_next_node_only_while_waiting_for_the_game()
    {
        var s = new DialogueSession(Node(id: "a", line: ""));
        Assert.False(s.Advance(Node(id: "b")));
        s.Choose(0);
        s.Tick(0.2);

        Assert.Equal(DialogueState.Waiting, s.State);
        Assert.True(s.Advance(Node(id: "b", line: "Next")));

        Assert.Equal("b", s.Node.Id);
        Assert.Equal(DialogueState.Speaking, s.State);
    }

    [Fact]
    public void A_handler_can_advance_synchronously_from_the_selection_event()
    {
        var s = new DialogueSession(Node(id: "a", line: ""));
        s.ChoiceSelected += (_, _) => s.Advance(Node(id: "b", line: ""));
        s.Choose(0);
        s.Tick(0.2);

        Assert.Equal("b", s.Node.Id);
        Assert.Equal(DialogueState.Choosing, s.State);
    }

    [Fact]
    public void Waiting_notice_appears_after_five_seconds_but_the_session_stays_open()
    {
        var s = new DialogueSession(Node(line: ""));
        s.Choose(0);
        Run(s, 0.2);
        Run(s, 4.7);
        Assert.False(s.ShowWaitingNotice);
        Run(s, 0.5);

        Assert.True(s.ShowWaitingNotice);
        Assert.Equal(DialogueState.Waiting, s.State);
    }

    [Fact]
    public void Challenge_result_shows_a_one_second_banner_then_raises_done_once()
    {
        var s = new DialogueSession(Node(line: ""));
        var probe = new Probe(s);
        s.Choose(0);
        Run(s, 0.2);
        probe.Cues.Clear();

        Assert.True(s.PlayChallengeResult(true));
        Assert.False(s.Choose(1));
        Assert.False(s.Advance(Node()));
        Run(s, 0.5);
        Assert.Equal(1.0, s.BannerAlpha, 6);
        Run(s, 0.4);
        Assert.Equal(0, probe.ChallengeDone);
        Run(s, 0.2);

        Assert.Equal(1, probe.ChallengeDone);
        Assert.Equal(new[] { DialogueSession.AcceptCue }, probe.Cues);
        Assert.Equal(DialogueState.Waiting, s.State);
        Assert.True(s.ChallengeSucceeded);
        Assert.True(s.Advance(Node(id: "after", line: "")));
    }

    [Fact]
    public void A_failed_challenge_requests_the_error_cue()
    {
        var s = new DialogueSession(Node(line: ""));
        var probe = new Probe(s);
        s.Choose(0);
        Run(s, 0.2);
        probe.Cues.Clear();

        s.PlayChallengeResult(false);

        Assert.Equal(new[] { DialogueSession.ErrorCue }, probe.Cues);
        Assert.False(s.ChallengeSucceeded);
    }

    [Fact]
    public void A_false_validity_callback_aborts_at_once_with_no_fade_and_no_cue()
    {
        bool alive = true;
        var s = new DialogueSession(Node(), stillValid: () => alive);
        var probe = new Probe(s);
        s.Tick(0.01);
        Assert.Empty(probe.Outcomes);

        alive = false;
        s.Tick(0.016);

        Assert.Equal(new[] { DialogueOutcome.Aborted }, probe.Outcomes);
        Assert.Empty(probe.Cues);
        Assert.Equal(DialogueState.Ended, s.State);
    }

    [Fact]
    public void The_line_shows_only_with_subtitles_on_or_when_forced()
    {
        Assert.True(new DialogueSession(Node(), subtitles: true).LineVisible);
        Assert.False(new DialogueSession(Node(), subtitles: false).LineVisible);
        Assert.True(new DialogueSession(Node(force: true), subtitles: false).LineVisible);
        var silent = new DialogueSession(Node(line: ""), subtitles: true);
        Assert.False(silent.LineVisible);
    }

    [Fact]
    public void Focus_starts_on_the_first_enabled_slot_and_confirm_chooses_the_focused_one()
    {
        var s = new DialogueSession(Node(line: ""));

        Assert.Equal(0, s.FocusedSlot);
        s.FocusSlot(2);
        Assert.Equal(2, s.FocusedSlot);

        Assert.True(s.ConfirmFocused());
        Assert.Equal("Two", s.EchoText);
    }

    [Fact]
    public void Choices_are_snapshotted_when_the_node_starts()
    {
        var list = new List<DialogueChoice>(Four());
        var s = new DialogueSession(Node(line: "", choices: list));
        list.Clear();

        Assert.Equal(new[] { "Zero", "One", "Two", "Three" }, Views(s).Select(v => v.Choice?.Text));
        Assert.True(s.Choose(3));
    }

    [Fact]
    public void A_non_finite_tick_changes_nothing()
    {
        var s = new DialogueSession(Node(ms: 100));
        s.Tick(double.NaN);
        s.Tick(-1);
        Assert.Equal(DialogueState.Speaking, s.State);
        s.Tick(0.2);
        Assert.Equal(DialogueState.Choosing, s.State);
    }

    [Fact]
    public void Back_while_waiting_is_ignored_until_the_notice_then_leaves()
    {
        var s = new DialogueSession(Node(line: ""));
        var probe = new Probe(s);
        s.Choose(0);
        Run(s, 0.2);
        Run(s, 4.0);
        Assert.Equal(DialogueState.Waiting, s.State);
        Assert.False(s.Cancel());
        Assert.Equal(DialogueState.Waiting, s.State);

        Run(s, 1.2);
        Assert.True(s.ShowWaitingNotice);
        Assert.True(s.Cancel());
        Run(s, 0.3);

        Assert.Equal(DialogueState.Ended, s.State);
        Assert.Equal(new[] { DialogueOutcome.Left }, probe.Outcomes);
    }

    [Fact]
    public void A_node_with_every_choice_disabled_and_no_exit_allows_back_after_five_seconds()
    {
        var disabled = new[] { new DialogueChoice("A", Enabled: false), new DialogueChoice("B", Enabled: false) };
        var s = new DialogueSession(Node(line: "", exit: false, choices: disabled));
        var probe = new Probe(s);
        Assert.Equal(DialogueState.Choosing, s.State);
        Run(s, 4.0);
        Assert.False(s.Cancel());
        Assert.Equal(DialogueState.Choosing, s.State);

        Run(s, 1.2);
        Assert.True(s.Cancel());
        Run(s, 0.3);
        Assert.Equal(new[] { DialogueOutcome.Left }, probe.Outcomes);
    }

    [Fact]
    public void A_node_with_an_enabled_choice_and_no_exit_never_allows_back_in_choosing()
    {
        var s = new DialogueSession(Node(line: "", exit: false));
        Run(s, 6.0);
        Assert.False(s.Cancel());
        Assert.Equal(DialogueState.Choosing, s.State);
    }

    [Fact]
    public void NodeStarted_is_not_raised_for_the_first_node_and_is_raised_on_Advance()
    {
        var s = new DialogueSession(Node(id: "a", line: ""));
        var started = new List<string>();
        s.NodeStarted += n => started.Add(n.Id);
        Assert.Empty(started);
        s.Choose(0);
        s.Tick(0.2);
        Assert.Equal(DialogueState.Waiting, s.State);
        Assert.Empty(started);
        Assert.True(s.Advance(Node(id: "b", line: "Next")));
        Assert.Equal(["b"], started);
    }

    [Fact]
    public void NodeStarted_is_not_raised_when_Advance_is_refused()
    {
        var s = new DialogueSession(Node(id: "a", ms: 1000));
        var started = 0;
        s.NodeStarted += _ => started++;
        Assert.False(s.Advance(Node(id: "b")));
        Assert.Equal(0, started);
    }

    [Fact]
    public void Closing_is_raised_once_when_the_dialogue_is_closed()
    {
        var s = new DialogueSession(Node());
        var closing = 0;
        s.Closing += () => closing++;
        Assert.Equal(0, closing);
        Assert.True(s.Finish());
        Assert.Equal(1, closing);
        Assert.False(s.Finish());
        Assert.Equal(1, closing);
    }
}
