using OpenCommonwealth.Services.Interaction;
using Xunit;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCw.Ui.Tests;

public sealed class ActivationDispatcherTests
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static ActivationTarget Target(string baseType, int count = 0, string? owner = null, bool noCrime = false, string key = "000800:A.esp") =>
        new(key, "000801:A.esp", baseType, None, count, owner, noCrime);

    private static (ItemPickup Pickup, PlayerItems Store, List<string> Removed, List<ItemTaken> Taken) Pickup(string? player = null)
    {
        var store = new PlayerItems();
        var removed = new List<string>();
        var taken = new List<ItemTaken>();
        var pickup = new ItemPickup(store, ItemPickup.SameOwner(player), target => removed.Add(target.RefKey));
        pickup.Taken += taken.Add;
        return (pickup, store, removed, taken);
    }

    [Fact]
    public void A_stacked_loose_item_adds_its_whole_count_and_removes_the_reference()
    {
        var (pickup, store, removed, taken) = Pickup();
        var dispatcher = new ActivationDispatcher();
        dispatcher.Register(pickup);

        var result = dispatcher.Dispatch(Target("Ammunition", count: 30));

        Assert.Equal(ActivationStatus.Handled, result.Status);
        Assert.Equal(30, store.CountOf("000801:A.esp"));
        Assert.Equal(new[] { "000800:A.esp" }, removed);
        Assert.Equal(new ItemTaken("000800:A.esp", "000801:A.esp", 30, false, "Ammunition"), Assert.Single(taken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_missing_or_non_positive_count_is_one_item(int count)
    {
        var (pickup, store, _, _) = Pickup();
        pickup.Activate(Target("MiscItem", count));
        Assert.Equal(1, store.CountOf("000801:A.esp"));
    }

    [Fact]
    public void Taking_twice_accumulates()
    {
        var (pickup, store, _, _) = Pickup();
        pickup.Activate(Target("Weapon", 2, key: "r1"));
        pickup.Activate(Target("Weapon", 3, key: "r2"));
        Assert.Equal(5, store.CountOf("000801:A.esp"));
    }

    [Theory]
    [InlineData("Fallout4.esm:0001", false, "Fallout4.esm:0002", true)]
    [InlineData("Fallout4.esm:0001", true, "Fallout4.esm:0002", false)]
    [InlineData("Fallout4.esm:0002", false, "Fallout4.esm:0002", false)]
    [InlineData(null, false, "Fallout4.esm:0002", false)]
    [InlineData("", false, "Fallout4.esm:0002", false)]
    public void Ownership_marks_a_pickup_stolen_unless_no_crime_or_player_owned(string? owner, bool noCrime, string player, bool stolen)
    {
        var (pickup, store, _, taken) = Pickup(player);
        pickup.Activate(Target("Armor", owner: owner, noCrime: noCrime));
        Assert.Equal(stolen, Assert.Single(taken).Stolen);
        Assert.Equal(1, store.CountOf("000801:A.esp"));
    }

    [Theory]
    [InlineData("Container")]
    [InlineData("Door")]
    [InlineData("Furniture")]
    [InlineData("Flora")]
    [InlineData("MovableStatic")]
    [InlineData("")]
    public void Pickup_leaves_non_items_to_the_other_lanes(string baseType)
    {
        var (pickup, store, removed, taken) = Pickup();
        var dispatcher = new ActivationDispatcher();
        dispatcher.Register(pickup);

        Assert.Equal(ActivationStatus.Unhandled, dispatcher.Dispatch(Target(baseType)).Status);
        Assert.Empty(store.Entries);
        Assert.Empty(removed);
        Assert.Empty(taken);
    }

    [Fact]
    public void The_script_event_fires_before_the_default_and_the_default_still_runs_when_unblocked()
    {
        var order = new List<string>();
        var dispatcher = new ActivationDispatcher(t => { order.Add("OnActivate " + t.RefKey); return true; });
        dispatcher.Register(new DelegateActivationHandler(_ => true, t => { order.Add("default"); return new(ActivationStatus.Handled); }));

        Assert.Equal(ActivationStatus.Handled, dispatcher.Dispatch(Target("Container")).Status);
        Assert.Equal(new[] { "OnActivate 000800:A.esp", "default" }, order);
        Assert.Equal(1, dispatcher.ScriptEventsDelivered);
    }

    [Fact]
    public void A_blocked_reference_still_gets_the_script_event_but_no_default()
    {
        var delivered = 0;
        var defaults = 0;
        var dispatcher = new ActivationDispatcher(_ => { delivered++; return true; });
        dispatcher.Register(new DelegateActivationHandler(_ => true, _ => { defaults++; return new(ActivationStatus.Handled); }));
        dispatcher.Block("000800:A.esp", true);

        Assert.Equal(ActivationStatus.Blocked, dispatcher.Dispatch(Target("Door")).Status);
        Assert.Equal((1, 0), (delivered, defaults));

        dispatcher.Block("000800:A.esp", false);
        Assert.Equal(ActivationStatus.Handled, dispatcher.Dispatch(Target("Door")).Status);
        Assert.Equal((2, 1), (delivered, defaults));
    }

    [Fact]
    public void A_blocked_loose_item_is_not_picked_up()
    {
        var (pickup, store, removed, _) = Pickup();
        var dispatcher = new ActivationDispatcher();
        dispatcher.Register(pickup);
        dispatcher.Block("000800:A.esp", true);

        Assert.Equal(ActivationStatus.Blocked, dispatcher.Dispatch(Target("Key")).Status);
        Assert.Empty(store.Entries);
        Assert.Empty(removed);
    }

    [Fact]
    public void The_first_matching_handler_wins_and_no_match_is_unhandled()
    {
        var dispatcher = new ActivationDispatcher();
        var hits = new List<string>();
        dispatcher.Register(new DelegateActivationHandler(t => t.BaseType == "Door", _ => { hits.Add("door"); return new(ActivationStatus.Handled); }));
        dispatcher.Register(new DelegateActivationHandler(_ => true, _ => { hits.Add("any"); return new(ActivationStatus.Handled); }));

        dispatcher.Dispatch(Target("Door"));
        dispatcher.Dispatch(Target("Furniture"));
        Assert.Equal(new[] { "door", "any" }, hits);
        Assert.Equal(ActivationStatus.Unhandled, new ActivationDispatcher().Dispatch(Target("Door")).Status);
    }

    [Fact]
    public void A_script_event_that_is_not_delivered_is_not_counted()
    {
        var dispatcher = new ActivationDispatcher(_ => false);
        dispatcher.Dispatch(Target("Door"));
        Assert.Equal(0, dispatcher.ScriptEventsDelivered);
    }
}

public sealed class ActivationHandlersTests
{
    private static readonly string[] Documented =
        ["pickup", "powerArmor", "container", "door", "terminal", "furniture", "activator", "actorLoot", "pickpocket", "catchAll"];

    private static ActivationTarget Door() => new("00A1", "00B2", "Door", new HashSet<string>());

    private static ActivationHandlers Recording(List<string> asked, params string[] accepting)
    {
        IActivationHandler Named(string name) => new DelegateActivationHandler(
            _ => { asked.Add(name); return accepting.Contains(name); }, _ => { asked.Add("ran " + name); return new(ActivationStatus.Handled, name); });
        return new(Named("pickup"), Named("powerArmor"), Named("container"), Named("door"), Named("terminal"),
            Named("furniture"), Named("activator"), Named("actorLoot"), Named("pickpocket"), Named("catchAll"));
    }

    [Fact]
    public void Handlers_are_asked_in_the_documented_order()
    {
        var asked = new List<string>();
        var result = ActivationDispatcher.Build(null, null, Recording(asked)).Dispatch(Door());

        Assert.Equal(Documented, asked);
        Assert.Equal(ActivationStatus.Unhandled, result.Status);
    }

    [Fact]
    public void The_first_accepting_handler_in_order_wins_and_later_ones_are_not_asked()
    {
        var asked = new List<string>();
        var result = ActivationDispatcher.Build(null, null, Recording(asked, "door", "catchAll")).Dispatch(Door());

        Assert.Equal(new[] { "pickup", "powerArmor", "container", "door", "ran door" }, asked);
        Assert.Equal("door", result.Detail);
    }

    [Fact]
    public void The_script_event_is_consulted_before_any_handler()
    {
        var asked = new List<string>();
        var dispatcher = ActivationDispatcher.Build(_ => { asked.Add("script"); return true; }, null, Recording(asked, "pickup"));

        dispatcher.Dispatch(Door());

        Assert.Equal(new[] { "script", "pickup", "ran pickup" }, asked);
        Assert.Equal(1, dispatcher.ScriptEventsDelivered);
    }

    [Fact]
    public void A_blocked_reference_skips_every_handler_but_still_gets_the_script_event()
    {
        var asked = new List<string>();
        var dispatcher = ActivationDispatcher.Build(_ => { asked.Add("script"); return true; }, key => key == "00A1", Recording(asked, "catchAll"));

        var result = dispatcher.Dispatch(Door());

        Assert.Equal(ActivationStatus.Blocked, result.Status);
        Assert.Equal(new[] { "script" }, asked);
    }
}

public sealed class DoorVisualTests
{
    [Theory]
    [InlineData(false, true, true, false, true)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, false, false, true, true)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, false, false, true, false)]
    public void An_in_place_door_hides_when_opened_and_returns_only_when_this_hid_it(bool loadDoor, bool open, bool visible, bool hiddenByDoor, bool toggles) =>
        Assert.Equal(toggles, DoorVisual.ShouldToggle(loadDoor, open, visible, hiddenByDoor));
}

