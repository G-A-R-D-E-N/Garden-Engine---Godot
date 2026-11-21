using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Workshop;

public sealed record WorkshopMaterial(string Id, string Name, int Needed);

public sealed record WorkshopPerk(string Name, int Rank, bool Met);

public sealed record WorkshopOutput(int Food = 0, int Water = 0, int Power = 0, int Safety = 0, int Happiness = 0);

public sealed record WorkshopItem(string Id, string Name)
{
    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<WorkshopMaterial> Materials { get; init; } = Array.Empty<WorkshopMaterial>();

    public IReadOnlyList<WorkshopPerk> Perks { get; init; } = Array.Empty<WorkshopPerk>();

    public bool RequiresWorker { get; init; }

    public int PowerRequired { get; init; }

    public WorkshopOutput Produces { get; init; } = new();

    public bool IsNew { get; init; }

    public int Stored { get; init; }
}

public sealed record WorkshopNode(string Id, string Name)
{
    public IReadOnlyList<WorkshopNode> Children { get; init; } = Array.Empty<WorkshopNode>();

    public WorkshopItem? Item { get; init; }

    public bool IsCategory => Item is null;

    public static WorkshopNode Category(string id, string name, params WorkshopNode[] children) =>
        new(id, name) { Children = children };

    public static WorkshopNode Leaf(WorkshopItem item) => new(item.Id, item.Name) { Item = item };
}

public enum WorkshopItemStatus
{
    Available,
    LackingMaterials,
    PerkRequired,
    ConditionsUnknown,
    BudgetFull,
    Unavailable,
}

public interface IWorkshopCatalog
{
    IReadOnlyList<WorkshopNode> Roots { get; }

    bool FreeBuild { get; }

    int AvailableCount(string materialId);

    int BuildableCount(string itemId);

    WorkshopItemStatus StatusOf(string itemId);

    IReadOnlyList<WorkshopPerk> PerksOf(WorkshopItem item) => item.Perks;
}

public enum WorkshopAim
{
    Browsing,
    Ghost,
    Holding,
    TargetObject,
    TargetSettler,
    WireHeld,
}

public enum PlacementStatus
{
    Valid,
    OutsideArea,
    Blocked,
    Unsupported,
    NeedsTerrain,
    NeedsWater,
    WireTooLong,
    WireDuplicate,
    BudgetFull,
}

public enum SettlementStat
{
    People,
    Food,
    Water,
    Power,
    Defense,
    Beds,
    Happiness,
}

public sealed record WorkshopSettlement
{
    public IReadOnlyDictionary<SettlementStat, int> Values { get; init; } = new Dictionary<SettlementStat, int>();

    public IReadOnlySet<SettlementStat> Alerts { get; init; } = new HashSet<SettlementStat>();

    public int HappinessTrend { get; init; }

    public double BuildSize { get; init; }

    public bool ReadoutVisible { get; init; } = true;
}

public sealed record WorkshopTarget(string Id, string Name)
{
    public bool Selectable { get; init; }

    public bool Storable { get; init; }

    public bool Scrappable { get; init; }

    public bool ScrapBlocked { get; init; }

    public string? ScrapBlockedReason { get; init; }

    public bool Repairable { get; init; }

    public bool RepairBlocked { get; init; }

    public bool PowerConnectable { get; init; }

    public bool RepairAffordable { get; init; }

    public IReadOnlyList<WorkshopMaterial> Yield { get; init; } = Array.Empty<WorkshopMaterial>();

    public IReadOnlyList<WorkshopMaterial> RepairCost { get; init; } = Array.Empty<WorkshopMaterial>();
}

public sealed record WorkshopSettlerCard(string Id, string Name)
{
    public string JobText { get; init; } = string.Empty;

    public string HomeId { get; init; } = string.Empty;

    public string HomeName { get; init; } = string.Empty;

    public bool CanCommand { get; init; }

    public bool CanMove { get; init; }

    public bool CanCaravan { get; init; }

    public string CommandReason { get; init; } = string.Empty;

    public string MoveReason { get; init; } = string.Empty;

    public string CaravanReason { get; init; } = string.Empty;
}

public interface IWorkshopWorld
{
    WorkshopAim Aim { get; }

    PlacementStatus Placement { get; }

    WorkshopTarget? Target { get; }

    WorkshopSettlerCard? Settler { get; }

    bool OutsideBuildArea { get; }

    WorkshopSettlement? Settlement { get; }

    // Invariant: counts committed placements; -1 means the world does not report them and a dispatched placement is taken as done.
    int PlacedCount => -1;
}

public abstract record WorkshopIntent;

public sealed record GhostRequested(string ItemId) : WorkshopIntent;

public sealed record GhostCleared : WorkshopIntent;

public sealed record PlaceRequested : WorkshopIntent;

public sealed record CancelRequested : WorkshopIntent;

public sealed record MoveRequested(string ObjectId) : WorkshopIntent;

public sealed record StoreRequested(string ObjectId) : WorkshopIntent;

public sealed record ScrapRequested(string ObjectId) : WorkshopIntent;

public sealed record RepairRequested(string ObjectId) : WorkshopIntent;

public sealed record WireRequested(string ObjectId) : WorkshopIntent;

public sealed record TrackRequested(string ItemId, bool Tracked) : WorkshopIntent;

public sealed record SettlerCommandRequested(string SettlerId) : WorkshopIntent;
