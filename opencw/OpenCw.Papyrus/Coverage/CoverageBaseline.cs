using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Events;

namespace OpenCommonwealth.Services.Papyrus.Coverage;

public enum CoverageStatus
{
    /// <summary>Garden implements this and the evidence allows it.</summary>
    Covered,

    /// <summary>Garden deliberately implements only part of it, and reports the reliance.</summary>
    Approximated,

    /// <summary>The behavior is not proven, so Garden stops and counts it.</summary>
    Unsupported,

    /// <summary>Garden has no system for this yet.</summary>
    Missing,
}

public sealed record CoverageRow(string Id, string Group, long Count, CoverageStatus Status, string? Note);

public sealed record CoverageBaseline(IReadOnlyList<CoverageRow> Rows, IReadOnlyList<string> Inputs)
{
    public long CountOf(CoverageStatus status) => Rows.Where(row => row.Status == status).Sum(row => row.Count);

    public int RowsOf(CoverageStatus status) => Rows.Count(row => row.Status == status);

    public CoverageRow? Find(string id) => Rows.FirstOrDefault(row => row.Id == id);

    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"rows={Rows.Count} covered={RowsOf(CoverageStatus.Covered)} approximated={RowsOf(CoverageStatus.Approximated)} unsupported={RowsOf(CoverageStatus.Unsupported)} missing={RowsOf(CoverageStatus.Missing)}\n");
        foreach (var row in Rows)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {row.Id} {row.Status.ToString().ToLowerInvariant()} count={row.Count} {row.Note}\n");
        }
        return text.ToString();
    }

    /// <summary>A stable digest, so a rerun can be compared with the baseline byte for byte.</summary>
    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>What one task closed, measured against the baseline.</summary>
public sealed record CoverageClosure(string Task, string RowId, long Before, long After)
{
    public long Delta => After - Before;
}

public sealed record CoverageDelta(string BaselineDigest, IReadOnlyList<CoverageClosure> Closures)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"baseline={BaselineDigest} closures={Closures.Count}\n");
        foreach (var closure in Closures)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {closure.Task} {closure.RowId} before={closure.Before} after={closure.After} delta={closure.Delta}\n");
        }
        return text.ToString();
    }
}

/// <summary>What the baseline measures over the effective load order.</summary>
/// <remarks>
/// <c>EventHandlers</c> is what the scripts declare. <c>Events</c> is the explicit event set Garden can
/// actually emit, from <see cref="PapyrusEventContract"/>; a declared handler is covered only when the
/// contract says Garden can emit it, so the baseline is never read as an engine-complete event set.
/// When no contract is supplied nothing is deliverable and every handler is counted, which is the
/// fail-closed default.
/// </remarks>
public sealed record CoverageInputs(
    IReadOnlyDictionary<string, long> OwnerTypes,
    long AttachedScripts,
    IReadOnlyDictionary<string, long> NativeCallSites,
    IReadOnlyDictionary<string, long> EventHandlers,
    long Fragments,
    IReadOnlyList<string> UnsupportedKeys,
    IReadOnlyList<string> MissingSystems,
    IReadOnlyList<EventSupport>? Events = null);

/// <summary>
/// The full-load-order Papyrus coverage baseline.
/// </summary>
/// <remarks>
/// The baseline turns the effective load order into rows with stable ids, so a later task can point at
/// the exact row and count it closes. Owner types, attached scripts, native call sites, event handlers
/// and fragments are counted; every unsupported or unverified evidence key becomes its own row, and
/// every Garden system that does not exist yet is a `Missing` row rather than an omission. An event
/// handler is covered only when the explicit event contract says Garden can emit it, so the baseline
/// never treats a declared handler as an event the runtime can deliver. The report is ordinally ordered
/// and carries a digest, so a rerun is comparable with the baseline.
/// </remarks>
public static class CoverageBaselineBuilder
{
    public static CoverageBaseline Build(CoverageInputs inputs)
    {
        var rows = new List<CoverageRow>();
        const string measurement = "measurement alone does not establish runtime support";
        foreach (var owner in inputs.OwnerTypes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            rows.Add(new CoverageRow($"owner.{owner.Key}", "owner", owner.Value, CoverageStatus.Unsupported, measurement));
        }
        rows.Add(new CoverageRow("scripts.attached", "scripts", inputs.AttachedScripts, CoverageStatus.Unsupported, measurement));
        foreach (var native in inputs.NativeCallSites.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            rows.Add(new CoverageRow($"native.{native.Key}", "native", native.Value, CoverageStatus.Unsupported, measurement));
        }
        foreach (var handler in inputs.EventHandlers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var support = inputs.Events?.FirstOrDefault(entry => PapyrusEventContract.Handles(entry.Name, handler.Key));
            var status = support?.State switch
            {
                EventSupportState.Supported => CoverageStatus.Covered,
                EventSupportState.Approximated => CoverageStatus.Approximated,
                _ => CoverageStatus.Unsupported,
            };
            var note = status == CoverageStatus.Covered
                ? null
                : support?.Reason ?? "Garden constructs no fixed emitter for this event, so no Garden system can emit it";
            rows.Add(new CoverageRow($"event.{handler.Key}", "event", handler.Value, status, note));
        }
        rows.Add(new CoverageRow("fragments.stage", "fragment", inputs.Fragments, CoverageStatus.Unsupported, measurement));
        foreach (var key in inputs.UnsupportedKeys.OrderBy(key => key, StringComparer.Ordinal))
        {
            rows.Add(new CoverageRow($"unsupported.{key}", "unsupported", 0, CoverageStatus.Unsupported,
                "the behavior is unproven, so Garden stops and counts it"));
        }
        foreach (var system in inputs.MissingSystems.OrderBy(system => system, StringComparer.Ordinal))
        {
            rows.Add(new CoverageRow($"missing.{system}", "missing", 0, CoverageStatus.Missing,
                "Garden has no system for this yet"));
        }

        var ordered = rows.OrderBy(row => row.Id, StringComparer.Ordinal).ToList();
        var inputsUsed = new List<string> { "effective load order", "evidence registry" };
        return new CoverageBaseline(ordered, inputsUsed);
    }

    /// <summary>Measures what a task closed, and refuses a row that is not in the baseline.</summary>
    public static CoverageDelta Close(CoverageBaseline baseline, string task,
        IReadOnlyDictionary<string, long> afterCounts)
    {
        var closures = new List<CoverageClosure>();
        foreach (var pair in afterCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var row = baseline.Find(pair.Key)
                ?? throw new InvalidOperationException($"the baseline has no row {pair.Key}, so a closure cannot be claimed");
            closures.Add(new CoverageClosure(task, row.Id, row.Count, pair.Value));
        }
        return new CoverageDelta(baseline.Digest(), closures);
    }
}
