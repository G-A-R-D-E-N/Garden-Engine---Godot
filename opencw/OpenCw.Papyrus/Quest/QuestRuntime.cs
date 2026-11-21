using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Host;
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
    QuestStartupMetadata? Metadata = null,
    IReadOnlyList<QuestStartupStage>? Stages = null);

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
    int Blocked,
    IReadOnlyList<string> Reasons);

public sealed record QuestStartupResult(bool Applied, int Started, int AttachedScripts, IReadOnlyList<string> Reasons,
    IReadOnlyList<QuestStartupOutcome> Blocked);

public sealed class QuestRuntime
{
    public const string StartupOrderingKey = "quest.runtime.startup_ordering";
    public const string FragmentTimingKey = "quest.runtime.fragment_dispatch_timing";
    public const string AliasFillKey = "quest.runtime.alias_fill_order";

    private readonly List<string> _started = new();
    private readonly Dictionary<string, IReadOnlyList<ScriptInstance>> _instances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lifecycleAttached = new(StringComparer.Ordinal);

    public QuestRuntime(EvidenceGate gate) { }

    public int StartedQuests => _started.Count;

    public int BlockedQuests { get; private set; }

    public int AttachedScripts { get; private set; }

    public IReadOnlyList<string> Started => _started;

    public bool IsLifecycle(string quest) => _lifecycleAttached.ContainsKey(quest);

    public IReadOnlyList<ScriptInstance> InstancesFor(string quest) =>
        _instances.TryGetValue(quest, out var instances) ? instances : Array.Empty<ScriptInstance>();

    public QuestStartupPlan Preflight(IEnumerable<QuestStartupInput> inputs)
    {
        var ordered = inputs.OrderBy(input => input.Quest, StringComparer.Ordinal).ToList();
        var outcomes = new List<QuestStartupOutcome>(ordered.Count);
        var reasons = new List<string>();
        var blocked = 0;
        var duplicates = ordered.GroupBy(input => input.Quest, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var input in ordered)
        {
            var refusal = duplicates.Contains(input.Quest) ? "duplicate quest inputs are unsupported"
                : _failed.TryGetValue(input.Quest, out var failure) ? failure
                : null;
            if (refusal is not null)
            {
                blocked++;
                outcomes.Add(new QuestStartupOutcome(input.Quest, QuestStartupState.Blocked, refusal));
                reasons.Add($"{input.Quest}: {refusal}");
                continue;
            }
            if (_started.Contains(input.Quest))
            {
                outcomes.Add(new QuestStartupOutcome(input.Quest, QuestStartupState.Skipped, "already started"));
                continue;
            }

            var unsupported = input.Unsupported is { Count: > 0 } ? string.Join("; ", input.Unsupported) : null;
            if (!input.StartGameEnabled)
            {
                outcomes.Add(new QuestStartupOutcome(input.Quest, QuestStartupState.Skipped,
                    unsupported is null ? "not start-game enabled" : $"not start-game enabled; unsupported: {unsupported}"));
                continue;
            }

            refusal = unsupported;
            if (refusal is not null)
            {
                blocked++;
                outcomes.Add(new QuestStartupOutcome(input.Quest, QuestStartupState.Blocked, refusal));
                reasons.Add($"{input.Quest}: {refusal}");
                continue;
            }

            outcomes.Add(new QuestStartupOutcome(input.Quest, QuestStartupState.Ready, null));
        }

        return new QuestStartupPlan(outcomes, ordered, blocked, reasons);
    }

    public QuestStartupResult Apply(QuestStartupPlan plan, IScriptHost host) => Apply(plan.Inputs, host);

