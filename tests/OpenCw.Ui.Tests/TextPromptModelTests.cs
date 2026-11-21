using OpenCommonwealth.Services.Ui.Dialogs;
using Xunit;

namespace OpenCw.Ui.Tests;

public class TextPromptModelTests
{
    [Fact]
    public void Defaults_allow_sixty_four_characters_and_reject_an_empty_value()
    {
        var model = new TextPromptModel();

        Assert.Equal(64, model.MaxLength);
        Assert.Equal("", model.Value);
        Assert.Equal(new PromptValidity(false, "Enter a value."), model.Validity);
        Assert.False(model.HasSelection);
    }

    [Fact]
    public void A_custom_empty_message_is_reported_and_a_valid_value_has_no_message()
    {
        var empty = new TextPromptModel("", new TextPromptOptions { EmptyMessage = "Name needed" });
        var filled = new TextPromptModel("x");

        Assert.Equal(new PromptValidity(false, "Name needed"), empty.Validity);
        Assert.Equal(new PromptValidity(true, null), filled.Validity);
    }

    [Fact]
    public void The_initial_text_is_filtered_and_clamped_and_becomes_the_original()
    {
        var model = new TextPromptModel("ab\ncdef", new TextPromptOptions { MaxLength = 4 });

        Assert.Equal("abcd", model.Value);
        Assert.Equal("abcd", model.Original);
        Assert.Equal(4, model.Caret);

        Assert.Equal(0, model.Insert("x"));
        model.Backspace();

        Assert.Equal("abc", model.Value);
        Assert.Equal("abcd", model.Original);
    }

    [Fact]
    public void Paste_of_nothing_inserts_nothing()
    {
        var model = new TextPromptModel("ab");
        var changes = 0;
        model.Changed += () => changes++;

        Assert.Equal(0, model.Paste(null));
        Assert.Equal(0, model.Paste(""));

        Assert.Equal("ab", model.Value);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Paste_keeps_a_leading_and_a_trailing_break_as_one_space_each()
    {
        var leading = new TextPromptModel();
        var trailing = new TextPromptModel();

        Assert.Equal(2, leading.Paste("\r\nx"));
        Assert.Equal(2, trailing.Paste("a\n\t"));

        Assert.Equal(" x", leading.Value);
        Assert.Equal("a ", trailing.Value);
    }

    [Fact]
    public void Normalize_applies_each_trim_mode_to_the_same_text()
    {
        const string text = "  a \t\n b  ";

        Assert.Equal(text, TextPromptModel.Normalize(text, TrimMode.None));
        Assert.Equal("a \t\n b", TextPromptModel.Normalize(text, TrimMode.Ends));
        Assert.Equal("a b", TextPromptModel.Normalize(text, TrimMode.Collapse));
        Assert.Equal("", TextPromptModel.Normalize(" \t ", TrimMode.Collapse));
    }

    [Fact]
    public void A_zero_direction_caret_move_does_nothing_even_with_a_selection()
    {
        var model = new TextPromptModel("abc", new TextPromptOptions { SelectAllInitially = true });
        var changes = 0;
        model.Changed += () => changes++;

        model.MoveCaret(0);

        Assert.Equal(0, changes);
        Assert.Equal(3, model.Caret);
        Assert.Equal(0, model.SelectionStart);
        Assert.Equal("abc", model.SelectedText);
    }

    [Fact]
    public void Delete_by_word_removes_forward_over_the_word_then_over_the_gap_and_the_next_word()
    {
        var model = new TextPromptModel("ab  cd");
        model.MoveToStart();

        Assert.True(model.Delete(byWord: true));
        Assert.Equal("  cd", model.Value);
        Assert.Equal(0, model.Caret);

        Assert.True(model.Delete(byWord: true));
        Assert.Equal("", model.Value);

        Assert.False(model.Delete(byWord: true));
    }

    [Fact]
    public void Move_by_word_forward_skips_the_gap_before_the_next_word()
    {
        var model = new TextPromptModel("ab  cd");
        model.MoveToStart();

        model.MoveCaret(1, byWord: true);
        Assert.Equal(2, model.Caret);

        model.MoveCaret(1, byWord: true);
        Assert.Equal(6, model.Caret);

        model.MoveCaret(-1, byWord: true);
        Assert.Equal(4, model.Caret);
    }

    [Fact]
    public void An_empty_insert_while_composing_drops_the_composition_and_raises_changed_once()
    {
        var model = new TextPromptModel("ab");
        model.SetComposition("zz");
        var changes = 0;
        model.Changed += () => changes++;

        Assert.Equal(0, model.Insert(""));

        Assert.False(model.IsComposing);
        Assert.Equal("ab", model.Value);
        Assert.Equal(1, changes);

        Assert.Equal(0, model.Insert(""));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Once_confirmed_the_text_cannot_be_replaced_composed_or_cancelled_back()
    {
        var model = new TextPromptModel("abc");
        model.Insert("d");
        Assert.Equal("abcd", model.Confirm());

        Assert.Equal("abcd", model.ReplaceAll("zzz", 1));
        model.SetComposition("q");
        Assert.False(model.IsComposing);
        Assert.Equal(0, model.Insert("e"));
        Assert.False(model.Backspace());

        Assert.Equal("abc", model.Cancel());
        Assert.Equal(DialogOutcome.Confirmed, model.Outcome);
        Assert.Equal("abcd", model.Value);
    }

    [Fact]
    public void Once_cancelled_a_second_cancel_returns_the_original_and_confirm_is_refused()
    {
        var model = new TextPromptModel("abc");
        model.Insert("d");

        Assert.Equal("abc", model.Cancel());
        Assert.Equal("abc", model.Cancel());
        Assert.Equal(DialogOutcome.Cancelled, model.Outcome);
        Assert.Null(model.Confirm());
    }
}
