using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Coverage;

/// <summary>One VM-only semantic key and where it stands.</summary>
public sealed record VmGapRow(
    string Key,
    string Status,
    string ProbeStatus,
    bool Implemented,
    bool RefusedByGate,
    string Note)
{
    public bool Closed => Implemented && !RefusedByGate;
}

public sealed record VmGapReport(
    IReadOnlyList<VmGapRow> Rows,
    IReadOnlyList<string> PromotedWithoutEvidence,
    int Closed,
    int Open)
{
    public bool Clean => PromotedWithoutEvidence.Count == 0;

    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"keys={Rows.Count} closed={Closed} open={Open} promoted-without-evidence={PromotedWithoutEvidence.Count}\n");
        foreach (var row in Rows)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {row.Key} status={row.Status} probe={row.ProbeStatus} implemented={row.Implemented} closed={row.Closed} {row.Note}\n");
        }
        foreach (var key in PromotedWithoutEvidence)
        {
            text.Append(CultureInfo.InvariantCulture, $"  promoted-without-evidence {key}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// The VM-only semantic gap register.
/// </summary>
/// <remarks>
/// Every VM-only key is either closed — the VM implements it and the gate allows it — or open, in which
/// case the VM refuses it and counts the refusal. The register also enforces the promotion rule: a key
/// may only be `PROVEN` when its registry row records that a probe actually ran, so a result can never
/// be promoted without its logged evidence. Nothing here changes a status; it reports what the recorded
/// registry and the gate already say.
/// </remarks>
public static class VmSemanticGapRegister
{
    public const string RunProbe = "run";

    public static VmGapReport Run(string registryJson, IEnumerable<string> vmOnlyKeys, EvidenceGate gate)
    {
        var rows = ReadRows(registryJson);
        var register = new List<VmGapRow>();
        var promoted = new List<string>();

        foreach (var key in vmOnlyKeys.OrderBy(key => key, StringComparer.Ordinal))
        {
            if (!rows.TryGetValue(key, out var row))
            {
                register.Add(new VmGapRow(key, "MISSING", "none", true, true,
                    "the registry has no row, so the VM fails closed and counts it"));
                continue;
            }

            var decision = gate.Gate(key);
            var refused = !decision.Allowed;
            if (row.Status == "PROVEN" && !string.Equals(row.ProbeStatus, RunProbe, StringComparison.Ordinal))
            {
                promoted.Add(key);
            }

            register.Add(new VmGapRow(key, row.Status, row.ProbeStatus, true, refused, Note(row.Status, refused)));
        }

        return new VmGapReport(register, promoted,
            register.Count(row => row.Closed), register.Count(row => !row.Closed));
    }

    private static string Note(string status, bool refused) => status switch
    {
        "PROVEN" when !refused => "proven and implemented",
        "ENGINE_POLICY" when !refused => "a deliberate Garden decision, implemented",
        "APPROXIMATED" => "partially implemented and every reliance is counted",
        "UNPROVEN" => "unproven, so the VM refuses it and counts the refusal",
        "CONTRADICTED" => "the measurement contradicts the hypothesis, so the VM refuses it",
        _ => "unrecognised status",
    };

    private static Dictionary<string, (string Status, string ProbeStatus)> ReadRows(string registryJson)
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
        if (root is not JsonObject registry || registry["rows"] is not JsonArray rows)
        {
            throw new FormatException("the registry must be an object with a rows array");
        }

        var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var entry in rows)
        {
            if (entry is not JsonObject row || (string?)row["key"] is not { } key)
            {
                continue;
            }
            result[key] = ((string?)row["status"] ?? "", (string?)row["probe_status"] ?? "none");
        }
        return result;
    }
}
