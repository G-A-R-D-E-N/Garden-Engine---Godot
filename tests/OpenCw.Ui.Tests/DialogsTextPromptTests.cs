using System.Text;
using OpenCommonwealth.Services.Ui.Dialogs;
using Xunit;

namespace OpenCw.Ui.Tests;

public class DialogsTextPromptTests
{
    const string Smile = "\U0001F600";

    static TextPromptModel Make(string? initial = null, int max = 64, Func<Rune, bool>? allowed = null,
        TrimMode trim = TrimMode.Ends, bool allowEmpty = false, Func<string, string?>? validator = null) =>
        new(initial, new TextPromptOptions
        {
            MaxLength = max,
            AllowedChar = allowed,
            Trim = trim,
            AllowEmpty = allowEmpty,
            Validator = validator,
        });

    [Fact]
    public void Insert_truncates_at_the_maximum_length_and_reports_how_many_units_went_in()
    {
        var model = Make(max: 5);

        Assert.Equal(5, model.Insert("abcdefgh"));
        Assert.Equal("abcde", model.Value);
        Assert.Equal(0, model.Insert("z"));
        Assert.Equal("abcde", model.Value);
    }

    [Fact]
    public void The_maximum_length_never_splits_a_surrogate_pair()
    {
        var model = Make(max: 3);

        Assert.Equal(2, model.Insert(Smile + Smile));
        Assert.Equal(Smile, model.Value);
    }

    [Fact]
    public void Disallowed_characters_are_dropped_and_control_characters_never_get_in()
    {
        var model = Make(allowed: r => Rune.IsDigit(r));

        model.Insert("a1b2\n3");

        Assert.Equal("123", model.Value);
    }

    [Fact]
    public void Typing_only_disallowed_text_over_a_selection_leaves_the_selection_alone()
    {
        var model = Make("hello", allowed: r => Rune.IsLetter(r));
        model.SelectAll();

        Assert.Equal(0, model.Insert("9"));

        Assert.Equal("hello", model.Value);
        Assert.Equal("hello", model.SelectedText);
    }

    [Fact]
    public void Insert_replaces_the_selection_and_leaves_the_caret_after_the_new_text()
    {
        var model = Make("hello");
        model.SelectAll();

        model.Insert("yo");

        Assert.Equal("yo", model.Value);
        Assert.Equal(2, model.Caret);
        Assert.False(model.HasSelection);
    }

    [Fact]
    public void Insert_at_the_caret_in_the_middle_keeps_both_sides()
    {
        var model = Make("ac");
        model.SetCaret(1);

        model.Insert("b");

        Assert.Equal("abc", model.Value);
        Assert.Equal(2, model.Caret);
    }

    [Fact]
    public void The_maximum_length_counts_the_text_that_a_selection_will_free()
    {
        var model = Make("abcde", max: 5);
        model.SetCaret(1);
        model.SetCaret(3, extend: true);

        Assert.Equal(2, model.Insert("XYZ"));

        Assert.Equal("aXYde", model.Value);
    }

    [Fact]
    public void Paste_turns_line_breaks_and_tabs_into_a_single_space()
    {
        var model = Make();

        model.Paste("a\r\nb\t\tc");

        Assert.Equal("a b c", model.Value);
    }

    [Fact]
    public void Backspace_and_delete_remove_a_whole_surrogate_pair()
    {
        var model = Make("a" + Smile + "b");

        Assert.True(model.Backspace());
        Assert.Equal("a" + Smile, model.Value);
        Assert.True(model.Backspace());
        Assert.Equal("a", model.Value);

        model.MoveToStart();
        model.Insert(Smile);
        model.MoveToStart();
        Assert.True(model.Delete());
        Assert.Equal("a", model.Value);
    }

    [Fact]
    public void Backspace_at_the_start_and_delete_at_the_end_do_nothing()
    {
        var model = Make("ab");
        model.MoveToStart();
        Assert.False(model.Backspace());
        model.MoveToEnd();
        Assert.False(model.Delete());
        Assert.Equal("ab", model.Value);
    }

