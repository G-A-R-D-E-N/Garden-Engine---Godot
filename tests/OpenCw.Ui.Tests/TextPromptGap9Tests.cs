using OpenCommonwealth.Services.Ui.Dialogs;
using Xunit;

namespace OpenCw.Ui.Tests;

public class TextPromptGap9Tests
{
    [Fact]
    public void Without_a_selection_the_range_is_empty_at_the_caret()
    {
        var model = new TextPromptModel("hello");
        model.SetCaret(3);

        Assert.Equal((3, 3, 0), (model.SelectionStart, model.SelectionEnd, model.SelectionLength));
        Assert.False(model.HasSelection);
        Assert.Equal("", model.SelectedText);
    }

    [Fact]
    public void Selecting_all_initially_spans_the_whole_value()
    {
        var model = new TextPromptModel("hello", new TextPromptOptions { SelectAllInitially = true });

        Assert.Equal((0, 5, 5), (model.SelectionStart, model.SelectionEnd, model.SelectionLength));
        Assert.Equal(5, model.Caret);
        Assert.Equal("hello", model.SelectedText);
    }

    [Fact]
    public void Extending_the_selection_backwards_keeps_the_end_at_the_anchor()
    {
        var model = new TextPromptModel("hello");

        model.MoveCaret(-1, extend: true);
        model.MoveCaret(-1, extend: true);

        Assert.Equal(3, model.Caret);
        Assert.Equal((3, 5, 2), (model.SelectionStart, model.SelectionEnd, model.SelectionLength));
        Assert.Equal("lo", model.SelectedText);
    }

    [Fact]
    public void Extending_the_selection_forwards_ends_at_the_caret()
    {
        var model = new TextPromptModel("hello");
        model.SetCaret(1);

        model.MoveCaret(1, extend: true, byWord: true);

        Assert.Equal(5, model.Caret);
        Assert.Equal((1, 5, 4), (model.SelectionStart, model.SelectionEnd, model.SelectionLength));
        Assert.Equal("ello", model.SelectedText);
    }

    [Fact]
    public void The_selection_length_counts_utf16_units_for_a_surrogate_pair()
    {
        var model = new TextPromptModel("a\U0001F600b");

        model.SelectAll();

        Assert.Equal((0, 4, 4), (model.SelectionStart, model.SelectionEnd, model.SelectionLength));
        model.SetCaret(1);
        model.MoveCaret(1, extend: true);
        Assert.Equal((1, 3, 2), (model.SelectionStart, model.SelectionEnd, model.SelectionLength));
        Assert.Equal("\U0001F600", model.SelectedText);
    }

    [Fact]
    public void Composition_replaces_the_selected_range_in_the_display_text_only()
    {
        var model = new TextPromptModel("hello");
        model.SetCaret(3);
        model.MoveCaret(-1, extend: true);
        model.MoveCaret(-1, extend: true);
        Assert.Equal((1, 3, 2), (model.SelectionStart, model.SelectionEnd, model.SelectionLength));

        model.SetComposition("XY");

        Assert.Equal("hXYlo", model.DisplayText);
        Assert.Equal("hello", model.Value);
    }

    [Fact]
    public void Inserting_over_a_selection_replaces_exactly_its_range_and_collapses_it()
    {
        var model = new TextPromptModel("hello");
        model.SetCaret(4);
        model.MoveCaret(-1, extend: true);
        model.MoveCaret(-1, extend: true);

        Assert.Equal(1, model.Insert("Z"));

        Assert.Equal("heZo", model.Value);
        Assert.Equal((3, 3, 0), (model.SelectionStart, model.SelectionEnd, model.SelectionLength));
    }

    [Fact]
    public void Collapsing_a_selection_by_moving_lands_on_its_edges()
    {
        var model = new TextPromptModel("hello");
        model.SetCaret(4);
        model.MoveCaret(-1, extend: true);
        model.MoveCaret(-1, extend: true);

        model.MoveCaret(1);

        Assert.Equal((4, 4, 0), (model.SelectionStart, model.SelectionEnd, model.SelectionLength));
        Assert.Equal(4, model.Caret);
    }
}
