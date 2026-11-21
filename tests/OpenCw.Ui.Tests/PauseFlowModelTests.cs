using System.Linq;
using OpenCommonwealth.Services.Ui.Flow;
using OpenCommonwealth.Services.Ui.Lists;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PauseFlowModelTests
{
    static PauseFlowModel Opened(bool hasSaves)
    {
        var model = new PauseFlowModel(includeGallery: true);
        Assert.True(model.Open(hasSaves));
        return model;
    }

    static PauseFlowModel At(bool hasSaves, PauseAction action)
    {
        var model = Opened(hasSaves);
        Assert.True(model.Select(action));
        return model;
    }

    [Fact]
    public void Starts_closed()
    {
        var model = new PauseFlowModel();

        Assert.Equal(PauseFlowState.Closed, model.State);
        Assert.False(model.IsOpen);
        Assert.False(model.QuitRequested);
    }

    [Fact]
    public void Entries_are_resume_load_settings_character_appearance_gallery_quit_in_that_order()
    {
        var model = Opened(true);

        Assert.Equal(
            new[]
            {
                PauseAction.Resume, PauseAction.LoadGame, PauseAction.Settings, PauseAction.Character, PauseAction.Appearance,
                PauseAction.Gallery, PauseAction.Quit,
            },
            model.Entries.Select(e => e.Action).ToArray());
        Assert.Equal(
            new[] { "Resume", "Load Game", "Settings", "Character", "Appearance", "UI Gallery", "Quit" },
            model.Entries.Select(e => e.Label).ToArray());
    }

    [Fact]
    public void The_gallery_entry_is_left_out_unless_the_model_is_asked_to_include_it()
    {
        var model = new PauseFlowModel();
        Assert.True(model.Open(true));

        Assert.DoesNotContain(model.Entries, e => e.Action == PauseAction.Gallery);
        Assert.False(model.Select(PauseAction.Gallery));
        Assert.Equal(PauseAction.Quit, model.Entries[^1].Action);
    }

    [Fact]
    public void Load_game_is_disabled_with_a_reason_when_there_are_no_saves()
    {
        var model = Opened(false);
        var load = model.Entries.Single(e => e.Action == PauseAction.LoadGame);

        Assert.False(load.Enabled);
        Assert.Equal(PauseFlowModel.NoSaves, load.DisabledReason);
        Assert.All(model.Entries.Where(e => e.Action != PauseAction.LoadGame), e => Assert.True(e.Enabled));
    }

    [Fact]
    public void Load_game_is_enabled_when_a_save_exists()
    {
        var load = Opened(true).Entries.Single(e => e.Action == PauseAction.LoadGame);

        Assert.True(load.Enabled);
        Assert.Equal("", load.DisabledReason);
    }

    [Fact]
    public void Open_enters_pause_with_resume_selected_and_refuses_a_second_open()
    {
        var model = Opened(true);

        Assert.Equal(PauseFlowState.Pause, model.State);
        Assert.True(model.IsOpen);
        Assert.Equal(PauseAction.Resume, model.Selected.Action);
        Assert.False(model.Open(true));
    }

    [Fact]
    public void Moving_skips_the_disabled_entry_and_wraps()
    {
        var model = Opened(false);

        model.Move(CursorMove.Down);
        Assert.Equal(PauseAction.Settings, model.Selected.Action);
        model.Move(CursorMove.Down);
        Assert.Equal(PauseAction.Character, model.Selected.Action);
        model.Move(CursorMove.Down);
        Assert.Equal(PauseAction.Appearance, model.Selected.Action);
        model.Move(CursorMove.Down);
        Assert.Equal(PauseAction.Gallery, model.Selected.Action);
        model.Move(CursorMove.Down);
        Assert.Equal(PauseAction.Quit, model.Selected.Action);
        model.Move(CursorMove.Down);
        Assert.Equal(PauseAction.Resume, model.Selected.Action);
        model.Move(CursorMove.Up);
        Assert.Equal(PauseAction.Quit, model.Selected.Action);
    }

    [Fact]
    public void Home_and_end_jump_to_the_first_and_last_enabled_entry()
    {
        var model = Opened(true);

        model.Move(CursorMove.End);
        Assert.Equal(PauseAction.Quit, model.Selected.Action);
        model.Move(CursorMove.Home);
        Assert.Equal(PauseAction.Resume, model.Selected.Action);
    }

    [Fact]
    public void Select_refuses_a_disabled_entry_and_keeps_the_cursor()
    {
        var model = Opened(false);

        Assert.False(model.Select(PauseAction.LoadGame));
        Assert.Equal(PauseAction.Resume, model.Selected.Action);
    }

    [Fact]
    public void Activating_resume_closes_the_flow()
    {
        var model = At(true, PauseAction.Resume);

        Assert.Equal(PauseAction.Resume, model.Activate()?.Action);
        Assert.Equal(PauseFlowState.Closed, model.State);
    }

    [Fact]
    public void Activating_settings_load_gallery_and_quit_move_to_their_states()
    {
        var settings = At(true, PauseAction.Settings);
        var load = At(true, PauseAction.LoadGame);
        var gallery = At(true, PauseAction.Gallery);
        var quit = At(true, PauseAction.Quit);

        Assert.Equal(PauseAction.Settings, settings.Activate()?.Action);
        Assert.Equal(PauseAction.LoadGame, load.Activate()?.Action);
        Assert.Equal(PauseAction.Gallery, gallery.Activate()?.Action);
        Assert.Equal(PauseAction.Quit, quit.Activate()?.Action);
        Assert.Equal(PauseFlowState.Settings, settings.State);
        Assert.Equal(PauseFlowState.Load, load.State);
        Assert.Equal(PauseFlowState.Gallery, gallery.State);
        Assert.Equal(PauseFlowState.ConfirmQuit, quit.State);
    }

    [Fact]
    public void Activating_does_nothing_when_closed_or_already_in_a_sub_state()
    {
        var closed = new PauseFlowModel();
        var nested = At(true, PauseAction.Settings);
        nested.Activate();

        Assert.Null(closed.Activate());
        Assert.Null(nested.Activate());
        Assert.Equal(PauseFlowState.Settings, nested.State);
    }

    [Fact]
    public void Back_steps_out_one_level_at_a_time()
    {
        foreach (var action in new[] { PauseAction.Settings, PauseAction.LoadGame, PauseAction.Gallery, PauseAction.Quit })
        {
            var model = At(true, action);
            model.Activate();

            Assert.True(model.Back());
            Assert.Equal(PauseFlowState.Pause, model.State);
            Assert.True(model.Back());
            Assert.Equal(PauseFlowState.Closed, model.State);
            Assert.False(model.Back());
        }
    }

    [Fact]
    public void Confirming_quit_requests_it_and_closes()
    {
        var model = At(true, PauseAction.Quit);
        model.Activate();

        Assert.True(model.ConfirmQuit(true));
        Assert.True(model.QuitRequested);
        Assert.Equal(PauseFlowState.Closed, model.State);
    }

    [Fact]
    public void Declining_quit_returns_to_the_pause_menu_without_a_request()
    {
        var model = At(true, PauseAction.Quit);
        model.Activate();

        Assert.True(model.ConfirmQuit(false));
        Assert.False(model.QuitRequested);
        Assert.Equal(PauseFlowState.Pause, model.State);
    }

    [Fact]
    public void Quit_confirmation_is_refused_outside_the_confirm_state()
    {
        var model = Opened(true);

        Assert.False(model.ConfirmQuit(true));
        Assert.False(model.QuitRequested);
        Assert.Equal(PauseFlowState.Pause, model.State);
    }

    [Fact]
    public void Choosing_a_save_closes_the_flow_only_from_the_load_state()
    {
        var model = At(true, PauseAction.LoadGame);

        Assert.False(model.LoadChosen());
        model.Activate();
        Assert.True(model.LoadChosen());
        Assert.Equal(PauseFlowState.Closed, model.State);
    }

    [Fact]
    public void Close_forces_the_flow_shut_from_any_state()
    {
        var model = At(true, PauseAction.Settings);
        model.Activate();

        Assert.True(model.Close());
        Assert.Equal(PauseFlowState.Closed, model.State);
        Assert.False(model.Close());
    }

    [Fact]
    public void Reopening_clears_an_earlier_quit_request()
    {
        var model = At(true, PauseAction.Quit);
        model.Activate();
        model.ConfirmQuit(true);

        Assert.True(model.Open(true));
        Assert.False(model.QuitRequested);
    }

    [Fact]
    public void Refresh_keeps_the_selection_while_it_stays_enabled()
    {
        var model = At(true, PauseAction.Settings);

        model.Refresh(false);

        Assert.Equal(PauseAction.Settings, model.Selected.Action);
    }

    [Fact]
    public void Refresh_moves_the_cursor_to_resume_when_the_selected_entry_becomes_disabled()
    {
        var model = At(true, PauseAction.LoadGame);

        model.Refresh(false);

        Assert.Equal(PauseAction.Resume, model.Selected.Action);
        Assert.False(model.Entries.Single(e => e.Action == PauseAction.LoadGame).Enabled);
    }

    [Fact]
    public void Modal_session_enters_once_and_exits_once()
    {
        var session = new ModalSession();

        Assert.Equal(ModalChange.None, session.Update(false, true));
        Assert.Equal(ModalChange.Entered, session.Update(true, true));
        Assert.True(session.Active);
        Assert.Equal(ModalChange.None, session.Update(true, false));
        Assert.Equal(ModalChange.ExitedRestoreCapture, session.Update(false, false));
        Assert.False(session.Active);
        Assert.Equal(ModalChange.None, session.Update(false, false));
    }

    [Fact]
    public void Modal_session_restores_capture_only_if_the_mouse_was_captured_on_entry()
    {
        var session = new ModalSession();

        session.Update(true, false);

        Assert.Equal(ModalChange.Exited, session.Update(false, false));
    }

    [Fact]
    public void Modal_session_forgets_the_previous_capture_between_sessions()
    {
        var session = new ModalSession();
        session.Update(true, true);
        session.Update(false, false);

        session.Update(true, false);

        Assert.Equal(ModalChange.Exited, session.Update(false, true));
    }
}
