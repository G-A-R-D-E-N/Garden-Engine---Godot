using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vmad;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Quest;

/// <summary>One alias of a filled C11a report, with what its script needs.</summary>
public sealed record QuestAliasScript(
    string Quest,
    int AliasId,
    string? Script,
    IReadOnlyList<VmadValueShape> Properties,
    IReadOnlyList<int> Objectives);

public enum AliasAttachState
{
    Attached,

    /// <summary>The alias was not filled, so it has no owner to attach to.</summary>
    Skipped,

    Refused,
}

public sealed record AliasAttachOutcome(
    PapyrusOwnerId Owner,
    AliasAttachState State,
    int Properties,
    int Natives,
    int Objectives,
    string Reason);

public sealed record AliasStageOutcome(PapyrusOwnerId Owner, bool Delivered, string Reason);

public sealed record AliasRuntimeReport(
    IReadOnlyList<AliasAttachOutcome> Aliases,
    IReadOnlyList<AliasStageOutcome> Stages,
    int Attached,
    int Skipped,
    int Properties,
    int Natives,
    int Objectives,
    int StageDelivered,
    int StageRefused)
{
    public int Refused { get; init; }

    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"aliases={Aliases.Count} attached={Attached} skipped={Skipped} refused={Refused} properties={Properties} natives={Natives} objectives={Objectives} stage-delivered={StageDelivered} stage-refused={StageRefused}\n");
        foreach (var alias in Aliases)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {alias.Owner.Key} {alias.State.ToString().ToLowerInvariant()} properties={alias.Properties} natives={alias.Natives} objectives={alias.Objectives} {alias.Reason}\n");
        }
        foreach (var stage in Stages)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  stage {stage.Owner.Key} delivered={stage.Delivered} {stage.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Binds the alias scripts, properties, natives and objectives of filled C11a aliases.
/// </summary>
/// <remarks>
/// Only an alias that C11a actually filled gets an owner and a script: an alias left open has nothing to
/// attach to and is counted as skipped rather than given a guessed target. Properties bind through the
/// VMAD binder, so an unsupported shape stays explicit and owner-local, and the objective state lives
/// here, which makes this runtime authoritative for it. An alias receives the stage event only when it
/// registered for that event through the C2b registry; an alias that did not register receives nothing
/// and is counted, so no remote delivery is ever assumed.
/// </remarks>
public sealed class QuestAliasRuntime
{
    public const string RuntimeKey = "quest.alias.runtime";
    public const string StageEventName = "Quest.OnStageSet";

    private readonly IScriptHost _host;
    private readonly PapyrusEventBridge _bridge;
    private readonly ScriptRegistrationRegistry _registrations;
    private readonly EvidenceGate _gate;
    private readonly Dictionary<PapyrusOwnerId, ScriptInstance> _instances = new();
    private readonly Dictionary<PapyrusOwnerId, Dictionary<int, int>> _objectives = new();

    public QuestAliasRuntime(
        IScriptHost host,
        PapyrusEventBridge bridge,
        ScriptRegistrationRegistry registrations,
        EvidenceGate gate)
    {
        _host = host;
        _bridge = bridge;
        _registrations = registrations;
        _gate = gate;
    }

    public int Attached { get; private set; }

    public int Skipped { get; private set; }

    public int Refused { get; private set; }

    public int Properties { get; private set; }

    public int Natives { get; private set; }

    public int Objectives { get; private set; }

    public int StageDelivered { get; private set; }

    public int StageRefused { get; private set; }

    /// <summary>The authoritative objective state, per alias owner.</summary>
    public IReadOnlyDictionary<PapyrusOwnerId, Dictionary<int, int>> ObjectiveState => _objectives;

    public int ObjectiveOf(PapyrusOwnerId owner, int index) =>
        _objectives.TryGetValue(owner, out var objectives) && objectives.TryGetValue(index, out var state) ? state : -1;

    public AliasRuntimeReport Attach(AliasFillReport fills, IEnumerable<QuestAliasScript> scripts)
    {
        var attached = new List<AliasAttachOutcome>();
        var byOwner = scripts
            .GroupBy(script => PapyrusOwnerId.ForQuestAlias(script.Quest, script.AliasId))
            .ToDictionary(group => group.Key, group => group.First());
        var filled = fills.Aliases
            .Where(alias => alias.State == AliasState.Filled)
            .Select(alias => alias.Owner)
            .OrderBy(owner => owner.Key, StringComparer.Ordinal)
            .ToList();

        var properties = 0;
        var natives = 0;
        var objectives = 0;
        var skipped = 0;
        var refused = 0;

        foreach (var owner in filled)
        {
            if (!byOwner.TryGetValue(owner, out var script) || string.IsNullOrWhiteSpace(script.Script))
            {
                skipped++;
                Skipped++;
                attached.Add(new AliasAttachOutcome(owner, AliasAttachState.Skipped, 0, 0, 0,
                    "the alias has no script, so nothing was attached"));
                continue;
            }

            var values = new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase);
            string? refusal = !_gate.Gate(RuntimeKey).Allowed ? $"unverified {RuntimeKey}" : null;
            if (refusal is null && script.Properties.Count > 0 && !_gate.Gate("vmad.value_binding").Allowed)
                refusal = "unverified vmad.value_binding";
            if (refusal is null && script.Properties.Any(property => property.Type == "Object")
                && !_gate.Gate("owner.engine_identity").Allowed)
                refusal = "unverified owner.engine_identity";
            if (refusal is null && script.Properties.Any(property => property.Type == "String" && property.Text is null))
                refusal = "a string property carried no value";
            if (refusal is null)
            {
                var binding = VmadValueBinder.Bind(owner, script.Properties);
                if (binding.Unsupported != 0)
                    refusal = string.Join("; ", binding.UnsupportedShapes);
                else
                    foreach (var property in binding.Properties)
                        if (string.IsNullOrWhiteSpace(property.Name) || !values.TryAdd(property.Name, property.Value))
                        {
                            refusal = $"duplicate or empty property names on {script.Script} are unsupported";
                            break;
                        }
            }
            if (refusal is not null)
            {
                refused++;
                Refused++;
                attached.Add(new AliasAttachOutcome(owner, AliasAttachState.Refused, 0, 0, 0, refusal));
                continue;
            }

            var instance = _host.Attach(script.Script!, values);
            var initialization = _host.Initialize(instance);
            if (!initialization.Completed)
            {
                refused++;
                Refused++;
                attached.Add(new AliasAttachOutcome(owner, AliasAttachState.Refused, 0, 0, 0,
                    $"initialization refused: {initialization.Key}: {initialization.Message ?? initialization.Reason.ToString()}"));
                continue;
            }
            _instances[owner] = instance;
            _bridge.Register(owner, instance);

            var nativeCount = 0;
            foreach (var name in script.Objectives.Count > 0
                         ? new[] { new NativeId("Alias", $"SetObjective{owner.Discriminator}") }
                         : Array.Empty<NativeId>())
            {
                _host.RegisterNative(name, _ => PapyrusValue.FromBool(true), NativeTargetKind.Static, "Alias");
                nativeCount++;
            }

            var objectiveCount = 0;
            foreach (var index in script.Objectives.OrderBy(index => index))
            {
                SetObjective(owner, index, 1);
                objectiveCount++;
            }

            properties += values.Count;
            natives += nativeCount;
            objectives += objectiveCount;
            Attached++;
            attached.Add(new AliasAttachOutcome(owner, AliasAttachState.Attached, values.Count, nativeCount, objectiveCount,
                "the filled alias runs its own script"));
        }

        Properties += properties;
        Natives += natives;
        Objectives += objectives;

        return new AliasRuntimeReport(attached, Array.Empty<AliasStageOutcome>(), attached.Count - skipped - refused, skipped,
            properties, natives, objectives, 0, 0) { Refused = refused };
    }

    public void SetObjective(PapyrusOwnerId owner, int index, int state)
    {
        if (!_objectives.TryGetValue(owner, out var objectives))
        {
            objectives = new Dictionary<int, int>();
            _objectives[owner] = objectives;
        }
        objectives[index] = state;
    }

    /// <summary>
    /// Sends the stage event to the aliases that actually registered for it.
    /// </summary>
    public AliasRuntimeReport StageSet(IEnumerable<PapyrusOwnerId> owners, PapyrusOwnerId? source = null)
    {
        var outcomes = new List<AliasStageOutcome>();
        var delivered = 0;
        var refused = 0;

        foreach (var owner in owners.OrderBy(owner => owner.Key, StringComparer.Ordinal))
        {
            var registered = source is { } sender && !string.IsNullOrWhiteSpace(sender.BaseFormKey)
                && _registrations.MatchRemoteEvent(sender, StageEventName).Any(entry => entry.Owner == owner);
            if (!registered)
            {
                refused++;
                StageRefused++;
                outcomes.Add(new AliasStageOutcome(owner, false,
                    "the alias did not register for the supplied remote source and stage event, so it received nothing"));
                continue;
            }

            var batch = _bridge.Send(new EngineEvent(StageEventName, Array.Empty<PapyrusValue>(), owner));
            if (batch.Delivered > 0)
            {
                delivered++;
                StageDelivered++;
                outcomes.Add(new AliasStageOutcome(owner, true, "the registered alias received the stage event"));
            }
            else
            {
                refused++;
                StageRefused++;
                outcomes.Add(new AliasStageOutcome(owner, false,
                    batch.Outcomes.FirstOrDefault()?.Result.ToString() ?? "the event was not delivered"));
            }
        }

        return new AliasRuntimeReport(Array.Empty<AliasAttachOutcome>(), outcomes, Attached, Skipped,
            Properties, Natives, Objectives, delivered, refused) { Refused = Refused };
    }
}
