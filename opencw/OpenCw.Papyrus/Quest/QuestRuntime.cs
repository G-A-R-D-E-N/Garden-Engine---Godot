using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Quest;

/// <summary>
/// Converted quest data: what Radicle decoded, in Garden's own shape.
/// </summary>
/// <remarks>
/// The plugin is the record's own owner, taken from the plugin that defines the quest — or, for a SEQ
/// entry, from the plugin whose SEQ file listed it — and never derived from the quest key. The scripts
/// are the names the quest's own record attaches; a script name is a global asset name and is not an
/// owner, so the two identities are kept apart here.
/// </remarks>
public sealed record QuestStartupInput(
    string Quest,
    bool StartGameEnabled,
    IReadOnlyList<string> Scripts,
    int Fragments,
    int Aliases,
    string Plugin = "",
    IReadOnlyList<string>? Unsupported = null,
    QuestStartupMetadata? Metadata = null);

public enum QuestStartupState
{
    Ready,
    Blocked,
    Skipped,
}

public sealed record QuestStartupOutcome(string Quest, QuestStartupState State, string? Reason);

public sealed record QuestStartupPlan(
    IReadOnlyList<QuestStartupOutcome> Outcomes,
    IReadOnlyList<QuestStartupInput> Inputs,
    int Ready,
    int Blocked,
    int Skipped,
    IReadOnlyList<string> Reasons)
{
    public bool CanApply => Blocked == 0;

    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"quests={Outcomes.Count} ready={Ready} blocked={Blocked} skipped={Skipped}\n");
        foreach (var outcome in Outcomes)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {outcome.State.ToString().ToLowerInvariant()} {outcome.Quest} {outcome.Reason}\n");
        }
        return text.ToString();
    }
}

public sealed record QuestStartupResult(bool Applied, int Started, int AttachedScripts, IReadOnlyList<string> Reasons);

/// <summary>
/// The proof-gated quest startup chain.
/// </summary>
/// <remarks>
/// Startup preflights first and mutates only if the whole plan passes, so an unsupported
/// startup-stage, alias or fragment behavior blocks the run before anything is attached: a quest that
/// carries fragments, aliases or scripts whose runtime behavior is not proven is blocked rather than
/// started with a guess. Quest-owned scripts attach through the one session host, so a quest script
/// and a reference script share the same interpreter, and every counter — ready, blocked, skipped,
/// started and attached — is the exact number of decisions the gate made.
/// </remarks>
public sealed class QuestRuntime
{
    public const string StartupOrderingKey = "quest.runtime.startup_ordering";
    public const string FragmentTimingKey = "quest.runtime.fragment_dispatch_timing";
    public const string AliasFillKey = "quest.runtime.alias_fill_order";

    private readonly EvidenceGate _gate;
    private readonly List<string> _started = new();
    private readonly Dictionary<string, IReadOnlyList<ScriptInstance>> _instances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failed = new(StringComparer.Ordinal);

    public QuestRuntime(EvidenceGate gate) => _gate = gate;

    public int StartedQuests => _started.Count;

    public int BlockedQuests { get; private set; }

    public int SkippedQuests { get; private set; }

    public int AttachedScripts { get; private set; }

    public int Approximations { get; private set; }

    public IReadOnlyList<string> Started => _started;

    public IReadOnlyList<ScriptInstance> InstancesFor(string quest) =>
        _instances.TryGetValue(quest, out var instances) ? instances : Array.Empty<ScriptInstance>();

    public QuestStartupPlan Preflight(IEnumerable<QuestStartupInput> inputs)
    {
        var ordered = inputs.OrderBy(input => input.Quest, StringComparer.Ordinal).ToList();
        var outcomes = new List<QuestStartupOutcome>(ordered.Count);
        var reasons = new List<string>();
        var ready = 0;
        var blocked = 0;
        var skipped = 0;
        var duplicates = ordered.GroupBy(input => input.Quest, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var input in ordered)
        {
            var refusal = duplicates.Contains(input.Quest) ? "duplicate quest inputs are unsupported"
                : _failed.TryGetValue(input.Quest, out var failure) ? failure
                : input.Unsupported is { Count: > 0 } ? string.Join("; ", input.Unsupported) : null;
            if (_started.Contains(input.Quest) && refusal is null)
            {
                skipped++;
                outcomes.Add(new QuestStartupOutcome(input.Quest, QuestStartupState.Skipped, "already started"));
                continue;
            }
            if (!input.StartGameEnabled && refusal is null)
            {
                skipped++;
                outcomes.Add(new QuestStartupOutcome(input.Quest, QuestStartupState.Skipped,
                    "not start-game enabled"));
                continue;
            }

            refusal ??= Refusal(input);
            if (refusal is not null)
            {
                blocked++;
                outcomes.Add(new QuestStartupOutcome(input.Quest, QuestStartupState.Blocked, refusal));
                reasons.Add($"{input.Quest}: {refusal}");
                continue;
            }

            ready++;
            outcomes.Add(new QuestStartupOutcome(input.Quest, QuestStartupState.Ready, null));
        }

        return new QuestStartupPlan(outcomes, ordered, ready, blocked, skipped, reasons);
    }

    /// <summary>Applies a plan only when nothing is blocked, and attaches through the one host.</summary>
    public QuestStartupResult Apply(QuestStartupPlan plan, IScriptHost host) => Apply(plan.Inputs, host);

