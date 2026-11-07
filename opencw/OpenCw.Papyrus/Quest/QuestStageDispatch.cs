using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Quest;

/// <summary>One decoded stage fragment, as Radicle hands it over.</summary>
public sealed record StageFragment(int Stage, int Order, string Script, string Function, bool QuestOwned);

public sealed record StageDispatchInput(string Quest, string QuestScript, IReadOnlyList<StageFragment> Fragments);

public enum FragmentOutcome
{
    /// <summary>The quest-owned fragment ran.</summary>
    Ran,

    /// <summary>The fragment belongs to a quest alias, which C12a owns.</summary>
    Deferred,

    /// <summary>The stage was refused, so nothing ran.</summary>
    Refused,
    Pending,
}

public sealed record FragmentResult(int Stage, int Order, string Script, string Function, FragmentOutcome Outcome, string? Reason);

public sealed record StageDispatchReport(
    string Quest,
    int Stage,
    IReadOnlyList<FragmentResult> Results,
    int Ran,
    int Deferred,
    bool Applied,
    int Pending = 0)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"quest={Quest} stage={Stage} applied={Applied} ran={Ran} deferred={Deferred} pending={Pending} results={Results.Count}\n");
        foreach (var result in Results)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {result.Stage}/{result.Order} {result.Script}.{result.Function} {result.Outcome.ToString().ToLowerInvariant()} {result.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Runs stage fragments and the quest-owned half of Quest.OnStageSet.
/// </summary>
/// <remarks>
/// The whole dispatch is gated on the fragment timing: while it is unproven the stage is refused and
/// counted, so B9's refusal stands rather than being worked around. When it is proven, the quest-owned
/// fragments of the changed stage run on the quest's own script instance in fragment order, and the
/// stage is applied only if every one of them exists — a missing function refuses the stage and runs
/// nothing. A fragment owned by a quest alias is deferred and
/// counted; this task does not run it and does not claim that alias recipients work.
/// </remarks>
public sealed class QuestStageDispatch
{
    public const string TimingKey = "quest.runtime.fragment_dispatch_timing";

    private readonly IScriptHost _host;
    private readonly EvidenceGate _gate;

    public QuestStageDispatch(IScriptHost host, EvidenceGate gate)
    {
        _host = host;
        _gate = gate;
    }

    public int Ran { get; private set; }

    public int Deferred { get; private set; }

    public int RefusedStages { get; private set; }

    public int Pending { get; private set; }

    public StageDispatchReport Dispatch(StageDispatchInput quest, int stage, ScriptInstance? instance)
    {
        var fragments = quest.Fragments
            .Where(fragment => fragment.Stage == stage)
            .OrderBy(fragment => fragment.Order)
            .ToList();

        if (!_gate.Gate(TimingKey).Allowed)
        {
            RefusedStages++;
            var refused = fragments.Select(fragment => new FragmentResult(fragment.Stage, fragment.Order,
                fragment.Script, fragment.Function, FragmentOutcome.Refused,
                "the fragment dispatch timing is unproven, so the stage was not dispatched")).ToList();
            return new StageDispatchReport(quest.Quest, stage, refused, 0, 0, false);
        }

        if (instance is null)
        {
            RefusedStages++;
            var missing = fragments.Select(fragment => new FragmentResult(fragment.Stage, fragment.Order,
                fragment.Script, fragment.Function, FragmentOutcome.Refused,
                "the quest has no script instance, so nothing was dispatched")).ToList();
            return new StageDispatchReport(quest.Quest, stage, missing, 0, 0, false);
        }

        var questOwned = fragments.Where(fragment => fragment.QuestOwned).ToList();
        var absent = questOwned.FirstOrDefault(fragment => !instance.DefinesFunction(fragment.Function));
        if (absent is not null)
        {
            RefusedStages++;
            var blocked = fragments.Select(fragment => new FragmentResult(fragment.Stage, fragment.Order,
                fragment.Script, fragment.Function, FragmentOutcome.Refused,
                $"the quest script {instance.ScriptName} has no {absent.Function}, so the stage was refused whole")).ToList();
            return new StageDispatchReport(quest.Quest, stage, blocked, 0, 0, false);
        }

        var results = new List<FragmentResult>(fragments.Count);
        var ran = 0;
        var deferred = 0;
        foreach (var fragment in fragments)
        {
            if (!fragment.QuestOwned)
            {
                deferred++;
                Deferred++;
                results.Add(new FragmentResult(fragment.Stage, fragment.Order, fragment.Script, fragment.Function,
                    FragmentOutcome.Deferred, "the fragment belongs to a quest alias, which is deferred"));
                continue;
            }

            var result = _host.RunEvent(instance, fragment.Function, Array.Empty<PapyrusValue>());
            if (result.Completed)
            {
                ran++;
                Ran++;
                results.Add(new FragmentResult(fragment.Stage, fragment.Order, fragment.Script, fragment.Function,
                    FragmentOutcome.Ran, null));
            }
            else if (result.Reason == VmStopReason.Waiting)
            {
                Pending++;
                results.Add(new FragmentResult(fragment.Stage, fragment.Order, fragment.Script, fragment.Function,
                    FragmentOutcome.Pending, $"latent wait: {result.Message}; later fragments have no automatic continuation"));
                break;
            }
            else
            {
                results.Add(new FragmentResult(fragment.Stage, fragment.Order, fragment.Script, fragment.Function,
                    FragmentOutcome.Refused, result.Message ?? result.Reason.ToString()));
                break;
            }
        }

        var pending = results.Count(result => result.Outcome == FragmentOutcome.Pending);
        var hasRefusal = results.Any(result => result.Outcome == FragmentOutcome.Refused);
        var applied = !hasRefusal && pending == 0;
        if (hasRefusal)
        {
            RefusedStages++;
        }
        return new StageDispatchReport(quest.Quest, stage, results, ran, deferred, applied, pending);
    }
}
