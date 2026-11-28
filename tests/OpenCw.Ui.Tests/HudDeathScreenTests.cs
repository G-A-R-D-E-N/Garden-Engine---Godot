using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudDeathScreenTests
{
    sealed class Harness
    {
        public readonly DeathScreenModel Model;
        public readonly List<string> Cues = new();
        public readonly List<DeathChoice> Chosen = new();
        public readonly List<DeathChoice> Performed = new();
        public string? Error;

        public Harness(DeathInfo? info = null)
        {
            Model = new DeathScreenModel(info ?? new DeathInfo("Shot", true, "Quick save", true), choice =>
            {
                Performed.Add(choice);
                return Error;
            });
            Model.CueRequested += Cues.Add;
            Model.Chosen += Chosen.Add;
        }

        public void Run(double seconds)
        {
            for (double t = 0; t < seconds - 1e-9; t += 0.01)
                Model.Tick(0.01);
        }
    }

    [Fact]
    public void The_list_is_invisible_and_unfocused_until_2800_ms_then_the_first_enabled_action_has_focus()
    {
        var h = new Harness();
        h.Run(2.7);
        Assert.Equal(0.0, h.Model.ActionsAlpha, 6);
        Assert.Equal(DeathPhase.Intro, h.Model.Phase);
        Assert.DoesNotContain(h.Model.Actions, a => a.Focused);
        Assert.False(h.Model.Activate(0));

        h.Run(0.4);

        Assert.Equal(1.0, h.Model.ActionsAlpha, 6);
        Assert.Equal(DeathPhase.Interactive, h.Model.Phase);
        Assert.Equal(0, h.Model.FocusedIndex);
        Assert.Equal(new[] { DeathScreenModel.WarnCue, DeathScreenModel.OpenCue }, h.Cues);
    }

    [Fact]
    public void The_timeline_fades_tint_title_and_cause_in_order()
    {
        var h = new Harness();
        h.Run(0.6);
        Assert.InRange(h.Model.TintAlpha, 0.34, 0.36);
        Assert.Equal(0.0, h.Model.TitleAlpha, 6);
        Assert.False(h.Model.TreePaused);
        h.Run(0.7);
        Assert.Equal(DeathScreenModel.TintMaxAlpha, h.Model.TintAlpha, 6);
        Assert.True(h.Model.TreePaused);
        Assert.InRange(h.Model.TitleAlpha, 0.01, 0.99);
        Assert.Equal(0.0, h.Model.CauseAlpha, 6);
        h.Run(0.9);
        Assert.Equal(1.0, h.Model.TitleAlpha, 6);
        Assert.InRange(h.Model.CauseAlpha, 0.49, 0.51);
        Assert.Equal("Shot", h.Model.CauseLine);
    }

    [Fact]
    public void An_empty_cause_is_omitted()
    {
        var h = new Harness(new DeathInfo(CanLoadOther: true));
        h.Run(3.2);
        Assert.Equal(0.0, h.Model.CauseAlpha, 6);
    }

    [Fact]
    public void Confirm_before_the_list_skips_once_and_the_next_confirm_activates_the_focused_action()
    {
        var h = new Harness();
        h.Run(0.5);

        Assert.True(h.Model.Confirm());
        Assert.Equal(DeathPhase.Interactive, h.Model.Phase);
        Assert.Equal(1.0, h.Model.ActionsAlpha, 6);
        Assert.Empty(h.Chosen);
        Assert.False(h.Model.Skip());

        Assert.True(h.Model.Confirm());
        Assert.Equal(new[] { DeathChoice.LoadLast }, h.Chosen);
    }

    [Fact]
    public void Without_a_last_save_load_last_is_disabled_skipped_by_focus_and_never_chosen()
    {
        var h = new Harness(new DeathInfo(HasLastSave: false, CanLoadOther: true));
        h.Run(3.2);

        Assert.False(h.Model.Actions[0].Enabled);
        Assert.Equal(1, h.Model.FocusedIndex);
        h.Model.Move(-1);
        Assert.Equal(1, h.Model.FocusedIndex);
        h.Model.Hover(0);
        Assert.Equal(1, h.Model.FocusedIndex);
        Assert.False(h.Model.Activate(0));
        Assert.Empty(h.Chosen);
    }

    [Fact]
    public void Focus_moves_without_wrapping()
    {
        var h = new Harness();
        h.Run(3.2);
        h.Model.Move(-1);
        Assert.Equal(0, h.Model.FocusedIndex);
        for (int i = 0; i < 6; i++)
            h.Model.Move(1);
        Assert.Equal(3, h.Model.FocusedIndex);
    }

    [Fact]
    public void Cancel_does_nothing_but_request_an_error_cue()
    {
        var h = new Harness();
        h.Run(3.2);
        h.Cues.Clear();

        h.Model.Cancel();

        Assert.Equal(new[] { DeathScreenModel.ErrorCue }, h.Cues);
        Assert.Equal(DeathPhase.Interactive, h.Model.Phase);
        Assert.Empty(h.Chosen);
    }

    [Fact]
    public void Main_menu_and_quit_ask_first_and_cancelling_returns_focus_with_no_event()
    {
        foreach (int index in new[] { 2, 3 })
        {
            var h = new Harness();
            h.Run(3.2);

            Assert.True(h.Model.Activate(index));
            Assert.Equal(DeathPhase.Confirming, h.Model.Phase);
            Assert.Equal(DeathChoice.MainMenu + (index - 2), h.Model.PendingConfirm);
            Assert.Empty(h.Chosen);

            Assert.True(h.Model.ResolveConfirm(false));
            Assert.Equal(DeathPhase.Interactive, h.Model.Phase);
            Assert.Equal(index, h.Model.FocusedIndex);
            Assert.Empty(h.Chosen);

            h.Model.Activate(index);
            h.Model.ResolveConfirm(true);
            Assert.Single(h.Chosen);
        }
    }

    [Fact]
    public void A_confirmation_is_not_offered_while_the_list_is_still_fading_in()
    {
        var h = new Harness();
        h.Run(2.85);

        Assert.Equal(DeathPhase.Interactive, h.Model.Phase);
        Assert.False(h.Model.Activate(2));
        Assert.Equal(DeathPhase.Interactive, h.Model.Phase);
    }

    [Fact]
    public void Activation_raises_chosen_once_even_when_repeated_and_runs_the_callback_after_the_400_ms_fade()
    {
        var h = new Harness();
        h.Run(3.2);

        Assert.True(h.Model.Activate(1));
        Assert.False(h.Model.Activate(1));
        Assert.False(h.Model.Confirm());

        Assert.Equal(new[] { DeathChoice.LoadOther }, h.Chosen);
        Assert.Equal(0.0, h.Model.ActionsAlpha, 6);
        h.Run(0.2);
        Assert.InRange(h.Model.CurtainAlpha, 0.4, 0.6);
        Assert.Empty(h.Performed);
        h.Run(0.3);

        Assert.Equal(new[] { DeathChoice.LoadOther }, h.Performed);
        Assert.Equal(DeathPhase.Done, h.Model.Phase);
        Assert.Equal(1.0, h.Model.CurtainAlpha, 6);
    }

    [Fact]
    public void A_failed_callback_restores_the_buttons_and_shows_the_message_for_five_seconds()
    {
        var h = new Harness { Error = "Cannot read" };
        h.Run(3.2);
        h.Model.Activate(0);
        h.Cues.Clear();
        h.Run(0.5);

        Assert.Equal(DeathPhase.Interactive, h.Model.Phase);
        Assert.Equal("Cannot read", h.Model.CauseLine);
        Assert.Equal(1.0, h.Model.ActionsAlpha, 6);
        Assert.Equal(0.0, h.Model.CurtainAlpha, 6);
        Assert.Contains(DeathScreenModel.ErrorCue, h.Cues);
        h.Run(4.8);
        Assert.Equal("Cannot read", h.Model.CauseLine);
        h.Run(0.4);
        Assert.Equal("Shot", h.Model.CauseLine);

        h.Error = null;
        Assert.True(h.Model.Activate(0));
        Assert.Equal(2, h.Chosen.Count);
    }

    [Fact]
    public void Auto_reload_counts_down_and_chooses_the_last_save()
    {
        var h = new Harness(new DeathInfo("Shot", true, "Quick save", true, 3));
        h.Run(2.9);
        Assert.True(h.Model.CountdownActive);
        Assert.InRange(h.Model.AutoReloadRemaining, 2.7, 3.0);

        h.Run(3.2);

        Assert.Equal(new[] { DeathChoice.LoadLast }, h.Chosen);
    }

    [Fact]
    public void Navigation_cancels_auto_reload_for_good()
    {
        var h = new Harness(new DeathInfo("Shot", true, "Quick save", true, 3));
        h.Run(3.0);
        h.Model.Move(1);
        Assert.False(h.Model.CountdownActive);

        h.Run(5);

        Assert.Empty(h.Chosen);
        Assert.Equal(DeathPhase.Interactive, h.Model.Phase);
    }

    [Fact]
    public void Auto_reload_does_nothing_without_a_last_save()
    {
        var h = new Harness(new DeathInfo("Shot", false, "", true, 1));
        h.Run(6);
        Assert.Empty(h.Chosen);
    }

    [Fact]
    public void Hide_unpauses_the_tree_and_a_second_call_does_nothing()
    {
        var h = new Harness();
        h.Run(2);
        Assert.True(h.Model.TreePaused);

        Assert.True(h.Model.Hide());
        Assert.False(h.Model.Hide());

        Assert.False(h.Model.TreePaused);
        Assert.Equal(DeathPhase.Hidden, h.Model.Phase);
        h.Model.Tick(1);
        Assert.False(h.Model.Activate(0));
    }

    [Fact]
    public void Ignored_ticks_and_non_finite_info_do_not_break_the_screen()
    {
        var h = new Harness(new DeathInfo("x", true, "l", true, double.NaN));
        h.Model.Tick(double.NaN);
        h.Model.Tick(-3);
        Assert.Equal(DeathPhase.Intro, h.Model.Phase);
        h.Run(3.2);
        Assert.False(h.Model.CountdownActive);
    }

    [Fact]
    public void A_callback_that_hides_the_screen_leaves_the_phase_hidden()
    {
        DeathScreenModel? model = null;
        model = new DeathScreenModel(new DeathInfo("Shot", true, "Quick save", true), _ =>
        {
            model!.Hide();
            return null;
        });
        for (double t = 0; t < 3.2; t += 0.01)
            model.Tick(0.01);
        Assert.True(model.Activate(0));
        for (double t = 0; t < 0.5; t += 0.01)
            model.Tick(0.01);

        Assert.Equal(DeathPhase.Hidden, model.Phase);
        Assert.False(model.TreePaused);
        Assert.Equal(0.0, model.CurtainAlpha, 6);
    }

    [Fact]
    public void A_successful_callback_ends_in_done_so_the_menu_can_close_itself()
    {
        var h = new Harness();
        h.Run(3.2);
        h.Model.Activate(0);
        h.Run(0.5);

        Assert.Equal(DeathPhase.Done, h.Model.Phase);
        Assert.True(h.Model.Hide());
        Assert.False(h.Model.TreePaused);
    }
}
