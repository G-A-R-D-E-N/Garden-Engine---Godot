using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Service;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ServiceModManagerTests
{
    static ModEntry E(string name, ModKind kind = ModKind.Plugin, bool on = true, string[]? masters = null,
                      bool mandatory = false, bool missing = false, string error = "") =>
        new(name, kind, on, "Data/" + name, masters ?? Array.Empty<string>(), 10, mandatory, missing, error);

    static InMemoryModLibrary Library() => new(new[]
    {
        E("Base.esm", ModKind.Master, true, mandatory: true),
        E("Mod.esp", masters: new[] { "Base.esm" }),
        E("Alpha.esp", on: false, masters: new[] { "Base.esm" }),
        E("Zed.esl", ModKind.Light),
    });

    static string[] Names(ModManagerModel model) => model.Order.Select(e => e.FileName).ToArray();

    static string[] Visible(ModManagerModel model) => model.View.Inner.Rows.Select(e => e.FileName).ToArray();

    [Fact]
    public void Lists_plugins_in_load_order_with_enabled_state()
    {
        var model = new ModManagerModel(Library());

        Assert.Equal(new[] { "Base.esm", "Mod.esp", "Alpha.esp", "Zed.esl" }, Names(model));
        Assert.Equal("001 [x] Base.esm (required)", model.View.RowText(0));
        Assert.Equal("003 [ ] Alpha.esp", model.View.RowText(2));
        Assert.False(model.Dirty);
    }

    [Fact]
    public void Accept_toggles_the_selected_plugin_in_the_working_copy_only()
    {
        var library = Library();
        var model = new ModManagerModel(library);
        model.View.Select(1);

        Assert.True(model.View.Handle(MenuInputAction.Accept));

        Assert.False(model.Order[1].Enabled);
        Assert.True(model.Dirty);
        Assert.True(library.Entries[1].Enabled);
        Assert.Equal(0, library.ApplyCount);
    }

    [Fact]
    public void Mouse_activation_toggles_the_clicked_row()
    {
        var model = new ModManagerModel(Library());

        Assert.True(model.View.Activate(2));

        Assert.True(model.Order[2].Enabled);
    }

    [Fact]
    public void Mandatory_plugin_cannot_be_disabled_or_moved_and_cannot_be_passed()
    {
        var model = new ModManagerModel(Library());

        model.View.Handle(MenuInputAction.Accept);
        Assert.True(model.Order[0].Enabled);
        Assert.Contains("required", model.Status);

        Assert.False(model.MoveSelected(1));
        model.View.Select(1);
        Assert.False(model.MoveSelected(-1));

        Assert.Equal(new[] { "Base.esm", "Mod.esp", "Alpha.esp", "Zed.esl" }, Names(model));
        Assert.False(model.Dirty);
    }

    [Fact]
    public void Shoulders_move_the_plugin_earlier_or_later_and_selection_follows_it()
    {
        var model = new ModManagerModel(Library());
        model.View.Select(3);

        model.View.Handle(MenuInputAction.TabPrev);

        Assert.Equal(new[] { "Base.esm", "Mod.esp", "Zed.esl", "Alpha.esp" }, Names(model));
        Assert.Equal("Zed.esl", model.Selected?.FileName);
        Assert.True(model.Dirty);

        model.View.Handle(MenuInputAction.TabNext);

        Assert.Equal(new[] { "Base.esm", "Mod.esp", "Alpha.esp", "Zed.esl" }, Names(model));
        Assert.False(model.Dirty);
    }

    [Fact]
    public void Moving_past_either_end_is_refused()
    {
        var model = new ModManagerModel(Library());
        model.View.Select(3);

        Assert.False(model.MoveSelected(1));
        Assert.Contains("last", model.Status);
    }

    [Fact]
    public void Moving_is_refused_while_a_sort_hides_load_order()
    {
        var model = new ModManagerModel(Library());
        model.View.SortBy(0);
        model.View.Select(model.View.Inner.Rows.ToList().FindIndex(e => e.FileName == "Zed.esl"));

        Assert.True(model.View.Handle(MenuInputAction.TabPrev));
        Assert.Contains("sort", model.Status);
        Assert.Equal(new[] { "Base.esm", "Mod.esp", "Alpha.esp", "Zed.esl" }, Names(model));
    }

    [Fact]
    public void Sorting_changes_the_visible_rows_but_keeps_the_load_order_numbers()
    {
        var model = new ModManagerModel(Library());

        model.View.SortBy(0);
        Assert.Equal(new[] { "Alpha.esp", "Base.esm", "Mod.esp", "Zed.esl" }, Visible(model));
        Assert.StartsWith("003", model.View.RowText(0));

        model.View.SortBy(1);
        Assert.Equal(new[] { "Base.esm", "Alpha.esp", "Mod.esp", "Zed.esl" }, Visible(model));

        model.View.SortBy(2);
        Assert.Equal(new[] { "Base.esm", "Mod.esp", "Zed.esl", "Alpha.esp" }, Visible(model));
    }

    [Fact]
    public void Search_filters_by_file_name()
    {
        var model = new ModManagerModel(Library());

        model.View.SetSearch("ZED");

        Assert.Equal(new[] { "Zed.esl" }, Visible(model));
    }

    [Fact]
    public void Apply_refuses_a_load_order_the_library_reports_errors_for()
    {
        var library = new InMemoryModLibrary(new[]
        {
            E("Base.esm", ModKind.Master, mandatory: true),
            E("Parent.esp"),
            E("Child.esp", masters: new[] { "Parent.esp" }),
        });
        var model = new ModManagerModel(library);
        model.View.Select(1);
        model.View.Handle(MenuInputAction.Accept);

        Assert.True(model.Dirty);
        Assert.False(model.Apply());

        Assert.Equal(0, library.ApplyCount);
        Assert.Contains("Cannot apply: Child.esp needs Parent.esp enabled.", model.Status);
        Assert.Contains(model.Problems, p => p.Severity == ProblemSeverity.Error && p.FileName == "Child.esp");
        Assert.True(model.Dirty);

        model.View.Handle(MenuInputAction.Accept);
        Assert.False(model.Dirty);
    }

    [Fact]
    public void Apply_writes_through_the_library_and_asks_for_a_restart()
    {
        var library = Library();
        var model = new ModManagerModel(library);
        model.View.Select(2);
        model.View.Handle(MenuInputAction.Accept);

        Assert.True(model.View.Handle(MenuInputAction.Favourite));

        Assert.Equal(1, library.ApplyCount);
        Assert.True(library.Entries[2].Enabled);
        Assert.Equal(new[] { "Base.esm", "Mod.esp", "Alpha.esp", "Zed.esl" }, library.Applied!.Select(e => e.FileName).ToArray());
        Assert.False(model.Dirty);
        Assert.True(model.RestartRequired);
        Assert.Contains("Restart", model.Status);
    }

    [Fact]
    public void A_failed_apply_keeps_the_edits_and_shows_the_reason()
    {
        var library = Library();
        library.FailApply = true;
        var model = new ModManagerModel(library);
        model.View.Select(2);
        model.View.Handle(MenuInputAction.Accept);

        Assert.False(model.Apply());

        Assert.True(model.Dirty);
        Assert.True(model.Order[2].Enabled);
        Assert.Contains("Apply failed", model.Status);
        Assert.False(library.Entries[2].Enabled);
    }

    [Fact]
    public void Apply_with_no_changes_is_refused()
    {
        var library = Library();
        var model = new ModManagerModel(library);

        Assert.False(model.Apply());

        Assert.Equal(0, library.ApplyCount);
    }

    [Fact]
    public void Discard_restores_the_loaded_order_and_states()
    {
        var model = new ModManagerModel(Library());
        model.View.Select(3);
        model.View.Handle(MenuInputAction.TabPrev);
        model.View.Handle(MenuInputAction.Accept);
        Assert.True(model.Dirty);

        model.View.Handle(MenuInputAction.Compare);

        Assert.False(model.Dirty);
        Assert.Equal(new[] { "Base.esm", "Mod.esp", "Alpha.esp", "Zed.esl" }, Names(model));
        Assert.True(model.Order[3].Enabled);
    }

    [Fact]
    public void Back_with_unsaved_edits_needs_a_second_back_which_discards()
    {
        var model = new ModManagerModel(Library());
        model.View.Select(2);
        model.View.Handle(MenuInputAction.Accept);

        Assert.True(model.View.Handle(MenuInputAction.Back));
        Assert.True(model.Dirty);
        Assert.Contains("Unsaved", model.Status);

        Assert.False(model.View.Handle(MenuInputAction.Back));
        Assert.False(model.Dirty);
    }

    [Fact]
    public void Another_action_between_two_backs_restarts_the_guard()
    {
        var model = new ModManagerModel(Library());
        model.View.Select(2);
        model.View.Handle(MenuInputAction.Accept);

        model.View.Handle(MenuInputAction.Back);
        model.View.Handle(MenuInputAction.Down);

        Assert.True(model.View.Handle(MenuInputAction.Back));
        Assert.True(model.Dirty);
    }

    [Fact]
    public void Back_without_edits_is_left_to_the_menu_stack()
    {
        var model = new ModManagerModel(Library());

        Assert.False(model.View.Handle(MenuInputAction.Back));
    }

    [Fact]
    public void A_missing_plugin_stays_listed_and_cannot_be_enabled()
    {
        var library = new InMemoryModLibrary(new[] { E("Base.esm", ModKind.Master, mandatory: true), E("Gone.esp", on: false, missing: true) });
        var model = new ModManagerModel(library);
        model.View.Select(1);

        model.View.Handle(MenuInputAction.Accept);

        Assert.False(model.Order[1].Enabled);
        Assert.Contains("(missing)", model.View.RowText(1));
        Assert.Contains("missing", model.Status);
    }

    [Fact]
    public void Focus_selects_the_named_plugin_even_after_a_search()
    {
        var model = new ModManagerModel(Library());
        model.View.SetSearch("mod");

        Assert.True(model.Focus("Zed.esl"));

        Assert.Equal("Zed.esl", model.Selected?.FileName);
        Assert.False(model.Focus("Nope.esp"));
    }

    [Fact]
    public void Reload_picks_up_changes_made_outside_the_menu_and_drops_edits()
    {
        var library = Library();
        var model = new ModManagerModel(library);
        model.View.Handle(MenuInputAction.Down);
        model.View.Handle(MenuInputAction.Accept);
        library.Entries[2] = library.Entries[2] with { Enabled = true };

        model.Reload();

        Assert.True(model.Order[2].Enabled);
        Assert.True(model.Order[1].Enabled);
        Assert.False(model.Dirty);
    }

    [Fact]
    public void Hints_name_the_service_actions_for_the_pad_buttons()
    {
        var model = new ModManagerModel(Library());
        model.View.Select(1);

        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Accept && h.Label == "Disable");
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Favourite && h.Label == "Apply");
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Compare && h.Label == "Discard changes");
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.TabPrev && h.Label == "Move earlier");
        Assert.Empty(model.View.Tabs);
    }

    [Fact]
    public void Details_show_path_masters_load_position_and_errors()
    {
        var library = new InMemoryModLibrary(new[]
        {
            E("Base.esm", ModKind.Master, mandatory: true),
            E("Bad.esp", on: false, masters: new[] { "Base.esm" }, error: "header damaged"),
        });
        var model = new ModManagerModel(library);
        model.View.Select(1);

        var text = model.DetailText;

        Assert.Contains("Path: Data/Bad.esp", text);
        Assert.Contains("Masters: Base.esm", text);
        Assert.Contains("Load order: 2 of 2", text);
        Assert.Contains("Error: header damaged", text);
    }

    [Fact]
    public void Focus_on_content_that_is_not_a_plugin_says_so_in_the_status()
    {
        var model = new ModManagerModel(Library());

        Assert.False(model.Focus("Nope.esp"));

        Assert.Equal("Nope.esp is not in the load order.", model.Status);
        Assert.Contains("Nope.esp is not in the load order.", model.DetailText);
    }

    [Fact]
    public void The_back_hint_names_unsaved_changes_instead_of_promising_a_discard()
    {
        var model = new ModManagerModel(Library());
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Back && h.Label == "Back");
        model.View.Select(1);
        model.View.Handle(MenuInputAction.Accept);

        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Back && h.Label == "Back (unsaved changes)");
    }
}