public sealed class ActivationBlocksTests
{
    private static ActivationTarget Door() => new("00A1", "00B2", "Door", new HashSet<string>());

    [Fact]
    public void ScriptBlockSkipsDefaultHandlerButStillDeliversTheEvent()
    {
        var blocks = new ActivationBlocks();
        var ran = false;
        var dispatcher = new ActivationDispatcher(_ => true, blocks.IsBlocked);
        dispatcher.Register(new DelegateActivationHandler(_ => true, _ => { ran = true; return new(ActivationStatus.Handled); }));

        blocks.Set("00A1", true, false);
        var blocked = dispatcher.Dispatch(Door());
        blocks.Set("00A1", false, false);
        var open = dispatcher.Dispatch(Door());

        Assert.Equal(ActivationStatus.Blocked, blocked.Status);
        Assert.Equal(ActivationStatus.Handled, open.Status);
        Assert.Equal(2, dispatcher.ScriptEventsDelivered);
        Assert.True(ran);
    }

    [Fact]
    public void HideTextOnlyAppliesWhileBlocked()
    {
        var blocks = new ActivationBlocks();
        blocks.Set("00A1", true, true);
        Assert.True(blocks.HidesText("00A1"));
        Assert.False(blocks.HidesText("00A2"));
        blocks.Set("00A1", false, true);
        Assert.False(blocks.IsBlocked("00A1"));
        Assert.False(blocks.HidesText("00A1"));
    }
}
