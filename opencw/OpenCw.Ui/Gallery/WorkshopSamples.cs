using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Credits;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Travel;
using OpenCommonwealth.Services.Ui.Workbench;
using OpenCommonwealth.Services.Ui.Workshop;

namespace OpenCommonwealth.Services.Ui.Gallery;

public sealed class SampleWorkshopCatalog : IWorkshopCatalog
{
    public IReadOnlyList<WorkshopNode> Roots { get; } = new[]
    {
        WorkshopNode.Category("cat1", "Category 1",
            WorkshopNode.Leaf(new WorkshopItem("item_a", "Item A")
            {
                Description = "Sample description.",
                Materials = new[] { new WorkshopMaterial("mat_a", "Material A", 4) },
                Produces = new WorkshopOutput(Food: 2),
            }),
            WorkshopNode.Leaf(new WorkshopItem("item_b", "Item B")
            {
                Materials = new[] { new WorkshopMaterial("mat_a", "Material A", 30) },
                IsNew = true,
            })),
        WorkshopNode.Category("cat2", "Category 2",
            WorkshopNode.Leaf(new WorkshopItem("item_c", "Item C")
            {
                Materials = new[] { new WorkshopMaterial("mat_b", "Material B", 2) },
                RequiresWorker = true,
                PowerRequired = 1,
            })),
    };

    public bool FreeBuild => false;

    public int AvailableCount(string materialId) => materialId == "mat_a" ? 12 : 5;

    public int BuildableCount(string itemId) => itemId switch { "item_a" => 3, "item_c" => 2, _ => 0 };

    public WorkshopItemStatus StatusOf(string itemId) =>
        itemId == "item_b" ? WorkshopItemStatus.LackingMaterials : WorkshopItemStatus.Available;
}

public sealed class SampleWorkshopWorld : IWorkshopWorld
{
    public WorkshopAim Aim { get; set; }

    public PlacementStatus Placement { get; set; }

    public WorkshopTarget? Target { get; set; }

    public WorkshopSettlerCard? Settler { get; set; }

    public bool OutsideBuildArea { get; set; }

    public WorkshopSettlement? Settlement { get; set; } = new()
    {
        Values = new Dictionary<SettlementStat, int> { [SettlementStat.People] = 6, [SettlementStat.Food] = 8, [SettlementStat.Water] = 4 },
        BuildSize = 0.6,
    };

    public void Follow(WorkshopIntent intent)
    {
        switch (intent)
        {
            case GhostRequested:
                Aim = WorkshopAim.Ghost;
                break;
            case GhostCleared:
            case CancelRequested:
                Aim = WorkshopAim.Browsing;
                break;
            case PlaceRequested when Settlement is { } strip:
                Settlement = strip with { BuildSize = Math.Min(1.0, strip.BuildSize + 0.1) };
                break;
        }
    }
}

public sealed class SampleSettlements : ISettlementProvider
{
    private readonly List<SettlementInfo> _settlements = new()
    {
        new SettlementInfo("place_a", "Place A", new[] { new SettlementMetric("Food", 8, 1), new SettlementMetric("Water", null, 0) }),
        new SettlementInfo("place_b", "Place B", new[] { new SettlementMetric("Food", 2, -1) }, 0, true),
    };

    private readonly List<SettlerInfo> _settlers = new()
    {
        new SettlerInfo("person_a", "Person A", "place_a", SettlerJob.Food) { WorkObject = "Sample plot" },
        new SettlerInfo("person_b", "Person B", "place_a"),
    };

    private readonly List<RouteInfo> _routes = new() { new RouteInfo("route_a", "place_a", "place_b", "person_a") };

    public event Action? Changed;

    public IReadOnlyList<SettlementInfo> Settlements => _settlements;

    public IReadOnlyList<SettlerInfo> Settlers => _settlers;

    public IReadOnlyList<RouteInfo> Routes => _routes;

    public IReadOnlyList<JobTarget> ListJobTargets(string settlerId) => new[]
    {
        new JobTarget("plot_a", "Sample plot A", true),
        new JobTarget("plot_b", "Sample plot B", true, "Person C"),
    };

    public ActionResult MoveHome(string settlerId, string settlementId) =>
        Edit(settlerId, settler => settler with { HomeId = settlementId });

    public ActionResult AssignCaravan(string settlerId, string settlementId)
    {
        _routes.Add(new RouteInfo("route_" + (_routes.Count + 1), _settlers.First(s => s.Id == settlerId).HomeId, settlementId, settlerId));
        return Edit(settlerId, settler => settler with { RouteId = _routes[^1].Id });
    }

    public ActionResult AssignJob(string settlerId, string objectId) =>
        Edit(settlerId, settler => settler with { Job = SettlerJob.Other, WorkObject = objectId });

    public ActionResult Unassign(string settlerId) =>
        Edit(settlerId, settler => settler with { Job = SettlerJob.Unassigned, WorkObject = string.Empty, RouteId = string.Empty });

    public ActionResult RemoveRoute(string routeId)
    {
        _routes.RemoveAll(route => route.Id == routeId);
        Changed?.Invoke();
        return new ActionResult(true);
    }

    private ActionResult Edit(string settlerId, Func<SettlerInfo, SettlerInfo> change)
    {
        var index = _settlers.FindIndex(settler => settler.Id == settlerId);
        if (index < 0) return new ActionResult(false, "Unknown settler.");
        _settlers[index] = change(_settlers[index]);
        Changed?.Invoke();
        return new ActionResult(true);
    }
}

