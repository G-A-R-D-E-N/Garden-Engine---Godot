using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Prompts;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ContextPromptTests
{
    // Invariant: a stored activate text keeps no trailing NUL or whitespace, and a blank one reads as null so the default verb shows.
    [Theory]
    [InlineData("Open the hatch\0", "Open the hatch")]
    [InlineData("  Pull lever \0\0", "Pull lever")]
    [InlineData("\0", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Activate_text_drops_trailing_nuls_and_blank_text(string? stored, string? expected)
    {
        Assert.Equal(expected, ActivationTexts.Clean(stored));
    }

    [Fact]
    public void Showing_details_stores_the_supplied_text_exactly()
    {
        var model = new ContextPromptModel();
        model.ShowDetails("Sample Door", "OPEN it", "e");

        Assert.True(model.Visible);
        Assert.Equal("Sample Door", model.TargetName);
        Assert.Equal("OPEN it", model.Action);
        Assert.Equal("e", model.Binding);
        Assert.Equal("e: OPEN it", model.ActionLine);
    }

    [Fact]
    public void Markup_looking_text_and_newlines_are_kept_literally()
    {
        var model = new ContextPromptModel();
        model.ShowDetails("[b]Door[/b]\nSecond line", "[color=red]Open[/color]\r\nmore", "[E]");

        Assert.Equal("[b]Door[/b]\nSecond line", model.TargetName);
        Assert.Equal("[color=red]Open[/color]\r\nmore", model.Action);
        Assert.Equal("[E]: [color=red]Open[/color]\r\nmore", model.ActionLine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData(null)]
    public void An_empty_or_whitespace_action_clears_the_display(string? action)
    {
        var model = new ContextPromptModel();
        model.ShowDetails("Door", "Open", "E");

        model.ShowDetails("Door", action, "E");

        Assert.False(model.Visible);
        Assert.Equal("", model.TargetName);
        Assert.Equal("", model.Action);
        Assert.Equal("", model.Binding);
        Assert.Equal("", model.ActionLine);
    }

    [Fact]
    public void An_empty_target_name_or_binding_is_allowed_and_nothing_is_invented()
    {
        var model = new ContextPromptModel();
        model.ShowDetails("", "Take", "");

        Assert.True(model.Visible);
        Assert.Equal("", model.TargetName);
        Assert.Equal("Take", model.ActionLine);

        model.ShowDetails(null, "Take", "   ");
        Assert.Equal("", model.TargetName);
        Assert.Equal("Take", model.ActionLine);
    }

    [Fact]
    public void Clearing_drops_all_retained_text_and_a_new_show_reuses_the_model()
    {
        var model = new ContextPromptModel();
        model.ShowDetails("Door", "Open", "E");
        model.ClearDetails();

        Assert.False(model.Visible);
        Assert.Equal("", model.TargetName);
        Assert.Equal("", model.Binding);

        model.ShowDetails("Crate", "Search", "F");
        Assert.True(model.Visible);
        Assert.Equal("Crate", model.TargetName);
    }

    [Fact]
    public void Clearing_before_anything_was_shown_is_harmless_and_does_not_change_the_version()
    {
        var model = new ContextPromptModel();
        int version = model.Version;

        model.ClearDetails();

        Assert.Equal(version, model.Version);
        Assert.False(model.Visible);
    }

    [Fact]
    public void The_version_moves_only_when_the_shown_text_changes()
    {
        var model = new ContextPromptModel();
        model.ShowDetails("Door", "Open", "E");
        int version = model.Version;

        model.ShowDetails("Door", "Open", "E");
        Assert.Equal(version, model.Version);

        model.ShowDetails("Door", "Close", "E");
        Assert.NotEqual(version, model.Version);
    }

    [Fact]
    public void Very_long_text_is_stored_whole_for_the_view_to_wrap_and_trim()
    {
        var model = new ContextPromptModel();
        string target = new string('x', 5000);
        model.ShowDetails(target, "Use", "E");

        Assert.Equal(5000, model.TargetName.Length);
    }

    [Fact]
    public void A_source_feeds_and_clears_the_prompt()
    {
        var source = new InMemoryContextPromptSource();
        var model = new ContextPromptModel();

        source.Set("Terminal", "Use", "E");
        model.Sync(source);
        Assert.Equal("E: Use", model.ActionLine);
        Assert.Equal("Terminal", model.TargetName);

        source.Clear();
        model.Sync(source);
        Assert.False(model.Visible);
        Assert.Equal("", model.ActionLine);
    }

    [Fact]
    public void A_source_with_a_blank_action_leaves_the_prompt_hidden()
    {
        var source = new InMemoryContextPromptSource();
        source.Set("Terminal", " ", "E");
        var model = new ContextPromptModel();

        model.Sync(source);

        Assert.False(model.Visible);
    }
}
