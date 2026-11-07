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

public sealed record ActorBinding(
    PapyrusOwnerId Alias,
    AliasFillKind Kind,
    PapyrusOwnerId? Actor,
    bool Bound,
    string? Script,
    string Reason);

public sealed record UnresolvedAlias(string Name, AliasFillKind Kind, string Reason);

public sealed record ActorBindingReport(
    IReadOnlyList<ActorBinding> Bindings,
    IReadOnlyList<UnresolvedAlias> Unresolved,
    int Bound,
    int Unbound,
    int Attached)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"bindings={Bindings.Count} bound={Bound} unbound={Unbound} attached={Attached} unresolved-named={Unresolved.Count}\n");
        foreach (var binding in Bindings)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {binding.Alias.Key} {binding.Kind.ToString().ToLowerInvariant()} actor={binding.Actor?.Key} bound={binding.Bound} script={binding.Script} {binding.Reason}\n");
        }
        foreach (var unresolved in Unresolved)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  unresolved {unresolved.Name} {unresolved.Kind.ToString().ToLowerInvariant()} {unresolved.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Binds the aliases that C11b filled from actors, and names the ones it did not.
/// </summary>
/// <remarks>
/// The C12a machinery is reused for the actor-dependent fills: a filled alias gets its own script
/// instance on the shared host, is registered with the event bridge, and records the actor it is bound
/// to, so its script runs against that actor's owner. An alias C11b left unbound stays unbound here and
/// is named with the quest, the alias and the reason, so nothing is silently missing.
/// </remarks>
public sealed class QuestAliasActorBinding
{
    public const string BindingKey = "quest.alias.actor_binding";

    private readonly IScriptHost _host;
    private readonly PapyrusEventBridge _bridge;
    private readonly EvidenceGate _gate;
    private readonly Dictionary<PapyrusOwnerId, PapyrusOwnerId> _targets = new();
    private readonly Dictionary<PapyrusOwnerId, ScriptInstance> _instances = new();

    public QuestAliasActorBinding(IScriptHost host, PapyrusEventBridge bridge, EvidenceGate gate)
    {
        _host = host;
        _bridge = bridge;
        _gate = gate;
    }

    public int Bound { get; private set; }

    public int Unbound { get; private set; }

    public int Attached { get; private set; }

    public PapyrusOwnerId? ActorOf(PapyrusOwnerId alias) =>
        _targets.TryGetValue(alias, out var actor) ? actor : null;

    public ScriptInstance? InstanceOf(PapyrusOwnerId alias) =>
        _instances.TryGetValue(alias, out var instance) ? instance : null;

    public ActorBindingReport Bind(ActorAliasReport report, IEnumerable<QuestAliasScript> scripts)
    {
        var byOwner = scripts
            .GroupBy(script => PapyrusOwnerId.ForQuestAlias(script.Quest, script.AliasId))
            .ToDictionary(group => group.Key, group => group.First());
        var bindings = new List<ActorBinding>();
        var unresolved = new List<UnresolvedAlias>();
        var bound = 0;
        var unbound = 0;
        var attached = 0;

        foreach (var alias in report.Aliases.OrderBy(alias => alias.Owner.Key, StringComparer.Ordinal))
        {
            if (!alias.IsBound || alias.Target is not { } actor)
            {
                unbound++;
                Unbound++;
                unresolved.Add(new UnresolvedAlias(alias.Owner.Key, alias.Kind, alias.Reason));
                bindings.Add(new ActorBinding(alias.Owner, alias.Kind, null, false, null, alias.Reason));
                continue;
            }

            if (!_gate.Gate(BindingKey).Allowed)
            {
                unbound++;
                Unbound++;
                var reason = "the actor binding is not proven, so the alias stays unbound";
                unresolved.Add(new UnresolvedAlias(alias.Owner.Key, alias.Kind, reason));
                bindings.Add(new ActorBinding(alias.Owner, alias.Kind, actor, false, null, reason));
                continue;
            }

            _targets[alias.Owner] = actor;
            bound++;
            Bound++;

            var script = byOwner.TryGetValue(alias.Owner, out var declared) ? declared.Script : null;
            if (string.IsNullOrWhiteSpace(script))
            {
                bindings.Add(new ActorBinding(alias.Owner, alias.Kind, actor, true, null,
                    "the alias is bound to its actor but declares no script"));
                continue;
            }

            var values = new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase);
            string? refusal = declared!.Properties.Count > 0 && !_gate.Gate("vmad.value_binding").Allowed
                ? "unverified vmad.value_binding" : null;
            if (refusal is null && declared.Properties.Any(property => property.Type == "Object")
                && !_gate.Gate("owner.engine_identity").Allowed)
                refusal = "unverified owner.engine_identity";
            if (refusal is null && declared.Properties.Any(property => property.Type == "String" && property.Text is null))
                refusal = "a string property carried no value";
            if (refusal is null)
            {
                var properties = VmadValueBinder.Bind(alias.Owner, declared.Properties);
                if (properties.Unsupported != 0)
                    refusal = string.Join("; ", properties.UnsupportedShapes);
                else
                    foreach (var property in properties.Properties)
                        if (string.IsNullOrWhiteSpace(property.Name) || !values.TryAdd(property.Name, property.Value))
                        {
                            refusal = $"duplicate or empty property names on {script} are unsupported";
                            break;
                        }
            }
            if (refusal is not null)
            {
                bindings.Add(new ActorBinding(alias.Owner, alias.Kind, actor, true, script,
                    $"the alias is bound to its actor but script properties refused: {refusal}"));
                continue;
            }

            var instance = _host.Attach(script!, values);
            var initialization = _host.Initialize(instance);
            if (!initialization.Completed)
            {
                bindings.Add(new ActorBinding(alias.Owner, alias.Kind, actor, true, script,
                    $"the alias is bound to its actor but script initialization refused: {initialization.Key}: {initialization.Message ?? initialization.Reason.ToString()}"));
                continue;
            }
            _instances[alias.Owner] = instance;
            _bridge.Register(alias.Owner, instance);
            attached++;
            Attached++;
            bindings.Add(new ActorBinding(alias.Owner, alias.Kind, actor, true, script,
                "the alias runs its script against its actor"));
        }

        return new ActorBindingReport(bindings, unresolved, bound, unbound, attached);
    }
}
