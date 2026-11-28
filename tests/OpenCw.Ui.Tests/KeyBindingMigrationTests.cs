using System.Text.Json;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class KeyBindingMigrationTests
{
    const InputDevice Pad = InputDevice.Controller;
    const BindingSlot P = BindingSlot.Primary;
    const BindingSlot S = BindingSlot.Secondary;

    static InMemoryFileStore Saved(string controllerJson, string? version = "1")
    {
        var store = new InMemoryFileStore();
        var versionField = version is null ? "" : "\"version\":" + version + ",";
        store.Files[KeyBindingModel.FileName] = "{" + versionField + "\"keyboard\":{},\"controller\":{" + controllerJson + "}}";
        return store;
    }

    static KeyBindingModel Load(InMemoryFileStore store) => new(DefaultBindableActions.All, store);

    const string AllLegacy =
        "\"sneak\":[\"Pad RightStick\",null],\"attack\":[\"Pad RightShoulder\",null]," +
        "\"aim\":[\"Pad LeftShoulder\",null],\"reload\":[\"Pad Y\",null]";

    [Theory]
    [InlineData("1")]
    [InlineData(null)]
    public void Unmodified_legacy_controller_defaults_move_to_the_new_defaults(string? version)
    {
        var model = Load(Saved(AllLegacy, version));

        Assert.Equal("Pad B", model.Get("sneak", Pad, P));
        Assert.Equal("Pad Axis TriggerRight", model.Get("attack", Pad, P));
        Assert.Equal("Pad Axis TriggerLeft", model.Get("aim", Pad, P));
        Assert.Equal("Pad DpadDown", model.Get("reload", Pad, P));
        Assert.All(new[] { "sneak", "attack", "aim", "reload" }, id => Assert.True(model.IsDefault(id, Pad)));
    }

    [Fact]
    public void Customised_bindings_survive_while_untouched_legacy_ones_upgrade()
    {
        var model = Load(Saved(
            "\"sneak\":[\"Pad RightStick\",\"Pad DpadUp\"],\"attack\":[\"Pad X\",null],\"aim\":[\"Pad LeftShoulder\",null],\"jump\":[\"Pad Y\",null]"));

        Assert.Equal("Pad RightStick", model.Get("sneak", Pad, P));
        Assert.Equal("Pad DpadUp", model.Get("sneak", Pad, S));
        Assert.Equal("Pad X", model.Get("attack", Pad, P));
        Assert.Equal("Pad Axis TriggerLeft", model.Get("aim", Pad, P));
        Assert.Equal("Pad Y", model.Get("jump", Pad, P));
    }

    [Fact]
    public void A_legacy_binding_is_kept_when_its_new_default_is_held_by_another_action()
    {
        var model = Load(Saved("\"sneak\":[\"Pad RightStick\",null],\"jump\":[\"Pad B\",null]"));

        Assert.Equal("Pad RightStick", model.Get("sneak", Pad, P));
        Assert.Equal("Pad B", model.Get("jump", Pad, P));
    }

    [Fact]
    public void Migration_writes_the_current_version_and_is_idempotent()
    {
        var store = Saved(AllLegacy);
        Load(store);
        var after = store.Files[KeyBindingModel.FileName];

        using (var doc = JsonDocument.Parse(after))
            Assert.Equal(KeyBindingMigration.CurrentVersion, doc.RootElement.GetProperty("version").GetInt32());
        var again = Load(store);

        Assert.Equal(after, store.Files[KeyBindingModel.FileName]);
        Assert.Equal("Pad B", again.Get("sneak", Pad, P));
    }

    [Fact]
    public void A_current_version_file_is_never_rewritten_by_the_migration()
    {
        var store = Saved("\"attack\":[\"Pad RightShoulder\",null]", KeyBindingMigration.CurrentVersion.ToString());
        var before = store.Files[KeyBindingModel.FileName];

        var model = Load(store);

        Assert.Equal("Pad RightShoulder", model.Get("attack", Pad, P));
        Assert.Equal(before, store.Files[KeyBindingModel.FileName]);
    }

    [Theory]
    [InlineData("\"2\"")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("true")]
    public void A_version_that_is_not_a_number_loads_as_an_older_file_instead_of_throwing(string version)
    {
        var model = Load(Saved(AllLegacy, version));

        Assert.Equal(SettingsLoadResult.Loaded, model.LoadResult);
        Assert.Equal("Pad B", model.Get("sneak", Pad, P));
        Assert.Equal("Pad Axis TriggerRight", model.Get("attack", Pad, P));
        Assert.Equal("Pad Axis TriggerLeft", model.Get("aim", Pad, P));
        Assert.Equal("Pad DpadDown", model.Get("reload", Pad, P));
        Assert.True(model.IsDefault("attack", Pad));
    }
}
