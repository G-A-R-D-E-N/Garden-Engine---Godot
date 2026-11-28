using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingsPageModelTests
{
    static SettingDef[] Defs() => new[]
    {
        SettingDef.Toggle("audio.mute", SettingCategory.Audio, "Mute when unfocused", true) with { Keywords = "background" },
        SettingDef.Slider("audio.vol", SettingCategory.Audio, "Master volume", 50, 0, 100, 5),
        SettingDef.Choice("display.mode", SettingCategory.Display, "Window mode", "windowed",
            new SettingChoice("windowed", "Windowed"), new SettingChoice("fullscreen", "Fullscreen")) with { Risky = true },
        SettingDef.Slider("controls.sens", SettingCategory.Controls, "Mouse sensitivity", 0.5, 0, 1, 0.25),
    };

    static BindableAction[] Actions() => new[]
    {
        new BindableAction("jump", "Jump", "Game", "Space", null, "JoyA"),
        new BindableAction("use", "Use", "Game", "E", "F", "JoyX"),
    };

    static SettingsPageModel Make() =>
        new(new SettingsModel(Defs(), new InMemoryFileStore()), new KeyBindingModel(Actions(), new InMemoryFileStore()));

    static SettingsRow Row(SettingsPageModel page, string key) => page.Rows.Single(r => r.Key == key);

    [Fact]
    public void Rows_are_grouped_by_category_in_enum_order_with_binding_rows_after_the_controls_settings()
    {
        var page = Make();

        Assert.Equal(
            new[]
            {
                "display.mode", "audio.mute", "audio.vol", "controls.sens",
                "bind:jump:Keyboard", "bind:jump:Controller", "bind:use:Keyboard", "bind:use:Controller",
            },
            page.Rows.Select(r => r.Key).ToArray());
        Assert.All(page.Rows.Where(r => r.IsBinding), r => Assert.Equal(SettingCategory.Controls, r.Category));
    }

    [Fact]
    public void Values_are_formatted_per_kind()
    {
        var page = Make();

        Assert.Equal("On", page.Value(Row(page, "audio.mute")));
        Assert.Equal("50", page.Value(Row(page, "audio.vol")));
        Assert.Equal("Windowed", page.Value(Row(page, "display.mode")));
        Assert.Equal("0.5", page.Value(Row(page, "controls.sens")));
        Assert.Equal("[Space] None", page.Value(Row(page, "bind:jump:Keyboard")));
        Assert.Equal("[JoyA] None", page.Value(Row(page, "bind:jump:Controller")));
    }

    [Fact]
    public void Adjust_flips_toggles_cycles_choices_and_steps_sliders_stopping_at_the_ends()
    {
        var page = Make();
        var mute = Row(page, "audio.mute");
        var mode = Row(page, "display.mode");
        var vol = Row(page, "audio.vol");
        var sens = Row(page, "controls.sens");

        Assert.True(page.Adjust(mute, 1));
        Assert.Equal("Off", page.Value(mute));
        Assert.True(page.Adjust(mute, -1));
        Assert.Equal("On", page.Value(mute));

        Assert.True(page.Adjust(mode, 1));
        Assert.Equal("Fullscreen", page.Value(mode));
        Assert.True(page.Adjust(mode, 1));
        Assert.Equal("Windowed", page.Value(mode));
        Assert.True(page.Adjust(mode, -1));
        Assert.Equal("Fullscreen", page.Value(mode));

        Assert.True(page.Adjust(vol, 1));
        Assert.Equal("55", page.Value(vol));
        page.SetSlider(vol, 100);
        Assert.False(page.Adjust(vol, 1));
        Assert.Equal("100", page.Value(vol));

        Assert.True(page.Adjust(sens, 1));
        Assert.Equal("0.75", page.Value(sens));
        Assert.False(page.Adjust(mute, 0));
    }

    [Fact]
    public void Changed_rows_are_marked_dirty_until_applied()
    {
        var page = Make();
        var vol = Row(page, "audio.vol");
        Assert.Equal("Master volume", page.DisplayLabel(vol));

        page.Adjust(vol, 1);
        Assert.Equal("* Master volume", page.DisplayLabel(vol));

        page.Apply(null);
        Assert.Equal("Master volume", page.DisplayLabel(vol));
    }

    [Fact]
    public void Direct_slider_setting_follows_the_range()
    {
        var page = Make();
        var vol = Row(page, "audio.vol");

        Assert.Equal("50", page.Value(vol));
        Assert.True(page.SetSlider(vol, 80));
        Assert.Equal("80", page.Value(vol));
        Assert.False(page.SetSlider(vol, 80));
        Assert.False(page.SetSlider(Row(page, "audio.mute"), 1));
    }

    [Fact]
    public void Direct_toggle_setting_flips_only_toggle_rows_and_ignores_repeats()
    {
        var page = Make();
        var mute = Row(page, "audio.mute");

        Assert.Equal("On", page.Value(mute));
        Assert.True(page.SetToggle(mute, false));
        Assert.Equal("Off", page.Value(mute));
        Assert.True(page.IsDirty(mute));
        Assert.False(page.SetToggle(mute, false));
        Assert.False(page.SetToggle(Row(page, "audio.vol"), true));
        Assert.Equal("50", page.Value(Row(page, "audio.vol")));
    }

    [Fact]
    public void Capture_and_conflict_text_name_the_target_and_clear_with_the_capture()
    {
        var page = Make();
        Assert.Equal("", page.CaptureText);
        Assert.Equal("", page.ConflictText);

        page.Activate(Row(page, "bind:jump:Keyboard"));
        Assert.Equal("Press a key or mouse button for Jump (Keyboard, Primary). Esc cancels, Delete clears.", page.CaptureText);
        page.CancelCapture();
        Assert.Equal("", page.CaptureText);

        page.Activate(Row(page, "bind:jump:Controller"));
        Assert.Equal("Press a controller button for Jump (Controller, Primary). Esc cancels, View clears.", page.CaptureText);
        page.CancelCapture();

        page.Activate(Row(page, "bind:jump:Keyboard"));
        Assert.Equal(CaptureOutcome.Conflict, page.Capture("E"));
        Assert.Equal("E is already used by Use (Keyboard, Primary).", page.ConflictText);
        page.Resolve(ConflictResolution.Cancel);
        Assert.Equal("", page.ConflictText);
    }

    [Fact]
    public void Activate_toggles_and_cycles_but_does_nothing_for_sliders()
    {
        var page = Make();

        Assert.True(page.Activate(Row(page, "audio.mute")));
        Assert.Equal("Off", page.Value(Row(page, "audio.mute")));
        Assert.True(page.Activate(Row(page, "display.mode")));
        Assert.Equal("Fullscreen", page.Value(Row(page, "display.mode")));
        Assert.False(page.Activate(Row(page, "audio.vol")));
        Assert.Equal("50", page.Value(Row(page, "audio.vol")));
    }

    [Fact]
    public void Binding_rows_pick_a_slot_with_left_and_right_and_activating_starts_capture_on_it()
    {
        var page = Make();
        var jump = Row(page, "bind:jump:Keyboard");

        Assert.False(page.Adjust(jump, -1));
        Assert.True(page.Adjust(jump, 1));
        Assert.Equal("Space [None]", page.Value(jump));
        Assert.True(page.Activate(jump));

        Assert.True(page.IsListening);
        Assert.Equal(new BindingRef("jump", InputDevice.Keyboard, BindingSlot.Secondary), page.Keys.Target);
        Assert.Equal(InputDevice.Keyboard, page.CaptureDevice);
        Assert.Contains("Jump", page.CaptureText);
        Assert.Contains("Esc", page.CaptureText);
    }

    [Fact]
    public void Capturing_a_free_key_updates_the_row_and_a_taken_key_asks_how_to_resolve()
    {
        var page = Make();
        var jump = Row(page, "bind:jump:Keyboard");
        page.Activate(jump);
        Assert.Equal(CaptureOutcome.Bound, page.Capture("G"));
        Assert.Equal("[G] None", page.Value(jump));
        Assert.False(page.IsListening);

        page.Activate(jump);
        Assert.Equal(CaptureOutcome.Conflict, page.Capture("E"));
        Assert.True(page.IsResolving);
        Assert.Contains("E", page.ConflictText);
        Assert.Contains("Use", page.ConflictText);
        Assert.True(page.Resolve(ConflictResolution.Swap));
        Assert.Equal("[E] None", page.Value(jump));
        Assert.Equal("[G] F", page.Value(Row(page, "bind:use:Keyboard")));
    }

    [Fact]
    public void Clearing_while_listening_unbinds_the_target_and_ends_the_capture()
    {
        var page = Make();
        var jump = Row(page, "bind:jump:Keyboard");
        page.Activate(jump);

        page.ClearCaptured();

        Assert.False(page.IsListening);
        Assert.Equal("[None] None", page.Value(jump));
        page.ClearBinding(Row(page, "bind:use:Keyboard"));
        Assert.Equal("[None] F", page.Value(Row(page, "bind:use:Keyboard")));
    }

    [Fact]
    public void A_pending_risky_revert_blocks_editing_and_rebinding()
    {
        var page = Make();
        Assert.True(page.SetChoice(Row(page, "display.mode"), "fullscreen"));
        page.Apply(null);
        Assert.True(page.Settings.RevertPending);

        Assert.False(page.Adjust(Row(page, "audio.vol"), 1));
        Assert.False(page.Activate(Row(page, "bind:jump:Keyboard")));
        Assert.False(page.IsListening);

        page.Tick(15);
        Assert.False(page.Settings.RevertPending);
        Assert.Equal("Windowed", page.Value(Row(page, "display.mode")));
    }

    [Fact]
    public void Reset_to_defaults_covers_bindings_only_for_the_controls_category_or_everything()
    {
        var page = Make();
        page.Adjust(Row(page, "controls.sens"), 1);
        page.Adjust(Row(page, "audio.vol"), 1);
        page.Activate(Row(page, "bind:jump:Keyboard"));
        page.Capture("G");

        Assert.Equal(1, page.ResetToDefaults(SettingCategory.Audio));
        Assert.Equal("[G] None", page.Value(Row(page, "bind:jump:Keyboard")));

        Assert.Equal(2, page.ResetToDefaults(SettingCategory.Controls));
        Assert.Equal("[Space] None", page.Value(Row(page, "bind:jump:Keyboard")));
        Assert.Equal("0.5", page.Value(Row(page, "controls.sens")));
        Assert.Equal(0, page.ResetToDefaults(null));
    }

    [Fact]
    public void Scope_comes_from_the_tab_name_and_is_null_for_all_and_favourites()
    {
        Assert.Equal(SettingCategory.Audio, SettingsPageModel.ScopeOf("Audio"));
        Assert.Null(SettingsPageModel.ScopeOf("All"));
        Assert.Null(SettingsPageModel.ScopeOf("Favourites"));
        Assert.Null(SettingsPageModel.ScopeOf(null));
        Assert.Null(SettingsPageModel.ScopeOf("99"));
        Assert.Equal("all", SettingsPageModel.ScopeName(null));
        Assert.Equal("Audio", SettingsPageModel.ScopeName(SettingCategory.Audio));
    }

    [Fact]
    public void The_favourites_tab_and_out_of_range_tabs_have_no_scope_for_apply_revert_and_reset()
    {
        var tabs = new[] { "All", "Display", "Audio", "Favourites" };

        Assert.True(SettingsPageModel.IsActionTab(tabs, 0));
        Assert.True(SettingsPageModel.IsActionTab(tabs, 2));
        Assert.False(SettingsPageModel.IsActionTab(tabs, 3));
        Assert.False(SettingsPageModel.IsActionTab(tabs, -1));
        Assert.False(SettingsPageModel.IsActionTab(tabs, 4));
        Assert.Null(SettingsPageModel.ScopeOfTab(tabs, 0));
        Assert.Equal(SettingCategory.Audio, SettingsPageModel.ScopeOfTab(tabs, 2));
        Assert.Null(SettingsPageModel.ScopeOfTab(tabs, 3));
    }

    [Fact]
    public void The_always_present_favourites_tab_of_the_list_model_is_never_an_action_tab()
    {
        var page = Make();
        var model = new ListMenuModel<SettingsRow>(page.Rows, r => r.Key, r => r.SearchText, r => r.Category.ToString(), page.Value);

        model.SelectTab(model.Tabs.Count - 1);

        Assert.False(SettingsPageModel.IsActionTab(model.Tabs, model.TabIndex));
        model.SelectTab(0);
        Assert.True(SettingsPageModel.IsActionTab(model.Tabs, model.TabIndex));
    }

    [Fact]
    public void The_list_view_searches_keywords_but_shows_only_the_label_and_the_value_as_detail()
    {
        var page = Make();
        var model = new ListMenuModel<SettingsRow>(page.Rows, r => r.Key, r => r.SearchText,
            r => r.Category.ToString(), page.Value);
        var view = new DisplayTextListView<SettingsRow>(model, page.DisplayLabel);

        view.SetSearch("background");

        Assert.Equal(1, view.RowCount);
        Assert.Equal("Mute when unfocused", view.RowText(0));
        Assert.Equal("On", view.RowDetail(0));
        Assert.Equal("", view.RowText(5));
        view.SetSearch("rebind jump");
        Assert.Equal(2, view.RowCount);
        Assert.Equal("Jump (Keyboard)", view.RowText(0));
        view.SetSearch("");
        Assert.Equal(new[] { "All", "Display", "Audio", "Controls", "Favourites" }, view.Tabs.ToArray());
    }

    [Fact]
    public void The_default_catalog_and_actions_build_a_page_with_unique_keys_and_no_default_conflicts()
    {
        var keys = new KeyBindingModel(DefaultBindableActions.All, new InMemoryFileStore());
        var page = new SettingsPageModel(new SettingsModel(DefaultSettingDefs.All, new InMemoryFileStore()), keys);

        Assert.Equal(page.Rows.Count, page.Rows.Select(r => r.Key).Distinct().Count());
        foreach (var action in keys.Actions)
        {
            foreach (var device in new[] { InputDevice.Keyboard, InputDevice.Controller })
            {
                foreach (var slot in new[] { BindingSlot.Primary, BindingSlot.Secondary })
                {
                    var code = keys.Get(action.Id, device, slot);
                    if (code is not null) Assert.Empty(keys.FindConflicts(action.Id, device, slot, code));
                }
            }
        }
    }
}
