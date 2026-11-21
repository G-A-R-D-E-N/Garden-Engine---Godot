using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Workshop;

public sealed record WorkObject(string Id, string Name, bool RequiresActor, bool IsBed, bool AllowAssignment = true);

public interface ISettlerRoster
{
    event Action? Changed;

    IReadOnlyList<SettlementInfo> Settlements { get; }

    IReadOnlyList<SettlerInfo> Settlers { get; }

    IReadOnlyList<WorkObject> Objects(string settlerId);
}

// Requires: main thread only; assignments live in the store, the roster only names settlers, settlements and objects; an owner outside the roster is named by nameOf, else by a fixed fallback, never by its key. Moving and caravan routes have no engine path here and always fail.
public sealed class SettlerAssignmentsProvider : ISettlementProvider
{
    public const string NotAvailableText = "Not available.";
    public const string NotAssignableText = "That can't be assigned.";
    public const string NothingToUnassignText = "Nothing to unassign.";

    private readonly ISettlerRoster _roster;
    private readonly SettlerAssignments _assignments;
    private readonly Func<string, string?>? _nameOf;

    public SettlerAssignmentsProvider(ISettlerRoster roster, SettlerAssignments assignments, Func<string, string?>? nameOf = null)
    {
        _nameOf = nameOf;
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        _assignments = assignments ?? throw new ArgumentNullException(nameof(assignments));
        roster.Changed += () => Changed?.Invoke();
    }

    public event Action? Changed;

    public IReadOnlyList<SettlementInfo> Settlements => _roster.Settlements;

    public IReadOnlyList<SettlerInfo> Settlers => _roster.Settlers.Select(Overlay).ToList();

    public IReadOnlyList<RouteInfo> Routes => Array.Empty<RouteInfo>();

    public IReadOnlyList<JobTarget> ListJobTargets(string settlerId) =>
        _roster.Objects(settlerId).Where(item => item.AllowAssignment && (item.RequiresActor || item.IsBed))
            .Select(item => new JobTarget(item.Id, item.Name, item.RequiresActor, TakenBy(item.Id, settlerId))).ToList();

    public ActionResult AssignJob(string settlerId, string objectId)
    {
        if (_roster.Objects(settlerId).FirstOrDefault(item => item.Id == objectId) is not { } target)
            return new ActionResult(false, SettlerCommandController.ChangedText);
        if (!target.AllowAssignment || !(target.RequiresActor || target.IsBed) || _assignments.Conflicts(objectId, target.IsBed))
            return new ActionResult(false, NotAssignableText);
        var displaced = _assignments.Assign(settlerId, objectId, target.IsBed);
        Changed?.Invoke();
        return displaced is null ? new ActionResult(true) : new ActionResult(true, SettlerCommandText.Unassigned(NameOf(displaced)));
    }

    public ActionResult Unassign(string settlerId)
    {
        if (!_assignments.Unassign(settlerId)) return new ActionResult(false, NothingToUnassignText);
        Changed?.Invoke();
        return new ActionResult(true);
    }

    public ActionResult MoveHome(string settlerId, string settlementId) => new(false, NotAvailableText);

    public ActionResult AssignCaravan(string settlerId, string settlementId) => new(false, NotAvailableText);

    public ActionResult RemoveRoute(string routeId) => new(false, NotAvailableText);

    private SettlerInfo Overlay(SettlerInfo settler)
    {
        var moved = settler with { CanMove = false, MoveReason = NotAvailableText, CanCaravan = false, CaravanReason = NotAvailableText };
        if (_assignments.WorkObjectOf(settler.Id) is not { } work) return moved;
        var name = _roster.Objects(settler.Id).FirstOrDefault(item => item.Id == work)?.Name ?? work;
        return moved with { WorkObject = name, Job = settler.Job == SettlerJob.Unassigned ? SettlerJob.Other : settler.Job };
    }

    private string TakenBy(string objectId, string settlerId)
    {
        if (_assignments.OwnerOf(objectId) is not { } owner || owner == settlerId) return string.Empty;
        return NameOf(owner) ?? SettlerCommandText.UnknownSettlerText;
    }

    private string? NameOf(string settlerId)
    {
        var name = _roster.Settlers.FirstOrDefault(settler => settler.Id == settlerId)?.Name;
        if (string.IsNullOrWhiteSpace(name)) name = _nameOf?.Invoke(settlerId);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