    private QuestStartupResult Apply(IEnumerable<QuestStartupInput> startup, IScriptHost host)
    {
        var plan = Preflight(startup);
        SkippedQuests += plan.Skipped;
        if (!plan.CanApply)
        {
            BlockedQuests += plan.Blocked;
            Approximations += plan.Outcomes.Count(outcome => outcome.State == QuestStartupState.Blocked
                && !_failed.ContainsKey(outcome.Quest));
            return new QuestStartupResult(false, 0, 0, plan.Reasons);
        }

        var attached = 0;
        var started = 0;
        var inputs = plan.Inputs.ToDictionary(input => input.Quest, StringComparer.Ordinal);
        var missing = plan.Outcomes.Where(outcome => outcome.State == QuestStartupState.Ready)
            .SelectMany(outcome => inputs[outcome.Quest].Scripts)
            .Where(script => !host.HasScript(script)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0)
        {
            BlockedQuests += plan.Outcomes.Count(outcome => outcome.State == QuestStartupState.Ready
                && inputs[outcome.Quest].Scripts.Any(script => !host.HasScript(script)));
            return new QuestStartupResult(false, 0, 0, missing.Select(script => $"missing script {script}").ToList());
        }
        var invalid = plan.Outcomes.Where(outcome => outcome.State == QuestStartupState.Ready
            && inputs[outcome.Quest].Scripts.Any(host.HasInvalidParentChain)).ToArray();
        if (invalid.Length > 0)
        {
            BlockedQuests += invalid.Length;
            return new QuestStartupResult(false, 0, 0,
                invalid.Select(outcome => $"{outcome.Quest}: missing or cyclic parent chain in a quest script").ToArray());
        }
        var prepared = new Dictionary<string, Dictionary<string, Dictionary<string, PapyrusValue>>>(StringComparer.Ordinal);
        var bindingRefusals = new List<string>();
        foreach (var outcome in plan.Outcomes.Where(outcome => outcome.State == QuestStartupState.Ready))
        {
            var refusal = QuestStartupBinding.Prepare(inputs[outcome.Quest], out var properties);
            if (refusal is not null) bindingRefusals.Add($"{outcome.Quest}: {refusal}");
            else prepared.Add(outcome.Quest, properties);
        }
        if (bindingRefusals.Count > 0)
        {
            BlockedQuests += bindingRefusals.Count;
            return new QuestStartupResult(false, 0, 0, bindingRefusals);
        }
        var initializing = plan.Outcomes.Where(outcome => outcome.State == QuestStartupState.Ready
            && inputs[outcome.Quest].Scripts.Count > 0).ToArray();
        if (initializing.Length > 0)
        {
            var decision = host.Gate.Gate(EvidenceKeys.OnInitBarrier, countApproximation: false);
            if (!decision.Allowed)
            {
                BlockedQuests += initializing.Length;
                return new QuestStartupResult(false, 0, 0, initializing.Select(outcome =>
                    $"{outcome.Quest}: {decision.Key}: {decision.Reason}").ToArray());
            }
        }
        foreach (var outcome in plan.Outcomes.Where(outcome => outcome.State == QuestStartupState.Ready))
        {
            var input = inputs[outcome.Quest];
            var instances = new List<ScriptInstance>();
            _instances.Add(input.Quest, instances);
            foreach (var script in input.Scripts)
            {
                var instance = host.Attach(script, prepared[input.Quest][script], PapyrusOwnerId.ForForm(input.Quest));
                instances.Add(instance);
                attached++;
                AttachedScripts++;
                var initialization = host.Initialize(instance);
                if (!initialization.Completed)
                {
                    var reason = $"{input.Quest} {script}: {initialization.Message ?? initialization.Key ?? initialization.Reason.ToString()}";
                    _failed.Add(input.Quest, reason);
                    BlockedQuests++;
                    return new QuestStartupResult(false, started, attached, new[] { reason });
                }
            }
            _started.Add(outcome.Quest);
            started++;
        }
        return new QuestStartupResult(true, started, attached, Array.Empty<string>());
    }

    /// <summary>Preflights and, only if nothing is blocked, applies in one call.</summary>
    public QuestStartupResult StartNewGame(IEnumerable<QuestStartupInput> inputs, IScriptHost host) =>
        Apply(inputs, host);

    private string? Refusal(QuestStartupInput input)
    {
        if (input.Metadata?.ScriptProperties.Count > 0 && !_gate.Gate("vmad.value_binding").Allowed)
            return "quest property binding is not allowed by the evidence registry";
        if (input.Metadata?.ScriptProperties.Any(property => property.Type == 1) == true
            && !_gate.Gate("owner.engine_identity").Allowed)
            return "engine identities in quest properties are unproven";
        if (input.Scripts.Count > 0 && !_gate.Gate(StartupOrderingKey).Allowed)
        {
            return "the startup ordering of quest-owned scripts is unproven";
        }
        if (input.Fragments > 0 && !_gate.Gate(FragmentTimingKey).Allowed)
        {
            return "the dispatch timing of stage fragments is unproven";
        }
        if (input.Aliases > 0 && !_gate.Gate(AliasFillKey).Allowed)
        {
            return "the fill order of quest aliases is unproven";
        }
        return null;
    }
}