    private QuestStartupResult Apply(IEnumerable<QuestStartupInput> startup, IScriptHost host)
    {
        var plan = Preflight(startup);
        var lifecycle = host as RadicleScriptHost;
        var blocked = plan.Outcomes.Where(outcome => outcome.State == QuestStartupState.Blocked).ToList();
        var stopFailures = new List<string>();
        var attached = 0;
        var started = 0;
        var inputs = plan.Inputs.GroupBy(input => input.Quest, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var outcome in plan.Outcomes.Where(outcome => outcome.State == QuestStartupState.Ready))
        {
            var input = inputs[outcome.Quest];
            var refusal = PreAttachRefusal(input, host, out var properties);
            if (refusal is not null)
            {
                blocked.Add(new QuestStartupOutcome(input.Quest, QuestStartupState.Blocked, refusal));
                continue;
            }
            var instances = new List<ScriptInstance>();
            var questAttached = 0;
            var viaLifecycle = false;
            if (lifecycle is not null && input.Scripts.All(lifecycle.ChainEndsInLoadedRoot)
                && QuestStartupBinding.TryLifecycleProperties(input, out var lifecycleProperties))
            {
                viaLifecycle = true;
                var start = lifecycle.StartQuest(input.Quest, input.Scripts, lifecycleProperties, input.Fragments,
                    input.Aliases, properties);
                instances.AddRange(start.Instances);
                questAttached = start.AttachedScripts;
                if (!start.Applied) refusal = start.Reason ?? "the quest lifecycle refused the start";
            }
            else
            {
                foreach (var script in input.Scripts)
                {
                    var instance = host.Attach(script, properties[script], PapyrusOwnerId.ForForm(input.Quest));
                    instances.Add(instance);
                    questAttached++;
                    var initialization = host.Initialize(instance);
                    if (initialization.Completed) continue;
                    refusal = $"{script}: {initialization.Message ?? initialization.Key ?? initialization.Reason.ToString()}";
                    break;
                }
            }
            attached += questAttached;
            if (refusal is not null)
            {
                _failed.Add(input.Quest, refusal);
                stopFailures.AddRange(RollBack(input.Quest, instances, host, viaLifecycle ? lifecycle : null));
                blocked.Add(new QuestStartupOutcome(input.Quest, QuestStartupState.Blocked, refusal));
                continue;
            }
            AttachedScripts += questAttached;
            if (viaLifecycle) _lifecycleAttached[input.Quest] = questAttached;
            _instances.Add(input.Quest, instances);
            _started.Add(input.Quest);
            started++;
        }
        BlockedQuests = blocked.Count;
        var reasons = blocked.Select(outcome => $"{outcome.Quest}: {outcome.Reason}").Concat(stopFailures).ToArray();
        return new QuestStartupResult(_started.Count > 0 || blocked.Count == 0, started, attached, reasons, blocked);
    }

    private static string? PreAttachRefusal(QuestStartupInput input, IScriptHost host,
        out Dictionary<string, Dictionary<string, PapyrusValue>> properties)
    {
        properties = new(StringComparer.OrdinalIgnoreCase);
        var missing = input.Scripts.Where(script => !host.HasScript(script)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0) return string.Join("; ", missing.Select(script => $"missing script {script}"));
        if (input.Scripts.Any(host.HasInvalidParentChain)) return "missing or cyclic parent chain in a quest script";
        return QuestStartupBinding.Prepare(input, out properties);
    }

    public QuestStartupResult StartLate(QuestStartupInput input, IScriptHost host)
    {
        var blockedBefore = BlockedQuests;
        var result = Apply(new[] { input with { StartGameEnabled = true } }, host);
        BlockedQuests = blockedBefore + result.Blocked.Count;
        return result;
    }

    public QuestStartupResult StartNewGame(IEnumerable<QuestStartupInput> inputs, IScriptHost host) =>
        Apply(inputs, host);

    // Requires: lifecycle is null for the direct path, whose instances this call created and so must discard from the host.
    private List<string> RollBack(string quest, List<ScriptInstance> instances, IScriptHost host, RadicleScriptHost? lifecycle)
    {
        var stopFailures = new List<string>();
        foreach (var instance in instances)
        {
            instance.Detached = true;
            if (lifecycle is not null) continue;
            try { host.Discard(instance); }
            catch (Exception discard) { stopFailures.Add($"{quest}: discard of {instance.ScriptName} failed: {discard.Message}"); }
        }
        _instances.Remove(quest);
        _started.Remove(quest);
        try { lifecycle?.StopQuest(quest); }
        catch (Exception stop) { stopFailures.Add($"{quest}: stop failed: {stop.Message}"); }
        return stopFailures;
    }
}
