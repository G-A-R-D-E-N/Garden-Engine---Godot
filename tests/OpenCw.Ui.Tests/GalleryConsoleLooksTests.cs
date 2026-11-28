using System.Linq;
using OpenCommonwealth.Services.Ui.Gallery;
using Xunit;

namespace OpenCw.Ui.Tests;

public class GalleryConsoleLooksTests
{
    [Theory]
    [InlineData(GalleryId.Console, "Console")]
    [InlineData(GalleryId.RemoteAccess, "Remote access")]
    [InlineData(GalleryId.Looks, "Looks")]
    public void The_long_tail_menus_are_listed_in_the_menus_category(GalleryId id, string title)
    {
        var entry = GalleryCatalog.Entry(id);

        Assert.Equal(title, entry.Title);
        Assert.Equal(GalleryCatalog.MenuCategory, entry.Category);
        Assert.False(GalleryCatalog.ClosesMenusFirst(id));
        Assert.Single(GalleryCatalog.Entries, e => e.Id == id);
    }

    [Fact]
    public void They_form_one_run_after_the_main_menu()
    {
        var ids = GalleryCatalog.Entries.Select(e => e.Id).ToList();
        var main = ids.IndexOf(GalleryId.MainMenu);

        Assert.Equal(
            new[] { GalleryId.Console, GalleryId.RemoteAccess, GalleryId.Looks },
            ids.Skip(main + 1).Take(3));
    }
}
