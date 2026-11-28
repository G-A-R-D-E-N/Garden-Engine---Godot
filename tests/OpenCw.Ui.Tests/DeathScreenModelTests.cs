using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public class DeathScreenModelTests
{
    static DeathScreenModel Make(DeathInfo? info = null, List<string>? cues = null, List<DeathChoice>? chosen = null)
    {
        var model = new DeathScreenModel(info ?? new DeathInfo("Shot", true, "Quick save", true), _ => null);
        if (cues is not null) model.CueRequested += cues.Add;
        if (chosen is not null) model.Chosen += chosen.Add;
        return model;
    }

    static DeathScreenModel Opened(DeathInfo? info = null, List<string>? cues = null, List<DeathChoice>? chosen = null)
    {
        var model = Make(info, cues, chosen);
        model.Tick(3.2);
        return model;
    }

    [Fact]
    public void The_actions_are_listed_in_a_fixed_order_with_labels_and_enabled_flags_from_the_info()
    {
        var model = Make(new DeathInfo("", true, "", false));

        Assert.Equal(
            new[]
            {
                new DeathAction(DeathChoice.LoadLast, "Load last save", true, false),
                new DeathAction(DeathChoice.LoadOther, "Load game", false, false),
                new DeathAction(DeathChoice.MainMenu, "Main menu", true, false),
                new DeathAction(DeathChoice.Quit, "Quit game", true, false),
            },
            model.Actions);
    }

    [Fact]
    public void The_last_save_label_is_only_shown_when_there_is_a_last_save()
    {
        Assert.Equal("Quick save", Make(new DeathInfo("", true, "Quick save")).LastSaveLabel);
        Assert.Equal("", Make(new DeathInfo("", false, "Quick save")).LastSaveLabel);
    }

    [Fact]
    public void Cancelling_a_confirmation_returns_to_the_list_with_the_asked_action_focused_and_no_choice()
    {
        var cues = new List<string>();
        var chosen = new List<DeathChoice>();
        var model = Opened(cues: cues, chosen: chosen);

        Assert.True(model.Activate(2));
        Assert.Equal(DeathPhase.Confirming, model.Phase);
        Assert.Equal(DeathChoice.MainMenu, model.PendingConfirm);

        model.Cancel();

        Assert.Equal(DeathPhase.Interactive, model.Phase);
        Assert.Null(model.PendingConfirm);
        Assert.Equal(2, model.FocusedIndex);
        Assert.Empty(chosen);
        Assert.Equal(new[] { DeathScreenModel.WarnCue, DeathScreenModel.OpenCue }, cues);
    }

    [Fact]
    public void Accepting_a_confirmation_commits_the_asked_action()
    {
        var chosen = new List<DeathChoice>();
        var model = Opened(chosen: chosen);
        model.Activate(3);

        Assert.True(model.ResolveConfirm(true));

        Assert.Equal(new[] { DeathChoice.Quit }, chosen);
        Assert.Equal(DeathPhase.Leaving, model.Phase);
        Assert.Equal(3, model.FocusedIndex);
    }

    [Fact]
    public void Resolving_a_confirmation_that_was_never_asked_is_refused()
    {
        var chosen = new List<DeathChoice>();
        var model = Opened(chosen: chosen);

        Assert.False(model.ResolveConfirm(true));
        Assert.False(model.ResolveConfirm(false));

        Assert.Empty(chosen);
        Assert.Equal(DeathPhase.Interactive, model.Phase);
    }

    [Fact]
    public void Confirm_and_skip_do_nothing_while_a_confirmation_is_pending()
    {
        var model = Opened();
        model.Activate(3);

        Assert.False(model.Confirm());
        Assert.False(model.Skip());
        Assert.Equal(DeathPhase.Confirming, model.Phase);
        Assert.Equal(DeathChoice.Quit, model.PendingConfirm);
    }

    [Fact]
    public void Hover_focuses_an_enabled_action_only_once_the_list_is_open()
    {
        var model = Make();

        model.Hover(1);
        Assert.Equal(-1, model.FocusedIndex);

        model.Tick(3.2);
        Assert.Equal(0, model.FocusedIndex);

        model.Hover(1);
        Assert.Equal(1, model.FocusedIndex);
        Assert.True(model.Actions[1].Focused);
        Assert.False(model.Actions[0].Focused);
    }

    [Fact]
    public void Hover_ignores_disabled_and_out_of_range_actions()
    {
        var model = Opened(new DeathInfo("", true, "", false));

        model.Hover(1);
        model.Hover(-1);
        model.Hover(4);

        Assert.Equal(0, model.FocusedIndex);
    }

    [Fact]
    public void Hovering_an_action_cancels_the_auto_reload_countdown()
    {
        var model = Opened(new DeathInfo("", true, "Quick save", true, 30));
        Assert.True(model.CountdownActive);

        model.Hover(2);

        Assert.False(model.CountdownActive);
        Assert.Equal(0.0, model.AutoReloadRemaining);
    }

    [Fact]
    public void A_blank_failure_message_reads_failed_and_replaces_the_cause_line()
    {
        var model = Opened(new DeathInfo("Shot", true, "Quick save", true));
        Assert.Equal("Shot", model.CauseLine);

        model.Failed("   ");

        Assert.Equal("Failed", model.FailureMessage);
        Assert.Equal("Failed", model.CauseLine);
        Assert.Equal(DeathPhase.Interactive, model.Phase);
    }

    [Fact]
    public void A_failure_while_leaving_puts_the_buttons_back_and_restarts_the_fade_for_the_next_try()
    {
        var model = Opened();
        model.Activate(0);
        model.Tick(0.2);
        Assert.Equal(DeathPhase.Leaving, model.Phase);
        Assert.Equal(0.5, model.CurtainAlpha, 6);

        model.Failed("No save");

        Assert.Equal(DeathPhase.Interactive, model.Phase);
        Assert.Equal(0.0, model.CurtainAlpha, 6);
        Assert.Equal("No save", model.CauseLine);
    }

    [Fact]
    public void A_failure_reported_after_the_screen_was_hidden_is_ignored()
    {
        var cues = new List<string>();
        var model = Opened(cues: cues);
        model.Hide();
        cues.Clear();

        model.Failed("Late");

        Assert.Equal(DeathPhase.Hidden, model.Phase);
        Assert.Equal("", model.FailureMessage);
        Assert.Empty(cues);
    }

    [Fact]
    public void Cancel_after_the_screen_is_hidden_or_leaving_raises_no_cue()
    {
        var cues = new List<string>();
        var model = Opened(cues: cues);
        model.Activate(0);
        cues.Clear();

        model.Cancel();
        Assert.Empty(cues);

        model.Hide();
        model.Cancel();
        Assert.Empty(cues);
    }
}
