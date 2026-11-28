using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Service;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ServiceContentLibraryTests
{
    static ContentItem Item(string name, ContentType type, bool on, string author, string description, params ContentFile[] files) =>
        new(name, type, on, author, description, files);

    static InMemoryContentLibrary Library() => new(new[]
    {
        Item("Alpha.esp", ContentType.Installed, true, "Zoe", "Adds guns", new ContentFile("Data/a.esp", 2048), new ContentFile("Data/a.ba2", 1048576)),
        Item("ccBeta.esl", ContentType.CreationClub, false, "", "", new ContentFile("Data/b.esl", 512)),
        Item("Gamma.esp", ContentType.Installed, true, "Abe", "Big world", new ContentFile("Data/g.ba2", 5000000)),
        Item("Delta.esp", ContentType.Installed, false, "Abe", "", new ContentFile("Data/d.esp", 512)),
    });

    static string[] Visible(ContentLibraryModel model) => model.View.Inner.Rows.Select(i => i.FileName).ToArray();

    [Fact]
    public void Lists_every_item_with_its_state_and_type()
    {
        var model = new ContentLibraryModel(Library());

        Assert.Equal(new[] { "Alpha.esp", "ccBeta.esl", "Gamma.esp", "Delta.esp" }, Visible(model));
        Assert.Equal("[x] Alpha.esp  Installed plugin", model.View.RowText(0));
        Assert.Equal("[ ] ccBeta.esl  Creation Club (manifest)", model.View.RowText(1));
        Assert.Empty(model.View.Tabs);
    }

    [Fact]
    public void Details_say_not_supplied_for_missing_author_and_description()
    {
        var model = new ContentLibraryModel(Library());
        model.View.Select(1);

        var text = model.DetailText;

        Assert.Contains("Type: Creation Club (manifest)", text);
        Assert.Contains("Enabled: No (active profile)", text);
        Assert.Contains("Author: not supplied", text);
        Assert.Contains("Description: not supplied", text);
    }

    [Fact]
    public void Details_list_associated_files_and_their_total_size()
    {
        var model = new ContentLibraryModel(Library());

        var text = model.DetailText;

        Assert.Contains("Associated files: 1.0 MB total (not the package size)", text);
        Assert.Contains("Data/a.esp (2.0 KB, 2048 bytes)", text);
        Assert.Contains("Data/a.ba2 (1.0 MB, 1048576 bytes)", text);
    }

    [Fact]
    public void Details_show_a_read_error()
    {
        var library = new InMemoryContentLibrary(new[] { Item("Bad.esp", ContentType.Installed, true, "", "") with { Error = "header damaged" } });
        var model = new ContentLibraryModel(library);

        Assert.Contains("Error: header damaged", model.DetailText);
        Assert.EndsWith("(error)", model.View.RowText(0));
    }

    [Fact]
    public void Search_matches_name_author_and_description_ignoring_case()
    {
        var model = new ContentLibraryModel(Library());

        model.View.SetSearch("zoe");
        Assert.Equal(new[] { "Alpha.esp" }, Visible(model));

        model.View.SetSearch("BIG WORLD");
        Assert.Equal(new[] { "Gamma.esp" }, Visible(model));

        model.View.SetSearch("ccbeta");
        Assert.Equal(new[] { "ccBeta.esl" }, Visible(model));
    }

    [Fact]
    public void Size_sort_puts_the_largest_first_and_breaks_ties_by_name()
    {
        var model = new ContentLibraryModel(Library());

        model.View.SortBy(2);

        Assert.Equal(new[] { "Gamma.esp", "Alpha.esp", "ccBeta.esl", "Delta.esp" }, Visible(model));
    }

    [Fact]
    public void Author_sort_breaks_ties_by_name_and_next_sort_button_steps_through_the_keys()
    {
        var model = new ContentLibraryModel(Library());

        model.View.Handle(MenuInputAction.TabNext);
        Assert.Equal(0, model.View.SortIndex);
        Assert.Equal(new[] { "Alpha.esp", "ccBeta.esl", "Delta.esp", "Gamma.esp" }, Visible(model));

        model.View.Handle(MenuInputAction.TabNext);
        Assert.True(model.View.SortDescending);

        model.View.Handle(MenuInputAction.TabNext);
        Assert.Equal(1, model.View.SortIndex);
        Assert.False(model.View.SortDescending);
        Assert.Equal(new[] { "ccBeta.esl", "Delta.esp", "Gamma.esp", "Alpha.esp" }, Visible(model));
    }

    [Fact]
    public void Previous_sort_button_starts_a_sort_then_flips_its_direction()
    {
        var model = new ContentLibraryModel(Library());

        model.View.Handle(MenuInputAction.TabPrev);
        Assert.Equal(0, model.View.SortIndex);
        Assert.False(model.View.SortDescending);

        model.View.Handle(MenuInputAction.TabPrev);
        Assert.True(model.View.SortDescending);
        Assert.Equal(new[] { "Gamma.esp", "Delta.esp", "ccBeta.esl", "Alpha.esp" }, Visible(model));
    }

    [Fact]
    public void Selection_follows_the_item_across_sort_and_reload()
    {
        var model = new ContentLibraryModel(Library());
        model.View.Select(2);

        model.View.SortBy(2);
        Assert.Equal("Gamma.esp", model.Selected?.FileName);

        model.Reload();
        Assert.Equal("Gamma.esp", model.Selected?.FileName);
    }

    [Fact]
    public void Accept_and_mouse_activation_ask_to_manage_the_selected_item()
    {
        var model = new ContentLibraryModel(Library());
        var asked = new List<string>();
        model.ManageRequested += item => asked.Add(item.FileName);
        model.View.Select(2);

        Assert.True(model.View.Handle(MenuInputAction.Accept));
        Assert.True(model.View.Activate(1));

        Assert.Equal(new[] { "Gamma.esp", "ccBeta.esl" }, asked);
    }

    [Fact]
    public void Nothing_to_manage_when_the_search_matches_nothing()
    {
        var model = new ContentLibraryModel(Library());
        var asked = 0;
        model.ManageRequested += _ => asked++;
        model.View.SetSearch("no such content");

        Assert.False(model.Manage());

        Assert.Equal(0, asked);
        Assert.Equal("No content matches.", model.DetailText);
    }

    [Fact]
    public void Reload_re_reads_the_library_and_picks_up_the_enabled_state_from_the_profile()
    {
        var enabled = new HashSet<string> { "Alpha.esp" };
        var library = Library();
        library.EnabledOf = name => enabled.Contains(name);
        var model = new ContentLibraryModel(library);
        Assert.Equal("[ ] Gamma.esp  Installed plugin", model.View.RowText(2));
        Assert.Equal(1, library.LoadCount);

        enabled.Add("Gamma.esp");
        Assert.True(model.View.Handle(MenuInputAction.Compare));

        Assert.Equal(2, library.LoadCount);
        Assert.Equal("[x] Gamma.esp  Installed plugin", model.View.RowText(2));
        Assert.Contains("Reloaded.", model.DetailText);
    }

    [Fact]
    public void Hints_label_the_service_actions()
    {
        var model = new ContentLibraryModel(Library());

        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Accept && h.Label == "Manage");
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Compare && h.Label == "Reload");
    }

    [Fact]
    public void Byte_sizes_are_formatted_with_one_decimal_and_invariant_digits()
    {
        Assert.Equal("0 B", ServiceText.Bytes(0));
        Assert.Equal("1023 B", ServiceText.Bytes(1023));
        Assert.Equal("1.0 KB", ServiceText.Bytes(1024));
        Assert.Equal("1.5 MB", ServiceText.Bytes(1572864));
    }

    [Fact]
    public void The_sort_direction_hint_says_name_sort_until_a_sort_is_active()
    {
        var model = new ContentLibraryModel(Library());
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.TabPrev && h.Label == "Sort by name");

        model.View.Handle(MenuInputAction.TabPrev);

        Assert.Equal(0, model.View.SortIndex);
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.TabPrev && h.Label == "Flip sort direction");
    }
}
