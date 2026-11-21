using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class InventoryActionBindingTests
{
    static (InventoryListView View, List<InventoryEvent> Events) Make()
    {
        var view = new InventoryListView(new InventoryModel(new[]
        {
            new InventoryItem("tonic", "Tonic", "Aid", 0.5, 40, Count: 5, Recent: 3, Usable: true),
            new InventoryItem("junk", "Scrap Metal", "Misc", 1, 2, Count: 10, Recent: 1),
        }, 8, null, 0));
        var events = new List<InventoryEvent>();
        view.Command += events.Add;
        return (view, events);
    }

    static void Pick(InventoryListView view, string id)
    {
        Assert.True(view.Inventory.Select(id));
        view.Refresh();
    }

    [Fact]
    public void An_action_offered_for_one_item_does_not_run_after_the_selection_moved_to_another()
    {
        var (view, events) = Make();
        Pick(view, "junk");
        var dropAll = view.CurrentActions().Single(action => action.Label == "Drop all (10)");
        Assert.Equal("junk", dropAll.ItemId);

        Pick(view, "tonic");

        Assert.False(view.Run(dropAll));
        Assert.Empty(events);
    }

    [Fact]
    public void An_action_still_runs_for_the_item_it_was_offered_for()
    {
        var (view, events) = Make();
        Pick(view, "junk");
        var dropOne = view.CurrentActions().Single(action => action.Label == "Drop one");

        Assert.True(view.Run(dropOne));

        var drop = Assert.Single(events);
        Assert.Equal(("junk", 1), (drop.ItemId, drop.Count));
    }

    [Fact]
    public void Every_offered_action_names_the_item_it_was_created_for()
    {
        var (view, _) = Make();
        Pick(view, "tonic");

        Assert.All(view.CurrentActions(), action => Assert.Equal("tonic", action.ItemId));
    }
}
