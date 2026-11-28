using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Gallery;
using OpenCommonwealth.Services.Ui.MainMenu;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ExtrasSamplesTests
{
    static readonly DateTimeOffset Now = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_new_gallery_entries_sit_in_their_categories()
    {
        Assert.Equal(GalleryCatalog.WorkshopCategory, GalleryCatalog.Entry(GalleryId.Container).Category);
        Assert.Equal(GalleryCatalog.WorkshopCategory, GalleryCatalog.Entry(GalleryId.SettlerCommand).Category);
        Assert.Equal(GalleryCatalog.MenuCategory, GalleryCatalog.Entry(GalleryId.PauseMenu).Category);
        Assert.Equal(GalleryCatalog.MenuCategory, GalleryCatalog.Entry(GalleryId.SaveGame).Category);
        Assert.False(GalleryCatalog.ClosesMenusFirst(GalleryId.Container));
    }

    [Fact]
    public void The_store_names_new_saves_in_sequence()
    {
        var store = new InMemorySaveStore(null, () => Now);

        Assert.True(store.SaveNew().Ok);
        Assert.True(store.SaveNew().Ok);

        Assert.Equal(new[] { "save_1", "save_2" }, store.List().Select(slot => slot.Id).ToArray());
        Assert.Equal(2, store.WriteCount);
    }

    [Fact]
    public void Overwriting_a_save_that_is_gone_fails_without_counting_a_write()
    {
        var store = new InMemorySaveStore(null, () => Now);

        var result = store.Overwrite("nope");

        Assert.False(result.Ok);
        Assert.Equal("That save is gone.", result.Reason);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public void A_second_quicksave_replaces_the_first()
    {
        var store = new InMemorySaveStore(null, () => Now);

        store.Quicksave();
        store.Quicksave();

        var slot = Assert.Single(store.List());
        Assert.Equal(InMemorySaveStore.QuicksaveId, slot.Id);
        Assert.Equal(SaveKind.Quick, slot.Kind);
        Assert.Equal(2, store.WriteCount);
    }

    [Fact]
    public void A_queued_failure_applies_to_one_write_only()
    {
        var store = new InMemorySaveStore(null, () => Now) { FailNext = "Disk full." };

        var failed = store.SaveNew();
        var retried = store.SaveNew();

        Assert.Equal(new ActionResult(false, "Disk full."), failed);
        Assert.True(retried.Ok);
        Assert.Single(store.List());
    }

    [Fact]
    public void The_settable_query_reports_its_reason_only_when_blocked()
    {
        var gate = new SettableCanSave { Allowed = false, Reason = "In combat." };

        Assert.False(gate.CanSave(out var reason));
        Assert.Equal("In combat.", reason);

        gate.Allowed = true;

        Assert.True(gate.CanSave(out reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void The_sample_container_moves_stacks_and_counts_a_revision()
    {
        var rules = new SampleContainer();

        var result = rules.Transfer(ContainerSide.Container, "item_a", 3);

        Assert.Equal(TransferStatus.Done, result.Status);
        Assert.Equal(5, rules.ContainerStacks.Single(stack => stack.Id == "item_a").Count);
        Assert.Equal(3, rules.PlayerStacks.Single(stack => stack.Id == "item_a").Count);
        Assert.Equal(1, rules.Revision);
        Assert.Equal(TransferStatus.Refused, rules.Transfer(ContainerSide.Container, "item_a", 99).Status);
    }

    [Fact]
    public void Taking_everything_from_the_sample_container_leaves_only_the_heavy_item()
    {
        var ctrl = new ContainerController(new SampleContainer());

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Equal(new[] { "Item D" }, WorkshopTestHelpers.RowTexts(ctrl.List));
        Assert.Equal("That is too heavy to lift.", ctrl.Message);
    }

    [Fact]
    public void The_sample_pickpocket_catches_the_player_on_the_watched_item()
    {
        var ctrl = new ContainerController(new SampleContainer(ContainerMode.Pickpocket));

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Equal(ContainerOutcome.Caught, ctrl.Outcome);
        Assert.True(ctrl.IsFinished);
    }

    [Fact]
    public void The_sample_rename_applies_validates_and_refuses_unknown_items()
    {
        var rename = new SampleBenchRename();

        Assert.True(rename.CanRename("target_a"));
        Assert.False(rename.CanRename("other"));
        Assert.Equal("Item X", rename.CurrentName("target_a"));
        Assert.Equal("Names cannot hold control characters.", rename.Validate("bad\u0007name"));
        Assert.Null(rename.Validate("Good name"));

        Assert.True(rename.Rename("target_a", "Good name").Ok);
        Assert.Equal("Good name", rename.CurrentName("target_a"));
        Assert.False(rename.Rename("other", "x").Ok);
    }
}
