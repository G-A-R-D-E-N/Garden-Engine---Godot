using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Workbench;

namespace OpenCommonwealth.Services.Ui.Workshop;

// Requires: main thread; the host supplies the placement check, the spawn and its undo, the aimed target and the scrap removal; materials are spent only after the spawn succeeded and parts are paid only after the removal succeeded.
public sealed class WorkshopSession(
    IWorkshopCatalog catalog, ComponentPool pool, Func<string, PlacementStatus> check, Func<string, bool> place,
    Func<WorkshopTarget?>? aimed = null, Func<string, bool>? scrap = null, Action<string>? undo = null,
    Func<WorkshopSettlerCard?>? aimedSettler = null, Func<bool>? outsideArea = null) : IWorkshopWorld
{
    private const string WorkbenchPrefix = "WorkshopWorkbench";

    private string? _ghost;

    public int PlacedCount { get; private set; }

    public event Action<string>? Placed;

    public event Action<string>? Scrapped;

    public WorkshopAim Aim => _ghost is not null ? WorkshopAim.Ghost : Target is not null ? WorkshopAim.TargetObject
        : Settler is not null ? WorkshopAim.TargetSettler : WorkshopAim.Browsing;

    public PlacementStatus Placement => _ghost is { } id ? check(id) : PlacementStatus.Valid;

    public WorkshopTarget? Target => aimed?.Invoke();

    public WorkshopSettlerCard? Settler => aimedSettler?.Invoke();

    public bool OutsideBuildArea => outsideArea?.Invoke() ?? false;

    public WorkshopSettlement? Settlement => null;

    public static bool IsWorkshopBench(IRecordReader reader, string baseKey) =>
        reader.Signature(baseKey) == "CONT" && reader.EditorId(baseKey)?.StartsWith(WorkbenchPrefix, StringComparison.Ordinal) == true;

    public void Follow(WorkshopIntent intent)
    {
        switch (intent)
        {
            case GhostRequested ghost:
                _ghost = ghost.ItemId;
                break;
            case GhostCleared:
            case CancelRequested:
                _ghost = null;
                break;
            case PlaceRequested when _ghost is { } id && Placement == PlacementStatus.Valid:
                TryPlace(id);
                break;
            case ScrapRequested request:
                TryScrap(request.ObjectId);
                break;
        }
    }

    private void TryPlace(string id)
    {
        if (catalog.StatusOf(id) != WorkshopItemStatus.Available
            || Flatten(catalog.Roots).FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal)) is not { } item) return;
        if (!place(id)) return;
        if (!catalog.FreeBuild && !pool.Consume(item.Materials))
        {
            undo?.Invoke(id);
            return;
        }
        PlacedCount++;
        Placed?.Invoke(id);
    }

    private void TryScrap(string targetId)
    {
        if (scrap is null || Target is not { Scrappable: true, ScrapBlocked: false } target || target.Id != targetId) return;
        if (pool.PlanCredit(target.Yield) is not { } rewards) return;
        if (!scrap(targetId)) return;
        pool.Pay(rewards);
        Scrapped?.Invoke(targetId);
    }

    private static IEnumerable<WorkshopItem> Flatten(IEnumerable<WorkshopNode> nodes) =>
        nodes.SelectMany(node => node.Item is { } item ? new[] { item } : Flatten(node.Children));
}
