using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workshop;

namespace OpenCw.Ui.Tests;

sealed class FakeCatalog : IWorkshopCatalog
{
    public IReadOnlyList<WorkshopNode> Roots { get; set; } = Array.Empty<WorkshopNode>();

    public bool FreeBuild { get; set; }

    public Dictionary<string, int> Stock { get; } = new();

    public Dictionary<string, int> Buildable { get; } = new();

    public Dictionary<string, WorkshopItemStatus> Status { get; } = new();

    public int AvailableCount(string materialId) => Stock.GetValueOrDefault(materialId);

    public int BuildableCount(string itemId) => Buildable.GetValueOrDefault(itemId);

    public WorkshopItemStatus StatusOf(string itemId) => Status.GetValueOrDefault(itemId, WorkshopItemStatus.Available);

    public static FakeCatalog Sample()
    {
        var wood = new WorkshopMaterial("wood", "Wood", 5);
        var chair = new WorkshopItem("chair", "Chair") { Materials = new[] { wood }, Description = "Somewhere to sit." };
        var bed = new WorkshopItem("bed", "Bed") { Materials = new[] { new WorkshopMaterial("wood", "Wood", 10) } };
        var catalog = new FakeCatalog
        {
            Roots = new[]
            {
                WorkshopNode.Category("furn", "Furniture", WorkshopNode.Leaf(chair), WorkshopNode.Leaf(bed)),
                WorkshopNode.Category("empty", "Empty"),
            },
        };
        catalog.Buildable["chair"] = 3;
        catalog.Status["bed"] = WorkshopItemStatus.LackingMaterials;
        catalog.Stock["wood"] = 7;
        return catalog;
    }
}

sealed class FakeWorld : IWorkshopWorld
{
    public WorkshopAim Aim { get; set; }

    public PlacementStatus Placement { get; set; }

    public WorkshopTarget? Target { get; set; }

    public WorkshopSettlerCard? Settler { get; set; }

    public bool OutsideBuildArea { get; set; }

    public WorkshopSettlement? Settlement { get; set; }

    public int PlacedCount { get; set; } = -1;
}

sealed class FakeSettlements : ISettlementProvider
{
    public event Action? Changed;

    public List<SettlementInfo> SettlementList { get; } = new()
    {
        new SettlementInfo("s1", "Sanctuary", new[] { new SettlementMetric("Food", 12.5, 1), new SettlementMetric("Water", null, 0) }),
        new SettlementInfo("s2", "Hill", new[] { new SettlementMetric("Food", 3, -1) }, 0, true),
    };

    public List<SettlerInfo> SettlerList { get; } = new()
    {
        new SettlerInfo("a", "Ann", "s1", SettlerJob.Food) { WorkObject = "Crop" },
        new SettlerInfo("b", "Bob", "s1"),
    };

    public List<RouteInfo> RouteList { get; } = new() { new RouteInfo("r1", "s1", "s2", "a") };

    public List<JobTarget> JobTargets { get; } = new()
    {
        new JobTarget("t1", "Field", true),
        new JobTarget("t2", "Tower", true, "Cy"),
    };

    public ActionResult Next { get; set; } = new(true);

    public bool Throw { get; set; }

    public List<string> Calls { get; } = new();

    public IReadOnlyList<SettlementInfo> Settlements => SettlementList;

    public IReadOnlyList<SettlerInfo> Settlers => SettlerList;

    public IReadOnlyList<RouteInfo> Routes => RouteList;

    public IReadOnlyList<JobTarget> ListJobTargets(string settlerId) => Throw ? throw new InvalidOperationException("boom") : JobTargets;

    public void Raise() => Changed?.Invoke();

    public ActionResult MoveHome(string settlerId, string settlementId)
    {
        if (Throw) throw new InvalidOperationException("boom");
        Calls.Add("home:" + settlerId + ">" + settlementId);
        if (Next.Ok) Edit(settlerId, settler => settler with { HomeId = settlementId });
        return Next;
    }

    public ActionResult AssignCaravan(string settlerId, string settlementId)
    {
        if (Throw) throw new InvalidOperationException("boom");
        Calls.Add("route:" + settlerId + ">" + settlementId);
        return Next;
    }

    public ActionResult AssignJob(string settlerId, string objectId)
    {
        if (Throw) throw new InvalidOperationException("boom");
        Calls.Add("job:" + settlerId + ">" + objectId);
        if (Next.Ok) Edit(settlerId, settler => settler with { Job = SettlerJob.Other, WorkObject = objectId });
        return Next;
    }

    public ActionResult Unassign(string settlerId)
    {
        Calls.Add("unassign:" + settlerId);
        if (Next.Ok) Edit(settlerId, settler => settler with { Job = SettlerJob.Unassigned, WorkObject = string.Empty });
        return Next;
    }

    public ActionResult UnassignBed(string settlerId)
    {
        Calls.Add("unassignbed:" + settlerId);
        if (Next.Ok) Edit(settlerId, settler => settler with { BedObject = string.Empty });
        return Next;
    }

    public ActionResult RemoveRoute(string routeId)
    {
        Calls.Add("remove:" + routeId);
        if (Next.Ok) RouteList.RemoveAll(route => route.Id == routeId);
        return Next;
    }

    void Edit(string settlerId, Func<SettlerInfo, SettlerInfo> change)
    {
        var index = SettlerList.FindIndex(settler => settler.Id == settlerId);
        if (index >= 0) SettlerList[index] = change(SettlerList[index]);
    }
}

static class WorkshopTestHelpers
{
    public static List<WorkshopIntent> Capture(WorkshopBuildController controller)
    {
        var intents = new List<WorkshopIntent>();
        controller.IntentRaised += intents.Add;
        return intents;
    }

    public static string[] Texts(IListDetailController controller) =>
        controller.DetailLines.Select(line => line.Text).ToArray();

    public static string[] RowTexts(IListMenuView list) =>
        Enumerable.Range(0, list.RowCount).Select(list.RowText).ToArray();
}
