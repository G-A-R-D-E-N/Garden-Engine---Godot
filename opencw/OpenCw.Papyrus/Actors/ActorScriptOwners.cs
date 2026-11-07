using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Actors;

/// <summary>How an actor came into the world.</summary>
public enum ActorOrigin
{
    Placed,
    Spawned,
}

/// <summary>One actor, with the scripts its base form and reference declare.</summary>
public sealed record ActorInput(
    ActorOrigin Origin,
    string BaseForm,
    string Discriminator,
    string? BaseScript,
    string? ReferenceScript);

public enum ActorState
{
    /// <summary>The actor has an owner and runs its script.</summary>
    Attached,

    /// <summary>The base/reference precedence is not proven, so no script was merged.</summary>
    UnresolvedPrecedence,

    /// <summary>Spawning is not proven, so the actor was not created.</summary>
    RefusedSpawn,
    RefusedInitialization,
}

public sealed record ActorOutcome(
    PapyrusOwnerId Owner,
    ActorOrigin Origin,
    ActorState State,
    string? Script,
    string Reason);

public sealed record ActorReport(IReadOnlyList<ActorOutcome> Actors, int Attached, int Unresolved, int Refused)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"actors={Actors.Count} attached={Attached} unresolved={Unresolved} refused={Refused}\n");
        foreach (var actor in Actors)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {actor.Owner.Key} {actor.Origin.ToString().ToLowerInvariant()} {actor.State.ToString().ToLowerInvariant()} script={actor.Script} {actor.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Gives placed and spawned actors independent owners and their own scripts.
/// </summary>
/// <remarks>
/// A placed actor is owned by its placed reference and a spawned actor by a dynamic reference, so two
/// actors of one base form are always distinct owners. Which of the base form's script and the
/// reference's script wins — and in what order they run — is not proven, so nothing is merged while
/// that is open: the actor is counted as unresolved rather than run with a guessed precedence. Spawning
/// is its own key as well, so the categories C4 refused stay refused here and are counted instead of
/// being created.
/// </remarks>
public sealed class ActorScriptOwners
{
    public const string PrecedenceKey = "actor.script.precedence";
    public const string SpawnKey = "actor.spawn";

    private readonly IScriptHost _host;
    private readonly EvidenceGate _gate;
    private readonly Dictionary<PapyrusOwnerId, ScriptInstance> _instances = new();

    public ActorScriptOwners(IScriptHost host, EvidenceGate gate)
    {
        _host = host;
        _gate = gate;
    }

    public int Attached { get; private set; }

    public int Unresolved { get; private set; }

    public int Refused { get; private set; }

    public ScriptInstance? InstanceOf(PapyrusOwnerId owner) =>
        _instances.TryGetValue(owner, out var instance) ? instance : null;

    public ActorReport Attach(IEnumerable<ActorInput> actors)
    {
        var ordered = actors
            .OrderBy(actor => actor.BaseForm, StringComparer.Ordinal)
            .ThenBy(actor => actor.Discriminator, StringComparer.Ordinal)
            .ToList();
        var outcomes = new List<ActorOutcome>(ordered.Count);
        var attached = 0;
        var unresolved = 0;
        var refused = 0;

        foreach (var actor in ordered)
        {
            var owner = actor.Origin == ActorOrigin.Placed
                ? PapyrusOwnerId.ForPlacedReference(actor.BaseForm, actor.Discriminator)
                : PapyrusOwnerId.ForDynamicReference(actor.BaseForm, actor.Discriminator);

            if (actor.Origin == ActorOrigin.Spawned && !_gate.Gate(SpawnKey).Allowed)
            {
                refused++;
                Refused++;
                outcomes.Add(new ActorOutcome(owner, actor.Origin, ActorState.RefusedSpawn, null,
                    "actor spawning is not proven, so the actor was not created"));
                continue;
            }

            if (!_gate.Gate(PrecedenceKey).Allowed)
            {
                unresolved++;
                Unresolved++;
                outcomes.Add(new ActorOutcome(owner, actor.Origin, ActorState.UnresolvedPrecedence, null,
                    "the base and reference script precedence is not proven, so no script was merged"));
                continue;
            }

            var script = Merge(actor);
            if (script is null)
            {
                unresolved++;
                Unresolved++;
                outcomes.Add(new ActorOutcome(owner, actor.Origin, ActorState.UnresolvedPrecedence, null,
                    "the actor declares no script"));
                continue;
            }

            var instance = _host.Attach(script, owner: owner);
            var initialization = _host.Initialize(instance);
            if (!initialization.Completed)
            {
                refused++;
                Refused++;
                outcomes.Add(new ActorOutcome(owner, actor.Origin, ActorState.RefusedInitialization, script,
                    $"initialization refused: {initialization.Key}: {initialization.Message ?? initialization.Reason.ToString()}"));
                continue;
            }
            _instances[owner] = instance;
            attached++;
            Attached++;
            outcomes.Add(new ActorOutcome(owner, actor.Origin, ActorState.Attached, script,
                "the actor runs its own script"));
        }

        return new ActorReport(outcomes, attached, unresolved, refused);
    }

    /// <summary>
    /// The reference script wins over the base script; only used once the precedence is proven.
    /// </summary>
    private static string? Merge(ActorInput actor) =>
        string.IsNullOrWhiteSpace(actor.ReferenceScript) ? actor.BaseScript : actor.ReferenceScript;
}
