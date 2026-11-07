using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Conditions;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Quest;

/// <summary>One candidate actor for an actor-dependent alias fill.</summary>
public sealed record ActorCandidate(PapyrusOwnerId Owner, string BaseForm, IReadOnlyList<ConditionInput> Conditions);

/// <summary>An actor-dependent alias fill request.</summary>
public sealed record ActorAliasRequest(
    string Quest,
    int AliasId,
    AliasFillKind Kind,
    string BaseForm,
    IReadOnlyList<ConditionInput> Conditions);

public enum ActorAliasState
{
    Filled,

    /// <summary>Nothing could be decided, so the alias holds no target at all.</summary>
    Unfilled,

    /// <summary>The actor fill kind is not proven, so nothing was attempted.</summary>
    Open,
}

public sealed record ActorAliasOutcome(
    PapyrusOwnerId Owner,
    AliasFillKind Kind,
    ActorAliasState State,
    PapyrusOwnerId? Target,
    string Reason)
{
    /// <summary>An unfilled alias is never bound, so no target reads as None.</summary>
    public bool IsBound => Target is not null;
}

public sealed record ActorAliasReport(
    IReadOnlyList<ActorAliasOutcome> Aliases,
    int Filled,
    int Unfilled,
    int Open,
    IReadOnlyList<string> OpenKinds)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"aliases={Aliases.Count} filled={Filled} unfilled={Unfilled} open={Open}\n");
        foreach (var alias in Aliases)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {alias.Owner.Key} {alias.Kind.ToString().ToLowerInvariant()} {alias.State.ToString().ToLowerInvariant()} target={alias.Target?.Key} {alias.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Fills the actor-dependent aliases from the C13 actor set and C10 conditions.
/// </summary>
/// <remarks>
/// A find-matching alias is filled only when its condition list is actually decided: an indeterminate
/// list leaves the alias unfilled and unbound, so it can never be read as bound to None. A unique-actor
/// alias is filled only when exactly one candidate matches; none or several is counted rather than
/// guessed. Both kinds are gated on the actor fill key, so while it is unproven nothing is attempted and
/// everything is counted.
/// </remarks>
public sealed class ActorAliasFills
{
    public const string ActorsKey = QuestAliasFills.ActorKey;

    private readonly ConditionEvaluator _conditions;
    private readonly EvidenceGate _gate;

    public ActorAliasFills(ConditionEvaluator conditions, EvidenceGate gate)
    {
        _conditions = conditions;
        _gate = gate;
    }

    public int Filled { get; private set; }

    public int Unfilled { get; private set; }

    public int Open { get; private set; }

    public ActorAliasReport Fill(IEnumerable<ActorAliasRequest> aliases, IEnumerable<ActorCandidate> candidates)
    {
        var pool = candidates.OrderBy(candidate => candidate.Owner.Key, StringComparer.Ordinal).ToList();
        var ordered = aliases
            .OrderBy(alias => alias.Quest, StringComparer.Ordinal)
            .ThenBy(alias => alias.AliasId)
            .ToList();
        var outcomes = new List<ActorAliasOutcome>(ordered.Count);
        var openKinds = new List<string>();
        var filled = 0;
        var unfilled = 0;
        var open = 0;

        foreach (var alias in ordered)
        {
            var owner = PapyrusOwnerId.ForQuestAlias(alias.Quest, alias.AliasId);
            if (!_gate.Gate(ActorsKey).Allowed)
            {
                open++;
                Open++;
                var kind = alias.Kind.ToString();
                if (!openKinds.Contains(kind, StringComparer.Ordinal))
                {
                    openKinds.Add(kind);
                }
                outcomes.Add(new ActorAliasOutcome(owner, alias.Kind, ActorAliasState.Open, null,
                    $"the {kind} fill is not proven, so the alias was left unfilled and counted"));
                continue;
            }

            var outcome = alias.Kind switch
            {
                AliasFillKind.FindMatching => FindMatching(owner, alias, pool),
                AliasFillKind.UniqueActor => UniqueActor(owner, alias, pool),
                _ => new ActorAliasOutcome(owner, alias.Kind, ActorAliasState.Unfilled, null,
                    "the fill kind does not depend on actors"),
            };
            outcomes.Add(outcome);
            if (outcome.IsBound)
            {
                filled++;
                Filled++;
            }
            else
            {
                unfilled++;
                Unfilled++;
            }
        }

        return new ActorAliasReport(outcomes, filled, unfilled, open, openKinds);
    }

    private ActorAliasOutcome FindMatching(PapyrusOwnerId owner, ActorAliasRequest alias,
        IReadOnlyList<ActorCandidate> pool)
    {
        foreach (var candidate in pool)
        {
            var report = _conditions.Evaluate(alias.Conditions.Count > 0 ? alias.Conditions : candidate.Conditions);
            if (report.Indeterminate)
            {
                return new ActorAliasOutcome(owner, alias.Kind, ActorAliasState.Unfilled, null,
                    $"the condition list is indeterminate, so the alias stays unbound: {string.Join(", ", report.UnsupportedFunctions)}");
            }
            if (report.Value == true)
            {
                return new ActorAliasOutcome(owner, alias.Kind, ActorAliasState.Filled, candidate.Owner,
                    $"the candidate {candidate.Owner.Key} matches the decided condition list");
            }
        }

        return new ActorAliasOutcome(owner, alias.Kind, ActorAliasState.Unfilled, null,
            "no candidate matched the decided condition list, so the alias stays unbound");
    }

    private static ActorAliasOutcome UniqueActor(PapyrusOwnerId owner, ActorAliasRequest alias,
        IReadOnlyList<ActorCandidate> pool)
    {
        var matching = pool.Where(candidate => string.Equals(candidate.BaseForm, alias.BaseForm, StringComparison.Ordinal)).ToList();
        if (matching.Count == 1)
        {
            return new ActorAliasOutcome(owner, alias.Kind, ActorAliasState.Filled, matching[0].Owner,
                $"exactly one actor has the base form {alias.BaseForm}");
        }
        if (matching.Count == 0)
        {
            return new ActorAliasOutcome(owner, alias.Kind, ActorAliasState.Unfilled, null,
                $"no actor has the base form {alias.BaseForm}, so the alias stays unbound");
        }
        return new ActorAliasOutcome(owner, alias.Kind, ActorAliasState.Unfilled, null,
            $"{matching.Count} actors have the base form {alias.BaseForm}, so the alias stays unbound rather than guessing one");
    }
}
