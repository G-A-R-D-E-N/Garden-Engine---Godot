using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingsInputDefaultsTests
{
    const int FirstFunctionKey = 4194332;

    static BindableAction Action(string id) => DefaultBindableActions.All.Single(a => a.Id == id);

    static string? ProjectGodotText()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "opencw", "project.godot");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        return null;
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
        foreach (var group in DefaultBindableActions.All.Where(a => a.SharedWith is null).GroupBy(a => a.Context))
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
    public void The_gallery_input_action_does_not_share_a_function_key_with_a_bindable_default()
    {
        var text = ProjectGodotText();
        if (text is null) return;
        var match = Regex.Match(text, @"ui_gallery=\{[^}]*?""physical_keycode"":(\d+)");
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
}
