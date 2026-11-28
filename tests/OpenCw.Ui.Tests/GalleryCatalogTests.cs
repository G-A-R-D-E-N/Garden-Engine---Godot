using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Gallery;
using Xunit;

namespace OpenCw.Ui.Tests;

public class GalleryCatalogTests
{
    [Fact]
    public void Every_gallery_id_is_listed_exactly_once()
    {
        var listed = GalleryCatalog.Entries.Select(e => e.Id).OrderBy(id => id).ToArray();

        Assert.Equal(Enum.GetValues<GalleryId>().OrderBy(id => id).ToArray(), listed);
    }

    [Fact]
    public void Entries_have_distinct_non_empty_titles_categories_and_descriptions()
    {
        Assert.Equal(GalleryCatalog.Entries.Count, GalleryCatalog.Entries.Select(e => e.Title).Distinct().Count());
        Assert.All(GalleryCatalog.Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Title));
            Assert.False(string.IsNullOrWhiteSpace(e.Category));
            Assert.False(string.IsNullOrWhiteSpace(e.Description));
        });
    }

    [Fact]
    public void The_pip_boy_comes_first_and_the_load_menu_last()
    {
        Assert.Equal(GalleryId.PipBoy, GalleryCatalog.Entries[0].Id);
        Assert.Equal(GalleryId.LoadGame, GalleryCatalog.Entries[^1].Id);
    }

    [Fact]
    public void The_pip_boy_game_art_entry_follows_the_pip_boy_in_its_block()
    {
        Assert.Equal(GalleryId.PipBoyArt, GalleryCatalog.Entries[1].Id);
        Assert.Equal("Pip-Boy (game art)", GalleryCatalog.Entry(GalleryId.PipBoyArt).Title);
        Assert.Equal(GalleryCatalog.PipBoyCategory, GalleryCatalog.Entry(GalleryId.PipBoyArt).Category);
        Assert.False(GalleryCatalog.ClosesMenusFirst(GalleryId.PipBoyArt));
    }

    [Fact]
    public void Categories_group_entries_in_six_contiguous_blocks()
    {
        var categories = GalleryCatalog.Entries.Select(e => e.Category).ToArray();
        var blocks = categories.Where((category, i) => i == 0 || categories[i - 1] != category).ToArray();

        Assert.Equal(
            new[]
            {
                GalleryCatalog.PipBoyCategory, GalleryCatalog.CharacterCategory, GalleryCatalog.DialogCategory,
                GalleryCatalog.HudCategory, GalleryCatalog.WorkshopCategory, GalleryCatalog.MenuCategory,
            },
            blocks);
    }

    [Fact]
    public void Entry_finds_by_id_and_refuses_an_unknown_id()
    {
        Assert.Equal("Settings", GalleryCatalog.Entry(GalleryId.Settings).Title);
        Assert.Throws<ArgumentOutOfRangeException>(() => GalleryCatalog.Entry((GalleryId)999));
    }

    [Fact]
    public void Only_the_hud_demos_close_the_menus_first()
    {
        var closing = Enum.GetValues<GalleryId>().Where(GalleryCatalog.ClosesMenusFirst).ToArray();

        Assert.Equal(
            new[]
            {
                GalleryId.Toast, GalleryId.Loading, GalleryId.Fade, GalleryId.Subtitles, GalleryId.HudInstruments,
                GalleryId.PowerArmorHud, GalleryId.ScopeOverlay, GalleryId.ActionHints, GalleryId.ContextPrompt,
                GalleryId.HudArt,
            },
            closing);
    }

    [Fact]
    public void The_progression_and_favourites_samples_sit_in_the_character_block_before_the_dialogs()
    {
        var character = GalleryCatalog.Entries.Where(e => e.Category == GalleryCatalog.CharacterCategory).Select(e => e.Id).ToArray();

        Assert.Equal(
            new[]
            {
                GalleryId.StatAllocation, GalleryId.LevelUp, GalleryId.Appearance, GalleryId.PerkChart,
                GalleryId.LevelUpSession, GalleryId.FavouritesAssign, GalleryId.FavouritesQuick,
            },
            character);
        Assert.True(GalleryCatalog.Entries.Select(e => e.Id).ToList().IndexOf(GalleryId.FavouritesQuick)
            < GalleryCatalog.Entries.Select(e => e.Id).ToList().IndexOf(GalleryId.Wait));
    }
}
