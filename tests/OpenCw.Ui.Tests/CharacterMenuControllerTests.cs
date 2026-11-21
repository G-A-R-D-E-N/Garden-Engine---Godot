using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterMenuControllerTests
{
    sealed class Probe : CharacterMenuController
    {
        public readonly List<string> Log = new();
        public readonly List<(int Row, double Fraction, bool Coalesce)> Fractions = new();
        public int Rows;
        public bool StepResult = true;
        public bool ActivateResult = true;
        public bool? HoldResult;
        public bool? ExtraResult;
        public string[] TabNames = Array.Empty<string>();
        public int CurrentTab;

        public Probe(int rows)
        {
            Rows = rows;
            Changed += () => Log.Add("changed");
            Finished += confirmed => Log.Add($"finished {confirmed}");
        }

        public override string Title => "Title";

        public override string Header => "Header";

        public override IReadOnlyList<MenuHint> Hints => Array.Empty<MenuHint>();

        public override int RowCount => Rows;

        public override IReadOnlyList<string> Tabs => TabNames;

        public override int TabIndex => CurrentTab;

        public override ValueRowKind Kind(int row) => ValueRowKind.Stepper;

        public override string Label(int row) => $"Row {row}";

        public override string ValueText(int row) => string.Empty;

        public override double Fraction(int row) => 0;

        public override bool Enabled(int row) => true;

        public override bool CanStep(int row, int direction) => true;

        public override bool SelectTab(int index)
        {
            Log.Add($"tab {index}");
            CurrentTab = index;
            return true;
        }

        public void SetMessage(string text) => Message = text;

        public void FinishNow(bool confirmed) => Finish(confirmed);

        public int ChangedCount => Log.Count(entry => entry == "changed");

        protected override bool UndoCore()
        {
            Log.Add("undo");
            return false;
        }

        protected override bool RedoCore()
        {
            Log.Add("redo");
            return false;
        }

        protected override bool StepCore(int row, int direction)
        {
            Log.Add($"step {row} {direction}");
            return StepResult;
        }

        protected override bool SetFractionCore(int row, double fraction, bool coalesce)
        {
            Log.Add($"fraction {row}");
            Fractions.Add((row, fraction, coalesce));
            return true;
        }

        protected override bool ActivateCore(int row)
        {
            Log.Add($"activate {row}");
            return ActivateResult;
        }

        protected override bool HoldCore(int row, bool down)
        {
            if (HoldResult is null) return base.HoldCore(row, down);
            Log.Add($"hold {row} {down}");
            return HoldResult.Value;
        }

        protected override bool HandleExtra(MenuInputAction action)
        {
            Log.Add($"extra {action}");
            return ExtraResult ?? base.HandleExtra(action);
        }

        protected override void OnCancel() => Log.Add("cancel");
    }

    [Fact]
    public void Select_moves_the_selection_and_raises_only_on_a_real_change()
    {
        var p = new Probe(3);

        Assert.True(p.Select(2));
        Assert.Equal(2, p.SelectedIndex);
        Assert.Equal(new[] { "changed" }, p.Log);

        Assert.True(p.Select(2));
        Assert.Equal(new[] { "changed" }, p.Log);

        Assert.False(p.Select(3));
        Assert.False(p.Select(-1));
        Assert.Equal(2, p.SelectedIndex);
        Assert.Equal(new[] { "changed" }, p.Log);
    }

    [Fact]
    public void Select_is_refused_once_finished()
    {
        var p = new Probe(3);
        p.FinishNow(true);
        p.Log.Clear();

        Assert.False(p.Select(1));
        Assert.Equal(0, p.SelectedIndex);
        Assert.Empty(p.Log);
    }

    [Fact]
    public void SelectedIndex_is_minus_one_with_no_rows_and_clamps_when_rows_shrink()
    {
        var p = new Probe(0);
        Assert.Equal(-1, p.SelectedIndex);

        p.Rows = 5;
        Assert.Equal(0, p.SelectedIndex);
        p.Select(4);
        Assert.Equal(4, p.SelectedIndex);

        p.Rows = 3;
        Assert.Equal(2, p.SelectedIndex);

        p.Rows = 0;
        Assert.Equal(-1, p.SelectedIndex);
    }

    [Fact]
    public void Step_clears_the_message_runs_the_core_raises_and_returns_the_core_result()
    {
        var p = new Probe(3);
        p.SetMessage("old");

        Assert.True(p.Step(1, -1));
        Assert.Equal(string.Empty, p.Message);
        Assert.Equal(new[] { "step 1 -1", "changed" }, p.Log);

        p.Log.Clear();
        p.StepResult = false;
        Assert.False(p.Step(2, 1));
        Assert.Equal(new[] { "step 2 1", "changed" }, p.Log);
    }

    [Fact]
    public void Activate_clears_the_message_runs_the_core_raises_and_returns_the_core_result()
    {
        var p = new Probe(3);
        p.SetMessage("old");
        p.ActivateResult = false;

        Assert.False(p.Activate(0));
        Assert.Equal(string.Empty, p.Message);
        Assert.Equal(new[] { "activate 0", "changed" }, p.Log);

        p.Log.Clear();
        p.ActivateResult = true;
        Assert.True(p.Activate(2));
        Assert.Equal(new[] { "activate 2", "changed" }, p.Log);
    }

    [Fact]
    public void Hold_defaults_to_false_and_passes_the_state_to_an_overriding_core()
    {
        var p = new Probe(3);
        p.SetMessage("old");

        Assert.False(p.Hold(1, true));
        Assert.Equal(string.Empty, p.Message);
        Assert.Equal(new[] { "changed" }, p.Log);

        p.Log.Clear();
        p.HoldResult = true;
        Assert.True(p.Hold(2, false));
        Assert.Equal(new[] { "hold 2 False", "changed" }, p.Log);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.25, 0.25)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    public void SetFraction_clamps_to_zero_through_one(double given, double expected)
    {
        var p = new Probe(3);

        Assert.True(p.SetFraction(1, given, true));

        Assert.Equal(new[] { (1, expected, true) }, p.Fractions);
        Assert.Equal(new[] { "fraction 1", "changed" }, p.Log);
    }

    [Fact]
    public void SetFraction_passes_coalesce_through_and_clears_the_message()
    {
        var p = new Probe(3);
        p.SetMessage("old");

        p.SetFraction(0, 0.5, false);

        Assert.Equal(new[] { (0, 0.5, false) }, p.Fractions);
        Assert.Equal(string.Empty, p.Message);
    }

    [Fact]
    public void Row_commands_ignore_out_of_range_rows_without_touching_message_or_events()
    {
        var p = new Probe(3);
        p.HoldResult = true;
        p.SetMessage("keep");

        foreach (var row in new[] { -1, 3, 99 })
        {
            Assert.False(p.Step(row, 1));
            Assert.False(p.SetFraction(row, 0.5, false));
            Assert.False(p.Activate(row));
            Assert.False(p.Hold(row, true));
        }

        Assert.Empty(p.Log);
        Assert.Empty(p.Fractions);
        Assert.Equal("keep", p.Message);
    }

    [Fact]
    public void Row_commands_ignore_valid_rows_once_finished()
    {
        var p = new Probe(3);
        p.HoldResult = true;
        p.SetMessage("keep");
        p.FinishNow(false);
        p.Log.Clear();

        Assert.False(p.Step(1, 1));
        Assert.False(p.SetFraction(1, 0.5, false));
        Assert.False(p.Activate(1));
        Assert.False(p.Hold(1, true));

        Assert.Empty(p.Log);
        Assert.Equal("keep", p.Message);
    }

    [Fact]
    public void Down_wraps_from_the_last_row_to_the_first_and_Up_wraps_back()
    {
        var p = new Probe(3);
        p.Select(2);
        p.Log.Clear();

        Assert.True(p.Handle(MenuInputAction.Down));
        Assert.Equal(0, p.SelectedIndex);

        Assert.True(p.Handle(MenuInputAction.Up));
        Assert.Equal(2, p.SelectedIndex);

        Assert.True(p.Handle(MenuInputAction.Up));
        Assert.Equal(1, p.SelectedIndex);
        Assert.Equal(3, p.ChangedCount);
    }

    [Fact]
    public void Up_and_Down_with_no_rows_are_handled_without_raising()
    {
        var p = new Probe(0);

        Assert.True(p.Handle(MenuInputAction.Up));
        Assert.True(p.Handle(MenuInputAction.Down));

        Assert.Equal(-1, p.SelectedIndex);
        Assert.Empty(p.Log);
    }

    [Fact]
    public void Up_and_Down_start_from_the_clamped_selection_after_the_rows_shrink()
    {
        var p = new Probe(5);
        p.Select(4);
        p.Rows = 3;

        p.Handle(MenuInputAction.Up);
        Assert.Equal(1, p.SelectedIndex);

        p.Select(2);
        p.Handle(MenuInputAction.Down);
        Assert.Equal(0, p.SelectedIndex);
    }

    [Fact]
    public void Left_and_Right_step_the_selected_row_by_minus_one_and_plus_one()
    {
        var p = new Probe(3);
        p.Select(1);
        p.Log.Clear();

        Assert.True(p.Handle(MenuInputAction.Left));
        Assert.True(p.Handle(MenuInputAction.Right));

        Assert.Equal(new[] { "step 1 -1", "changed", "step 1 1", "changed" }, p.Log);
    }

    [Fact]
    public void Left_Right_and_Accept_with_no_rows_are_handled_but_do_nothing()
    {
        var p = new Probe(0);

        Assert.True(p.Handle(MenuInputAction.Left));
        Assert.True(p.Handle(MenuInputAction.Right));
        Assert.True(p.Handle(MenuInputAction.Accept));

        Assert.Empty(p.Log);
    }

    [Fact]
    public void Accept_activates_the_selected_row()
    {
        var p = new Probe(3);
        p.Select(2);
        p.Log.Clear();

        Assert.True(p.Handle(MenuInputAction.Accept));

        Assert.Equal(new[] { "activate 2", "changed" }, p.Log);
        Assert.False(p.IsFinished);
    }

    [Fact]
    public void A_controller_starts_with_the_default_footer_no_side_lines_no_message_and_no_tabs()
    {
        var p = new Probe(3);

        Assert.Equal("PgUp or LT: undo    PgDn or RT: redo", p.Footer);
        Assert.Empty(p.SideLines);
        Assert.Empty(p.Tabs);
        Assert.Equal(string.Empty, p.Message);
        Assert.Equal(0, p.SelectedIndex);
        Assert.False(p.IsFinished);
    }

    [Fact]
    public void Back_cancels_runs_OnCancel_then_finishes_unconfirmed_then_raises()
    {
        var p = new Probe(3);

        Assert.True(p.Handle(MenuInputAction.Back));

        Assert.Equal(new[] { "cancel", "finished False", "changed" }, p.Log);
        Assert.True(p.IsFinished);
    }

    [Fact]
    public void PageUp_runs_undo_and_PageDown_runs_redo_each_clearing_the_message_and_raising()
    {
        var p = new Probe(3);
        p.SetMessage("old");

        Assert.True(p.Handle(MenuInputAction.PageUp));
        Assert.Equal(string.Empty, p.Message);
        Assert.Equal(new[] { "undo", "changed" }, p.Log);

        p.Log.Clear();
        p.SetMessage("old");
        Assert.True(p.Handle(MenuInputAction.PageDown));
        Assert.Equal(string.Empty, p.Message);
        Assert.Equal(new[] { "redo", "changed" }, p.Log);
    }

    [Fact]
    public void Finish_is_one_shot_and_keeps_the_first_outcome()
    {
        var p = new Probe(3);

        p.FinishNow(true);
        p.FinishNow(false);
        p.Cancel();

        Assert.True(p.IsFinished);
        Assert.Equal(new[] { "finished True", "changed" }, p.Log);
    }

    [Fact]
    public void Cancel_after_a_confirmed_finish_does_not_run_OnCancel()
    {
        var p = new Probe(3);
        p.FinishNow(true);
        p.Log.Clear();

        p.Cancel();

        Assert.Empty(p.Log);
    }

    [Fact]
    public void Finishing_unconfirmed_first_ignores_a_later_confirm()
    {
        var p = new Probe(3);

        p.Cancel();
        p.FinishNow(true);

        Assert.Equal(new[] { "cancel", "finished False", "changed" }, p.Log);
    }

    [Fact]
    public void Handle_returns_false_for_every_action_once_finished()
    {
        var p = new Probe(3);
        p.ExtraResult = true;
        p.FinishNow(false);
        p.Log.Clear();

        foreach (var action in Enum.GetValues<MenuInputAction>())
            Assert.False(p.Handle(action));

        Assert.Empty(p.Log);
    }

    [Fact]
    public void TabNext_and_TabPrev_wrap_around_the_tab_count()
    {
        var p = new Probe(3) { TabNames = new[] { "A", "B", "C" } };

        Assert.True(p.Handle(MenuInputAction.TabPrev));
        Assert.Equal(2, p.TabIndex);

        Assert.True(p.Handle(MenuInputAction.TabNext));
        Assert.Equal(0, p.TabIndex);

        Assert.True(p.Handle(MenuInputAction.TabNext));
        Assert.Equal(1, p.TabIndex);

        Assert.Equal(new[] { "tab 2", "changed", "tab 0", "changed", "tab 1", "changed" }, p.Log);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Tab_actions_are_unhandled_with_fewer_than_two_tabs_but_still_clear_the_message(int tabCount)
    {
        var p = new Probe(3) { TabNames = Enumerable.Repeat("T", tabCount).ToArray() };
        p.SetMessage("old");

        Assert.False(p.Handle(MenuInputAction.TabNext));
        Assert.False(p.Handle(MenuInputAction.TabPrev));

        Assert.Equal(string.Empty, p.Message);
        Assert.Empty(p.Log);
    }

    [Theory]
    [InlineData(MenuInputAction.Search)]
    [InlineData(MenuInputAction.Sort)]
    [InlineData(MenuInputAction.Compare)]
    [InlineData(MenuInputAction.Favourite)]
    public void Unmapped_actions_go_to_HandleExtra_which_is_false_by_default(MenuInputAction action)
    {
        var p = new Probe(3);

        Assert.False(p.Handle(action));
        Assert.Equal(new[] { $"extra {action}" }, p.Log);

        p.Log.Clear();
        p.ExtraResult = true;
        Assert.True(p.Handle(action));
        Assert.Equal(new[] { $"extra {action}" }, p.Log);
    }
}
