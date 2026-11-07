using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Conditions;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Story;

/// <summary>The story event families Garden knows.</summary>
public enum StoryFamily
{
    /// <summary>Start a quest.</summary>
    QuestStart,

    /// <summary>Deliver a named event to a script.</summary>
    ScriptEvent,

    /// <summary>A family Garden does not implement.</summary>
    Unsupported,
}

/// <summary>One decoded story-manager node, as Radicle hands it over.</summary>
public sealed record StoryEvent(
    string Node,
    StoryFamily Family,
    string Quest,
    string? EventName,
    IReadOnlyList<ConditionInput> Conditions);

public enum StoryState
{
    Dispatched,

    /// <summary>The node's conditions are not decided, so nothing was dispatched.</summary>
    ConditionsIndeterminate,

    /// <summary>The node's conditions are decided false, so nothing was dispatched.</summary>
    ConditionsFalse,

    /// <summary>The family is not implemented.</summary>
    FamilyUnsupported,

    /// <summary>The dispatch key is not proven, so nothing was dispatched.</summary>
    Refused,
}

public sealed record StoryOutcome(string Node, StoryFamily Family, StoryState State, string Reason);

public sealed record StoryReport(IReadOnlyList<StoryOutcome> Events, int Dispatched, int Skipped, int Unsupported)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"events={Events.Count} dispatched={Dispatched} skipped={Skipped} unsupported={Unsupported}\n");
        foreach (var outcome in Events)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {outcome.Node} {outcome.Family.ToString().ToLowerInvariant()} {outcome.State.ToString().ToLowerInvariant()} {outcome.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Connects the proven story-manager paths to quest startup and script delivery.
/// </summary>
/// <remarks>
/// Every node is decided by its own condition list first: an indeterminate list dispatches nothing and
/// names the undecided functions, and a list decided false skips the node rather than firing it. A
/// supported node then either starts its quest through the callback the caller supplies or delivers its
/// event to the quest's owner through the C2 bridge. A family Garden does not implement is counted and
/// named, so an unsupported event family is never quietly dropped or fired as if it were supported.
/// </remarks>
public sealed class StoryManagerPaths
{
    public const string DispatchKey = "story.dispatch";
    public const string FamiliesKey = "story.event_families";

    private readonly ConditionEvaluator _conditions;
    private readonly PapyrusEventBridge _bridge;
    private readonly EvidenceGate _gate;
    private readonly Action<string>? _startQuest;
    private readonly List<string> _started = new();

    public StoryManagerPaths(
        ConditionEvaluator conditions,
        PapyrusEventBridge bridge,
        EvidenceGate gate,
        Action<string>? startQuest = null)
    {
        _conditions = conditions;
        _bridge = bridge;
        _gate = gate;
        _startQuest = startQuest;
    }

    public int Dispatched { get; private set; }

    public int Skipped { get; private set; }

    public int Unsupported { get; private set; }

    public IReadOnlyList<string> StartedQuests => _started;

    public StoryOutcome Dispatch(StoryEvent storyEvent, PapyrusOwnerId? owner)
    {
        if (!_gate.Gate(DispatchKey).Allowed)
        {
            Skipped++;
            return new StoryOutcome(storyEvent.Node, storyEvent.Family, StoryState.Refused,
                "the story dispatch is not proven, so nothing was dispatched");
        }

        var condition = _conditions.Evaluate(storyEvent.Conditions);
        if (storyEvent.Conditions.Count > 0 && condition.Indeterminate)
        {
            Skipped++;
            return new StoryOutcome(storyEvent.Node, storyEvent.Family, StoryState.ConditionsIndeterminate,
                $"the node's conditions are indeterminate: {string.Join(", ", condition.UnsupportedFunctions)}");
        }
        if (storyEvent.Conditions.Count > 0 && condition.Value != true)
        {
            Skipped++;
            return new StoryOutcome(storyEvent.Node, storyEvent.Family, StoryState.ConditionsFalse,
                "the node's conditions are decided false, so nothing was dispatched");
        }

        if (storyEvent.Family == StoryFamily.Unsupported || !_gate.Gate(FamiliesKey).Allowed)
        {
            Unsupported++;
            return new StoryOutcome(storyEvent.Node, storyEvent.Family, StoryState.FamilyUnsupported,
                "this story event family is not implemented, so nothing was dispatched");
        }

        if (storyEvent.Family == StoryFamily.QuestStart)
        {
            if (_startQuest is null)
            {
                Skipped++;
                return new StoryOutcome(storyEvent.Node, storyEvent.Family, StoryState.Refused,
                    "the quest-start callback is missing, so no quest was started");
            }
            _startQuest(storyEvent.Quest);
            _started.Add(storyEvent.Quest);
            Dispatched++;
            return new StoryOutcome(storyEvent.Node, storyEvent.Family, StoryState.Dispatched,
                $"the quest {storyEvent.Quest} was started through the story path");
        }

        if (owner is null)
        {
            Skipped++;
            return new StoryOutcome(storyEvent.Node, storyEvent.Family, StoryState.Refused,
                "the script event has no owner, so nothing was delivered");
        }

        var batch = _bridge.Send(new EngineEvent(storyEvent.EventName ?? storyEvent.Node,
            Array.Empty<PapyrusValue>(), owner));
        if (batch.Delivered > 0)
        {
            Dispatched++;
            return new StoryOutcome(storyEvent.Node, storyEvent.Family, StoryState.Dispatched,
                "the story event was delivered through the bridge");
        }

        Skipped++;
        return new StoryOutcome(storyEvent.Node, storyEvent.Family, StoryState.Refused,
            batch.Outcomes.FirstOrDefault()?.Reason ?? "the event was not delivered");
    }

    public StoryReport Report(IEnumerable<StoryOutcome> outcomes) =>
        new(outcomes.ToList(), Dispatched, Skipped, Unsupported);
}
