using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingsBindingDefaultsTests
{
    const InputDevice Kb = InputDevice.Keyboard;
    const InputDevice Pad = InputDevice.Controller;
    const BindingSlot P = BindingSlot.Primary;
    const BindingSlot S = BindingSlot.Secondary;

    static readonly Dictionary<string, int> KeyCodes = new()
    {
        ["W"] = 87, ["S"] = 83, ["A"] = 65, ["D"] = 68, ["C"] = 67, ["V"] = 86, ["E"] = 69, ["R"] = 82, ["Z"] = 90, ["Q"] = 81,
        ["Space"] = 32, ["Shift"] = 4194325, ["Ctrl"] = 4194326, ["CapsLock"] = 4194329,
    };

    static readonly Dictionary<string, int> PadButtons = new()
    {
        ["Pad A"] = 0, ["Pad B"] = 1, ["Pad X"] = 2, ["Pad Y"] = 3, ["Pad LeftStick"] = 7, ["Pad RightStick"] = 8,
        ["Pad DpadUp"] = 11, ["Pad DpadLeft"] = 13, ["Pad DpadRight"] = 14,
    };

    sealed record ProjectEvents(HashSet<string> Keyboard, HashSet<string> Controller);

    static Dictionary<string, ProjectEvents> ProjectActions()
    {
        string? text = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && text is null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "opencw", "project.godot");
            if (File.Exists(path)) text = File.ReadAllText(path);
        }
        Assert.NotNull(text);
        var result = new Dictionary<string, ProjectEvents>();
        foreach (Match block in Regex.Matches(text!, @"^(\w+)=\{\r?\n(.*?)^\}", RegexOptions.Multiline | RegexOptions.Singleline))
        {
            var keyboard = new HashSet<string>();
            var controller = new HashSet<string>();
            foreach (var ev in block.Groups[2].Value.Split("Object(").Skip(1))
            {
                if (ev.StartsWith("InputEventKey"))
                    keyboard.Add("key:" + Regex.Match(ev, "\"physical_keycode\":(\\d+)").Groups[1].Value);
                else if (ev.StartsWith("InputEventMouseButton"))
                    keyboard.Add("mouse:" + Regex.Match(ev, "\"button_index\":(\\d+)").Groups[1].Value);
                else if (ev.StartsWith("InputEventJoypadButton"))
                    controller.Add("button:" + Regex.Match(ev, "\"button_index\":(\\d+)").Groups[1].Value);
                else if (ev.StartsWith("InputEventJoypadMotion") && int.Parse(Regex.Match(ev, "\"axis\":(\\d+)").Groups[1].Value) >= 4)
                    controller.Add("axis:" + Regex.Match(ev, "\"axis\":(\\d+)").Groups[1].Value);
            }
            result[block.Groups[1].Value] = new ProjectEvents(keyboard, controller);
        }
        return result;
    }

    static string? KeyboardEvent(string? code)
    {
        if (code is null) return null;
        if (KeyBindingInputMap.MouseButtonIndex(code) is { } mouse) return "mouse:" + mouse;
        return "key:" + KeyCodes[code];
    }

    static string? ControllerEvent(string? code)
    {
        if (code is null) return null;
        if (KeyBindingInputMap.TriggerAxis(code) is { } axis) return "axis:" + axis;
        return "button:" + PadButtons[code];
    }

    [Fact]
    public void Displayed_defaults_equal_the_project_input_map_for_every_action_it_defines()
    {
        var project = ProjectActions();
        var checkedActions = 0;
        foreach (var action in DefaultBindableActions.All)
        {
            if (!project.TryGetValue(KeyBindingInputMap.ActionNameFor(action.Id), out var live)) continue;
            checkedActions++;
            var keyboard = new[] { action.KeyboardPrimary, action.KeyboardSecondary }.Select(KeyboardEvent).Where(e => e is not null).ToHashSet();
            var controller = new[] { action.ControllerPrimary, action.ControllerSecondary }.Select(ControllerEvent).Where(e => e is not null).ToHashSet();
            Assert.True(keyboard.SetEquals(live.Keyboard!), $"{action.Id} keyboard: {string.Join(",", keyboard)} vs {string.Join(",", live.Keyboard)}");
            Assert.True(controller.SetEquals(live.Controller), $"{action.Id} controller: {string.Join(",", controller)} vs {string.Join(",", live.Controller)}");
        }
        Assert.True(checkedActions >= 15);
    }

    [Fact]
    public void Actions_registered_at_runtime_use_the_player_input_defaults()
    {
        var reload = DefaultBindableActions.All.Single(a => a.Id == "reload");
        Assert.Equal(("R", "Pad DpadDown"), (reload.KeyboardPrimary, reload.ControllerPrimary));
        var draw = DefaultBindableActions.All.Single(a => a.Id == "draw_weapon");
        Assert.Equal(("F", "Pad LeftShoulder"), (draw.KeyboardPrimary, draw.ControllerPrimary));
        Assert.Equal("Alt", DefaultBindableActions.All.Single(a => a.Id == "walk_modifier").KeyboardPrimary);
    }

    [Fact]
    public void Every_action_the_player_polls_is_listed_so_conflicts_can_see_it()
    {
        var ids = DefaultBindableActions.All.Select(a => a.Id).ToHashSet();
        foreach (var id in new[] { "vats", "toggle_run", "toggle_pov", "rotate_carried", "auto_move", "draw_weapon", "walk_modifier" })
            Assert.Contains(id, ids);
    }

    [Fact]
    public void Binding_jump_to_a_key_held_by_vats_reports_the_vats_conflict()
    {
        var model = new KeyBindingModel(DefaultBindableActions.All, new InMemoryFileStore());
        model.BeginCapture("jump", Kb, P);

        Assert.Equal(CaptureOutcome.Conflict, model.Capture("Q"));

        Assert.Equal(new[] { new BindingRef("vats", Kb, P) }, model.Conflicts.ToArray());
    }

    [Fact]
    public void Reload_and_rotate_carried_may_share_r_but_no_other_action_may()
    {
        var model = new KeyBindingModel(DefaultBindableActions.All, new InMemoryFileStore());

        Assert.Empty(model.FindConflicts("reload", Kb, P, "R"));
        Assert.Empty(model.FindConflicts("rotate_carried", Kb, P, "R"));
        Assert.Equal(2, model.FindConflicts("jump", Kb, P, "R").Count);
    }

    [Fact]
    public void Trigger_defaults_are_bindable_codes_and_survive_the_input_map_plan()
    {
        var model = new KeyBindingModel(DefaultBindableActions.All, new InMemoryFileStore());

        Assert.Equal("Trigger Right", model.Get("attack", Pad, P));
        Assert.Equal("Trigger Left", model.Get("aim", Pad, P));
        Assert.True(model.IsDefault("attack", Pad));

        model.BeginCapture("attack", Pad, P);
        Assert.Equal(CaptureOutcome.Bound, model.Capture("Pad Start"));
        var plan = KeyBindingInputMap.Build(model);

        Assert.Contains(new InputTarget("attack", Pad, true), plan.Targets);
        Assert.DoesNotContain(new InputMapEntry("attack", Pad, "Trigger Right"), plan.Entries);
    }

    [Fact]
    public void Mouse_and_trigger_codes_round_trip_and_include_the_left_button()
    {
        Assert.Equal("Mouse Left", KeyBindingInputMap.MouseCode(1));
        Assert.Null(KeyBindingInputMap.MouseCode(4));
        foreach (var button in new[] { 1, 2, 3, 8, 9 })
            Assert.Equal(button, KeyBindingInputMap.MouseButtonIndex(KeyBindingInputMap.MouseCode(button)!));
        Assert.Equal("Trigger Left", KeyBindingInputMap.TriggerCode(4));
        Assert.Equal("Trigger Right", KeyBindingInputMap.TriggerCode(5));
        Assert.Null(KeyBindingInputMap.TriggerCode(0));
        Assert.Equal(5, KeyBindingInputMap.TriggerAxis("Trigger Right"));
        Assert.Null(KeyBindingInputMap.TriggerAxis("Pad A"));
    }

    [Fact]
    public void A_version_one_file_keeps_real_overrides_but_not_the_old_wrong_defaults()
    {
        var store = new InMemoryFileStore();
        store.Files[KeyBindingModel.FileName] = """
            {"version":1,
             "keyboard":{"move_forward":["W","Up"],"sneak":["Ctrl",null],"jump":["G",null],"attack":["Mouse Left",null]},
             "controller":{"sneak":["Pad RightStick",null],"attack":["Pad RightShoulder",null],"jump":["Pad B",null]}}
            """;

        var model = new KeyBindingModel(DefaultBindableActions.All, store);

        Assert.Null(model.Get("move_forward", Kb, S));
        Assert.Equal("C", model.Get("sneak", Kb, P));
        Assert.Equal("Ctrl", model.Get("sneak", Kb, S));
        Assert.Equal("Pad B", model.Get("sneak", Pad, P));
        Assert.Equal("Trigger Right", model.Get("attack", Pad, P));
        Assert.Equal("G", model.Get("jump", Kb, P));
        Assert.Equal("Pad B", model.Get("jump", Pad, P));
        Assert.True(model.IsDefault("move_forward", Kb));
        Assert.False(model.IsDefault("jump", Kb));
    }

    [Fact]
    public void A_version_two_file_is_read_as_written()
    {
        var store = new InMemoryFileStore();
        var first = new KeyBindingModel(DefaultBindableActions.All, store);
        first.BeginCapture("sneak", Kb, P);
        first.Capture("G");
        Assert.Contains("\"version\": 2", store.Files[KeyBindingModel.FileName]);

        var second = new KeyBindingModel(DefaultBindableActions.All, store);

        Assert.Equal("G", second.Get("sneak", Kb, P));
    }
}
