using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Papyrus.Coverage;

/// <summary>One Part C task and whether it is finished.</summary>
public sealed record PartCTask(string Id, string Title, bool Done);

public enum ClosureState
{
    /// <summary>The row is implemented under a proven key or a Garden policy.</summary>
    Closed,

    /// <summary>The row is approximated and its reliance is counted.</summary>
    Approximated,

    /// <summary>The row is unproven and names an open evidence key.</summary>
    OpenEvidenceKey,

    /// <summary>The row is unproven because the Garden system it needs does not exist.</summary>
    MissingGardenSystem,
}

public sealed record ClosureRow(string Key, string Status, ClosureState State, string Reason);

public sealed record ClosureGap(string Subject, string Reason);

public sealed record ClosureReport(
    IReadOnlyList<ClosureRow> Rows,
    IReadOnlyList<ClosureGap> Gaps,
    int Closed,
    int Approximated,
    int OpenKeys,
    int MissingSystems)
{
    /// <summary>
    /// A clean gate means every row is accounted for and every Part C task is done. It is not a
    /// compatibility claim.
    /// </summary>
    public bool Clean => Gaps.Count == 0;

    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"closure gate, not a compatibility claim: rows={Rows.Count} closed={Closed} approximated={Approximated} open-keys={OpenKeys} missing-systems={MissingSystems} gaps={Gaps.Count}\n");
        foreach (var row in Rows)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {row.Key} {row.State.ToString().ToLowerInvariant()} {row.Reason}\n");
        }
        foreach (var gap in Gaps)
        {
            text.Append(CultureInfo.InvariantCulture, $"  gap {gap.Subject} {gap.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// The Part C closure gate.
/// </summary>
/// <remarks>
/// The gate reruns the coverage view over the recorded registry and checks two things: every remaining
/// unsupported or unverified row names only an open evidence key or a Garden system that does not exist,
/// and no Part C task is still open. A row that an implementation consumes may not stay unproven, and a
/// row with no consumer must be a probe that has not run or a named missing system, otherwise it is a
/// gap. A contradicted row is closed: the measurement refuted the hypothesis and the refutation is
/// recorded. The gate reports its own limits: a clean result means the bookkeeping closes, never that
/// Garden is compatible with the engine.
/// </remarks>
public static class PartCClosureGate
{
    public static ClosureReport Run(string registryJson, IEnumerable<PartCTask> tasks)
    {
        var rows = ReadRows(registryJson);
        var closed = 0;
        var approximated = 0;
        var openKeys = 0;
        var missing = 0;
        var report = new List<ClosureRow>();
        var gaps = new List<ClosureGap>();

        foreach (var row in rows.OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            switch (row.Status)
            {
                case "PROVEN":
                case "ENGINE_POLICY":
                    closed++;
                    report.Add(new ClosureRow(row.Key, row.Status, ClosureState.Closed, "implemented under a proven key or a Garden policy"));
                    break;
                case "CONTRADICTED":
                    closed++;
                    report.Add(new ClosureRow(row.Key, row.Status, ClosureState.Closed,
                        "the measurement contradicts the hypothesis and the contradiction is recorded"));
                    break;
                case "APPROXIMATED":
                    approximated++;
                    report.Add(new ClosureRow(row.Key, row.Status, ClosureState.Approximated,
                        "partially implemented, and the reliance is counted"));
                    break;
                default:
                    if (row.Consumers.Count > 0)
                    {
                        gaps.Add(new ClosureGap(row.Key,
                            $"a Part C implementation consumes this row, so it may not stay {row.Status}"));
                        report.Add(new ClosureRow(row.Key, row.Status, ClosureState.OpenEvidenceKey,
                            "an implementation consumes this row but it is not proven"));
                        openKeys++;
                        break;
                    }
                    if (NamesAMissingSystem(row.Key))
                    {
                        missing++;
                        report.Add(new ClosureRow(row.Key, row.Status, ClosureState.MissingGardenSystem,
                            "the Garden system this needs does not exist yet"));
                        break;
                    }
                    if (!string.Equals(row.ProbeStatus, "run", StringComparison.Ordinal))
                    {
                        openKeys++;
                        report.Add(new ClosureRow(row.Key, row.Status, ClosureState.OpenEvidenceKey,
                            $"the probe for this key has not run ({row.ProbeStatus})"));
                        break;
                    }
                    gaps.Add(new ClosureGap(row.Key,
                        $"the row is {row.Status} with a probe that ran, no consumer and no named missing system"));
                    report.Add(new ClosureRow(row.Key, row.Status, ClosureState.OpenEvidenceKey,
                        "unclassified: neither an open key nor a named missing system"));
                    break;
            }
        }

        foreach (var task in tasks.OrderBy(task => task.Id, StringComparer.Ordinal))
        {
            if (!task.Done)
            {
                gaps.Add(new ClosureGap(task.Id, $"the Part C task {task.Title} is still open"));
            }
        }

        return new ClosureReport(report, gaps, closed, approximated, openKeys, missing);
    }

    /// <summary>Keys whose name is a Garden system, not an evidence question.</summary>
    public static bool NamesAMissingSystem(string key) =>
        key.StartsWith("system.", StringComparison.Ordinal)
        || key.EndsWith(".system", StringComparison.Ordinal)
        || key.Contains(".system.", StringComparison.Ordinal);

    private sealed record Row(string Key, string Status, string ProbeStatus, IReadOnlyList<string> Consumers);

    private static List<Row> ReadRows(string registryJson)
    {
        JsonNode root;
        try
        {
            root = JsonNode.Parse(registryJson) ?? throw new FormatException("the registry was empty");
        }
        catch (JsonException error)
        {
            throw new FormatException("the registry is not valid JSON", error);
        }
        if (root is not JsonObject registry || registry["rows"] is not JsonArray rows || rows.Count == 0)
        {
            throw new FormatException("the registry must be an object with a non-empty rows array");
        }

        var result = new List<Row>();
        foreach (var entry in rows)
        {
            if (entry is not JsonObject row || (string?)row["key"] is not { } key)
            {
                throw new FormatException("every registry row must be an object with a key");
            }
            var consumers = row["allowed_consumers"] is JsonArray array
                ? array.Select(item => (string?)item ?? string.Empty).Where(item => item.Length > 0).ToList()
                : new List<string>();
            result.Add(new Row(key, (string?)row["status"] ?? "", (string?)row["probe_status"] ?? "none", consumers));
        }
        return result;
    }
}
