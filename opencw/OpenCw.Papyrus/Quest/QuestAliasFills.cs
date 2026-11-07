using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Quest;

/// <summary>The alias fill kinds Garden knows.</summary>
public enum AliasFillKind
{
    ForcedReference,
    CreateReference,
    Location,

    /// <summary>Needs the actor runtime, which C11b owns.</summary>
    FindMatching,

    /// <summary>Needs the actor runtime, which C11b owns.</summary>
    UniqueActor,
}

/// <summary>One decoded alias, as Radicle hands it over.</summary>
public sealed record QuestAliasInput(string Quest, int AliasId, AliasFillKind Kind, string? Target, string? Location);

public enum AliasState
{
    Filled,

    /// <summary>The kind is open, so the alias was left empty and counted.</summary>
    Open,
}

public sealed record AliasOutcome(PapyrusOwnerId Owner, AliasFillKind Kind, AliasState State, string? Value, string Reason);

public sealed record AliasFillReport(IReadOnlyList<AliasOutcome> Aliases, int Filled, int Open, IReadOnlyList<string> OpenKinds)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"aliases={Aliases.Count} filled={Filled} open={Open}\n");
        foreach (var alias in Aliases)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {alias.Owner.Key} {alias.Kind.ToString().ToLowerInvariant()} {alias.State.ToString().ToLowerInvariant()} value={alias.Value} {alias.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Fills the quest aliases that do not need the actor runtime.
/// </summary>
/// <remarks>
/// Each fill kind has its own evidence key, so a kind that is not proven leaves its alias empty and
/// counts it rather than guessing a target. Forced-reference, create-reference and location fills work
/// without the actor runtime: a forced reference binds the reference the quest declares, a created
/// reference allocates a fresh dynamic owner so two creations of one base stay distinct, and a location
/// records the location's form key. Find-matching and unique-actor stay explicitly open for C11b and
/// are never filled here.
/// </remarks>
public sealed class QuestAliasFills
{
    public const string KeyPrefix = "quest.alias.fill.";
    public const string ActorKey = "quest.alias.fill.actors";

    private readonly EvidenceGate _gate;
    private readonly PapyrusOwnerRegistry _references;

    public QuestAliasFills(EvidenceGate gate, PapyrusOwnerRegistry? references = null)
    {
        _gate = gate;
        _references = references ?? new PapyrusOwnerRegistry();
    }

    public int Filled { get; private set; }

    public int Open { get; private set; }

    public PapyrusOwnerRegistry References => _references;

    public AliasFillReport Fill(IEnumerable<QuestAliasInput> aliases)
    {
        var ordered = aliases
            .OrderBy(alias => alias.Quest, StringComparer.Ordinal)
            .ThenBy(alias => alias.AliasId)
            .ToList();
        var outcomes = new List<AliasOutcome>(ordered.Count);
        var openKinds = new List<string>();
        var filled = 0;
        var open = 0;

        foreach (var alias in ordered)
        {
            var owner = PapyrusOwnerId.ForQuestAlias(alias.Quest, alias.AliasId);
            var key = Key(alias.Kind);
            if (!_gate.Gate(key).Allowed)
            {
                open++;
                Open++;
                var kind = alias.Kind.ToString();
                if (!openKinds.Contains(kind, StringComparer.Ordinal))
                {
                    openKinds.Add(kind);
                }
                outcomes.Add(new AliasOutcome(owner, alias.Kind, AliasState.Open, null,
                    $"the {kind} fill is not proven, so the alias was left empty and counted"));
                continue;
            }

            var value = alias.Kind switch
            {
                AliasFillKind.ForcedReference => Require(alias.Target, "the forced reference"),
                AliasFillKind.CreateReference => _references.AllocateRuntimeInstance(Require(alias.Target, "the base form")).Key,
                AliasFillKind.Location => Require(alias.Location, "the location"),
                _ => throw new InvalidOperationException($"the {alias.Kind} fill needs the actor runtime"),
            };
            filled++;
            Filled++;
            outcomes.Add(new AliasOutcome(owner, alias.Kind, AliasState.Filled, value,
                "the fill is proven and the alias was filled"));
        }

        return new AliasFillReport(outcomes, filled, open, openKinds);
    }

    public static string Key(AliasFillKind kind) => kind switch
    {
        AliasFillKind.ForcedReference => KeyPrefix + "forced_reference",
        AliasFillKind.CreateReference => KeyPrefix + "create_reference",
        AliasFillKind.Location => KeyPrefix + "location",
        _ => ActorKey,
    };

    private static string Require(string? value, string what) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"a proven fill still needs {what}")
            : value;
}
