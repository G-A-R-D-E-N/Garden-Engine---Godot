using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Papyrus.Vm;

/// <summary>The status a registry row carries.</summary>
public enum EvidenceStatus
{
    Proven,
    Unproven,
    Contradicted,
    Approximated,
    EnginePolicy,
}

/// <summary>
/// The evidence registry the VM gates itself on.
/// </summary>
/// <remarks>
/// The registry is the artifact B0 records (`docs/proof/papyrus-evidence-registry.json`). Loading it
/// here, instead of hard-coding a table, is what makes the gate real: a behavior the VM implements
/// only runs when its key says it may, and an unproven key stops the VM and is counted.
/// </remarks>
public sealed class EvidenceRegistry
{
    public const string SchemaName = "garden.papyrus.evidence-registry";

    private readonly Dictionary<string, EvidenceStatus> _statuses;

    private EvidenceRegistry(Dictionary<string, EvidenceStatus> statuses) => _statuses = statuses;

    public int Count => _statuses.Count;

    public static EvidenceRegistry Load(string json)
    {
        JsonNode root;
        try
        {
            root = JsonNode.Parse(json) ?? throw new FormatException("the evidence registry was empty");
        }
        catch (JsonException error)
        {
            throw new FormatException("the evidence registry is not valid JSON", error);
        }

        if (root is not JsonObject registry)
        {
            throw new FormatException("the evidence registry must be a JSON object");
        }
        if ((string?)registry["schema"] != SchemaName)
        {
            throw new FormatException($"the evidence registry schema must be {SchemaName}");
        }
        if (registry["rows"] is not JsonArray rows)
        {
            throw new FormatException("the evidence registry must carry a rows array");
        }

        var statuses = new Dictionary<string, EvidenceStatus>(StringComparer.Ordinal);
        foreach (var entry in rows)
        {
            if (entry is not JsonObject row)
            {
                continue;
            }
            var key = (string?)row["key"] ?? throw new FormatException("an evidence row has no key");
            var status = ParseStatus((string?)row["status"]);
            statuses[key] = status;
        }
        if (statuses.Count == 0)
        {
            throw new FormatException("the evidence registry has no rows");
        }
        return new EvidenceRegistry(statuses);
    }

    public bool TryStatus(string key, out EvidenceStatus status) => _statuses.TryGetValue(key, out status);

    public EvidenceStatus StatusFor(string key) =>
        _statuses.TryGetValue(key, out var status)
            ? status
            : throw new KeyNotFoundException($"the evidence registry has no row for {key}");

    public bool Has(string key) => _statuses.ContainsKey(key);

    private static EvidenceStatus ParseStatus(string? text) => text switch
    {
        "PROVEN" => EvidenceStatus.Proven,
        "UNPROVEN" => EvidenceStatus.Unproven,
        "CONTRADICTED" => EvidenceStatus.Contradicted,
        "APPROXIMATED" => EvidenceStatus.Approximated,
        "ENGINE_POLICY" => EvidenceStatus.EnginePolicy,
        _ => throw new FormatException($"unknown evidence status {text}"),
    };
}

public enum GateOutcome
{
    Allowed,

    /// <summary>Allowed as a deliberate approximation, with the reliance counted.</summary>
    Approximated,

    /// <summary>Refused. The VM must not guess.</summary>
    Stopped,
}

public sealed record GateDecision(GateOutcome Outcome, string Key, EvidenceStatus Status, string Reason)
{
    public bool Allowed => Outcome != GateOutcome.Stopped;
}

/// <summary>
/// Decides whether the VM may run one semantic, and counts what it refused.
/// </summary>
/// <remarks>
/// PROVEN and ENGINE_POLICY may run. APPROXIMATED may run but every reliance is counted, because the
/// registry says Garden deliberately implements only part of the behavior. UNPROVEN and
/// CONTRADICTED stop, and the stop is counted under its key, which is what the coverage work reads.
/// </remarks>
public sealed class EvidenceGate
{
    private readonly EvidenceRegistry _registry;
    private readonly Dictionary<string, int> _stops = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _approximations = new(StringComparer.Ordinal);

    public EvidenceGate(EvidenceRegistry registry) => _registry = registry;

    public int StopCount { get; private set; }

    public int ApproximationCount { get; private set; }

    public IReadOnlyDictionary<string, int> Stops => _stops;

    public IReadOnlyDictionary<string, int> Approximations => _approximations;

    public GateDecision Gate(string key) => Gate(key, countApproximation: true);

    internal GateDecision Gate(string key, bool countApproximation)
    {
        if (!_registry.TryStatus(key, out var status))
        {
            StopCount++;
            Bump(_stops, key);
            return new GateDecision(GateOutcome.Stopped, key, EvidenceStatus.Unproven,
                "the evidence registry has no row for this key, so the VM fails closed");
        }

        switch (status)
        {
            case EvidenceStatus.Proven:
                return new GateDecision(GateOutcome.Allowed, key, status, "proven");
            case EvidenceStatus.EnginePolicy:
                return new GateDecision(GateOutcome.Allowed, key, status, "a deliberate Garden decision");
            case EvidenceStatus.Approximated:
                if (countApproximation)
                {
                    ApproximationCount++;
                    Bump(_approximations, key);
                }
                return new GateDecision(GateOutcome.Approximated, key, status,
                    "Garden implements this only in part and reports every reliance");
            default:
                StopCount++;
                Bump(_stops, key);
                return new GateDecision(GateOutcome.Stopped, key, status,
                    status == EvidenceStatus.Contradicted
                        ? "the measurement contradicts the hypothesis this would rely on"
                        : "the behavior is unproven, so the VM does not guess");
        }
    }

    private static void Bump(Dictionary<string, int> counters, string key) =>
        counters[key] = counters.TryGetValue(key, out var count) ? count + 1 : 1;
}
