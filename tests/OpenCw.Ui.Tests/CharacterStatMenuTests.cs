using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterStatMenuTests
{
    static readonly GenericCharacterData Data = new();

    const int Undo = 7;
    const int Redo = 8;
    const int Reset = 9;
    const int Preset = 10;
    const int Confirm = 11;

    static (StatAllocationController Ctrl, StatAllocationModel Model) Make(bool withSheet = true, bool require = true)
    {
        var model = new StatAllocationModel(Data.Stats, Data.StatPoints, require, Data.StatPresets);
        var sheet = withSheet
            ? new CharacterSheetModel(Data.DerivedStats, Data.Stats.ToDictionary(s => s.Id, s => (double)s.Min))
            : null;
        return (new StatAllocationController(model, sheet), model);
    }

    [Fact]
    public void Rows_are_the_stats_followed_by_the_command_rows()
    {
        var (ctrl, _) = Make();

        Assert.Equal(12, ctrl.RowCount);
        Assert.Equal(ValueRowKind.Stepper, ctrl.Kind(0));
        Assert.Equal(ValueRowKind.Command, ctrl.Kind(Undo));
        Assert.Equal("Stat 1", ctrl.Label(0));
        Assert.Equal("1", ctrl.ValueText(0));
        Assert.Equal(0.0, ctrl.Fraction(0), 6);
        Assert.Equal(-1.0, ctrl.Fraction(Undo), 6);
        Assert.Equal("Preset: Even", ctrl.Label(Preset));
        Assert.Equal("Confirm", ctrl.Label(Confirm));
        Assert.Equal("Points left: 5 of 5", ctrl.Header);
        Assert.Equal(0, ctrl.SelectedIndex);
    }

    [Fact]
    public void Left_and_right_change_the_selected_stat_and_up_and_down_move_with_wrap()
    {
        var (ctrl, model) = Make();

        Assert.True(ctrl.Handle(MenuInputAction.Right));
        Assert.Equal(2, model.Value("s1"));
        Assert.True(ctrl.Handle(MenuInputAction.Down));
        Assert.True(ctrl.Handle(MenuInputAction.Right));
        Assert.Equal(2, model.Value("s2"));
        Assert.True(ctrl.Handle(MenuInputAction.Left));
        Assert.Equal(1, model.Value("s2"));
        Assert.Equal("Points left: 4 of 5", ctrl.Header);

        Assert.True(ctrl.Handle(MenuInputAction.Up));
        Assert.True(ctrl.Handle(MenuInputAction.Up));
        Assert.Equal(ctrl.RowCount - 1, ctrl.SelectedIndex);
    }

    [Fact]
    public void An_exhausted_pool_reports_no_points_left_and_the_row_cannot_step_up()
    {
        var (ctrl, model) = Make();
        for (var i = 0; i < 5; i++) ctrl.Step(0, 1);

        Assert.False(ctrl.CanStep(0, 1));
        Assert.True(ctrl.CanStep(0, -1));
        Assert.False(ctrl.Step(0, 1));
        Assert.Equal("No points left.", ctrl.Message);
        Assert.Equal(6, model.Value("s1"));
    }

    [Fact]
    public void Confirm_is_refused_with_a_message_while_points_are_unspent()
    {
        var (ctrl, model) = Make();
        var finished = new List<bool>();
        ctrl.Finished += finished.Add;

        Assert.True(ctrl.Handle(MenuInputAction.Accept));
        Assert.Equal("Spend every point before confirming (5 left).", ctrl.Message);
        Assert.False(ctrl.IsFinished);
        Assert.Empty(finished);
        Assert.False(model.IsConfirmed);

        ctrl.Activate(Preset);
        ctrl.Activate(Confirm);
        Assert.Equal(new[] { true }, finished.ToArray());
        Assert.False(ctrl.Handle(MenuInputAction.Right));
    }

    [Fact]
    public void Back_cancels_the_allocation_and_finishes_once()
    {
        var (ctrl, model) = Make();
        var finished = new List<bool>();
        ctrl.Finished += finished.Add;
        ctrl.Handle(MenuInputAction.Right);

        Assert.True(ctrl.Handle(MenuInputAction.Back));
        Assert.True(model.IsCancelled);
        Assert.False(ctrl.Handle(MenuInputAction.Back));
        Assert.Equal(new[] { false }, finished.ToArray());
    }

    [Fact]
    public void The_side_panel_shows_current_to_after_for_each_derived_value()
    {
        var (ctrl, _) = Make();
        Assert.All(ctrl.SideLines.Skip(1), line => Assert.Equal(LineTone.Muted, line.Tone));

        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal("Effect of changes", ctrl.SideLines[0].Text);
        Assert.Contains(ctrl.SideLines, l => l.Tone == LineTone.Good && l.Text.StartsWith("Total of 1 and 2: 2  >  3"));
        Assert.Contains(ctrl.SideLines, l => l.Tone == LineTone.Muted && l.Text == "Double of 3: 2");
    }

    [Fact]
    public void Without_a_sheet_there_is_no_side_panel()
    {
        var (ctrl, _) = Make(withSheet: false);

        Assert.Empty(ctrl.SideLines);
    }

    [Fact]
    public void Tab_actions_cycle_through_presets_and_the_hints_name_the_next_ones()
    {
        var (ctrl, model) = Make();
        Assert.Equal("Preset: Even", ctrl.Hints.Single(h => h.Action == MenuInputAction.TabNext).Label);
        Assert.Equal("Preset: Focused", ctrl.Hints.Single(h => h.Action == MenuInputAction.TabPrev).Label);

        Assert.True(ctrl.Handle(MenuInputAction.TabNext));
        Assert.Equal(2, model.Value("s5"));
        Assert.Equal("Preset: Focused", ctrl.Hints.Single(h => h.Action == MenuInputAction.TabNext).Label);

        Assert.True(ctrl.Handle(MenuInputAction.TabNext));
        Assert.Equal(6, model.Value("s1"));
        Assert.Equal(1, model.Value("s5"));

        Assert.True(ctrl.Handle(MenuInputAction.TabPrev));
        Assert.Equal(2, model.Value("s5"));
    }

    [Fact]
    public void Page_keys_undo_and_redo_and_sort_resets()
    {
        var (ctrl, model) = Make();
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Right);

        Assert.True(ctrl.Handle(MenuInputAction.PageUp));
        Assert.Equal(2, model.Value("s1"));
        Assert.True(ctrl.Handle(MenuInputAction.PageDown));
        Assert.Equal(3, model.Value("s1"));

        Assert.True(ctrl.Handle(MenuInputAction.Sort));
        Assert.Equal(1, model.Value("s1"));
        Assert.True(model.CanUndo);
    }

    [Fact]
    public void Command_rows_can_be_clicked_like_the_mouse_would()
    {
        var (ctrl, model) = Make();
        Assert.False(ctrl.Enabled(Undo));
        Assert.False(ctrl.Enabled(Reset));

        Assert.True(ctrl.Select(2));
        Assert.True(ctrl.Step(2, 1));
        Assert.Equal(2, model.Value("s3"));
        Assert.True(ctrl.Enabled(Undo));
        Assert.True(ctrl.Enabled(Reset));

        Assert.True(ctrl.Activate(Undo));
        Assert.Equal(1, model.Value("s3"));
        Assert.True(ctrl.Activate(Redo));
        Assert.Equal(2, model.Value("s3"));
        Assert.True(ctrl.Activate(Reset));
        Assert.Equal(1, model.Value("s3"));
        Assert.False(ctrl.Select(99));
        Assert.False(ctrl.Step(99, 1));
    }

    [Fact]
    public void The_hint_for_accept_follows_the_selected_row()
    {
        var (ctrl, _) = Make();
        Assert.Equal("Confirm", ctrl.Hints.Single(h => h.Action == MenuInputAction.Accept).Label);

        ctrl.Select(Undo);
        Assert.Equal("Undo", ctrl.Hints.Single(h => h.Action == MenuInputAction.Accept).Label);
        Assert.Equal("Cancel", ctrl.Hints.Single(h => h.Action == MenuInputAction.Back).Label);
    }

    [Fact]
    public void Changed_fires_after_every_action_the_view_can_see()
    {
        var (ctrl, _) = Make();
        var count = 0;
        ctrl.Changed += () => count++;

        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Right);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Unknown_actions_are_not_consumed_so_the_stack_can_route_them()
    {
        var (ctrl, _) = Make();

        Assert.False(ctrl.Handle(MenuInputAction.Search));
        Assert.False(ctrl.Handle(MenuInputAction.Compare));
        Assert.False(ctrl.Handle(MenuInputAction.Favourite));
    }
}
