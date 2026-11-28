using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class PlayerInputParityFactAttribute : FactAttribute
{
    public PlayerInputParityFactAttribute()
    {
        if (SettingsInputDefaultsTests.PlayerInputParityUnavailable())
            Skip = "opencw/scripts/Player/PlayerInput.cs is not in this checkout";
    }
}

public class SettingsInputDefaultsTests
{
    const int FirstFunctionKey = 4194332;

    static readonly Dictionary<string, int> PadButtonIndex = new(StringComparer.Ordinal)
    {
        ["A"] = 0,
        ["B"] = 1,
        ["X"] = 2,
        ["Y"] = 3,
        ["LeftStick"] = 7,
        ["RightStick"] = 8,
        ["LeftShoulder"] = 9,
        ["RightShoulder"] = 10,
        ["DpadUp"] = 11,
        ["DpadDown"] = 12,
        ["DpadLeft"] = 13,
        ["DpadRight"] = 14,
    };

    static readonly Dictionary<string, int> PadAxisIndex = new(StringComparer.Ordinal)
    {
        ["LeftX"] = 0,
        ["LeftY"] = 1,
        ["RightX"] = 2,
        ["RightY"] = 3,
        ["TriggerLeft"] = 4,
        ["TriggerRight"] = 5,
    };

    static readonly (string Id, string Runtime)[] RuntimeBindings =
    {
        ("jump", "Jump"),
        ("sprint", "Sprint"),
        ("sneak", "Sneak"),
        ("interact", "Activate"),
        ("attack", "Attack"),
        ("aim", "IronSights"),
        ("reload", "Reload"),
    };

    static readonly (string Id, string Action)[] ProjectBindings =
    {
        ("jump", "jump"),
        ("sprint", "sprint"),
        ("sneak", "sneak"),
        ("interact", "activate"),
        ("attack", "attack"),
        ("aim", "iron_sights"),
    };

    static BindableAction Action(string id) => DefaultBindableActions.All.Single(a => a.Id == id);

    static string? RepoText(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        return null;
    }

    internal static bool PlayerInputParityUnavailable() =>
        RepoText("opencw", "OpenCommonwealth.csproj") is null &&
        RepoText("opencw", "scripts", "Player", "PlayerInput.cs") is null;

    static string? ProjectGodotText() => RepoText("opencw", "project.godot");

