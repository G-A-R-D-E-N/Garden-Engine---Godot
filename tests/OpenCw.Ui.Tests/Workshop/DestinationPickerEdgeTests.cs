using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class DestinationPickerEdgeTests
{
    static readonly DestinationEntry[] Entries = [new("h", "Hill"), new("s", "Sanctuary")];

    [Fact]
    public void Every_name_placeholder_in_the_prompt_is_replaced_and_the_header_shows_it_only_while_confirming()
    {
        var picker = DestinationPickerController.TryCreate("Go", "Go to {name}, {name}?", Entries, null, () => true, _ => new ActionResult(true))!;
        Assert.Equal(string.Empty, picker.Header);

        picker.Handle(MenuInputAction.Accept);
        Assert.Equal("Go to Hill, Hill?", picker.Header);
        Assert.Equal("Go to Hill, Hill?", picker.DetailLines[0].Text);

        picker.Handle(MenuInputAction.Back);
        Assert.Equal(string.Empty, picker.Header);
    }

    [Fact]
    public void A_tick_that_arrives_during_a_commit_does_not_close_the_picker()
    {
        var valid = true;
        DestinationPickerController? picker = null;
        picker = DestinationPickerController.TryCreate("Go", "Go to {name}?", Entries, null, () => valid, _ =>
        {
            valid = false;
            picker!.Tick(0.1);
            return new ActionResult(false, "Nope.");
        })!;
        picker.Handle(MenuInputAction.Accept);

        picker.Handle(MenuInputAction.Accept);

        Assert.False(picker.IsFinished);
        Assert.Equal("Nope.", picker.Message);
        picker.Tick(0.1);
        Assert.True(picker.IsFinished);
    }

    [Fact]
    public void A_duplicate_id_or_a_blank_text_or_a_blank_header_yields_no_picker()
    {
        var duplicate = new List<DestinationEntry> { new("h", "Hill"), new("h", "Hill again") };
        var blank = new List<DestinationEntry> { new("h", " ") };

        Assert.Null(DestinationPickerController.TryCreate("Go", "{name}?", duplicate, null, () => true, _ => new ActionResult(true)));
        Assert.Null(DestinationPickerController.TryCreate("Go", "{name}?", blank, null, () => true, _ => new ActionResult(true)));
        Assert.Null(DestinationPickerController.TryCreate(" ", "{name}?", Entries, null, () => true, _ => new ActionResult(true)));
        Assert.Null(DestinationPickerController.TryCreate("Go", "", Entries, null, () => true, _ => new ActionResult(true)));
    }
}