    [Fact]
    public void Backspace_with_a_selection_removes_only_the_selection()
    {
        var model = Make("abcd");
        model.SetCaret(1);
        model.SetCaret(3, extend: true);

        Assert.True(model.Backspace());

        Assert.Equal("ad", model.Value);
        Assert.Equal(1, model.Caret);
    }

    [Fact]
    public void Word_delete_removes_back_to_the_start_of_the_previous_word()
    {
        var model = Make("one two three");

        model.Backspace(byWord: true);
        Assert.Equal("one two ", model.Value);
        model.Backspace(byWord: true);
        Assert.Equal("one ", model.Value);

        model.MoveToStart();
        model.Delete(byWord: true);
        Assert.Equal(" ", model.Value);
    }

    [Fact]
    public void Extending_the_caret_builds_a_selection_and_a_plain_move_collapses_it_to_an_edge()
    {
        var model = Make("abc");
        model.MoveToStart();
        model.MoveCaret(1, extend: true);
        model.MoveCaret(1, extend: true);
        Assert.Equal("ab", model.SelectedText);

        model.MoveCaret(1);
        Assert.False(model.HasSelection);
        Assert.Equal(2, model.Caret);

        model.SelectAll();
        model.MoveCaret(-1);
        Assert.False(model.HasSelection);
        Assert.Equal(0, model.Caret);
    }

    [Fact]
    public void Setting_the_caret_inside_a_surrogate_pair_snaps_to_before_the_pair()
    {
        var model = Make("a" + Smile + "b");

        model.SetCaret(2);

        Assert.Equal(1, model.Caret);
    }

    [Fact]
    public void Select_all_initially_lets_the_first_keystroke_replace_the_starting_text()
    {
        var model = new TextPromptModel("Old", new TextPromptOptions { SelectAllInitially = true });

        model.Insert("New");

        Assert.Equal("New", model.Value);
    }

    [Fact]
    public void Result_applies_the_trim_rule_without_changing_the_edited_value()
    {
        Assert.Equal("a  b", Make("  a  b  ", trim: TrimMode.Ends).Result);
        Assert.Equal("a b", Make("  a  b  ", trim: TrimMode.Collapse).Result);
        Assert.Equal("  a  b  ", Make("  a  b  ", trim: TrimMode.None).Result);
        Assert.Equal("  a  b  ", Make("  a  b  ").Value);
    }

    [Fact]
    public void Empty_and_whitespace_only_input_is_invalid_unless_empty_is_allowed()
    {
        var model = Make("   ");

        Assert.False(model.Validity.IsValid);
        Assert.Equal("Enter a value.", model.Validity.Message);
        Assert.False(model.CanConfirm);
        Assert.Null(model.Confirm());

        Assert.True(Make("", allowEmpty: true).Validity.IsValid);
        Assert.Equal("", Make("", allowEmpty: true).Confirm());
    }

    [Fact]
    public void The_injected_validator_sees_the_trimmed_value_and_blocks_confirm_with_its_message()
    {
        string? seen = null;
        var model = Make("  bad  ", validator: s =>
        {
            seen = s;
            return s == "bad" ? "That name is taken." : null;
        });

        Assert.Equal("That name is taken.", model.Validity.Message);
        Assert.Equal("bad", seen);
        Assert.Null(model.Confirm());
        Assert.Equal(DialogOutcome.Pending, model.Outcome);

        model.SelectAll();
        model.Insert("good");

        Assert.True(model.Validity.IsValid);
        Assert.Equal("good", model.Confirm());
        Assert.Equal(DialogOutcome.Confirmed, model.Outcome);
    }

    [Fact]
    public void Confirm_returns_the_trimmed_result_and_freezes_the_model()
    {
        var model = Make("  Alex ");

        Assert.Equal("Alex", model.Confirm());

        Assert.Equal(0, model.Insert("x"));
        Assert.False(model.Backspace());
        Assert.Null(model.Confirm());
        Assert.Equal("  Alex ", model.Value);
    }

