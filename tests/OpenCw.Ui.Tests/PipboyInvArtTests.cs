using OpenCommonwealth.Services.Ui.Gallery;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Swf;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipboyInvArtTests
{
    private const string Info = "Menu_mc/BottomBar_mc/Info_mc/";
    private const string Carousel = "Menu_mc/Header_mc/TabHeader_mc/AlphaHolder/";

    private static InventoryListView ViewOf(params InventoryItem[] items) => new(new InventoryModel(items));

    private static InventoryItem Item(string id, string category, double weight = 1, double value = 10, int count = 1, bool equipped = false) =>
        new(id, "Item " + id, category, weight, value, count, equipped);

    private static InventoryListView Numbered(int count)
    {
        var items = Enumerable.Range(0, count).Select(i => Item($"i{i:00}", "Cat A")).ToArray();
        return ViewOf(items);
    }

    [Fact]
    public void ShellBindsTheFooterSegmentsInOrderAndHidesTheSlotsTheInventoryDoesNotUse()
    {
        var source = new SamplePlayerStatus();
        var segments = PipBoyFooter.Segments(PipBoyTab.Inventory, source);
        var b = PipboyInvArt.Shell(ViewOf(Item("a", "Cat A")), source);
        Assert.Equal(2, segments.Count);
        Assert.Equal(segments[0], b.Text[Info + "HP_tf"]);
        Assert.Equal(segments[1], b.Text[Info + "LVL_tf"]);
        Assert.DoesNotContain(Info + "HP_tf", b.Hide);
        Assert.DoesNotContain(Info + "LVL_tf", b.Hide);
        Assert.Contains(Info + "AP_tf", b.Hide);
        Assert.False(b.Text.ContainsKey(Info + "AP_tf"));
        Assert.Contains(Info + "XPMeter_mc", b.Hide);
        Assert.Empty(b.FillX);
        Assert.Contains("Menu_mc/Header_mc/LeftTriggerButton_mc", b.Hide);
        Assert.Contains("Menu_mc/Header_mc/RightTriggerButton_mc", b.Hide);
    }

    [Fact]
    public void ShellShowsDashesWithoutAStatusSource()
    {
        var b = PipboyInvArt.Shell(ViewOf(Item("a", "Cat A")), null);
        Assert.Equal(PipBoyFooter.Unknown, b.Text[Info + "HP_tf"]);
        Assert.Contains(Info + "LVL_tf", b.Hide);
        Assert.Contains(Info + "AP_tf", b.Hide);
    }

    [Fact]
    public void ShellFillsTheTabCarouselAroundTheCurrentTabAndWrapsAtTheEnds()
    {
        var view = ViewOf(Item("a", "Cat A"), Item("b", "Cat B"), Item("c", "Cat C"));
        Assert.Equal(new[] { "All", "Cat A", "Cat B", "Cat C", "Favourites" }, view.Tabs);
        var b = PipboyInvArt.Shell(view, null);
        Assert.Equal("ALL", b.Text[Carousel + "Selected/textField_tf"]);
        Assert.Equal("CAT A", b.Text[Carousel + "RightOne/textField_tf"]);
        Assert.Equal("CAT B", b.Text[Carousel + "RightTwo/textField_tf"]);
        Assert.Equal("FAVOURITES", b.Text[Carousel + "LeftOne/textField_tf"]);
        Assert.Equal("CAT C", b.Text[Carousel + "LeftTwo/textField_tf"]);
        view.SelectTab(2);
        b = PipboyInvArt.Shell(view, null);
        Assert.Equal("CAT B", b.Text[Carousel + "Selected/textField_tf"]);
        Assert.Equal("CAT C", b.Text[Carousel + "RightOne/textField_tf"]);
        Assert.Equal("CAT A", b.Text[Carousel + "LeftOne/textField_tf"]);
    }

    [Fact]
    public void ShellHidesCarouselSlotsThatWouldRepeatATab()
    {
        var view = ViewOf();
        Assert.Equal(2, view.Tabs.Count);
        var b = PipboyInvArt.Shell(view, null);
        Assert.Equal("ALL", b.Text[Carousel + "Selected/textField_tf"]);
        Assert.Equal("FAVOURITES", b.Text[Carousel + "RightOne/textField_tf"]);
        foreach (var slot in new[] { "LeftOne", "RightTwo", "LeftTwo" })
        {
            Assert.Contains(Carousel + slot, b.Hide);
            Assert.False(b.Text.ContainsKey(Carousel + slot + "/textField_tf"));
        }
    }

    [Fact]
    public void PageListsTheVisibleItemsMarksTheCursorAndAppendsTheSortDetail()
    {
        var view = ViewOf(Item("a", "Cat A", weight: 2.5, count: 3), Item("b", "Cat A", equipped: true), Item("c", "Cat B"));
        view.Select(1);
        var b = PipboyInvArt.Page(view);
        var rows = b.Lists["List_mc"];
        Assert.Equal(new[] { "Item a (3)  2.5 wt", "Item b (equipped)  1 wt", "Item c  1 wt" }, rows.Select(r => r.Text));
        Assert.Equal(new[] { false, true, false }, rows.Select(r => r.Selected));
        view.Inventory.SetSort(InventorySort.Value);
        Assert.Equal("Item a (3)  10", PipboyInvArt.Page(view).Lists["List_mc"][0].Text);
    }

    [Fact]
    public void PageFollowsTheCategoryTabAndTheFavouritesFilter()
    {
        var view = ViewOf(Item("a", "Cat A"), Item("b", "Cat B"), Item("c", "Cat B"));
        view.SelectTab(2);
        Assert.Equal(new[] { "Item b", "Item c" }, PipboyInvArt.Page(view).Lists["List_mc"].Select(r => r.Text.Split("  ")[0]));
        view.SelectTab(view.Tabs.Count - 1);
        Assert.Empty(PipboyInvArt.Page(view).Lists["List_mc"]);
        view.SelectTab(0);
        view.Select(2);
        Assert.Equal(new InventoryEvent(InventoryEventKind.AssignFavourite, "c", 1, 1), view.Inventory.ToggleFavouriteCurrent());
        view.SelectTab(view.Tabs.Count - 1);
        Assert.Equal(new[] { "Item c" }, PipboyInvArt.Page(view).Lists["List_mc"].Select(r => r.Text.Split("  ")[0]));
    }

    [Fact]
    public void PageHidesTheTemplateListsAndBorderAndTheScrollChevronsThatHaveNothingToShow()
    {
        var b = PipboyInvArt.Page(Numbered(3));
        Assert.Contains("ComponentList_mc", b.Hide);
        Assert.Contains("ComponentOwnersList_mc", b.Hide);
        Assert.Contains("List_mc/border", b.Hide);
        Assert.Contains("List_mc/ScrollUp", b.Hide);
        Assert.Contains("List_mc/ScrollDown", b.Hide);
        Assert.DoesNotContain("List_mc", b.Hide);
    }

    [Fact]
    public void PageWindowsLongListsToNineRowsAroundTheCursorAndShowsTheMatchingChevrons()
    {
        var view = Numbered(20);
        var b = PipboyInvArt.Page(view);
        Assert.Equal(SwfBindings.ListRows, b.Lists["List_mc"].Count);
        Assert.Equal("Item i00", b.Lists["List_mc"][0].Text.Split("  ")[0]);
        Assert.Contains("List_mc/ScrollUp", b.Hide);
        Assert.DoesNotContain("List_mc/ScrollDown", b.Hide);
        view.Select(12);
        b = PipboyInvArt.Page(view);
        var rows = b.Lists["List_mc"];
        Assert.Equal(SwfBindings.ListRows, rows.Count);
        Assert.Equal("Item i12", rows.Single(r => r.Selected).Text.Split("  ")[0]);
        Assert.Equal("Item i04", rows[0].Text.Split("  ")[0]);
        Assert.DoesNotContain("List_mc/ScrollUp", b.Hide);
        Assert.DoesNotContain("List_mc/ScrollDown", b.Hide);
        view.Select(19);
        b = PipboyInvArt.Page(view);
        Assert.DoesNotContain("List_mc/ScrollUp", b.Hide);
        Assert.Contains("List_mc/ScrollDown", b.Hide);
    }

    [Fact]
    public void PageOfAnEmptyInventoryHasNoRowsAndNoChevrons()
    {
        var b = PipboyInvArt.Page(ViewOf());
        Assert.Empty(b.Lists["List_mc"]);
        Assert.Contains("List_mc/ScrollUp", b.Hide);
        Assert.Contains("List_mc/ScrollDown", b.Hide);
    }
}
