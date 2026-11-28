using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OpenCommonwealth.Services.Ui.Character;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterAppearanceTests
{
    static readonly GenericCharacterData Data = new();

    static AppearanceModel Make(int seed = 1, IAppearancePresetStore? store = null,
                                IReadOnlyDictionary<string, double>? initial = null) =>
        new(Data.AppearanceControls, new Random(seed), store, initial);

    static double[] Vals(AppearanceModel model) => model.Values.Select(p => p.Value).ToArray();

    static AppearanceModel Build(params AppearanceControl[] controls) => new(controls);

    [Fact]
    public void Values_are_clamped_and_snapped_to_the_control_range_and_step()
    {
        var model = Make();

        Assert.True(model.Set("sl3", 70.4));
        Assert.Equal(70, model.Get("sl3"));
        Assert.True(model.Set("sl3", 500));
        Assert.Equal(100, model.Get("sl3"));
        Assert.True(model.Set("ch1", 1.6));
        Assert.Equal(2, model.Get("ch1"));
        Assert.True(model.Set("ch1", -4));
        Assert.Equal(0, model.Get("ch1"));
        Assert.False(model.Set("ch1", 0.2));
    }

    [Fact]
    public void A_non_numeric_value_falls_back_to_the_default_instead_of_corrupting_state()
    {
        var model = Make();
        model.Set("sl3", 10);

        Assert.True(model.Set("sl3", double.NaN));
        Assert.Equal(50, model.Get("sl3"));
    }

    [Fact]
    public void Undoing_every_edit_returns_to_the_start_and_redoing_returns_to_the_end()
    {
        var model = Make(5);
        var rng = new Random(11);
        var ids = Data.AppearanceControls.Select(c => c.Id).ToArray();
        var start = Vals(model);

        for (var n = 0; n < 60; n++)
        {
            switch (rng.Next(5))
            {
                case 0: model.Set(ids[rng.Next(ids.Length)], rng.NextDouble() * 120 - 10, rng.Next(2) == 0); break;
                case 1: model.Randomise(); break;
                case 2: model.Reset(rng.Next(2) == 0 ? "Shape" : null); break;
                case 3: model.Symmetric = !model.Symmetric; break;
                default: model.Set(ids[rng.Next(ids.Length)], rng.Next(0, 4)); break;
            }

            var before = Vals(model);
            if (model.Undo()) model.Redo();
            Assert.Equal(before, Vals(model));
            foreach (var control in Data.AppearanceControls)
            {
                Assert.InRange(model.Get(control.Id), control.Min, control.Max);
            }
        }

        var end = Vals(model);
        while (model.Undo()) { }
        Assert.Equal(start, Vals(model));
        while (model.Redo()) { }
        Assert.Equal(end, Vals(model));
    }

    [Fact]
    public void A_new_edit_after_undo_drops_the_redo_branch()
    {
        var model = Make();
        model.Set("sl3", 10);
        model.Set("sl3", 20);
        model.Undo();
        Assert.True(model.CanRedo);

        model.Set("ch1", 1);

        Assert.False(model.CanRedo);
        Assert.Equal(10, model.Get("sl3"));
    }

    [Fact]
    public void Coalesced_edits_of_one_control_are_a_single_undo_step_and_other_controls_are_not_merged()
    {
        var model = Make();
        model.Set("sl3", 10, coalesce: true);
        model.Set("sl3", 20, coalesce: true);
        model.Set("sl3", 30, coalesce: true);

        Assert.True(model.Undo());
        Assert.Equal(50, model.Get("sl3"));
        Assert.False(model.CanUndo);

        model.Set("sl3", 10, coalesce: true);
        model.Set("ch1", 2, coalesce: true);
        Assert.True(model.Undo());
        Assert.Equal(10, model.Get("sl3"));
        Assert.Equal(0, model.Get("ch1"));
        Assert.True(model.Undo());
        Assert.Equal(50, model.Get("sl3"));
    }

    [Fact]
    public void Symmetric_edits_mirror_in_both_directions_and_undo_as_one_step()
    {
        var model = Make();
        model.Set("sl1", 0.7);
        Assert.Equal(0.5, model.Get("sl2"), 6);

        model.Symmetric = true;
        model.Set("sl1", 0.3);
        Assert.Equal(0.3, model.Get("sl2"), 6);
        model.Set("sl2", 0.9);
        Assert.Equal(0.9, model.Get("sl1"), 6);

        model.Undo();
        model.Undo();
        Assert.Equal(0.7, model.Get("sl1"), 6);
        Assert.Equal(0.5, model.Get("sl2"), 6);
    }

    [Fact]
    public void Randomise_is_deterministic_for_a_seed_and_stays_on_the_control_grid()
    {
        for (var seed = 0; seed < 25; seed++)
        {
            var a = Make(seed);
            var b = Make(seed);

            var changed = a.Randomise();
            b.Randomise();

            Assert.Equal(Vals(a), Vals(b));
            foreach (var control in Data.AppearanceControls)
            {
                var value = a.Get(control.Id);
                Assert.InRange(value, control.Min, control.Max);
                Assert.Equal(control.Normalize(value), value, 9);
            }
            Assert.Equal(Math.Round(a.Get("ch1")), a.Get("ch1"));

            Assert.Equal(changed, a.CanUndo);
            a.Undo();
            Assert.Equal(Data.AppearanceControls.Select(c => c.Default).ToArray(), Vals(a));
        }
    }

    [Fact]
    public void Randomise_can_be_limited_to_a_category()
    {
        for (var seed = 0; seed < 25; seed++)
        {
            var model = Make(seed);

            for (var i = 0; i < 10; i++)
            {
                model.Randomise("Style");
                Assert.InRange(model.Get("ch1"), 0, 2);
                Assert.Equal(Math.Round(model.Get("ch1")), model.Get("ch1"));
            }

            Assert.Equal(0.5, model.Get("sl1"), 6);
            Assert.Equal(0.5, model.Get("sl2"), 6);
            Assert.Equal(50, model.Get("sl3"));
        }
    }

    [Fact]
    public void Symmetric_randomise_keeps_mirrored_controls_equal()
    {
        var model = Make(21);
        model.Symmetric = true;

        for (var i = 0; i < 25; i++)
        {
            model.Randomise();
            Assert.Equal(model.Get("sl1"), model.Get("sl2"), 9);
        }
    }

    [Fact]
    public void Reset_can_target_one_category_and_reports_when_nothing_changed()
    {
        var model = Make();
        model.Set("sl3", 10);
        model.Set("ch1", 2);

        Assert.True(model.Reset("Shape"));
        Assert.Equal(50, model.Get("sl3"));
        Assert.Equal(2, model.Get("ch1"));
        Assert.False(model.Reset("Shape"));
        Assert.False(model.Reset("No such category"));

        Assert.True(model.Reset());
        Assert.Equal(0, model.Get("ch1"));
        Assert.True(model.Undo());
        Assert.Equal(2, model.Get("ch1"));
    }

    [Fact]
    public void Compare_with_original_shows_the_starting_values_without_losing_the_edits()
    {
        var model = Make(initial: new Dictionary<string, double> { ["sl3"] = 20 });
        Assert.Equal(20, model.Get("sl3"));
        Assert.False(model.IsDirty);

        model.Set("sl3", 80);
        Assert.True(model.IsDirty);
        Assert.Equal(new[] { "sl3" }, model.ChangedFromOriginal.ToArray());

        model.ShowOriginal = true;
        Assert.Equal(20, model.Displayed("sl3"));
        Assert.Equal(80, model.Get("sl3"));

        model.ShowOriginal = false;
        Assert.Equal(80, model.Displayed("sl3"));
    }

    [Fact]
    public void Editing_while_comparing_switches_back_to_the_edited_values()
    {
        var model = Make();
        model.Set("sl3", 80);
        model.ShowOriginal = true;

        model.Set("ch1", 1);

        Assert.False(model.ShowOriginal);
        Assert.Equal(80, model.Displayed("sl3"));
    }

    [Fact]
    public void Revert_to_original_is_one_undoable_step()
    {
        var model = Make(initial: new Dictionary<string, double> { ["sl3"] = 20 });
        Assert.False(model.RevertToOriginal());

        model.Set("sl3", 80);
        model.Set("ch1", 2);
        Assert.True(model.RevertToOriginal());
        Assert.Equal(20, model.Get("sl3"));
        Assert.False(model.IsDirty);

        Assert.True(model.Undo());
        Assert.Equal(80, model.Get("sl3"));
        Assert.Equal(2, model.Get("ch1"));
    }

    [Fact]
    public void Initial_values_are_normalised_and_unlisted_controls_use_their_default()
    {
        var model = Make(initial: new Dictionary<string, double> { ["sl3"] = 999, ["ch1"] = 1.2, ["zz"] = 5 });

        Assert.Equal(100, model.Get("sl3"));
        Assert.Equal(1, model.Get("ch1"));
        Assert.Equal(0.5, model.Get("sl1"), 6);
    }

    [Fact]
    public void Json_export_round_trips_into_a_fresh_model_as_one_undoable_step()
    {
        var source = Make();
        source.Set("sl3", 70);
        source.Set("ch1", 2);
        source.Set("sl1", 0.8);
        var json = source.ExportJson();

        using (var doc = JsonDocument.Parse(json))
        {
            Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
            Assert.Equal(70, doc.RootElement.GetProperty("values").GetProperty("sl3").GetDouble());
        }

        var target = Make(2);
        Assert.True(target.TryImportJson(json, out var error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(Vals(source), Vals(target));

        Assert.True(target.Undo());
        Assert.False(target.CanUndo);
        Assert.Equal(Data.AppearanceControls.Select(c => c.Default).ToArray(), Vals(target));
    }

    [Fact]
    public void Import_clamps_values_ignores_unknown_ids_and_leaves_absent_controls_alone()
    {
        var model = Make();
        model.Set("sl1", 0.2);

        Assert.True(model.TryImportJson("{\"version\":1,\"values\":{\"sl3\":250.4,\"ch1\":1.4,\"zz\":3}}", out _));

        Assert.Equal(100, model.Get("sl3"));
        Assert.Equal(1, model.Get("ch1"));
        Assert.Equal(0.2, model.Get("sl1"), 6);
    }

    [Fact]
    public void Import_does_not_apply_symmetry_and_an_identical_import_adds_no_undo_step()
    {
        var model = Make();
        model.Symmetric = true;

        Assert.True(model.TryImportJson("{\"values\":{\"sl1\":0.9}}", out _));
        Assert.Equal(0.9, model.Get("sl1"), 6);
        Assert.Equal(0.5, model.Get("sl2"), 6);

        model.Undo();
        Assert.False(model.CanUndo);
        Assert.True(model.TryImportJson(model.ExportJson(), out _));
        Assert.False(model.CanUndo);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"values\":[]}")]
    [InlineData("{\"values\":{\"sl3\":\"x\"}}")]
    [InlineData("{\"values\":{\"sl3\":1e999}}")]
    [InlineData("{\"version\":2,\"values\":{\"sl3\":10}}")]
    [InlineData("{\"version\":\"1\",\"values\":{\"sl3\":10}}")]
    public void Invalid_preset_json_is_rejected_without_touching_the_state(string json)
    {
        var model = Make();
        var before = Vals(model);

        Assert.False(model.TryImportJson(json, out var error));

        Assert.NotEqual(string.Empty, error);
        Assert.Equal(before, Vals(model));
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void Named_presets_save_load_and_delete_through_the_injected_store()
    {
        var store = new InMemoryAppearancePresetStore();
        var model = Make(store: store);
        model.Set("sl3", 70);

        Assert.True(model.SavePreset("  Look A "));
        Assert.Equal(new[] { "Look A" }, model.PresetNames.ToArray());
        Assert.False(model.SavePreset("   "));

        model.Set("sl3", 10);
        Assert.True(model.LoadPreset("Look A"));
        Assert.Equal(70, model.Get("sl3"));
        Assert.False(model.LoadPreset("missing"));

        var other = Make(store: store);
        Assert.True(other.LoadPreset("Look A"));
        Assert.Equal(70, other.Get("sl3"));

        Assert.True(model.DeletePreset("Look A"));
        Assert.False(model.DeletePreset("Look A"));
        Assert.Empty(model.PresetNames);
    }

    [Fact]
    public void A_corrupt_stored_preset_fails_to_load_and_changes_nothing()
    {
        var store = new InMemoryAppearancePresetStore();
        store.Save("bad", "nope");
        var model = Make(store: store);
        var before = Vals(model);

        Assert.False(model.LoadPreset("bad"));
        Assert.Equal(before, Vals(model));
    }

    [Fact]
    public void Controls_list_supports_category_and_text_filtering()
    {
        var model = Make();

        Assert.Equal(new[] { "Shape", "Style" }, model.Categories.ToArray());
        Assert.Equal(3, model.ControlsIn("Shape").Count);

        model.Controls.SetCategory("Style");
        Assert.Equal(new[] { "ch1" }, model.Controls.Visible.Select(c => c.Id).ToArray());

        model.Controls.SetCategory(null);
        model.Controls.SetSearch("height");
        Assert.Equal(new[] { "sl3" }, model.Controls.Visible.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void Changed_fires_for_real_edits_and_comparison_toggles_only()
    {
        var model = Make();
        var count = 0;
        model.Changed += () => count++;

        model.Set("sl3", 50);
        Assert.Equal(0, count);
        model.Set("sl3", 60);
        model.Undo();
        model.ShowOriginal = true;
        model.ShowOriginal = true;
        Assert.Equal(3, count);
    }

    [Fact]
    public void Unknown_ids_are_an_error_not_a_silent_no_op()
    {
        var model = Make();

        Assert.Throws<ArgumentException>(() => model.Set("zz", 1));
        Assert.Throws<ArgumentException>(() => model.Get("zz"));
    }

    [Fact]
    public void Construction_rejects_inconsistent_control_data()
    {
        var a = AppearanceControl.Slider("a", "A", "C", 0, 1, 0.5);
        Assert.Throws<ArgumentException>(() => Build(a, a));
        Assert.Throws<ArgumentException>(() => Build(AppearanceControl.Slider("a", "A", "C", 1, 0, 0.5)));
        Assert.Throws<ArgumentException>(() => Build(AppearanceControl.Slider("a", "A", "C", 0, 1, 2)));
        Assert.Throws<ArgumentException>(() => Build(AppearanceControl.Slider("a", "A", "C", 0, 1, 0.5, -1)));
        Assert.Throws<ArgumentException>(() => Build(AppearanceControl.Slider("a", "A", "C", 0, 1, 0.5, 0, "missing")));
        Assert.Throws<ArgumentException>(() => Build(AppearanceControl.Slider("a", "A", "C", 0, 1, 0.5, 0, "a")));
        Assert.Throws<ArgumentException>(() => Build(
            AppearanceControl.Slider("a", "A", "C", 0, 1, 0.5, 0, "b"),
            AppearanceControl.Slider("b", "B", "C", 0, 2, 0.5)));
        Assert.Throws<ArgumentException>(() => Build(
            AppearanceControl.Slider("a", "A", "C", 0, 1, 0.5, 0, "c"),
            AppearanceControl.Slider("b", "B", "C", 0, 1, 0.5, 0, "c"),
            AppearanceControl.Slider("c", "C", "C", 0, 1, 0.5)));
        Assert.Throws<ArgumentException>(() => Build(AppearanceControl.Choice("a", "A", "C", Array.Empty<string>())));
    }

    [Fact]
    public void The_undo_history_is_capped_and_keeps_the_newest_edits()
    {
        var model = Build(AppearanceControl.Slider("a", "A", "Cat", 0, 1000, 0, 1));
        for (var i = 1; i <= 100; i++) Assert.True(model.Set("a", i));

        Assert.True(model.Undo());
        Assert.Equal(99, model.Get("a"));

        var undone = 1;
        while (model.Undo()) undone++;

        Assert.Equal(AppearanceModel.MaxUndo, undone);
        Assert.Equal(100 - AppearanceModel.MaxUndo, model.Get("a"));
        Assert.False(model.CanUndo);
    }
}
