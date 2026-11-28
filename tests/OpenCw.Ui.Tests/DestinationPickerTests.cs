using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Travel;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class DestinationPickerTests
{
    static readonly DestinationEntry[] Entries =
    {
        new("a", "Alpha"),
        new("b", "Beta", true, "Too far."),
        new("c", "Gamma"),
    };

    static DestinationPickerController Make(List<string> commits, Func<bool>? valid = null, ActionResult? result = null,
                                            string? highlight = null)
    {
        var picker = DestinationPickerController.TryCreate("Go", "Go to {name}?", Entries, highlight, valid ?? (() => true), id =>
        {
            commits.Add(id);
            return result ?? new ActionResult(true);
        });
        Assert.NotNull(picker);
        return picker!;
    }

    [Fact]
    public void Selecting_asks_for_confirmation_and_confirming_commits_once_and_finishes()
    {
        var commits = new List<string>();
        var picker = Make(commits);

        picker.Handle(MenuInputAction.Accept);

        Assert.True(picker.IsConfirming);
        Assert.Equal("Go to Alpha?", picker.Header);
        Assert.Empty(commits);

        picker.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "a" }, commits);
        Assert.True(picker.IsFinished);
        Assert.True(picker.IsConfirmed);
    }

    [Fact]
    public void Back_from_the_confirmation_returns_to_the_list_and_back_from_the_list_closes()
    {
        var commits = new List<string>();
        var picker = Make(commits);
        picker.Handle(MenuInputAction.Accept);

        picker.Handle(MenuInputAction.Back);

        Assert.False(picker.IsConfirming);
        Assert.False(picker.IsFinished);

        picker.Handle(MenuInputAction.Back);

        Assert.True(picker.IsFinished);
        Assert.False(picker.IsConfirmed);
        Assert.Empty(commits);
    }

    [Fact]
    public void A_disabled_entry_says_its_reason_and_never_asks_for_confirmation()
    {
        var commits = new List<string>();
        var picker = Make(commits, highlight: "b");
        var sounds = new List<string>();
        picker.SoundRequested += sounds.Add;

        Assert.Equal("Too far.", picker.DetailLines.Single().Text);
        picker.Handle(MenuInputAction.Accept);

        Assert.Equal("Too far.", picker.Message);
        Assert.False(picker.IsConfirming);
        Assert.Equal(new[] { "menu.blocked" }, sounds);
    }

    [Fact]
    public void The_highlighted_destination_starts_selected()
    {
        var picker = Make(new List<string>(), highlight: "c");

        Assert.Equal("c", picker.Selected!.Id);
    }

    [Fact]
    public void A_commit_that_calls_back_into_the_picker_cannot_commit_twice()
    {
        var commits = new List<string>();
        DestinationPickerController? picker = null;
        picker = DestinationPickerController.TryCreate("Go", "Go to {name}?", Entries, null, () => true, id =>
        {
            commits.Add(id);
            picker!.Handle(MenuInputAction.Accept);
            picker.Handle(MenuInputAction.Accept);
            return new ActionResult(true);
        });
        picker!.Handle(MenuInputAction.Accept);

        picker.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "a" }, commits);
    }

    [Fact]
    public void A_failed_commit_shows_the_reason_and_returns_to_the_list_unfinished()
    {
        var commits = new List<string>();
        var picker = Make(commits, result: new ActionResult(false, "No route."));
        picker.Handle(MenuInputAction.Accept);

        picker.Handle(MenuInputAction.Accept);

        Assert.Equal("No route.", picker.Message);
        Assert.False(picker.IsFinished);
        Assert.False(picker.IsConfirming);
    }

    [Fact]
    public void A_failed_commit_without_a_reason_uses_the_standard_words()
    {
        var picker = Make(new List<string>(), result: new ActionResult(false));
        picker.Handle(MenuInputAction.Accept);

        picker.Handle(MenuInputAction.Accept);

        Assert.Equal(DestinationPickerController.FailedText, picker.Message);
    }

    [Fact]
    public void The_picker_closes_on_tick_once_the_context_is_no_longer_valid()
    {
        var alive = true;
        var picker = Make(new List<string>(), () => alive);

        picker.Tick(0.1);
        Assert.False(picker.IsFinished);

        alive = false;
        picker.Tick(0.1);

        Assert.True(picker.IsFinished);
        Assert.False(picker.IsConfirmed);
    }

    [Fact]
    public void Invalid_input_yields_no_picker()
    {
        var entries = new[] { new DestinationEntry("a", "Alpha") };
        Func<bool> yes = () => true;
        Func<string, ActionResult> ok = _ => new ActionResult(true);

        Assert.Null(DestinationPickerController.TryCreate("", "x {name}", entries, null, yes, ok));
        Assert.Null(DestinationPickerController.TryCreate("Go", "", entries, null, yes, ok));
        Assert.Null(DestinationPickerController.TryCreate("Go", "x", null, null, yes, ok));
        Assert.Null(DestinationPickerController.TryCreate("Go", "x", entries, null, null, ok));
        Assert.Null(DestinationPickerController.TryCreate("Go", "x", entries, null, yes, null));
        Assert.Null(DestinationPickerController.TryCreate("Go", "x", entries, null, () => false, ok));
        Assert.Null(DestinationPickerController.TryCreate("Go", "x",
            new[] { new DestinationEntry("a", "A"), new DestinationEntry("a", "B") }, null, yes, ok));
        Assert.Null(DestinationPickerController.TryCreate("Go", "x", new[] { new DestinationEntry(" ", "A") }, null, yes, ok));
    }

    [Fact]
    public void An_empty_list_opens_and_says_there_is_nowhere_to_go()
    {
        var picker = DestinationPickerController.TryCreate("Go", "x", Array.Empty<DestinationEntry>(), null, () => true,
            _ => new ActionResult(true));

        Assert.NotNull(picker);
        Assert.Equal(DestinationPickerController.EmptyText, picker!.DetailLines.Single().Text);
        picker.Handle(MenuInputAction.Accept);
        Assert.False(picker.IsConfirming);
    }

    sealed class FakeTravel : ITravelDestinations
    {
        public string Prompt { get; set; } = string.Empty;

        public IReadOnlyList<DestinationEntry> Destinations { get; set; } = Entries;

        public string? HighlightedId { get; set; }

        public bool IsValid { get; set; } = true;

        public List<string> Trips { get; } = new();

        public ActionResult Travel(string destinationId)
        {
            Trips.Add(destinationId);
            return new ActionResult(true);
        }
    }

    [Fact]
    public void The_travel_prompt_asks_with_the_default_question_and_sends_the_trip_to_the_provider()
    {
        var source = new FakeTravel();
        var picker = TravelPrompt.Create(source);

        Assert.NotNull(picker);
        Assert.Equal("Travel", picker!.Title);
        picker.Handle(MenuInputAction.Accept);
        Assert.Equal("Travel to Alpha?", picker.Header);
        picker.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "a" }, source.Trips);
    }

    [Fact]
    public void The_travel_prompt_uses_the_supplied_question_and_refuses_when_travel_is_not_valid_or_missing()
    {
        var source = new FakeTravel { Prompt = "Fast travel to {name}?" };
        var picker = TravelPrompt.Create(source)!;
        picker.Handle(MenuInputAction.Accept);

        Assert.Equal("Fast travel to Alpha?", picker.Header);
        Assert.Null(TravelPrompt.Create(new FakeTravel { IsValid = false }));
        Assert.Null(TravelPrompt.Create(null));
    }
}
