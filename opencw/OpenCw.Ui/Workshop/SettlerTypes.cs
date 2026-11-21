using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Workshop;

public enum SettlerJob
{
    Unassigned,
    Food,
    Defense,
    Provisioner,
    Scavenger,
    Vendor,
    Other,
}

public enum SettlerChange
{
    Home,
    Job,
    Route,
    Unassigned,
}

public sealed record SettlementMetric(string Label, double? Value, int Rating);

public sealed record SettlementInfo(string Id, string Name, IReadOnlyList<SettlementMetric> Metrics, int Rating = 0, bool Raiders = false);

public sealed record SettlerInfo(string Id, string Name, string HomeId, SettlerJob Job = SettlerJob.Unassigned)
{
    public string WorkObject { get; init; } = string.Empty;

    public string RouteId { get; init; } = string.Empty;

    public bool CanCommand { get; init; } = true;

    public string CommandReason { get; init; } = string.Empty;

    public bool CanMove { get; init; } = true;

    public string MoveReason { get; init; } = string.Empty;

    public bool CanCaravan { get; init; } = true;

    public string CaravanReason { get; init; } = string.Empty;
}

public sealed record RouteInfo(string Id, string EndA, string EndB, string SettlerId);

public sealed record JobTarget(string ObjectId, string Name, bool NeedsWorker, string TakenBy = "");

public interface ISettlementProvider
{
    event Action? Changed;

    IReadOnlyList<SettlementInfo> Settlements { get; }

    IReadOnlyList<SettlerInfo> Settlers { get; }

    IReadOnlyList<RouteInfo> Routes { get; }

    IReadOnlyList<JobTarget> ListJobTargets(string settlerId);

    ActionResult MoveHome(string settlerId, string settlementId);

    ActionResult AssignCaravan(string settlerId, string settlementId);

    ActionResult AssignJob(string settlerId, string objectId);

    ActionResult Unassign(string settlerId);

    ActionResult RemoveRoute(string routeId);
}
