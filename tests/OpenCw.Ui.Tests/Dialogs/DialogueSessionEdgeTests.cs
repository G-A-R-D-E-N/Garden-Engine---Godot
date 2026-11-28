using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Dialogs;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class DialogueSessionEdgeTests
{
    static DialogueChoice[] Four() => new[]
    {
        new DialogueChoice("Zero"), new DialogueChoice("One"), new DialogueChoice("Two"), new DialogueChoice("Three"),
    };

    static DialogueNode Node(string line = "", IReadOnlyList<DialogueChoice>? choices = null) =>
        new("n1", "Speaker", line, choices ?? Four());

    static DialogueSession Waiting(List<string>? cues = null)
    {
        var session = new DialogueSession(Node());
        if (cues is not null) session.CueRequested += cues.Add;
        session.Choose(0);
        session.Tick(DialogueSession.OthersFadeOutSeconds);
        Assert.Equal(DialogueState.Waiting, session.State);
        return session;
    }

    static string?[] Texts(DialogueSession session) => Enumerable.Range(0, DialogueSession.SlotCount).Select(i => session.Slot(i).Choice?.Text).ToArray();

    [Fact]
    public void A_second_choice_asking_for_a_taken_slot_goes_to_its_own_position_or_the_first_free_one()
    {
        var session = new DialogueSession(Node(choices: new[]
        {
            new DialogueChoice("A", Slot: 2), new DialogueChoice("B", Slot: 2), new DialogueChoice("C"),
        }));

        Assert.Equal(new string?[] { "C", "B", "A", null }, Texts(session));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-1)]
    public void An_out_of_range_requested_slot_is_ignored_and_the_choice_keeps_its_position(int requested)
    {
        var session = new DialogueSession(Node(choices: new[] { new DialogueChoice("A", Slot: requested) }));

        Assert.Equal(new string?[] { "A", null, null, null }, Texts(session));
    }

    [Fact]
    public void A_missing_choice_is_skipped_and_the_next_one_takes_the_first_position()
    {
        var session = new DialogueSession(Node(choices: new[] { null!, new DialogueChoice("B") }));

        Assert.Equal(new string?[] { "B", null, null, null }, Texts(session));
    }

    [Fact]
    public void A_node_with_no_choice_list_finishes_straight_away()
    {
        var session = new DialogueSession(new DialogueNode("n", "S", "", null!));

        Assert.Equal(DialogueState.Closing, session.State);
    }

    [Fact]
    public void Slot_indexes_outside_zero_to_three_are_rejected()
    {
        var session = new DialogueSession(Node());

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Slot(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Slot(DialogueSession.SlotCount));
    }

    [Fact]
    public void The_echo_is_empty_until_a_choice_is_made_then_the_chosen_text()
    {
        var session = new DialogueSession(Node());
        Assert.Equal(string.Empty, session.EchoText);

        session.Choose(1);

        Assert.Equal("One", session.EchoText);
    }

    [Fact]
    public void Choosing_while_waiting_for_the_game_is_refused_without_a_cue()
    {
        var cues = new List<string>();
        var session = Waiting(cues);

        Assert.False(session.Choose(1));

        Assert.Equal(new[] { DialogueSession.AcceptCue }, cues);
        Assert.Equal(DialogueState.Waiting, session.State);
    }

    [Fact]
    public void A_challenge_result_can_only_be_played_while_waiting_and_advance_needs_a_node()
    {
        var session = new DialogueSession(Node("Hello"));
        Assert.False(session.PlayChallengeResult(true));
        Assert.Equal(DialogueState.Speaking, session.State);

        var waiting = Waiting();
        Assert.Throws<ArgumentNullException>(() => waiting.Advance(null!));
    }

    [Fact]
    public void The_panel_fades_over_the_close_time_and_is_gone_once_ended()
    {
        var session = new DialogueSession(Node());
        Assert.Equal(1.0, session.PanelAlpha);

        session.Finish();
        Assert.Equal(1.0, session.PanelAlpha);
        session.Tick(0.1);
        Assert.Equal(0.5, session.PanelAlpha);
        session.Tick(0.1);

        Assert.Equal(DialogueState.Ended, session.State);
        Assert.Equal(0.0, session.PanelAlpha);
        Assert.False(session.Finish());
    }

    [Fact]
    public void The_challenge_banner_ramps_up_holds_and_ramps_down()
    {
        var session = Waiting();
        Assert.Equal(0.0, session.BannerAlpha);
        session.PlayChallengeResult(true);
        Assert.Equal(0.0, session.BannerAlpha);

        session.Tick(0.075);
        Assert.Equal(0.5, session.BannerAlpha, 6);
        session.Tick(0.425);
        Assert.Equal(1.0, session.BannerAlpha, 6);
        session.Tick(0.425);
        Assert.Equal(0.5, session.BannerAlpha, 6);
    }

    [Theory]
    [InlineData(25, 0, 1500)]
    [InlineData(26, 0, 1560)]
    [InlineData(2, 100, 100)]
    [InlineData(2, -5, 1500)]
    [InlineData(0, 500, 0)]
    public void The_speaking_time_is_the_given_duration_or_sixty_ms_a_character_with_a_floor(int length, int durationMs, int expected)
    {
        var node = new DialogueNode("n", "S", new string('a', length), Four(), durationMs);

        Assert.Equal(expected, node.SpeakingMs);
    }

    [Fact]
    public void The_waiting_notice_is_only_for_the_waiting_state()
    {
        var session = new DialogueSession(Node());

        session.Tick(DialogueSession.WaitingNoticeSeconds);

        Assert.False(session.ShowWaitingNotice);
    }
}
