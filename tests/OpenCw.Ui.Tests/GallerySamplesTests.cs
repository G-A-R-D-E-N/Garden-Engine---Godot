using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Gallery;
using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public class GallerySamplesTests
{
    static readonly Regex Neutral = new(@"^[A-Z][a-z]+( [A-Z]| \d+)$");

    static readonly GenericCharacterData Data = new();

    [Fact]
    public void Inventory_has_unique_ids_and_builds_a_model_with_a_capacity()
    {
        var items = GallerySamples.Inventory();
        var model = GallerySamples.NewInventory();

        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
        Assert.Equal(items.Count, model.Items.Count);
        Assert.Equal(GallerySamples.SampleWeightCapacity, model.Capacity);
    }

    [Fact]
    public void Inventory_has_an_equipped_item_with_a_comparable_alternative_in_its_slot()
    {
        var items = GallerySamples.Inventory();
        var equipped = items.Single(i => i.Equipped);

        Assert.Contains(items, i => !i.Equipped && i.EquipKey == equipped.EquipKey && i.StatList.Count > 0);
    }

    [Fact]
    public void Quests_mix_active_and_completed_and_notes_have_unique_ids()
    {
        var quests = GallerySamples.Quests();
        var notes = GallerySamples.Notes();

        Assert.Equal(quests.Count, quests.Select(q => q.Id).Distinct().Count());
        Assert.Contains(quests, q => q.Completed);
        Assert.Contains(quests, q => !q.Completed && q.Objectives.Any(o => !o.Completed && !o.Optional));
        Assert.Equal(notes.Count, notes.Select(n => n.Id).Distinct().Count());
        Assert.Equal(new[] { "quest_a", "quest_b", "quest_d" }, GallerySamples.NewQuestLog().Quests.Visible.Select(q => q.Id).ToArray());
    }

    [Fact]
    public void Map_markers_lie_inside_the_bounds_and_include_one_undiscovered_marker()
    {
        var markers = GallerySamples.Markers();
        var bounds = GallerySamples.WorldBounds;

        Assert.Equal(markers.Count, markers.Select(m => m.Id).Distinct().Count());
        Assert.All(markers, m =>
        {
            Assert.InRange(m.X, bounds.MinX, bounds.MaxX);
            Assert.InRange(m.Y, bounds.MinY, bounds.MaxY);
        });
        Assert.Single(markers, m => !m.Discovered);
        Assert.Contains(markers, m => m.CanFastTravel);
        Assert.Equal(markers.Count - 1, GallerySamples.NewMap().VisibleMarkers.Count);
    }

    [Fact]
    public void Radio_builds_a_model_with_every_station()
    {
        Assert.Equal(GallerySamples.Stations().Count, GallerySamples.NewRadio().Stations.Count);
    }

    [Fact]
    public void Sample_names_are_neutral_placeholders()
    {
        var names = GallerySamples.Inventory().Select(i => i.Name)
            .Concat(GallerySamples.Quests().Select(q => q.Name))
            .Concat(GallerySamples.Quests().SelectMany(q => q.Objectives).Select(o => o.Text))
            .Concat(GallerySamples.Notes().Select(n => n.Title))
            .Concat(GallerySamples.Markers().Select(m => m.Name))
            .Concat(GallerySamples.Stations().Select(s => s.Name))
            .Concat(GallerySamples.Subtitles.Select(s => s.Speaker))
            .ToArray();

        Assert.All(names, name => Assert.Matches(Neutral, name));
    }

    [Fact]
    public void Loading_tips_subtitles_and_toasts_cover_what_the_demos_need()
    {
        Assert.Equal(3, GallerySamples.LoadingTips.Count);
        Assert.Equal(new[] { "Speaker A", "Speaker B" }, GallerySamples.Subtitles.Select(s => s.Speaker).Distinct());
        Assert.Equal(Enum.GetValues<OpenCommonwealth.Services.Ui.Hud.ToastKind>().Length, GallerySamples.Toasts.Select(t => t.Kind).Distinct().Count());
    }

    [Fact]
    public void Sample_saves_list_every_kind_for_two_characters()
    {
        var saves = new SampleSaves().List();

        Assert.Equal(saves.Count, saves.Select(s => s.Id).Distinct().Count());
        Assert.Equal(new[] { SaveKind.Manual, SaveKind.Quick, SaveKind.Auto }.OrderBy(k => k), saves.Select(s => s.Kind).Distinct().OrderBy(k => k));
        Assert.Equal(2, saves.Select(s => s.Character).Distinct().Count());
    }

    [Fact]
    public void Deleting_a_sample_save_removes_it_once_and_instances_do_not_share_state()
    {
        var saves = new SampleSaves();
        var id = saves.List()[0].Id;

        Assert.True(saves.Delete(id));
        Assert.False(saves.Delete(id));
        Assert.DoesNotContain(saves.List(), s => s.Id == id);
        Assert.Contains(new SampleSaves().List(), s => s.Id == id);
    }

    [Fact]
    public void Character_factories_build_fresh_models_from_the_generic_provider()
    {
        var allocation = GalleryCharacter.Allocation(Data);
        var sheet = GalleryCharacter.Sheet(Data);
        var levelUp = GalleryCharacter.LevelUp(Data);

        Assert.Equal(Data.StatPoints, allocation.Pool);
        Assert.Equal(Data.StatPoints, allocation.Remaining);
        Assert.Equal(Data.DerivedStats.Count, sheet.Rows.Count);
        Assert.Equal(new[] { "p1", "p3" }, levelUp.Perks.Visible.Select(e => e.Def.Id));
        Assert.NotSame(allocation, GalleryCharacter.Allocation(Data));
    }
}
