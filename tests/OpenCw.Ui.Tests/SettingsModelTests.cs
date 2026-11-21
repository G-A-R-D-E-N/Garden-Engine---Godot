using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingsModelTests
{
    sealed class ThrowingStore : IFileStore
    {
        public string? ReadAllText(string name) => null;

        public bool WriteAllText(string name, string text) => false;
    }

    static SettingDef[] Defs() => new[]
    {
        SettingDef.Toggle("audio.mute", SettingCategory.Audio, "Mute when unfocused", true) with { Keywords = "background" },
        SettingDef.Slider("audio.vol", SettingCategory.Audio, "Master volume", 50, 0, 100, 5),
        SettingDef.Choice("display.mode", SettingCategory.Display, "Window mode", "windowed",
            new SettingChoice("windowed", "Windowed"), new SettingChoice("fullscreen", "Fullscreen")) with { Risky = true },
        SettingDef.Toggle("display.vsync", SettingCategory.Display, "Vertical sync", false),
    };

    static SettingsModel Make(IFileStore? store = null, double revertSeconds = 15) =>
        new(Defs(), store ?? new InMemoryFileStore(), revertSeconds);

    [Fact]
    public void Slider_values_are_clamped_and_snapped_to_the_step_and_nan_is_rejected()
    {
        var model = Make();

        Assert.True(model.Set("audio.vol", SettingValue.Slider(53)));
        Assert.Equal(55.0, model.Value("audio.vol").Number);
        Assert.True(model.Set("audio.vol", SettingValue.Slider(500)));
        Assert.Equal(100.0, model.Value("audio.vol").Number);
        Assert.True(model.Set("audio.vol", SettingValue.Slider(-4)));
        Assert.Equal(0.0, model.Value("audio.vol").Number);
        Assert.False(model.Set("audio.vol", SettingValue.Slider(double.NaN)));
        Assert.Equal(0.0, model.Value("audio.vol").Number);
    }

    [Fact]
    public void Stray_fields_on_a_value_are_dropped_so_they_never_look_like_a_change()
    {
        var model = Make();

        Assert.True(model.Set("audio.mute", new SettingValue(SettingKind.Toggle, true, 0, "x")));
        Assert.False(model.IsDirty("audio.mute"));
        Assert.True(model.Set("display.mode", new SettingValue(SettingKind.Choice, true, 9, "windowed")));
        Assert.False(model.IsDirty("display.mode"));
        Assert.False(model.Set("display.mode", default(SettingValue) with { Kind = SettingKind.Choice }));
        Assert.True(model.IsAtDefaults());
    }

    [Fact]
    public void Set_rejects_unknown_ids_wrong_kinds_and_unknown_choices()
    {
        var model = Make();

        Assert.False(model.Set("nope", SettingValue.Toggle(true)));
        Assert.False(model.Set("audio.mute", SettingValue.Slider(1)));
        Assert.False(model.Set("display.mode", SettingValue.Choice("bogus")));
        Assert.True(model.Set("display.mode", SettingValue.Choice("fullscreen")));
        Assert.Equal("fullscreen", model.Value("display.mode").Text);
    }

    [Fact]
    public void Pending_changes_do_not_touch_applied_values_until_apply_and_apply_respects_the_category()
    {
        var model = Make();
        model.Set("audio.vol", SettingValue.Slider(80));
        model.Set("display.vsync", SettingValue.Toggle(true));

        Assert.Equal(50.0, model.AppliedValue("audio.vol").Number);
        Assert.True(model.IsDirty("audio.vol"));
        Assert.True(model.IsDirty(SettingCategory.Audio));
        Assert.True(model.IsDirty(SettingCategory.Display));
        Assert.False(model.IsDirty(SettingCategory.Gameplay));

        var changed = model.Apply(SettingCategory.Audio);

        Assert.Equal(new[] { "audio.vol" }, changed.ToArray());
        Assert.Equal(80.0, model.AppliedValue("audio.vol").Number);
        Assert.False(model.IsDirty(SettingCategory.Audio));
        Assert.True(model.IsDirty("display.vsync"));
        Assert.False(model.AppliedValue("display.vsync").Flag);
        Assert.True(model.IsDirty());
    }

    [Fact]
    public void Applied_event_lists_exactly_the_changed_ids()
    {
        var model = Make();
        var seen = new List<string>();
        model.Applied += ids => seen.AddRange(ids);
        model.Set("audio.mute", SettingValue.Toggle(false));
        model.Set("audio.vol", SettingValue.Slider(50));

        model.Apply();

        Assert.Equal(new[] { "audio.mute" }, seen.ToArray());
    }

    [Fact]
    public void Revert_drops_pending_changes_only_in_the_requested_category()
    {
        var model = Make();
        model.Set("audio.vol", SettingValue.Slider(80));
        model.Set("audio.mute", SettingValue.Toggle(false));
        model.Set("display.vsync", SettingValue.Toggle(true));

        Assert.Equal(2, model.Revert(SettingCategory.Audio));

        Assert.Equal(50.0, model.Value("audio.vol").Number);
        Assert.True(model.Value("audio.mute").Flag);
        Assert.True(model.Value("display.vsync").Flag);
        Assert.Equal(1, model.Revert());
        Assert.False(model.IsDirty());
    }

    [Fact]
    public void Reset_to_defaults_is_pending_until_applied_per_category_and_globally()
    {
        var model = Make();
        model.Set("audio.vol", SettingValue.Slider(80));
        model.Set("display.vsync", SettingValue.Toggle(true));
        model.Apply();
        Assert.False(model.IsAtDefaults());

        Assert.Equal(1, model.ResetToDefaults(SettingCategory.Audio));
        Assert.Equal(50.0, model.Value("audio.vol").Number);
        Assert.Equal(80.0, model.AppliedValue("audio.vol").Number);
        Assert.True(model.IsAtDefaults(SettingCategory.Audio));
        Assert.False(model.IsAtDefaults(SettingCategory.Display));

        Assert.Equal(1, model.ResetToDefaults());
        Assert.True(model.IsAtDefaults());
        model.Apply();
        Assert.False(model.AppliedValue("display.vsync").Flag);
        Assert.Equal(50.0, model.AppliedValue("audio.vol").Number);
    }

    [Fact]
    public void Applied_values_persist_through_the_store_and_reload_into_a_new_model()
    {
        var store = new InMemoryFileStore();
        var first = Make(store);
        Assert.Equal(SettingsLoadResult.NoFile, first.LoadResult);
        first.Set("audio.vol", SettingValue.Slider(20));
        first.Set("audio.mute", SettingValue.Toggle(false));
        first.Set("display.vsync", SettingValue.Toggle(true));
        first.Apply(SettingCategory.Audio);

        var second = Make(store);

        Assert.Equal(SettingsLoadResult.Loaded, second.LoadResult);
        Assert.Equal(20.0, second.AppliedValue("audio.vol").Number);
        Assert.False(second.AppliedValue("audio.mute").Flag);
        Assert.False(second.AppliedValue("display.vsync").Flag);
        Assert.False(second.IsDirty());
    }

    [Fact]
    public void Corrupt_files_fall_back_to_defaults()
    {
        var store = new InMemoryFileStore();
        store.Files[SettingsModel.FileName] = "{ not json";

        var model = Make(store);

        Assert.Equal(SettingsLoadResult.Corrupt, model.LoadResult);
        Assert.Equal(50.0, model.AppliedValue("audio.vol").Number);
        Assert.True(model.IsAtDefaults());
    }

    [Fact]
    public void Invalid_stored_values_are_ignored_or_clamped_and_unknown_ids_are_dropped()
    {
        var store = new InMemoryFileStore();
        store.Files[SettingsModel.FileName] =
            "{\"version\":1,\"values\":{\"audio.vol\":500,\"audio.mute\":\"yes\",\"display.mode\":\"bogus\",\"gone.setting\":1}}";

        var model = Make(store);

        Assert.Equal(SettingsLoadResult.Loaded, model.LoadResult);
        Assert.Equal(100.0, model.AppliedValue("audio.vol").Number);
        Assert.True(model.AppliedValue("audio.mute").Flag);
        Assert.Equal("windowed", model.AppliedValue("display.mode").Text);
    }

    [Fact]
    public void A_failing_store_marks_the_save_failed_but_keeps_the_applied_value()
    {
        var model = Make(new ThrowingStore());
        model.Set("audio.vol", SettingValue.Slider(10));

        model.Apply();

        Assert.True(model.LastSaveFailed);
        Assert.Equal(10.0, model.AppliedValue("audio.vol").Number);
    }

    [Fact]
    public void A_risky_apply_starts_a_countdown_locks_editing_and_reverts_on_expiry()
    {
        var store = new InMemoryFileStore();
        var model = Make(store);
        model.Set("display.mode", SettingValue.Choice("fullscreen"));
        model.Apply();

        Assert.True(model.RevertPending);
        Assert.Equal("fullscreen", model.AppliedValue("display.mode").Text);
        Assert.Equal(15, model.RevertSecondsLeft);
        Assert.False(model.Set("audio.vol", SettingValue.Slider(10)));
        Assert.Empty(model.Apply());
        Assert.Equal("windowed", Make(store).AppliedValue("display.mode").Text);

        Assert.False(model.Tick(10));
        Assert.Equal(5, model.RevertSecondsLeft);
        Assert.True(model.Tick(5));

        Assert.False(model.RevertPending);
        Assert.Equal("windowed", model.AppliedValue("display.mode").Text);
        Assert.Equal("windowed", model.Value("display.mode").Text);
        Assert.False(model.IsDirty());
        Assert.True(model.Set("audio.vol", SettingValue.Slider(10)));
    }

    [Fact]
    public void Keeping_a_risky_change_persists_it_and_stops_the_countdown()
    {
        var store = new InMemoryFileStore();
        var model = Make(store);
        model.Set("display.mode", SettingValue.Choice("fullscreen"));
        model.Apply();

        model.KeepChanges();

        Assert.False(model.RevertPending);
        Assert.False(model.Tick(100));
        Assert.Equal("fullscreen", model.AppliedValue("display.mode").Text);
        Assert.Equal("fullscreen", Make(store).AppliedValue("display.mode").Text);
    }

    [Fact]
    public void Expiry_reverts_only_the_risky_settings_and_reports_them_through_applied()
    {
        var model = Make();
        model.Set("audio.vol", SettingValue.Slider(70));
        model.Set("display.mode", SettingValue.Choice("fullscreen"));
        model.Apply();
        var seen = new List<string>();
        model.Applied += ids => seen.AddRange(ids);

        Assert.True(model.Tick(15));

        Assert.Equal(70.0, model.AppliedValue("audio.vol").Number);
        Assert.Equal("windowed", model.AppliedValue("display.mode").Text);
        Assert.Equal(new[] { "display.mode" }, seen.ToArray());
    }

    [Fact]
    public void Manual_revert_of_a_risky_change_works_without_waiting_for_the_timer()
    {
        var model = Make();
        model.Set("display.mode", SettingValue.Choice("fullscreen"));
        model.Apply();

        model.RevertRisky();

        Assert.False(model.RevertPending);
        Assert.Equal("windowed", model.AppliedValue("display.mode").Text);
        Assert.False(model.Tick(100));
    }

    [Fact]
    public void Non_risky_changes_never_start_a_countdown()
    {
        var model = Make();
        model.Set("audio.vol", SettingValue.Slider(10));

        model.Apply();

        Assert.False(model.RevertPending);
        Assert.False(model.Tick(100));
    }

    [Fact]
    public void The_menu_list_is_scoped_to_the_category_and_searchable()
    {
        var model = Make();

        var list = model.CreateMenuList(SettingCategory.Audio);
        Assert.Equal(2, list.Visible.Count);
        list.SetSearch("master");
        Assert.Equal("audio.vol", list.Current?.Id);
        Assert.Single(list.Visible);
        Assert.Equal(4, model.CreateMenuList().Visible.Count);
    }

    [Fact]
    public void Construction_rejects_duplicate_ids_and_defaults_that_are_not_valid()
    {
        var duplicate = new[]
        {
            SettingDef.Toggle("x", SettingCategory.Audio, "A", true),
            SettingDef.Toggle("x", SettingCategory.Audio, "B", true),
        };
        var badDefault = new[] { SettingDef.Slider("s", SettingCategory.Audio, "S", 500, 0, 100, 1) };
        var badChoice = new[] { SettingDef.Choice("c", SettingCategory.Audio, "C", "zzz", new SettingChoice("a", "A")) };

        Assert.Throws<ArgumentException>(() => new SettingsModel(duplicate, new InMemoryFileStore()));
        Assert.Throws<ArgumentException>(() => new SettingsModel(badDefault, new InMemoryFileStore()));
        Assert.Throws<ArgumentException>(() => new SettingsModel(badChoice, new InMemoryFileStore()));
    }

    [Fact]
    public void The_default_catalog_is_valid_has_unique_ids_and_covers_every_category()
    {
        var all = DefaultSettingDefs.All;
        var model = new SettingsModel(all, new InMemoryFileStore());

        Assert.Equal(all.Count, all.Select(d => d.Id).Distinct().Count());
        Assert.Equal(Enum.GetValues<SettingCategory>().Length, all.Select(d => d.Category).Distinct().Count());
        Assert.True(model.IsAtDefaults());
        Assert.Contains(model.CreateMenuList().Visible, d => d.Id == "display.fps_limit");
        Assert.Contains(all, d => d.Risky);
    }
}
