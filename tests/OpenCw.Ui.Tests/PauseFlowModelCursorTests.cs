using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Flow;
using OpenCommonwealth.Services.Ui.Lists;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PauseFlowModelCursorTests
{
    static PauseFlowModel Opened(bool hasSaves, bool gallery = false, bool services = false)
    {
        var model = new PauseFlowModel(gallery, services);
        Assert.True(model.Open(hasSaves));
        return model;
    }

    [Fact]
    public void Page_keys_jump_to_the_first_and_last_enabled_entry_like_home_and_end()
    {
        var model = Opened(false);
        model.Select(PauseAction.Character);

        model.Move(CursorMove.PageDown);
        Assert.Equal(PauseAction.Quit, model.Selected.Action);
        Assert.Equal(5, model.SelectedIndex);

        model.Move(CursorMove.PageUp);
        Assert.Equal(PauseAction.Resume, model.Selected.Action);
        Assert.Equal(0, model.SelectedIndex);
    }

    [Fact]
    public void Up_from_the_first_entry_wraps_to_the_last_and_up_skips_a_disabled_entry()
    {
        var model = Opened(false);

        model.Move(CursorMove.Up);
        Assert.Equal(PauseAction.Quit, model.Selected.Action);

        model.Select(PauseAction.Settings);
        model.Move(CursorMove.Up);
        Assert.Equal(PauseAction.Resume, model.Selected.Action);
    }

    [Fact]
    public void Moving_with_saves_visits_every_entry_once_in_order_and_wraps_to_the_start()
    {
        var model = Opened(true, gallery: true, services: true);
        var visited = new List<PauseAction> { model.Selected.Action };

        for (var i = 0; i < model.Entries.Count; i++)
        {
            model.Move(CursorMove.Down);
            visited.Add(model.Selected.Action);
        }

        Assert.Equal(
            new[]
            {
                PauseAction.Resume, PauseAction.LoadGame, PauseAction.Settings, PauseAction.Character, PauseAction.Appearance,
                PauseAction.Services, PauseAction.Gallery, PauseAction.Quit, PauseAction.Resume,
            },
            visited.ToArray());
    }

    [Fact]
    public void Next_category_leaves_the_cursor_alone()
    {
        var model = Opened(true);
        model.Select(PauseAction.Appearance);

        model.Move(CursorMove.NextCategory);

        Assert.Equal(PauseAction.Appearance, model.Selected.Action);
        Assert.Equal(4, model.SelectedIndex);
    }

    [Fact]
    public void Reopening_starts_on_resume_again_and_re_reads_whether_saves_exist()
    {
        var model = Opened(false);
        model.Select(PauseAction.Quit);
        Assert.False(model.Entries.Single(e => e.Action == PauseAction.LoadGame).Enabled);
        Assert.True(model.Close());

        Assert.True(model.Open(true));

        Assert.Equal(0, model.SelectedIndex);
        Assert.Equal(PauseAction.Resume, model.Selected.Action);
        Assert.True(model.Entries.Single(e => e.Action == PauseAction.LoadGame).Enabled);
    }

    [Fact]
    public void Refresh_keeps_the_cursor_on_quit_when_load_game_becomes_enabled()
    {
        var model = Opened(false, gallery: true);
        model.Select(PauseAction.Quit);
        Assert.Equal(6, model.SelectedIndex);

        model.Refresh(true);

        Assert.Equal(PauseAction.Quit, model.Selected.Action);
        Assert.Equal(6, model.SelectedIndex);
        Assert.True(model.Entries[1].Enabled);
    }

    [Fact]
    public void Selecting_an_entry_that_is_absent_changes_nothing()
    {
        var model = Opened(true);
        model.Select(PauseAction.Settings);

        Assert.False(model.Select(PauseAction.Gallery));
        Assert.False(model.Select(PauseAction.Services));
        Assert.Equal(PauseAction.Settings, model.Selected.Action);
    }

    [Fact]
    public void Backing_out_of_a_sub_state_keeps_the_cursor_on_the_entry_that_opened_it()
    {
        var model = Opened(true);
        model.Select(PauseAction.Character);
        model.Activate();
        model.Back();

        Assert.Equal(PauseFlowState.Pause, model.State);
        Assert.Equal(PauseAction.Character, model.Selected.Action);
        Assert.True(model.Back());
        Assert.Equal(6, model.Entries.Count);
    }
}
