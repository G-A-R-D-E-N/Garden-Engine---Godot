using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Prompts;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ActionHintBarTests
{
    private static InMemoryActionHintSource Source(params ActionHintSpec[] specs)
    {
        var source = new InMemoryActionHintSource();
        source.Set(specs);
        return source;
    }

    private static ActionHintBarModel Synced(InMemoryActionHintSource source)
    {
        var model = new ActionHintBarModel(source);
        model.Sync();
        return model;
    }

    [Fact]
    public void Keyboard_mode_shows_the_key_and_controller_mode_the_fallback()
    {
        var source = Source(new ActionHintSpec("accept", "Enter", "A", "Choose"));
        var model = Synced(source);
        Assert.Equal("Enter", model.Items[0].Binding);
        Assert.Equal("Enter: Choose", model.Items[0].Caption);

        source.Controller = true;
        model.Sync();
        Assert.Equal("A", model.Items[0].Binding);
        Assert.Equal("A: Choose", model.Items[0].Caption);
    }

    [Fact]
    public void An_empty_binding_is_absent_and_nothing_is_invented()
    {
        var model = Synced(Source(new ActionHintSpec("back", "", "B", "Back")));

        Assert.Equal("", model.Items[0].Binding);
        Assert.Equal("Back", model.Items[0].Caption);
    }

    [Fact]
    public void Held_feedback_follows_the_pad_set_in_controller_mode_only()
    {
        var source = Source(new ActionHintSpec("a", "Enter", "A", "Choose", 3), new ActionHintSpec("b", "Esc", "B", "Back", 4));
        source.Held = new HashSet<int> { 3 };
        var model = Synced(source);
        Assert.All(model.Items, item => Assert.False(item.Held));

        source.Controller = true;
        model.Sync();
        Assert.Equal(new[] { true, false }, model.Items.Select(i => i.Held).ToArray());
    }

    [Fact]
    public void A_later_change_to_the_held_set_waits_for_the_next_sync_and_never_invokes_anything()
    {
        var held = new HashSet<int> { 3 };
        var source = Source(new ActionHintSpec("a", "Enter", "A", "Choose", 3));
        source.Controller = true;
        source.Held = held;
        var model = Synced(source);
        Assert.True(model.Items[0].Held);

        held.Clear();
        Assert.True(model.Items[0].Held);
        model.Sync();
        Assert.False(model.Items[0].Held);
        Assert.Empty(source.Invoked);
    }

    [Fact]
    public void Items_keep_the_source_order_and_text_changes_show_up_after_a_sync()
    {
        var source = Source(new ActionHintSpec("one", "1", "A", "First"), new ActionHintSpec("two", "2", "B", "Second"));
        var model = Synced(source);
        Assert.Equal(new[] { "one", "two" }, model.Items.Select(i => i.Name).ToArray());

        source.Set(new[] { new ActionHintSpec("two", "2", "B", "Second changed"), new ActionHintSpec("one", "1", "A", "First") });
        model.Sync();
        Assert.Equal(new[] { "two", "one" }, model.Items.Select(i => i.Name).ToArray());
        Assert.Equal("Second changed", model.Items[0].Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a.b")]
    [InlineData("a:b")]
    [InlineData("a@b")]
    [InlineData("a/b")]
    [InlineData("a\"b")]
    [InlineData("a%b")]
    [InlineData("a\nb")]
    public void Invalid_names_are_rejected_without_leaving_a_half_added_item(string name)
    {
        var model = Synced(Source(new ActionHintSpec(name, "K", "P", "Bad"), new ActionHintSpec("good", "K", "P", "Good")));

        Assert.Equal(new[] { "good" }, model.Items.Select(i => i.Name).ToArray());
        Assert.Equal(1, model.Rejected);
        Assert.False(model.Activate(name));
    }

    [Fact]
    public void A_duplicate_name_keeps_the_first_and_counts_the_second_as_rejected()
    {
        var model = Synced(Source(new ActionHintSpec("same", "K", "P", "First"), new ActionHintSpec("same", "J", "Q", "Second")));

        Assert.Single(model.Items);
        Assert.Equal("First", model.Items[0].Text);
        Assert.Equal(1, model.Rejected);
    }

    [Fact]
    public void A_missing_text_is_rejected_as_an_absent_action()
    {
        var model = Synced(Source(new ActionHintSpec("none", "K", "P", null!)));

        Assert.Empty(model.Items);
        Assert.Equal(1, model.Rejected);
    }

    [Fact]
    public void Activating_a_visible_enabled_hint_invokes_the_source_once()
    {
        var source = Source(new ActionHintSpec("accept", "Enter", "A", "Choose"), new ActionHintSpec("back", "Esc", "B", "Back"));
        var model = Synced(source);

        Assert.True(model.Activate("accept"));
        Assert.Equal(new[] { "accept" }, source.Invoked.ToArray());
    }

    [Fact]
    public void Hidden_disabled_and_unknown_hints_never_activate()
    {
        var source = Source(
            new ActionHintSpec("hidden", "H", "P", "Hidden", Visible: false),
            new ActionHintSpec("disabled", "D", "P", "Disabled", Enabled: false));
        var model = Synced(source);

        Assert.False(model.Activate("hidden"));
        Assert.False(model.Activate("disabled"));
        Assert.False(model.Activate("nothing"));
        Assert.Empty(source.Invoked);
        Assert.Equal(2, model.Items.Count);
    }

    [Fact]
    public void Syncing_hiding_and_disabling_never_invoke_an_action()
    {
        var source = Source(new ActionHintSpec("a", "A", "P", "Do"));
        var model = Synced(source);
        source.Set(new[] { new ActionHintSpec("a", "A", "P", "Do", Visible: false) });
        model.Sync();
        source.Set(new[] { new ActionHintSpec("a", "A", "P", "Do", Enabled: false) });
        model.Sync();
        source.Set(Array.Empty<ActionHintSpec>());
        model.Sync();

        Assert.Empty(source.Invoked);
    }

    [Fact]
    public void The_version_only_moves_when_what_is_shown_changes()
    {
        var source = Source(new ActionHintSpec("a", "A", "P", "Do"));
        var model = Synced(source);
        int version = model.Version;

        model.Sync();
        Assert.Equal(version, model.Version);

        source.Controller = true;
        model.Sync();
        Assert.NotEqual(version, model.Version);
    }

    [Fact]
    public void An_empty_source_gives_an_empty_bar()
    {
        var model = Synced(Source());

        Assert.Empty(model.Items);
        Assert.Equal(0, model.Rejected);
    }

    [Fact]
    public void The_activation_event_of_the_fake_reports_each_invocation()
    {
        var source = Source(new ActionHintSpec("a", "A", "P", "Do"));
        var seen = new List<string>();
        source.Activated += seen.Add;
        var model = Synced(source);

        model.Activate("a");
        model.Activate("a");

        Assert.Equal(new[] { "a", "a" }, seen);
    }
}
