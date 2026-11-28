using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyQuantityTests
{
    static InventoryModel Model(int count, bool equipped = false) =>
        new(new[] { new InventoryItem("junk", "Scrap Metal", "Misc", 1, 2, Count: count, Equipped: equipped) });

    static (InventoryListView View, List<InventoryEvent> Events) View(int count)
    {
        var view = new InventoryListView(Model(count));
        var events = new List<InventoryEvent>();
        view.Command += events.Add;
        return (view, events);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void A_stack_of_five_or_fewer_drops_one_unit_at_once_without_a_selector(int count)
    {
        var model = Model(count);

        Assert.Equal(new InventoryEvent(InventoryEventKind.Drop, "junk", 1), model.BeginDrop());
        Assert.Null(model.Quantity);
    }

    [Fact]
    public void A_stack_above_five_opens_the_selector_at_the_maximum()
    {
        var model = Model(6);

        Assert.Null(model.BeginDrop());

        Assert.NotNull(model.Quantity);
        Assert.Equal(6, model.Quantity!.Max);
        Assert.Equal(6, model.Quantity.Value);
        Assert.Equal(model.Revision, model.Quantity.Revision);
    }

    [Theory]
    [InlineData(6, 1, 1)]
    [InlineData(19, 1, 4)]
    [InlineData(20, 1, 5)]
    [InlineData(40, 2, 10)]
    [InlineData(100, 5, 25)]
    public void Step_sizes_are_one_then_a_twentieth_then_a_quarter_of_the_stack_never_below_one(int stack, int medium, int coarse)
    {
        var selector = new QuantitySelector("junk", stack, 0);

        Assert.Equal(1, selector.StepSize(QuantityStep.Fine));
        Assert.Equal(medium, selector.StepSize(QuantityStep.Medium));
        Assert.Equal(coarse, selector.StepSize(QuantityStep.Coarse));
    }

    [Fact]
    public void Stepping_clamps_between_one_and_the_stack_and_reports_whether_it_moved()
    {
        var selector = new QuantitySelector("junk", 40, 0);

        Assert.False(selector.Step(1, QuantityStep.Fine));
        Assert.True(selector.Step(-1, QuantityStep.Coarse));
        Assert.Equal(30, selector.Value);
        Assert.True(selector.Step(-1, QuantityStep.Medium));
        Assert.Equal(28, selector.Value);
        Assert.True(selector.Step(1, QuantityStep.Coarse));
        Assert.Equal(38, selector.Value);
        Assert.True(selector.Step(1, QuantityStep.Coarse));
        Assert.Equal(40, selector.Value);
        for (var i = 0; i < 10; i++) selector.Step(-1, QuantityStep.Coarse);
        Assert.Equal(1, selector.Value);
        Assert.False(selector.Step(-1, QuantityStep.Fine));
    }

    [Fact]
    public void Confirming_requests_the_chosen_count_and_closes_the_selector()
    {
        var model = Model(40);
        model.BeginDrop();
        model.StepQuantity(-1, QuantityStep.Coarse);

        var drop = model.ConfirmDrop();

        Assert.Equal(new InventoryEvent(InventoryEventKind.Drop, "junk", 30), drop);
        Assert.Null(model.Quantity);
        Assert.Null(model.Message);
    }

    [Fact]
    public void Cancelling_leaves_the_inventory_and_revision_untouched()
    {
        var model = Model(40);
        var revision = model.Revision;
        model.BeginDrop();

        Assert.True(model.CancelDrop());

        Assert.Null(model.Quantity);
        Assert.Equal(revision, model.Revision);
        Assert.Equal(40, model.Items.Single().Count);
        Assert.False(model.CancelDrop());
    }

    [Fact]
    public void A_changed_inventory_blocks_the_drop_and_reports_it()
    {
        var model = Model(40);
        model.BeginDrop();
        model.SetCount("junk", 39);

        Assert.Null(model.ConfirmDrop());

        Assert.Equal(InventoryModel.ChangedMessage, model.Message);
        Assert.Null(model.Quantity);
        Assert.Equal(39, model.Items.Single().Count);
    }

    [Fact]
    public void The_selector_closes_when_its_item_disappears()
    {
        var model = Model(40);
        model.BeginDrop();

        model.SetCount("junk", 0);

        Assert.Null(model.Quantity);
        Assert.Null(model.ConfirmDrop());
    }

    [Fact]
    public void Every_inventory_mutation_advances_the_revision()
    {
        var model = Model(10);
        var start = model.Revision;

        model.SetCount("junk", 9);
        model.SetEquipped("junk", false);

        Assert.Equal(start + 2, model.Revision);
    }

    [Fact]
    public void Equipped_items_do_not_begin_a_drop()
    {
        var model = Model(40, equipped: true);

        Assert.Null(model.BeginDrop());
        Assert.Null(model.Quantity);
    }

    [Fact]
    public void The_view_routes_step_keys_to_the_selector_and_confirms_with_accept()
    {
        var (view, events) = View(40);
        Assert.True(view.BeginDrop());

        view.Handle(MenuInputAction.Left);
        Assert.Equal(39, view.Quantity!.Value);
        view.Handle(MenuInputAction.Down);
        Assert.Equal(37, view.Quantity.Value);
        view.Handle(MenuInputAction.PageDown);
        Assert.Equal(27, view.Quantity.Value);
        view.Handle(MenuInputAction.PageUp);
        Assert.Equal(37, view.Quantity.Value);
        view.Handle(MenuInputAction.TabNext);
        Assert.Equal(39, view.Quantity.Value);
        view.Handle(MenuInputAction.Right);
        Assert.Equal(40, view.Quantity.Value);
        view.Handle(MenuInputAction.TabPrev);
        Assert.Equal(38, view.Quantity.Value);
        view.Handle(MenuInputAction.Up);
        Assert.Equal(40, view.Quantity.Value);
        view.Handle(MenuInputAction.Left);

        Assert.True(view.Handle(MenuInputAction.Accept));

        Assert.Equal(new[] { new InventoryEvent(InventoryEventKind.Drop, "junk", 39) }, events);
        Assert.Null(view.Quantity);
    }

    [Fact]
    public void While_open_the_view_swallows_other_actions_and_offers_only_accept_and_cancel()
    {
        var (view, events) = View(40);
        view.BeginDrop();

        Assert.True(view.Handle(MenuInputAction.Sort));
        Assert.True(view.Handle(MenuInputAction.Favourite));
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Accept"), new MenuHint(MenuInputAction.Back, "Cancel") }, view.Hints);
        Assert.Empty(events);

        Assert.True(view.Handle(MenuInputAction.Back));
        Assert.Null(view.Quantity);
        Assert.Contains(new MenuHint(MenuInputAction.Back, "Back"), view.Hints);
    }

    [Fact]
    public void The_view_reports_a_stale_confirm_through_its_message()
    {
        var model = Model(40);
        var view = new InventoryListView(model);
        var events = new List<InventoryEvent>();
        view.Command += events.Add;
        view.BeginDrop();
        model.SetCount("junk", 38);

        Assert.False(view.ConfirmDrop());

        Assert.Empty(events);
        Assert.Equal(InventoryModel.ChangedMessage, view.Message);
    }

    [Fact]
    public void A_small_stack_drops_one_through_the_view()
    {
        var (view, events) = View(3);

        Assert.True(view.BeginDrop());

        Assert.Equal(new[] { new InventoryEvent(InventoryEventKind.Drop, "junk", 1) }, events);
        Assert.Null(view.Quantity);
    }
}
