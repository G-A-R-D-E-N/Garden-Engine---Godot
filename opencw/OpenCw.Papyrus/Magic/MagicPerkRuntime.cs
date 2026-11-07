using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Magic;

public enum MagicState
{
    Applied,

    /// <summary>The operation's key is not proven, so nothing changed.</summary>
    Refused,

    /// <summary>The system that would run this fragment does not exist yet.</summary>
    Blocked,
    Pending,
}

public sealed record MagicOutcome(PapyrusOwnerId Owner, string Operation, MagicState State, string Reason);

public sealed record MagicBlocker(string System, string Key, string Reason);

public sealed record MagicReport(
    IReadOnlyList<MagicOutcome> Outcomes,
    IReadOnlyList<MagicBlocker> Blockers,
    int Applied,
    int Refused,
    int Blocked,
    int Pending = 0)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"operations={Outcomes.Count} applied={Applied} refused={Refused} blocked={Blocked} pending={Pending} blockers={Blockers.Count}\n");
        foreach (var outcome in Outcomes)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {outcome.Owner.Key} {outcome.Operation} {outcome.State.ToString().ToLowerInvariant()} {outcome.Reason}\n");
        }
        foreach (var blocker in Blockers)
        {
            text.Append(CultureInfo.InvariantCulture, $"  blocker {blocker.System} key={blocker.Key} {blocker.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Active magic effect owners, perk and package fragments, and their native adapters.
/// </summary>
/// <remarks>
/// Every applied effect gets its own owner built from the effect, the target and an instance number, so
/// two applications of one effect on one target are distinct and can carry their own scripts. Perk
/// fragments run on the shared host once their key allows it; a package fragment needs the AI system,
/// which does not exist, so it is blocked and the missing system is named in the blockers list rather
/// than silently skipped. An operation whose key is unproven changes nothing and is counted.
/// </remarks>
public sealed class MagicPerkRuntime
{
    public const string ApplyKey = "magic.native.apply";
    public const string DispelKey = "magic.native.dispel";
    public const string PerkKey = "magic.perk.add";
    public const string PerkFragmentKey = "magic.perk.fragments";
    public const string MagicSystemKey = "magic.system";
    public const string PackageSystemKey = "ai.package.system";

    private readonly IScriptHost _host;
    private readonly EvidenceGate _gate;
    private readonly List<PapyrusOwnerId> _effects = new();
    private readonly Dictionary<PapyrusOwnerId, HashSet<string>> _perks = new();
    private long _nextInstance = 1;

    public MagicPerkRuntime(IScriptHost host, EvidenceGate gate)
    {
        _host = host;
        _gate = gate;
    }

    public int Applied { get; private set; }

    public int Refused { get; private set; }

    public int Blocked { get; private set; }

    public int Pending { get; private set; }

    public IReadOnlyList<PapyrusOwnerId> ActiveEffects => _effects;

    /// <summary>The owner of one application of an effect on a target.</summary>
    public PapyrusOwnerId Apply(PapyrusOwnerId target, string effectForm)
    {
        if (!_gate.Gate(ApplyKey).Allowed)
        {
            Refused++;
            return PapyrusOwnerId.ForDynamicReference(effectForm, $"{target.Key}#unapplied");
        }

        var owner = PapyrusOwnerId.ForDynamicReference(effectForm,
            $"{target.Key}#{_nextInstance.ToString(CultureInfo.InvariantCulture)}");
        _nextInstance++;
        _effects.Add(owner);
        Applied++;
        return owner;
    }

    public bool Dispel(PapyrusOwnerId effect)
    {
        if (!_gate.Gate(DispelKey).Allowed || !_effects.Remove(effect))
        {
            Refused++;
            return false;
        }
        Applied++;
        return true;
    }

    public bool HasPerk(PapyrusOwnerId actor, string perk) =>
        _perks.TryGetValue(actor, out var perks) && perks.Contains(perk);

    public bool AddPerk(PapyrusOwnerId actor, string perk)
    {
        if (!_gate.Gate(PerkKey).Allowed)
        {
            Refused++;
            return false;
        }
        if (!_perks.TryGetValue(actor, out var perks))
        {
            perks = new HashSet<string>(StringComparer.Ordinal);
            _perks[actor] = perks;
        }
        if (!perks.Add(perk))
        {
            Refused++;
            return false;
        }
        Applied++;
        return true;
    }

    /// <summary>Runs a perk or package fragment on the shared host.</summary>
    public MagicOutcome RunFragment(PapyrusOwnerId owner, string fragment, ScriptInstance? instance, bool package)
    {
        if (package)
        {
            Blocked++;
            return new MagicOutcome(owner, fragment, MagicState.Blocked,
                "the package fragment needs the AI system, which Garden does not have yet");
        }

        if (!_gate.Gate(PerkFragmentKey).Allowed || instance is null)
        {
            Refused++;
            return new MagicOutcome(owner, fragment, MagicState.Refused,
                "the perk fragment path is not proven or has no instance, so nothing ran");
        }

        var result = _host.RunEvent(instance, fragment, Array.Empty<PapyrusValue>());
        if (result.Reason == VmStopReason.Waiting)
        {
            Pending++;
            return new MagicOutcome(owner, fragment, MagicState.Pending,
                $"latent wait: {result.Message}; later items have no automatic continuation");
        }
        if (result.Completed)
        {
            Applied++;
            return new MagicOutcome(owner, fragment, MagicState.Applied, "the perk fragment ran");
        }

        Refused++;
        return new MagicOutcome(owner, fragment, MagicState.Refused,
            result.Message ?? result.Reason.ToString());
    }

    /// <summary>The missing systems, named so nothing is silently skipped.</summary>
    public IReadOnlyList<MagicBlocker> Blockers()
    {
        var blockers = new List<MagicBlocker>();
        if (!_gate.Gate(MagicSystemKey).Allowed)
        {
            blockers.Add(new MagicBlocker("magic", MagicSystemKey,
                "Garden has no magic system, so effects are owned but not simulated"));
        }
        if (!_gate.Gate(PackageSystemKey).Allowed)
        {
            blockers.Add(new MagicBlocker("ai.package", PackageSystemKey,
                "Garden has no AI package system, so package fragments cannot run"));
        }
        return blockers;
    }

    public MagicReport Report(IEnumerable<MagicOutcome> outcomes) =>
        new(outcomes.ToList(), Blockers(), Applied, Refused, Blocked, Pending);
}
