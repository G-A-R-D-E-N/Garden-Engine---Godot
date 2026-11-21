using System.Linq;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingsPersistenceTests
{
    sealed class FlakyStore : IFileStore
    {
        public bool Fail { get; set; } = true;

        public bool Throw { get; set; }

        public System.Collections.Generic.Dictionary<string, string> Files { get; } = new();

        public string? ReadAllText(string name)
        {
            if (Throw) throw new System.IO.IOException("locked");
            return Files.TryGetValue(name, out var text) ? text : null;
        }

        public bool WriteAllText(string name, string text)
        {
            if (Fail) return false;
            Files[name] = text;
            return true;
        }

        public bool Preserve(string name, string text)
        {
            Files[name + ".corrupt"] = text;
            return true;
        }
    }

    static SettingDef[] Defs() => new[]
    {
        SettingDef.Slider("audio.vol", SettingCategory.Audio, "Master volume", 50, 0, 100, 5),
        SettingDef.Slider("controls.sens", SettingCategory.Controls, "Mouse sensitivity", 0.5, 0, 1, 0.25),
    };

    static BindableAction[] Actions() => new[]
    {
        new BindableAction("jump", "Jump", "Game", "Space", null, "Pad A"),
        new BindableAction("use", "Use", "Game", "E", "F", "Pad X"),
    };

    static SettingsPageModel Page(IFileStore settings, IFileStore keys) =>
        new(new SettingsModel(Defs(), settings), new KeyBindingModel(Actions(), keys));

    static SettingsRow Row(SettingsPageModel page, string key) => page.Rows.Single(r => r.Key == key);

    [Fact]
    public void A_failed_settings_save_stays_unapplied_and_the_next_apply_retries_it()
    {
        var store = new FlakyStore();
        var page = Page(store, new InMemoryFileStore());
        page.Adjust(Row(page, "audio.vol"), 1);

        page.Apply(null);

        Assert.True(page.Settings.LastSaveFailed);
        Assert.True(page.IsDirty());
        Assert.NotEmpty(page.Notice);

        store.Fail = false;
        page.Apply(null);

        Assert.False(page.Settings.LastSaveFailed);
        Assert.False(page.IsDirty());
        Assert.Empty(page.Notice);
        Assert.Contains("55", store.Files[SettingsModel.FileName]);
    }

    [Fact]
    public void A_failed_binding_save_is_reported_and_apply_retries_it()
    {
        var keys = new FlakyStore();
        var page = Page(new InMemoryFileStore(), keys);
        page.BeginCapture(Row(page, "bind:jump:Keyboard"), BindingSlot.Primary);
        page.Capture("G");

        Assert.True(page.Keys.LastSaveFailed);
        Assert.True(page.IsDirty());
        Assert.NotEmpty(page.Notice);

        keys.Fail = false;
        page.Apply(SettingCategory.Audio);

        Assert.False(page.Keys.LastSaveFailed);
        Assert.False(page.IsDirty());
        Assert.Contains("\"G\"", keys.Files[KeyBindingModel.FileName]);
    }

    [Fact]
    public void A_corrupt_file_is_kept_aside_before_defaults_are_written_over_it()
    {
        var store = new FlakyStore { Fail = false };
        store.Files[SettingsModel.FileName] = "{ not json";
        store.Files[KeyBindingModel.FileName] = "[1,2";

        var page = Page(store, store);

        Assert.Equal(SettingsLoadResult.Corrupt, page.Settings.LoadResult);
        Assert.Equal(SettingsLoadResult.Corrupt, page.Keys.LoadResult);
        Assert.Equal("{ not json", store.Files[SettingsModel.FileName + ".corrupt"]);
        Assert.Equal("[1,2", store.Files[KeyBindingModel.FileName + ".corrupt"]);
        Assert.Contains(".corrupt", page.Notice);
    }

    [Fact]
    public void An_unreadable_file_is_never_overwritten_with_defaults()
    {
        var store = new FlakyStore { Fail = false, Throw = true };
        store.Files[SettingsModel.FileName] = "{\"version\":1,\"values\":{\"audio.vol\":90}}";
        var page = Page(store, new InMemoryFileStore());
        Assert.Equal(SettingsLoadResult.Unreadable, page.Settings.LoadResult);
        store.Throw = false;

        page.Adjust(Row(page, "audio.vol"), 1);
        page.Apply(null);

        Assert.True(page.Settings.LastSaveFailed);
        Assert.Contains("90", store.Files[SettingsModel.FileName]);
        Assert.Contains("could not be read", page.Notice);
    }

    [Fact]
    public void Resetting_controls_is_staged_and_revert_restores_the_customised_bindings()
    {
        var keys = new InMemoryFileStore();
        var page = Page(new InMemoryFileStore(), keys);
        page.BeginCapture(Row(page, "bind:jump:Keyboard"), BindingSlot.Primary);
        page.Capture("G");
        var saved = keys.Files[KeyBindingModel.FileName];
        var changes = 0;
        page.Keys.Changed += () => changes++;

        Assert.Equal(1, page.ResetToDefaults(SettingCategory.Controls));

        Assert.True(page.IsDirty(SettingCategory.Controls));
        Assert.Equal("[Space] None", page.Value(Row(page, "bind:jump:Keyboard")));
        Assert.Equal(saved, keys.Files[KeyBindingModel.FileName]);
        Assert.Equal(0, changes);

        Assert.Equal(1, page.Revert(null));

        Assert.False(page.IsDirty());
        Assert.Equal("[G] None", page.Value(Row(page, "bind:jump:Keyboard")));
        Assert.Equal(saved, keys.Files[KeyBindingModel.FileName]);
    }

    [Fact]
    public void Apply_commits_a_staged_control_reset()
    {
        var keys = new InMemoryFileStore();
        var page = Page(new InMemoryFileStore(), keys);
        page.BeginCapture(Row(page, "bind:jump:Keyboard"), BindingSlot.Primary);
        page.Capture("G");
        var changes = 0;
        page.Keys.Changed += () => changes++;
        page.ResetToDefaults(null);

        page.Apply(SettingCategory.Audio);
        Assert.True(page.Keys.HasStagedReset);
        Assert.Equal(0, changes);

        page.Apply(SettingCategory.Controls);

        Assert.False(page.Keys.HasStagedReset);
        Assert.Equal(1, changes);
        Assert.Contains("\"Space\"", keys.Files[KeyBindingModel.FileName]);
        Assert.False(page.IsDirty());
    }
}
