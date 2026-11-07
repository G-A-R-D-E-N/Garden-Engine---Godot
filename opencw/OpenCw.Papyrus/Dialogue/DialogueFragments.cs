using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Conditions;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Dialogue;

/// <summary>One decoded topic-info line, as Radicle hands it over.</summary>
public sealed record DialogueLine(
    string Topic,
    string Info,
    string Script,
    string BeginFragment,
    string? EndFragment,
    IReadOnlyList<PapyrusValue> Arguments,
    IReadOnlyList<ConditionInput> Conditions);

public enum DialogueState
{
    Played,

    /// <summary>The fragment timing is not proven, so nothing ran.</summary>
    TimingUnproven,

    /// <summary>The line's conditions could not be decided, so it was not played.</summary>
    ConditionsIndeterminate,

    /// <summary>The dialogue natives are not proven, so the line was blocked.</summary>
    NativesBlocked,

    /// <summary>The fragment itself was refused by the host.</summary>
    Refused,
    Pending,
}

public sealed record DialogueOutcome(string Topic, string Info, DialogueState State, int FragmentsRun, string Reason);

public sealed record DialogueBlocker(string System, string Key, string Reason);

public sealed record DialogueReport(
    IReadOnlyList<DialogueOutcome> Lines,
    IReadOnlyList<DialogueBlocker> Blockers,
    int Played,
    int Blocked,
    int Pending = 0)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"lines={Lines.Count} played={Played} blocked={Blocked} pending={Pending} blockers={Blockers.Count}\n");
        foreach (var line in Lines)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {line.Topic}/{line.Info} {line.State.ToString().ToLowerInvariant()} fragments={line.FragmentsRun} {line.Reason}\n");
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
/// Runs topic-info fragments when the dialogue runtime plays a line.
/// </summary>
/// <remarks>
/// The timing of a topic-info fragment is its own key: while it is unproven nothing runs and the line is
/// counted, because when the begin and end fragments fire relative to the line is not known. The
/// arguments come from Radicle's INFO decode and are passed to the fragment verbatim, and a line whose
/// conditions cannot be decided is blocked rather than played. The dialogue natives are their own key as
/// well, so while they are unproven the line is blocked and the missing support is named.
/// </remarks>
public sealed class DialogueFragments
{
    public const string TimingKey = "dialogue.fragment.timing";
    public const string DispatchKey = "dialogue.fragment.dispatch";
    public const string NativesKey = "dialogue.natives";

    private readonly IScriptHost _host;
    private readonly ConditionEvaluator _conditions;
    private readonly EvidenceGate _gate;
    private readonly List<PapyrusValue> _seenArguments = new();

    public DialogueFragments(IScriptHost host, ConditionEvaluator conditions, EvidenceGate gate)
    {
        _host = host;
        _conditions = conditions;
        _gate = gate;
    }

    public int Played { get; private set; }

    public int Blocked { get; private set; }

    public int Pending { get; private set; }

    /// <summary>The arguments the last fragment received, for verification.</summary>
    public IReadOnlyList<PapyrusValue> SeenArguments => _seenArguments;

    public DialogueOutcome Play(DialogueLine line, ScriptInstance? instance)
    {
        if (!_gate.Gate(TimingKey).Allowed)
        {
            Blocked++;
            return new DialogueOutcome(line.Topic, line.Info, DialogueState.TimingUnproven, 0,
                "the fragment timing is not proven, so nothing ran");
        }
        if (!_gate.Gate(DispatchKey).Allowed)
        {
            Blocked++;
            return new DialogueOutcome(line.Topic, line.Info, DialogueState.TimingUnproven, 0,
                "the fragment dispatch is not proven, so nothing ran");
        }
        if (!_gate.Gate(NativesKey).Allowed)
        {
            Blocked++;
            return new DialogueOutcome(line.Topic, line.Info, DialogueState.NativesBlocked, 0,
                "the dialogue natives are not proven, so the line was blocked");
        }

        var condition = _conditions.Evaluate(line.Conditions);
        if (line.Conditions.Count > 0 && condition.Indeterminate)
        {
            Blocked++;
            return new DialogueOutcome(line.Topic, line.Info, DialogueState.ConditionsIndeterminate, 0,
                $"the line's conditions are indeterminate: {string.Join(", ", condition.UnsupportedFunctions)}");
        }
        if (line.Conditions.Count > 0 && condition.Value != true)
        {
            Blocked++;
            return new DialogueOutcome(line.Topic, line.Info, DialogueState.ConditionsIndeterminate, 0,
                "the line's conditions are decided false, so the line was not played");
        }

        if (instance is null)
        {
            Blocked++;
            return new DialogueOutcome(line.Topic, line.Info, DialogueState.Refused, 0,
                "the line has no script instance, so nothing ran");
        }

        var run = 0;
        var begin = _host.RunEvent(instance, line.BeginFragment, line.Arguments.ToArray());
        if (begin.Reason == VmStopReason.Waiting)
        {
            Pending++;
            return new DialogueOutcome(line.Topic, line.Info, DialogueState.Pending, run,
                $"{line.BeginFragment} latent wait: {begin.Message}; later fragments have no automatic continuation");
        }
        if (!begin.Completed)
        {
            Blocked++;
            return new DialogueOutcome(line.Topic, line.Info, DialogueState.Refused, run,
                begin.Message ?? begin.Reason.ToString());
        }
        run++;
        _seenArguments.Clear();
        _seenArguments.AddRange(line.Arguments);

        if (!string.IsNullOrWhiteSpace(line.EndFragment))
        {
            var end = _host.RunEvent(instance, line.EndFragment!, Array.Empty<PapyrusValue>());
            if (end.Reason == VmStopReason.Waiting)
            {
                Pending++;
                return new DialogueOutcome(line.Topic, line.Info, DialogueState.Pending, run,
                    $"{line.EndFragment} latent wait: {end.Message}; later fragments have no automatic continuation");
            }
            if (!end.Completed)
            {
                Blocked++;
                return new DialogueOutcome(line.Topic, line.Info, DialogueState.Refused, run,
                    end.Message ?? end.Reason.ToString());
            }
            run++;
        }

        Played++;
        return new DialogueOutcome(line.Topic, line.Info, DialogueState.Played, run,
            "the line's fragments ran with the decoded arguments");
    }

    /// <summary>The dialogue support that does not exist yet, named.</summary>
    public IReadOnlyList<DialogueBlocker> Blockers()
    {
        var blockers = new List<DialogueBlocker>();
        if (!_gate.Gate(TimingKey).Allowed)
        {
            blockers.Add(new DialogueBlocker("dialogue.fragment.timing", TimingKey,
                "when a topic-info fragment fires relative to the line is not proven"));
        }
        if (!_gate.Gate(NativesKey).Allowed)
        {
            blockers.Add(new DialogueBlocker("dialogue.natives", NativesKey,
                "the dialogue natives are not implemented"));
        }
        return blockers;
    }

    public DialogueReport Report(IEnumerable<DialogueOutcome> lines) =>
        new(lines.ToList(), Blockers(), Played, Blocked, Pending);
}
