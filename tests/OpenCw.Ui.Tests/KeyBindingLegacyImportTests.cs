using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class KeyBindingLegacyImportTests
{
    const InputDevice Kb = InputDevice.Keyboard;
    const BindingSlot P = BindingSlot.Primary;

    static BindableAction[] Actions() => new[]
    {
        new BindableAction("jump", "Jump", "Game", "Space"),
        new BindableAction("use", "Use", "Game", "E"),
    };

    [Theory]
    [InlineData("SPACE", "Space")]
    [InlineData("LEFT MOUSE", "Mouse Left")]
    [InlineData("MOUSE 8", "Mouse 4")]
    [InlineData("MOUSE 9", "Mouse 5")]
    [InlineData("MOUSE 4", null)]
    [InlineData("F5", "F5")]
    [InlineData("W", "W")]
    [InlineData("NONE", null)]
    public void Title_screen_text_is_normalised(string text, string? code) =>
        Assert.Equal(code, KeyBindingLegacy.NormalizeKeyboard(text));

    [Fact]
    public void A_changed_title_binding_is_imported_once_and_persisted()
    {
        var store = new InMemoryFileStore();
        var model = new KeyBindingModel(Actions(), store);

        Assert.True(model.ImportLegacyKeyboard(new Dictionary<string, string?> { ["jump"] = "Q", ["use"] = "E" }));

        Assert.Equal("Q", model.Get("jump", Kb, P));
        Assert.Equal("Q", new KeyBindingModel(Actions(), store).Get("jump", Kb, P));
        Assert.False(model.ImportLegacyKeyboard(new Dictionary<string, string?> { ["jump"] = "Z" }));
    }

    [Fact]
    public void An_existing_keybindings_file_wins()
    {
        var store = new InMemoryFileStore();
        new KeyBindingModel(Actions(), store).ResetAll();
        var model = new KeyBindingModel(Actions(), store);

        Assert.False(model.ImportLegacyKeyboard(new Dictionary<string, string?> { ["jump"] = "Q" }));
        Assert.Equal("Space", model.Get("jump", Kb, P));
    }

    [Fact]
    public void Defaults_import_nothing_and_write_no_file()
    {
        var store = new InMemoryFileStore();
        var model = new KeyBindingModel(Actions(), store);

        Assert.False(model.ImportLegacyKeyboard(new Dictionary<string, string?> { ["jump"] = "Space" }));
        Assert.Null(store.ReadAllText(KeyBindingModel.FileName));
    }

    [Fact]
    public void An_imported_code_clears_an_untouched_action_that_held_it()
    {
        var model = new KeyBindingModel(Actions(), new InMemoryFileStore());

        model.ImportLegacyKeyboard(new Dictionary<string, string?> { ["jump"] = "E" });

        Assert.Equal("E", model.Get("jump", Kb, P));
        Assert.Null(model.Get("use", Kb, P));
    }

    [Fact]
    public void Side_buttons_import_as_the_codes_the_applier_understands()
    {
        var raw = new Dictionary<string, string> { ["attack"] = "MOUSE 8", ["aim"] = "MOUSE 9" };
        var model = new KeyBindingModel(DefaultBindableActions.All, new InMemoryFileStore());

        Assert.True(model.ImportLegacyKeyboard(KeyBindingLegacy.Normalize(raw)));

        Assert.Equal("Mouse 4", model.Get("attack", Kb, P));
        Assert.Equal("Mouse 5", model.Get("aim", Kb, P));
        Assert.Contains(KeyBindingInputMap.Build(model).Entries, e => e.ActionName == "attack" && e.Code == "Mouse 4");
    }

    [Fact]
    public void An_unbindable_mouse_text_is_skipped_not_cleared()
    {
        var raw = new Dictionary<string, string> { ["attack"] = "MOUSE 4", ["jump"] = "NONE" };

        var normal = KeyBindingLegacy.Normalize(raw);

        Assert.False(normal.ContainsKey("attack"));
        Assert.True(normal.ContainsKey("jump"));
        Assert.Null(normal["jump"]);
    }

    [Fact]
    public void Production_defaults_import_nothing_and_only_the_changed_action_moves()
    {
        var store = new InMemoryFileStore();
        var model = new KeyBindingModel(DefaultBindableActions.All, store);

        Assert.False(model.ImportLegacyKeyboard(KeyBindingLegacy.Normalize(new Dictionary<string, string>())));
        Assert.Null(store.ReadAllText(KeyBindingModel.FileName));

        var raw = new Dictionary<string, string> { ["interact"] = "F" };
        Assert.True(model.ImportLegacyKeyboard(KeyBindingLegacy.Normalize(raw)));
        Assert.Equal("F", model.Get("interact", Kb, P));
        Assert.Equal("Ctrl", model.Get("sneak", Kb, P));
    }

    [Fact]
    public void A_title_text_equal_to_the_model_default_imports_nothing()
    {
        var model = new KeyBindingModel(DefaultBindableActions.All, new InMemoryFileStore());
        var raw = new Dictionary<string, string> { ["sneak"] = "CTRL", ["attack"] = "LEFT MOUSE", ["jump"] = "SPACE" };

        Assert.False(model.ImportLegacyKeyboard(KeyBindingLegacy.Normalize(raw)));
    }
}
