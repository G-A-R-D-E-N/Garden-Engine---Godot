using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterPresetStoreTests
{
    sealed class FakeFiles : IPresetFiles
    {
        public readonly Dictionary<string, string> Files = new();
        public bool Throw;
        public bool RefuseWrites;

        public IReadOnlyList<string> List()
        {
            if (Throw) throw new InvalidOperationException("disk unavailable");
            return Files.Keys.ToArray();
        }

        public string? Read(string key)
        {
            if (Throw) throw new InvalidOperationException("disk unavailable");
            return Files.TryGetValue(key, out var text) ? text : null;
        }

        public bool Write(string key, string text)
        {
            if (Throw) throw new InvalidOperationException("disk unavailable");
            if (RefuseWrites) return false;
            Files[key] = text;
            return true;
        }

        public bool Remove(string key)
        {
            if (Throw) throw new InvalidOperationException("disk unavailable");
            return Files.Remove(key);
        }
    }

    [Theory]
    [InlineData("Look 1", "Look 1")]
    [InlineData("  padded  ", "padded")]
    [InlineData("a/b\\c:d", "a_b_c_d")]
    [InlineData("..\\..\\x", "______x")]
    public void Names_become_safe_file_keys(string name, string key)
    {
        Assert.Equal(key, FilePresetStore.Key(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("/ \\")]
    public void Names_without_a_letter_or_digit_are_refused(string name)
    {
        Assert.Null(FilePresetStore.Key(name));
    }

    [Fact]
    public void Long_names_are_cut_to_the_limit()
    {
        var key = FilePresetStore.Key(new string('a', 200));

        Assert.Equal(FilePresetStore.MaxNameLength, key!.Length);
    }

    [Fact]
    public void Save_load_list_and_delete_go_through_the_file_layer_with_sanitised_keys()
    {
        var files = new FakeFiles();
        var store = new FilePresetStore(files);

        Assert.True(store.Save("My_ look", "{}"));
        Assert.Equal(new[] { "My_ look" }, store.Names().ToArray());
        Assert.Equal("{}", store.Load("My: look"));
        Assert.Null(store.Load("other"));
        Assert.True(store.Delete("My: look"));
        Assert.False(store.Delete("My: look"));
        Assert.Empty(store.Names());
    }

    [Fact]
    public void Colliding_names_do_not_overwrite_existing_presets()
    {
        var files = new FakeFiles();
        var store = new FilePresetStore(files);

        Assert.True(store.Save("a_b", "original"));
        Assert.False(store.Save("a/b", "replacement"));
        Assert.False(store.Save("A_B", "replacement"));
        Assert.Equal("original", store.Load("a_b"));
        Assert.True(store.Save("a_b", "updated"));
        Assert.Equal("updated", store.Load("a_b"));
        Assert.False(store.Save(new string('a', 49), "replacement"));
    }

    [Fact]
    public void Names_are_listed_in_case_insensitive_order()
    {
        var files = new FakeFiles();
        files.Files["beta"] = "{}";
        files.Files["Alpha"] = "{}";
        files.Files["gamma"] = "{}";

        Assert.Equal(new[] { "Alpha", "beta", "gamma" }, new FilePresetStore(files).Names().ToArray());
    }

    [Fact]
    public void Disk_errors_are_returned_as_failures_with_a_message_and_never_thrown()
    {
        var files = new FakeFiles { Throw = true };
        var store = new FilePresetStore(files);

        Assert.False(store.Save("x", "{}"));
        Assert.Equal("disk unavailable", store.LastError);
        Assert.Null(store.Load("x"));
        Assert.False(store.Delete("x"));
        Assert.Empty(store.Names());
        Assert.Equal("disk unavailable", store.LastError);
    }

    [Fact]
    public void A_refused_write_and_an_unusable_name_both_report_failure()
    {
        var files = new FakeFiles { RefuseWrites = true };
        var store = new FilePresetStore(files);

        Assert.False(store.Save("x", "{}"));
        Assert.False(new FilePresetStore(new FakeFiles()).Save("...", "{}"));
        Assert.Null(new FilePresetStore(new FakeFiles()).Load("..."));
    }

    [Fact]
    public void An_appearance_model_round_trips_a_preset_through_the_file_store()
    {
        var files = new FakeFiles();
        var data = new GenericCharacterData();
        var first = new AppearanceModel(data.AppearanceControls, new Random(1), new FilePresetStore(files));
        first.Set("sl3", 70);
        first.Set("ch1", 2);

        Assert.True(first.SavePreset("Look 1"));

        var second = new AppearanceModel(data.AppearanceControls, new Random(1), new FilePresetStore(files));
        Assert.Equal(new[] { "Look 1" }, second.PresetNames.ToArray());
        Assert.True(second.LoadPreset("Look 1"));
        Assert.Equal(70, second.Get("sl3"));
        Assert.Equal(2, second.Get("ch1"));
    }

    [Fact]
    public void Saving_through_a_failing_disk_makes_the_model_report_false()
    {
        var data = new GenericCharacterData();
        var model = new AppearanceModel(data.AppearanceControls, new Random(1), new FilePresetStore(new FakeFiles { Throw = true }));

        Assert.False(model.SavePreset("Look 1"));
        Assert.False(model.LoadPreset("Look 1"));
        Assert.Empty(model.PresetNames);
    }
}
