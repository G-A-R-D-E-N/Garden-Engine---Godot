using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterNameTests
{
    static StatDef[] Stats() => new[]
    {
        new StatDef("a", "Alpha", 1, 5, 1, "Alpha does things."),
        new StatDef("b", "Beta", 1, 5),
    };

    static StatAllocationController Make(string name, out StatAllocationModel model, out CharacterNameModel names, string introduction = "Intro text")
    {
        model = new StatAllocationModel(Stats(), 2, true, null);
        names = new CharacterNameModel(name);
        return new StatAllocationController(model, null, "Title", introduction, names);
    }

    static void Rename(CharacterNameModel names, string text)
    {
        names.BeginEdit();
        names.Editor!.Insert(text);
        names.EndEdit();
    }

    [Fact]
    public void The_special_rules_trim_strip_angle_brackets_and_control_characters_and_keep_unicode()
    {
        var rules = CharacterNameRules.Special;

        Assert.Equal("Nate b", rules.Check("  Nate <b>  ").Name);
        Assert.Equal("AB", rules.Check("A\u0001B").Name);
        Assert.Equal("Zoë 名前", rules.Check("Zoë 名前").Name);
        Assert.True(rules.Check("Nate").IsValid);
        Assert.Null(rules.Check("Nate").Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<>")]
    public void An_empty_name_is_refused_with_the_enter_a_name_message(string? raw)
    {
        var check = CharacterNameRules.Special.Check(raw);

        Assert.Equal(NameProblem.Empty, check.Problem);
        Assert.False(check.IsValid);
        Assert.Equal("Enter a name", check.Message);
    }

    [Fact]
    public void The_special_limit_is_twenty_six_characters_after_cleaning()
    {
        Assert.True(CharacterNameRules.Special.Check(new string('a', 26)).IsValid);
        Assert.True(CharacterNameRules.Special.Check(new string('a', 26) + "<>").IsValid);

        var tooLong = CharacterNameRules.Special.Check(new string('a', 27));

        Assert.Equal(NameProblem.TooLong, tooLong.Problem);
        Assert.Equal("Name is too long (at most 26)", tooLong.Message);
    }

    [Fact]
    public void The_new_game_rules_allow_sixty_four_characters_keep_angle_brackets_and_reject_control_characters()
    {
        var rules = CharacterNameRules.NewGame;

        Assert.True(rules.Check(new string('a', 64)).IsValid);
        Assert.Equal("Name is too long (at most 64)", rules.Check(new string('a', 65)).Message);
        Assert.Equal("<b>", rules.Check("<b>").Name);
        Assert.Equal(NameProblem.InvalidCharacter, rules.Check("A\u0001B").Problem);
        Assert.Equal("Name has characters that are not allowed", rules.Check("A\u0001B").Message);
        Assert.Equal(64, rules.MaxLength);
    }

    [Fact]
    public void A_rule_set_needs_a_positive_limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterNameRules(0, true));
    }

    [Fact]
    public void Editing_replaces_the_selected_name_and_ending_the_edit_stores_the_cleaned_text()
    {
        var names = new CharacterNameModel("  Old  ");
        var changes = 0;
        names.Changed += () => changes++;

        Assert.Equal("Old", names.Name);
        Assert.True(names.BeginEdit());
        Assert.False(names.BeginEdit());
        Assert.True(names.IsEditing);
        Assert.Equal("Old", names.EditText);
        names.Editor!.Insert("<Bob>");
        Assert.Equal("<Bob>", names.EditText);
        Assert.Equal("Bob", names.CurrentCheck.Name);
        Assert.True(names.EndEdit());

        Assert.False(names.IsEditing);
        Assert.Equal("Bob", names.Name);
        Assert.False(names.EndEdit());
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Cancelling_an_edit_keeps_the_old_name_and_a_blank_edit_shows_the_empty_check_while_typing()
    {
        var names = new CharacterNameModel("Nate");

        names.BeginEdit();
        names.Editor!.Insert("   ");
        Assert.Equal(NameProblem.Empty, names.CurrentCheck.Problem);
        Assert.True(names.CancelEdit());

        Assert.Equal("Nate", names.Name);
        Assert.Equal(NameProblem.None, names.CurrentCheck.Problem);
        Assert.False(names.CancelEdit());
    }

    [Fact]
    public void A_name_model_can_use_other_rules_and_defaults_to_the_special_ones()
    {
        var names = new CharacterNameModel(new string('a', 40), CharacterNameRules.NewGame);

        Assert.Equal(40, names.Name.Length);
        Assert.True(names.CurrentCheck.IsValid);
        Assert.Equal(26, new CharacterNameModel(new string('a', 40)).CurrentCheck.MaxLength);
        Assert.Equal(string.Empty, new CharacterNameModel(null).Name);
    }

    [Fact]
    public void The_selected_stat_shows_its_description_under_its_label_and_a_stat_without_one_shows_nothing()
    {
        var ctrl = Make("Nate", out _, out _);

        ctrl.Select(0);
        Assert.Contains(new MenuTextLine("Alpha", LineTone.Heading), ctrl.SideLines);
        Assert.Contains(new MenuTextLine("Alpha does things."), ctrl.SideLines);

        ctrl.Select(1);
        Assert.DoesNotContain(ctrl.SideLines, l => l.Text == "Alpha does things." || l.Text == "Beta");
    }

    [Fact]
    public void The_introduction_shows_on_command_rows_only_and_is_skipped_when_empty()
    {
        var ctrl = Make("Nate", out _, out _);
        var plain = Make("Nate", out _, out _, "");

        ctrl.Select(2);
        plain.Select(2);
        Assert.Contains(new MenuTextLine("Intro text"), ctrl.SideLines);
        Assert.DoesNotContain(plain.SideLines, l => l.Text == "Intro text");

        ctrl.Select(0);
        Assert.DoesNotContain(ctrl.SideLines, l => l.Text == "Intro text");
    }

    [Fact]
    public void The_name_block_shows_the_name_and_a_bad_toned_message_for_an_invalid_one()
    {
        var good = Make("Nate", out _, out _);
        Assert.Equal(new MenuTextLine("Name", LineTone.Heading), good.SideLines[0]);
        Assert.Equal(new MenuTextLine("Nate"), good.SideLines[1]);
        Assert.DoesNotContain(good.SideLines, l => l.Tone == LineTone.Bad);

        var empty = Make("", out _, out _);
        Assert.Equal(new MenuTextLine("(no name)", LineTone.Muted), empty.SideLines[1]);
        Assert.Equal(new MenuTextLine("Enter a name", LineTone.Bad), empty.SideLines[2]);
    }

    [Fact]
    public void While_editing_the_name_block_follows_the_typed_text_and_the_controller_is_notified()
    {
        var ctrl = Make("Nate", out _, out var names);
        var changes = 0;
        ctrl.Changed += () => changes++;

        names.BeginEdit();
        names.Editor!.Insert(new string('x', 5));

        Assert.Equal(2, changes);
        Assert.Equal("xxxxx", ctrl.SideLines[1].Text);
        Assert.Same(names, ctrl.NameModel);
    }

    [Fact]
    public void An_invalid_name_blocks_confirm_with_its_message_even_when_every_point_is_spent()
    {
        var ctrl = Make("", out var model, out var names);
        var results = new List<bool>();
        ctrl.Finished += results.Add;
        model.Increment("a");
        model.Increment("b");
        var confirm = ctrl.RowCount - 1;

        Assert.False(ctrl.Activate(confirm));
        Assert.Equal("Enter a name", ctrl.Message);
        Assert.False(ctrl.IsFinished);

        Rename(names, "Nate");
        Assert.True(ctrl.Activate(confirm));
        Assert.True(ctrl.IsFinished);
        Assert.Equal(new[] { true }, results.ToArray());
    }

    [Fact]
    public void Confirm_commits_an_open_name_edit_first_and_unspent_points_still_block()
    {
        var ctrl = Make("Nate", out var model, out var names);
        var results = new List<bool>();
        ctrl.Finished += results.Add;
        var confirm = ctrl.RowCount - 1;

        Assert.False(ctrl.Activate(confirm));
        Assert.StartsWith("Spend every point", ctrl.Message);

        model.Increment("a");
        model.Increment("b");
        names.BeginEdit();
        names.Editor!.Insert("Bob");
        Assert.True(ctrl.Activate(confirm));
        Assert.False(names.IsEditing);
        Assert.Equal("Bob", names.Name);
        Assert.Equal(new[] { true }, results.ToArray());
    }

    [Fact]
    public void Confirming_while_the_edit_is_blank_commits_it_and_is_refused_for_the_empty_name()
    {
        var ctrl = Make("Nate", out var model, out var names);
        model.Increment("a");
        model.Increment("b");
        names.BeginEdit();
        names.Editor!.Insert("   ");

        Assert.False(ctrl.Activate(ctrl.RowCount - 1));
        Assert.False(names.IsEditing);
        Assert.Equal("Enter a name", ctrl.Message);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void A_rename_row_sits_before_confirm_only_when_there_is_a_name_and_starts_the_edit()
    {
        var ctrl = Make("Nate", out _, out var names);
        var rename = ctrl.RowCount - 2;

        Assert.Equal("Rename", ctrl.Label(rename));
        Assert.Equal(ValueRowKind.Command, ctrl.Kind(rename));
        Assert.True(ctrl.Activate(rename));
        Assert.True(names.IsEditing);
        Assert.Equal("Done", ctrl.Hints[0].Label);
        Assert.Equal("Cancel edit", ctrl.Hints[1].Label);

        var model = new StatAllocationModel(Stats(), 2, true, null);
        var plain = new StatAllocationController(model);
        Assert.DoesNotContain(Enumerable.Range(0, plain.RowCount), r => plain.Label(r) == "Rename");
    }

    [Fact]
    public void While_editing_accept_commits_back_cancels_only_the_edit_and_other_keys_are_swallowed()
    {
        var ctrl = Make("Nate", out _, out var names);
        var results = new List<bool>();
        ctrl.Finished += results.Add;
        var rename = ctrl.RowCount - 2;
        ctrl.Select(rename);

        ctrl.Handle(MenuInputAction.Accept);
        Assert.True(names.IsEditing);
        names.Editor!.Insert("Bob");
        Assert.True(ctrl.Handle(MenuInputAction.Down));
        Assert.Equal(rename, ctrl.SelectedIndex);
        Assert.True(ctrl.Handle(MenuInputAction.Sort));
        Assert.True(names.IsEditing);

        Assert.True(ctrl.Handle(MenuInputAction.Back));
        Assert.False(names.IsEditing);
        Assert.Equal("Nate", names.Name);
        Assert.False(ctrl.IsFinished);

        ctrl.Handle(MenuInputAction.Accept);
        names.Editor!.Insert("Bob");
        ctrl.Handle(MenuInputAction.Accept);
        Assert.False(names.IsEditing);
        Assert.Equal("Bob", names.Name);
        Assert.False(ctrl.IsFinished);

        ctrl.Handle(MenuInputAction.Back);
        Assert.True(ctrl.IsFinished);
        Assert.Equal(new[] { false }, results.ToArray());
    }

    [Fact]
    public void An_over_long_default_name_blocks_confirm_and_a_rename_recovers_it()
    {
        var ctrl = Make(new string('a', 40), out var model, out var names);
        model.Increment("a");
        model.Increment("b");
        var confirm = ctrl.RowCount - 1;

        Assert.False(ctrl.Activate(confirm));
        Assert.Contains("too long", ctrl.Message);

        ctrl.Activate(ctrl.RowCount - 2);
        Assert.Equal(26, names.EditText.Length);
        names.Editor!.Insert("Nate");
        Assert.True(ctrl.Activate(confirm));
        Assert.Equal("Nate", names.Name);
    }


    [Fact]
    public void Without_a_name_model_the_controller_behaves_as_before()
    {
        var model = new StatAllocationModel(Stats(), 2, true, null);
        var ctrl = new StatAllocationController(model);

        Assert.Null(ctrl.NameModel);
        ctrl.Select(2);
        Assert.Empty(ctrl.SideLines);
        ctrl.Select(0);
        Assert.Equal(new MenuTextLine("Alpha", LineTone.Heading), ctrl.SideLines[0]);
    }
}
