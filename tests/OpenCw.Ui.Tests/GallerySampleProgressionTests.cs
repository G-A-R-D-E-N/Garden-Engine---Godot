using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Gallery;
using Xunit;

namespace OpenCw.Ui.Tests;

public class GallerySampleProgressionTests
{
    static PerkChartEntry Entry(SampleProgression service, string id) => service.Snapshot.Chart.Entries.First(e => e.Id == id);

    [Fact]
    public void The_sample_snapshot_is_valid_and_opens_a_level_up_session()
    {
        var service = new SampleProgression();

        Assert.True(service.Snapshot.IsValid);
        Assert.Equal(2, service.Snapshot.PerkPoints);
        Assert.Equal(PerkChartMode.Spend, LevelUpSession.TryOpen(service, false, false)?.Chart.Mode);
    }

    [Fact]
    public void A_perk_that_needs_another_perk_is_locked_until_that_perk_is_taken()
    {
        var service = new SampleProgression();

        Assert.Equal(ChooseResult.Locked, service.TryChoose("perk_f", 1));
        Assert.False(Entry(service, "perk_f").Available);
        Assert.Contains(Entry(service, "perk_f").Ranks[0].UnmetOrEmpty, u => u.Kind == UnmetKind.Perk && u.Name == "Perk C");

        Assert.Equal(ChooseResult.Ok, service.TryChoose("perk_c", 1));
        Assert.True(Entry(service, "perk_f").Available);
        Assert.Equal(ChooseResult.Ok, service.TryChoose("perk_f", 1));
        Assert.Equal(0, service.Snapshot.PerkPoints);
        Assert.Equal(ChooseResult.NoPoints, service.TryChoose("perk_a", 1));
    }

    [Fact]
    public void A_choice_costs_its_rank_price_bumps_the_revision_and_raises_changed()
    {
        var service = new SampleProgression(6, 1);
        var changes = 0;
        service.Changed += () => changes++;

        Assert.Equal(ChooseResult.NoPoints, service.TryChoose("perk_b", 1));
        Assert.Equal(0, changes);
        Assert.Equal(ChooseResult.Ok, service.TryChoose("attr0", 6));
        Assert.Equal(6, service.Snapshot.Chart.Attributes[0]);
        Assert.Equal(0, service.Snapshot.PerkPoints);
        Assert.Equal(2, service.Snapshot.Revision);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Unknown_ids_skipped_ranks_and_taken_ranks_are_refused()
    {
        var service = new SampleProgression();

        Assert.Equal(ChooseResult.Unknown, service.TryChoose("nope", 1));
        Assert.Equal(ChooseResult.Unknown, service.TryChoose("perk_a", 2));
        Assert.Equal(ChooseResult.Ok, service.TryChoose("perk_d", 1));
        Assert.Equal(ChooseResult.AlreadyMax, service.TryChoose("perk_d", 1));
    }

    [Fact]
    public void Gaining_a_level_adds_a_point_and_raises_changed()
    {
        var service = new SampleProgression(6, 0);
        var changes = 0;
        service.Changed += () => changes++;

        service.GainLevel();

        Assert.Equal(7, service.Snapshot.Level);
        Assert.Equal(1, service.Snapshot.PerkPoints);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void The_sample_favourite_slots_open_the_cross_in_both_modes_and_include_an_unavailable_item()
    {
        var slots = GalleryCharacter.FavouriteSlots();

        Assert.Equal(FavouritesCrossModel.SlotCount, slots.Count);
        Assert.Equal(CrossMode.Assign, FavouritesCrossModel.TryOpen(CrossMode.Assign, slots, false, false)?.Mode);
        Assert.Equal(CrossMode.Quick, FavouritesCrossModel.TryOpen(CrossMode.Quick, slots, false, false)?.Mode);
        Assert.Contains(slots, s => s is { Available: false });
    }
}
