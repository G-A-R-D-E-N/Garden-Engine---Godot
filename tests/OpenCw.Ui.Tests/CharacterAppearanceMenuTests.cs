using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterAppearanceMenuTests
{
    static readonly GenericCharacterData Data = new();

    const int Randomise = 3;
    const int ResetCategory = 4;
    const int Undo = 5;
    const int Redo = 6;
    const int Symmetric = 7;
    const int Compare = 8;
    const int Save = 9;
    const int Load = 10;
    const int Delete = 11;
    const int Done = 12;

    sealed class FailingStore : IAppearancePresetStore
    {
        public IReadOnlyList<string> Names() => Array.Empty<string>();

        public string? Load(string name) => null;

        public bool Save(string name, string json) => false;

        public bool Delete(string name) => false;
    }

    static (AppearanceController Ctrl, AppearanceModel Model) Make(IAppearancePresetStore? store = null)
    {
        var model = new AppearanceModel(Data.AppearanceControls, new Random(1), store);
        return (new AppearanceController(model), model);
    }

    [Fact]
    public void Rows_are_the_current_categorys_controls_followed_by_the_command_rows()
    {
        var (ctrl, _) = Make();

        Assert.Equal(new[] { "Shape", "Style" }, ctrl.Tabs.ToArray());
        Assert.Equal(13, ctrl.RowCount);
        Assert.Equal(ValueRowKind.Slider, ctrl.Kind(0));
        Assert.Equal(ValueRowKind.Command, ctrl.Kind(Randomise));
        Assert.Equal("Height", ctrl.Label(2));
        Assert.Equal("50", ctrl.ValueText(2));
        Assert.Equal(0.5, ctrl.Fraction(2), 6);
        Assert.Equal("Done", ctrl.Label(Done));

        Assert.True(ctrl.Handle(MenuInputAction.TabNext));
        Assert.Equal(11, ctrl.RowCount);
        Assert.Equal(ValueRowKind.Stepper, ctrl.Kind(0));
        Assert.Equal("Option 1", ctrl.ValueText(0));
        Assert.Equal(-1.0, ctrl.Fraction(0), 6);
        Assert.Equal(1, ctrl.TabIndex);
    }

    [Fact]
    public void Repeated_steps_on_one_slider_are_a_single_undo_step()
    {
        var (ctrl, model) = Make();

        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Right);
        Assert.Equal(0.8, model.Get("sl1"), 6);

        Assert.True(ctrl.Handle(MenuInputAction.PageUp));
        Assert.Equal(0.5, model.Get("sl1"), 6);
        Assert.False(model.CanUndo);
        Assert.True(ctrl.Handle(MenuInputAction.PageDown));
        Assert.Equal(0.8, model.Get("sl1"), 6);
    }

    [Fact]
    public void A_choice_steps_by_one_option_and_stops_at_the_ends()
    {
        var (ctrl, model) = Make();
        ctrl.Handle(MenuInputAction.TabNext);

        Assert.False(ctrl.CanStep(0, -1));
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Right);
        Assert.Equal("Option 3", ctrl.ValueText(0));
        Assert.False(ctrl.CanStep(0, 1));
        Assert.False(ctrl.Step(0, 1));
        Assert.Equal(2, model.Get("ch1"));
    }

    [Fact]
    public void A_mouse_drag_sets_the_fraction_and_each_drag_is_its_own_undo_step()
    {
        var (ctrl, model) = Make();

        Assert.True(ctrl.SetFraction(2, 0.25, true));
        Assert.True(ctrl.SetFraction(2, 0.75, true));
        ctrl.EndDrag();
        Assert.True(ctrl.SetFraction(2, 0.1, true));
        ctrl.EndDrag();
        Assert.Equal(10, model.Get("sl3"));

        model.Undo();
        Assert.Equal(75, model.Get("sl3"));
        model.Undo();
        Assert.Equal(50, model.Get("sl3"));
        Assert.False(ctrl.SetFraction(Randomise, 0.5, true));
    }

    [Fact]
    public void A_fraction_outside_the_range_is_clamped()
    {
        var (ctrl, model) = Make();

        ctrl.SetFraction(2, 4, false);
        Assert.Equal(100, model.Get("sl3"));
        ctrl.SetFraction(2, -3, false);
        Assert.Equal(0, model.Get("sl3"));
    }

    [Fact]
    public void The_symmetric_row_toggles_mirroring_of_the_paired_sliders()
    {
        var (ctrl, model) = Make();
        Assert.Equal("Off", ctrl.ValueText(Symmetric));

        ctrl.Activate(Symmetric);
        Assert.Equal("On", ctrl.ValueText(Symmetric));
        Assert.Contains("symmetric", ctrl.Header);

        ctrl.Handle(MenuInputAction.Right);
        Assert.Equal(model.Get("sl1"), model.Get("sl2"), 9);
        Assert.Equal(0.6, model.Get("sl2"), 6);
    }

    [Fact]
    public void Randomise_only_touches_the_current_category_and_reports_it()
    {
        var (ctrl, model) = Make();
        var before = model.Get("ch1");

        Assert.True(ctrl.Handle(MenuInputAction.Favourite));
        Assert.NotEqual(string.Empty, ctrl.Message);
        Assert.Equal(before, model.Get("ch1"));
        ctrl.Activate(Randomise);
        Assert.Equal(before, model.Get("ch1"));
    }

    [Fact]
    public void Reset_category_restores_defaults_in_that_category_only()
    {
        var (ctrl, model) = Make();
        ctrl.SetFraction(2, 1, false);
        model.Set("ch1", 2);

        Assert.True(ctrl.Handle(MenuInputAction.Sort));
        Assert.Equal(50, model.Get("sl3"));
        Assert.Equal(2, model.Get("ch1"));
        Assert.True(ctrl.Activate(Undo));
        Assert.Equal(100, model.Get("sl3"));
    }

    [Fact]
    public void Holding_the_compare_row_shows_the_original_and_releasing_restores_the_edit()
    {
        var (ctrl, model) = Make();
        ctrl.SetFraction(2, 0.7, false);
        Assert.True(ctrl.HoldsOnPress(Compare));
        Assert.False(ctrl.HoldsOnPress(Done));

        Assert.True(ctrl.Hold(Compare, true));
        Assert.Equal("50", ctrl.ValueText(2));
        Assert.Equal("Showing the original look", ctrl.Header);
        Assert.Equal(70, model.Get("sl3"));

        Assert.True(ctrl.Hold(Compare, false));
        Assert.Equal("70", ctrl.ValueText(2));
    }

    [Fact]
    public void The_compare_action_toggles_for_keyboard_and_controller()
    {
        var (ctrl, model) = Make();

        Assert.True(ctrl.Handle(MenuInputAction.Compare));
        Assert.True(model.ShowOriginal);
        Assert.Equal("Show edit", ctrl.Hints.Single(h => h.Action == MenuInputAction.Compare).Label);
        Assert.True(ctrl.Handle(MenuInputAction.Compare));
        Assert.False(model.ShowOriginal);
    }

    [Fact]
    public void Presets_save_load_and_delete_through_the_command_rows()
    {
        var (ctrl, model) = Make(new InMemoryAppearancePresetStore());
        Assert.False(ctrl.Enabled(Load));
        ctrl.Activate(Load);
        Assert.Equal("No saved presets.", ctrl.Message);

        ctrl.SetFraction(2, 0.7, false);
        Assert.True(ctrl.Activate(Save));
        Assert.Equal("Saved Look 1.", ctrl.Message);
        Assert.Equal(new[] { "Look 1" }, ctrl.PresetNames.ToArray());
        Assert.Equal("Look 1", ctrl.ValueText(Load));

        ctrl.Activate(Save);
        Assert.Equal("Saved Look 2.", ctrl.Message);

        ctrl.SetFraction(2, 0.1, false);
        ctrl.Select(Load);
        ctrl.Handle(MenuInputAction.Left);
        Assert.Equal("Look 1", ctrl.ValueText(Load));
        Assert.True(ctrl.Activate(Load));
        Assert.Equal("Loaded Look 1.", ctrl.Message);
        Assert.Equal(70, model.Get("sl3"));

        Assert.True(ctrl.Activate(Delete));
        Assert.Equal("Deleted Look 1.", ctrl.Message);
        Assert.Equal(new[] { "Look 2" }, ctrl.PresetNames.ToArray());
    }

    sealed class StaleListStore : IAppearancePresetStore
    {
        readonly InMemoryAppearancePresetStore _real = new();

        bool _stale;

        public void GoStale() => _stale = true;

        public IReadOnlyList<string> Names() => _stale ? Array.Empty<string>() : _real.Names();

        public string? Load(string name) => _real.Load(name);

        public bool Save(string name, string json) => _real.Save(name, json);

        public bool Create(string name, string json)
        {
            if (!_real.Names().Contains(name, StringComparer.OrdinalIgnoreCase)) return _real.Save(name, json);
            _stale = false;
            return false;
        }

        public bool Delete(string name) => _real.Delete(name);
    }

    [Fact]
    public void A_preset_created_after_the_list_was_cached_is_not_overwritten_by_save()
    {
        var store = new StaleListStore();
        var (ctrl, _) = Make(store);
        store.Save("Look 1", "someone else's preset");
        store.GoStale();

        Assert.True(ctrl.Activate(Save));

        Assert.Equal("Saved Look 2.", ctrl.Message);
        Assert.Equal("someone else's preset", store.Load("Look 1"));
        Assert.NotNull(store.Load("Look 2"));
        Assert.Equal(new[] { "Look 1", "Look 2" }, ctrl.PresetNames.ToArray());
    }

    [Fact]
    public void A_store_that_fails_returns_a_message_instead_of_throwing()
    {
        var (ctrl, _) = Make(new FailingStore());

        Assert.False(ctrl.Activate(Save));
        Assert.Equal("Could not save the preset.", ctrl.Message);
        Assert.Empty(ctrl.PresetNames);
        Assert.False(ctrl.Activate(Delete));
    }

    [Fact]
    public void Done_confirms_and_back_reverts_to_the_original_look()
    {
        var (done, doneModel) = Make();
        var results = new List<bool>();
        done.Finished += results.Add;
        done.SetFraction(2, 0.7, false);
        Assert.True(done.Activate(Done));
        Assert.Equal(70, doneModel.Get("sl3"));
        Assert.Equal(new[] { true }, results.ToArray());

        var (back, backModel) = Make();
        var backResults = new List<bool>();
        back.Finished += backResults.Add;
        back.SetFraction(2, 0.7, false);
        Assert.True(back.Handle(MenuInputAction.Back));
        Assert.True(back.IsFinished);
        Assert.Equal(new[] { false }, backResults.ToArray());
        Assert.Equal(50, backModel.Get("sl3"));
    }

    [Fact]
    public void Accept_on_a_control_row_finishes_like_done()
    {
        var (ctrl, _) = Make();
        var results = new List<bool>();
        ctrl.Finished += results.Add;

        Assert.Equal("Done", ctrl.Hints.Single(h => h.Action == MenuInputAction.Accept).Label);
        Assert.True(ctrl.Handle(MenuInputAction.Accept));
        Assert.Equal(new[] { true }, results.ToArray());
    }

    [Fact]
    public void Tabs_switch_categories_by_action_or_click_and_the_hints_name_the_neighbours()
    {
        var (ctrl, _) = Make();
        Assert.Equal("Next: Style", ctrl.Hints.Single(h => h.Action == MenuInputAction.TabNext).Label);
        Assert.Equal("Previous: Style", ctrl.Hints.Single(h => h.Action == MenuInputAction.TabPrev).Label);

        Assert.True(ctrl.SelectTab(1));
        Assert.Equal("Style", ctrl.Category);
        Assert.False(ctrl.SelectTab(5));
        Assert.True(ctrl.Handle(MenuInputAction.TabNext));
        Assert.Equal("Shape", ctrl.Category);
        Assert.Equal(0, ctrl.SelectedIndex);
    }

    [Fact]
    public void The_side_panel_lists_what_changed_from_the_original()
    {
        var (ctrl, _) = Make();
        Assert.Equal("Nothing yet", ctrl.SideLines[1].Text);

        ctrl.SetFraction(2, 0.7, false);
        ctrl.Handle(MenuInputAction.TabNext);
        ctrl.Handle(MenuInputAction.Right);

        Assert.Contains(ctrl.SideLines, l => l.Text == "Height: 50  >  70");
        Assert.Contains(ctrl.SideLines, l => l.Text == "Style: Option 1  >  Option 2");
    }
}