    static Dictionary<string, string>? RuntimeControllerBindings()
    {
        var text = RepoText("opencw", "scripts", "Player", "PlayerInput.cs");
        if (text is null) return null;
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match call in Regex.Matches(text, @"\b(?:Add|AddMouse)\((\w+),([^)]*)\)"))
        {
            var args = call.Groups[2].Value.Split(',');
            if (args.Length < 2) continue;
            var code = args[1].Trim();
            if (code.StartsWith("JoyButton.", StringComparison.Ordinal) || code.StartsWith("JoyAxis.", StringComparison.Ordinal))
                bindings[call.Groups[1].Value] = code;
        }
        return bindings;
    }

    static string ActionSection(string text, string action)
    {
        var match = Regex.Match(text, "^" + Regex.Escape(action) + "=\\{.*?^\\}", RegexOptions.Multiline | RegexOptions.Singleline);
        return match.Success ? match.Value : string.Empty;
    }

    [Fact]
    public void Quick_save_and_quick_load_keep_the_classic_function_keys()
    {
        Assert.Equal("F5", Action("quick_save").KeyboardPrimary);
        Assert.Equal("F9", Action("quick_load").KeyboardPrimary);
    }

    [Fact]
    public void Default_keyboard_bindings_do_not_repeat_within_a_context()
    {
        foreach (var group in DefaultBindableActions.All.GroupBy(a => a.Context))
        {
            var codes = group
                .SelectMany(a => new[] { a.KeyboardPrimary, a.KeyboardSecondary })
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(c => c!.ToUpperInvariant())
                .ToList();
            Assert.Equal(codes.Count, codes.Distinct().Count());
        }
    }

    [Fact]
    public void Default_controller_bindings_do_not_repeat_within_a_context()
    {
        foreach (var group in DefaultBindableActions.All.GroupBy(a => a.Context))
        {
            var codes = group
                .SelectMany(a => new[] { a.ControllerPrimary, a.ControllerSecondary })
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(c => c!.ToUpperInvariant())
                .ToList();
            Assert.Equal(codes.Count, codes.Distinct().Count());
        }
    }

    [Fact]
    public void The_gallery_input_action_does_not_share_a_function_key_with_a_bindable_default()
    {
        var text = ProjectGodotText();
        Assert.True(text is not null, "opencw/project.godot was not found");
        var match = Regex.Match(text!, @"ui_gallery=\{[^}]*?""physical_keycode"":(\d+)");
        Assert.True(match.Success, "ui_gallery has no key event in project.godot");
        var gallery = int.Parse(match.Groups[1].Value);

        var taken = DefaultBindableActions.All
            .SelectMany(a => new[] { a.KeyboardPrimary, a.KeyboardSecondary })
            .Where(c => c is not null && Regex.IsMatch(c, "^F([1-9]|1[0-2])$"))
            .Select(c => FirstFunctionKey + int.Parse(c!.Substring(1)) - 1)
            .ToList();

        Assert.Equal(new[] { 4194336, 4194340 }, taken);
        Assert.DoesNotContain(gallery, taken);
    }

    [PlayerInputParityFact]
    public void Controller_defaults_match_the_player_input_map()
    {
        var runtime = RuntimeControllerBindings();
        Assert.True(runtime is not null, "opencw/scripts/Player/PlayerInput.cs was not found");
        Assert.NotEmpty(runtime!);

        foreach (var (id, runtimeName) in RuntimeBindings)
        {
            var binding = KeyBindingEvents.ParseController(Action(id).ControllerPrimary);
            Assert.NotEqual(ControllerBindingKind.None, binding.Kind);
            Assert.True(runtime.TryGetValue(runtimeName, out var runtimeCode), runtimeName + " has no joypad binding in PlayerInput.EnsureActions");
            var prefix = binding.Kind == ControllerBindingKind.Axis ? "JoyAxis." : "JoyButton.";
            Assert.Equal(prefix + binding.Name, runtimeCode);
        }
    }

    [Fact]
    public void Controller_defaults_match_the_project_input_map()
    {
        var text = ProjectGodotText();
        Assert.True(text is not null, "opencw/project.godot was not found");

        foreach (var (id, action) in ProjectBindings)
        {
            var section = ActionSection(text!, action);
            Assert.False(section.Length == 0, action + " is missing from project.godot");
            var binding = KeyBindingEvents.ParseController(Action(id).ControllerPrimary);
            if (binding.Kind == ControllerBindingKind.Axis)
            {
                Assert.True(PadAxisIndex.ContainsKey(binding.Name), binding.Name + " is not a known joypad axis");
                var axis = Regex.Match(section, @"""axis"":(\d+),""axis_value"":(-?[\d.]+)");
                Assert.True(axis.Success, action + " has no joypad axis event in project.godot");
                Assert.Equal(PadAxisIndex[binding.Name], int.Parse(axis.Groups[1].Value));
                Assert.Equal(1.0, double.Parse(axis.Groups[2].Value, CultureInfo.InvariantCulture));
                continue;
            }
            Assert.Equal(ControllerBindingKind.Button, binding.Kind);
            Assert.True(PadButtonIndex.ContainsKey(binding.Name), binding.Name + " is not a known joypad button");
            var button = Regex.Match(section, @"""button_index"":(\d+)");
            Assert.True(button.Success, action + " has no joypad button event in project.godot");
            Assert.Equal(PadButtonIndex[binding.Name], int.Parse(button.Groups[1].Value));
        }
    }
}