public sealed class SampleBench : IWorkbenchRecipes
{
    private int _stamp;
    private string _installed = "option_a";

    public string Title => "Sample bench";

    public string ApplyVerb => "Craft";

    public string Stamp => _stamp.ToString();

    public IReadOnlyList<WorkbenchTarget> Targets { get; } = new[] { new WorkbenchTarget("target_a", "Item X") { Description = "Sample item." } };

    public IReadOnlyList<WorkbenchSlot> SlotsOf(string targetId) => new[] { new WorkbenchSlot("slot_a", "Slot 1") };

    public IReadOnlyList<WorkbenchOption> OptionsOf(string targetId, string slotId) => new[]
    {
        new WorkbenchOption("option_a", "Option 1") { Description = "Sample option." },
        new WorkbenchOption("option_b", "Option 2"),
        new WorkbenchOption("option_c", "Option 3"),
    };

    public WorkbenchOptionState StateOf(string targetId, string slotId, string optionId) =>
        optionId == _installed ? WorkbenchOptionState.Installed
        : optionId == "option_c" ? WorkbenchOptionState.LackingMaterials
        : WorkbenchOptionState.Available;

    public IReadOnlyList<WorkshopMaterial> Requirements(string targetId, string slotId, string optionId) =>
        new[] { new WorkshopMaterial("mat_a", "Material A", 3) };

    public int AvailableCount(string materialId) => 5;

    public ActionResult Craft(string targetId, string slotId, string optionId)
    {
        _installed = optionId;
        _stamp++;
        return new ActionResult(true);
    }
}

public sealed class SampleBarter : IBarterRules
{
    private readonly List<BarterStack> _player = Enumerable.Range(0, 12)
        .Select(i => new BarterStack("item_" + (char)('a' + i), "Item " + (char)('A' + i), i % 3 + 1)).ToList();
    private readonly List<BarterStack> _vendor = Enumerable.Range(12, 5)
        .Select(i => new BarterStack("item_" + (char)('a' + i), "Item " + (char)('A' + i), i % 3 + 1)).ToList();
    private int _revision;

    public string VendorName => "Sample vendor";

    public string PlayerListHeader => "YOUR ITEMS";

    public string VendorListHeader => "VENDOR ITEMS";

    public int Revision => _revision;

    public int PlayerCaps { get; private set; } = 100;

    public int VendorCaps { get; private set; } = 100;

    public IReadOnlyList<BarterStack> PlayerStacks => _player;

    public IReadOnlyList<BarterStack> VendorStacks => _vendor;

    public bool CanInvest => true;

    public int InvestAmount => 20;

    public int UnitPrice(BarterSide side, string itemId) => side == BarterSide.Player ? 10 : 25;

    public string Refusal(BarterSide side, string itemId) => string.Empty;

    public BarterQuote Quote(IReadOnlyList<BarterLine> basket)
    {
        var net = basket.Sum(line => UnitPrice(line.Side, line.ItemId) * line.Count * (line.Side == BarterSide.Player ? 1 : -1));
        if (net < 0 && PlayerCaps < -net) return new BarterQuote(net, false, "Not enough caps.");
        if (net > 0 && VendorCaps < net) return new BarterQuote(net, false, "The vendor is short of caps.");
        return new BarterQuote(net, true);
    }

    public ActionResult Commit(IReadOnlyList<BarterLine> basket)
    {
        var quote = Quote(basket);
        if (!quote.CanCommit) return new ActionResult(false, quote.Refusal);
        foreach (var line in basket)
        {
            Move(line.Side == BarterSide.Player ? _player : _vendor, line.Side == BarterSide.Player ? _vendor : _player, line);
        }
        PlayerCaps += quote.PlayerNet;
        VendorCaps -= quote.PlayerNet;
        _revision++;
        return new ActionResult(true);
    }

    public ActionResult Invest()
    {
        if (PlayerCaps < InvestAmount) return new ActionResult(false, "Not enough caps.");
        PlayerCaps -= InvestAmount;
        VendorCaps += InvestAmount;
        _revision++;
        return new ActionResult(true);
    }

    private static void Move(List<BarterStack> from, List<BarterStack> to, BarterLine line)
    {
        var index = from.FindIndex(stack => stack.Id == line.ItemId);
        if (index < 0) return;
        var stack = from[index];
        if (stack.Count <= line.Count) from.RemoveAt(index);
        else from[index] = stack with { Count = stack.Count - line.Count };
        var target = to.FindIndex(candidate => candidate.Id == line.ItemId);
        if (target < 0) to.Add(new BarterStack(stack.Id, stack.Name, line.Count));
        else to[target] = to[target] with { Count = to[target].Count + line.Count };
    }
}

public sealed class SampleTravel : ITravelDestinations
{
    public string Prompt => string.Empty;

    public IReadOnlyList<DestinationEntry> Destinations { get; } = new[]
    {
        new DestinationEntry("place_a", "Place A"),
        new DestinationEntry("place_b", "Place B", true, "That route is blocked."),
        new DestinationEntry("place_c", "Place C"),
    };

    public string? HighlightedId => null;

    public bool IsValid => true;

    public ActionResult Travel(string destinationId) => new(true);
}

public sealed class SampleCredits : ICreditsSource
{
    public IReadOnlyList<string> Lines { get; } = new[]
    {
        "Sample Title",
        string.Empty,
        "A gallery sample",
        string.Empty,
        "Role A",
        "Person A",
        "Person B",
        string.Empty,
        "Role B",
        "Person C",
        "Person D",
        "Person E",
        string.Empty,
        "Thank you for playing.",
    };
}