    [Fact]
    public void Cancel_restores_the_original_text_and_resolves_as_cancelled()
    {
        var model = Make("Bob");
        model.SelectAll();
        model.Insert("Alice");
        Assert.Equal("Alice", model.Value);

        Assert.Equal("Bob", model.Cancel());

        Assert.Equal("Bob", model.Value);
        Assert.Equal(3, model.Caret);
        Assert.Equal(DialogOutcome.Cancelled, model.Outcome);
        Assert.Equal(0, model.Insert("z"));
        Assert.Null(model.Confirm());
    }

    [Fact]
    public void Composition_is_shown_in_the_display_text_but_not_in_the_value()
    {
        var model = Make("Bob");

        model.SetComposition("ka");

        Assert.Equal("Bob", model.Value);
        Assert.Equal("Bobka", model.DisplayText);
        Assert.True(model.IsComposing);
    }

    [Fact]
    public void Composition_over_a_selection_replaces_the_selection_in_the_display()
    {
        var model = Make("Bob");
        model.SelectAll();

        model.SetComposition("X");

        Assert.Equal("X", model.DisplayText);
        Assert.Equal("Bob", model.Value);
    }

    [Fact]
    public void Confirm_and_editing_are_refused_while_composing_and_commit_inserts_the_text()
    {
        var model = Make("Bob");
        model.SetComposition("ka");

        Assert.False(model.CanConfirm);
        Assert.Null(model.Confirm());
        Assert.False(model.Backspace());

        Assert.Equal(2, model.CommitComposition());

        Assert.Equal("Bobka", model.Value);
        Assert.Equal("", model.Composition);
        Assert.True(model.CanConfirm);
    }

    [Fact]
    public void Cancelling_a_composition_discards_it_and_cancel_clears_a_pending_one()
    {
        var model = Make("Bob");
        model.SetComposition("ka");
        model.CancelComposition();
        Assert.False(model.IsComposing);
        Assert.Equal("Bob", model.DisplayText);

        model.SetComposition("ka");
        model.Cancel();
        Assert.False(model.IsComposing);
    }

    [Fact]
    public void Committed_ime_text_is_filtered_like_any_other_input()
    {
        var model = Make(allowed: r => r.Value < 128);

        model.SetComposition("aか");
        model.CommitComposition();

        Assert.Equal("a", model.Value);
    }

    [Fact]
    public void Replace_all_filters_editor_text_and_snaps_the_caret()
    {
        var model = Make(max: 4, allowed: r => r.Value != 'x');

        string result = model.ReplaceAll("axb" + Smile + "cd", 99);

        Assert.Equal("ab" + Smile, result);
        Assert.Equal("ab" + Smile, model.Value);
        Assert.Equal(4, model.Caret);

        model.ReplaceAll("a" + Smile + "b", 2);
        Assert.Equal(1, model.Caret);
    }

    [Fact]
    public void Rune_and_utf16_index_helpers_agree_around_astral_characters()
    {
        string text = "a" + Smile + "b";

        Assert.Equal(3, TextPromptModel.RuneIndexToUtf16(text, 2));
        Assert.Equal(2, TextPromptModel.Utf16ToRuneIndex(text, 3));
        Assert.Equal(4, TextPromptModel.RuneIndexToUtf16(text, 99));
        Assert.Equal(3, TextPromptModel.Utf16ToRuneIndex(text, 99));
    }

    [Fact]
    public void A_zero_maximum_length_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new TextPromptModel("a", new TextPromptOptions { MaxLength = 0 }));
    }

    [Fact]
    public void Every_edit_caret_move_composition_and_outcome_raises_changed_and_a_no_op_does_not()
    {
        var prompt = new TextPromptModel("abc");
        var changes = 0;
        prompt.Changed += () => changes++;

        prompt.Insert("d");
        prompt.Backspace();
        prompt.MoveCaret(-1);
        prompt.SelectAll();
        prompt.SetComposition("x");
        prompt.CancelComposition();
        Assert.Equal(6, changes);

        prompt.Insert("");
        prompt.CancelComposition();
        Assert.Equal(6, changes);

        prompt.Cancel();
        Assert.Equal(7, changes);
    }
}
