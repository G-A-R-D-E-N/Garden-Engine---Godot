using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Quest;

public enum QuestState
{
    NotStarted,
    Running,
    Stopped,
    Completed,
}

public enum QuestNative
{
    Start,
    Stop,
    Reset,
    CompleteQuest,
    SetActive,
    SetStage,
}

public sealed record NativeBinding(QuestNative Native, bool Bound, string Key, string Reason);

public sealed record NativeReport(IReadOnlyList<NativeBinding> Natives, int Bound, int Blocked);

public sealed record QueuedFragment(string Quest, int Stage, int LogEntry, string Script, string Function);

// Invariant: SetActive only moves the per-quest Active flag, never the run state, and an illegal transition is refused; SetStage only queues a stage's fragments (the first time it is done), the session runs them outside any native callback.
public sealed class QuestStateMachine
{
    public const string LifecycleKey = "quest.native.lifecycle";
    public const string SetActiveKey = "quest.native.set_active";

    private readonly EvidenceGate _gate;
    private readonly Dictionary<string, QuestState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _stages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<int>> _done = new(StringComparer.Ordinal);
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<QuestStartupFragment>> _fragments = new(StringComparer.Ordinal);
    private readonly List<QueuedFragment> _queued = new();

    private IReadOnlySet<string> _owners = new HashSet<string>(StringComparer.Ordinal);

    public QuestStateMachine(EvidenceGate gate) => _gate = gate;

    public int StageChanges { get; private set; }

    public bool IsKnown(string quest) => _owners.Contains(quest) || _stages.ContainsKey(quest);

    public QuestState StateOf(string quest) =>
        _states.TryGetValue(quest, out var state) ? state : QuestState.NotStarted;

    public bool IsActive(string quest) => _active.Contains(quest);

    public int StageOf(string quest) => _stages.TryGetValue(quest, out var stage) ? stage : 0;

    public bool StageDone(string quest, int stage) => _done.TryGetValue(quest, out var set) && set.Contains(stage);

    public void BindFragments(string quest, IReadOnlyList<QuestStartupFragment> fragments) => _fragments[quest] = fragments;

    public IReadOnlyList<QueuedFragment> TakeQueuedFragments()
    {
        var taken = _queued.ToArray();
        _queued.Clear();
        return taken;
    }

    public int QueuedFragments => _queued.Count;

    // Invariant: the caller counted the entries ahead of the ones it keeps, so only that many leading entries go.
    public void DropFirstQueued(int count) => _queued.RemoveRange(0, Math.Min(count, _queued.Count));

    public QuestStateSnapshot Capture() => new(_states.Keys.Concat(_stages.Keys).Concat(_done.Keys).Concat(_active)
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
        .Select(quest => new QuestStateEntry(quest,
            _states.TryGetValue(quest, out var state) ? state : null,
            _stages.TryGetValue(quest, out var stage) ? stage : null,
            _done.TryGetValue(quest, out var done) ? done.Order().ToArray() : Array.Empty<int>(),
            _active.Contains(quest)))
        .ToArray(), _queued.Count == 0 ? null : _queued.ToArray());

    // Invariant: restore is all-or-nothing (throws FormatException before any change), drops quests outside the bound owners and returns how many, skips the gate, runs no fragment (the saved queue is put back to run once, never queued again by SetStage because the stage stays done), and only moves StageChanges forward.
    public int Restore(QuestStateSnapshot snapshot)
    {
        snapshot.Validate();
        var kept = _owners.Count == 0 ? snapshot.Quests : snapshot.Quests.Where(entry => _owners.Contains(entry.Quest)).ToArray();
        _states.Clear();
        _stages.Clear();
        _done.Clear();
        _active.Clear();
        _queued.Clear();
        foreach (var entry in kept)
        {
            if (entry.Active) _active.Add(entry.Quest);
            if (entry.State is { } state) _states[entry.Quest] = state;
            if (entry.Stage is { } stage) _stages[entry.Quest] = stage;
            if (entry.Done.Count != 0) _done[entry.Quest] = entry.Done.ToHashSet();
        }
        foreach (var queued in snapshot.QueuedOrEmpty)
            if (kept.Any(entry => entry.Quest == queued.Quest)) _queued.Add(queued);
        StageChanges++;
        return snapshot.Quests.Count - kept.Count;
    }

    public NativeReport Bind(IScriptHost host, IEnumerable<string> quests)
    {
        var bindings = new List<NativeBinding>();
        var bound = 0;
        var blocked = 0;
        var owners = quests.ToHashSet(StringComparer.Ordinal);
        _owners = owners;

        foreach (var native in Enum.GetValues<QuestNative>().OrderBy(native => native.ToString(), StringComparer.Ordinal))
        {
            var key = native switch
            {
                QuestNative.SetActive => SetActiveKey,
                QuestNative.SetStage => QuestRuntime.FragmentTimingKey,
                _ => LifecycleKey,
            };
            var allowed = _gate.Gate(key).Allowed;
            if (allowed && !_gate.Gate("owner.engine_identity").Allowed)
            {
                key = "owner.engine_identity";
                allowed = false;
            }
            if (!allowed || owners.Count == 0)
            {
                blocked++;
                bindings.Add(new NativeBinding(native, false, key,
                    allowed ? "no known quest owners were supplied"
                        : "the key does not allow it, so the native is not bound"));
                continue;
            }

            var name = new NativeId("Quest", native.ToString());
            if (!host.TryRegisterNative(name, (receiver, arguments) => ApplyNative(native, receiver, arguments, owners),
                    NativeTargetKind.EngineObject, "Quest"))
            {
                blocked++;
                bindings.Add(new NativeBinding(native, false, key,
                    "the native already has an owner, so its registration was preserved"));
                continue;
            }
            if (native == QuestNative.SetStage)
                host.TryRegisterNative(new NativeId("Quest", "SetCurrentStageID"), (receiver, arguments) => ApplyNative(native, receiver, arguments, owners),
                    NativeTargetKind.EngineObject, "Quest");
            bound++;
            bindings.Add(new NativeBinding(native, true, key, "the key allows it"));
        }

        BindQueries(host, owners);
        return new NativeReport(bindings, bound, blocked);
    }

    public event Action<string>? QuestReset;

    public bool Apply(string quest, QuestNative native, int stage = 0, bool active = true)
    {
        if (native == QuestNative.SetActive)
        {
            if (!_gate.Gate(SetActiveKey).Allowed)
            {
                return false;
            }
            if (active) _active.Add(quest);
            else _active.Remove(quest);
            return true;
        }
        if (native == QuestNative.SetStage)
        {
            if (!_gate.Gate(QuestRuntime.FragmentTimingKey).Allowed)
            {
                return false;
            }
            _stages[quest] = stage;
            if (!_done.TryGetValue(quest, out var done)) _done[quest] = done = new HashSet<int>();
            if (done.Add(stage) && _fragments.TryGetValue(quest, out var bound))
                foreach (var fragment in bound.Where(entry => entry.Stage == stage).OrderBy(entry => entry.StageIndex))
                    _queued.Add(new QueuedFragment(quest, stage, fragment.StageIndex, fragment.Script, fragment.Function));
            StageChanges++;
            return true;
        }

        if (!_gate.Gate(LifecycleKey).Allowed)
        {
            return false;
        }
        var current = StateOf(quest);
        var next = native switch
        {
            QuestNative.Start when current is QuestState.NotStarted or QuestState.Stopped => QuestState.Running,
            QuestNative.Stop when current == QuestState.Running => QuestState.Stopped,
            QuestNative.Reset => QuestState.NotStarted,
            QuestNative.CompleteQuest when current == QuestState.Running => QuestState.Completed,
            _ => (QuestState?)null,
        };

        if (next is null)
        {
            return false;
        }

        _states[quest] = next.Value;
        if (native == QuestNative.Stop) _queued.RemoveAll(fragment => fragment.Quest == quest);
        if (native == QuestNative.Reset)
        {
            _stages.Remove(quest);
            _done.Remove(quest);
            _queued.RemoveAll(fragment => fragment.Quest == quest);
            StageChanges++;
            QuestReset?.Invoke(quest);
        }
        return true;
    }

    private static readonly string[] QueryNames =
    {
        "GetCurrentStageID", "IsActive", "IsCompleted", "IsRunning", "IsStageDone", "IsStarting", "IsStopped", "IsStopping",
    };

    private void BindQueries(IScriptHost host, IReadOnlySet<string> owners)
    {
        if (owners.Count == 0 || !_gate.Gate(LifecycleKey).Allowed || !_gate.Gate("owner.engine_identity").Allowed) return;
        foreach (var name in QueryNames)
            host.TryRegisterNative(new NativeId("Quest", name), (receiver, arguments) => Query(name, receiver, arguments, owners),
                NativeTargetKind.EngineObject, "Quest");
    }

    private PapyrusValue Query(string name, PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments, IReadOnlySet<string> owners)
    {
        var quest = QuestOf(receiver, owners);
        var wantsStage = name == "IsStageDone";
        if (arguments.Count != (wantsStage ? 1 : 0) || (wantsStage && arguments[0].Kind != PapyrusValueKind.Int))
            throw new InvalidOperationException($"Quest.{name} has the wrong arguments");
        var state = StateOf(quest);
        return name switch
        {
            "IsStageDone" => PapyrusValue.FromBool(StageDone(quest, (int)arguments[0].Integer)),
            "GetCurrentStageID" => PapyrusValue.FromInt(StageOf(quest)),
            "IsActive" => PapyrusValue.FromBool(IsActive(quest)),
            "IsCompleted" => PapyrusValue.FromBool(state == QuestState.Completed),
            "IsRunning" => PapyrusValue.FromBool(state == QuestState.Running),
            "IsStopped" => PapyrusValue.FromBool(state is QuestState.NotStarted or QuestState.Stopped),
            _ => PapyrusValue.FromBool(false),
        };
    }

    private static string QuestOf(PapyrusValue receiver, IReadOnlySet<string> owners)
    {
        if (receiver.Kind == PapyrusValueKind.Object && receiver.Reference is EngineObjectReference { Owner: null } bare && owners.Contains(bare.Identity)) return bare.Identity;
        if (receiver.Kind != PapyrusValueKind.Object || receiver.Reference is not EngineObjectReference reference
            || reference.Owner is not { Kind: PapyrusOwnerKind.Form } owner
            || string.IsNullOrWhiteSpace(owner.BaseFormKey) || reference.Identity != owner.Key
            || !owners.Contains(owner.BaseFormKey))
        {
            throw new InvalidOperationException("the native needs a resolved, known Quest form owner");
        }
        return owner.BaseFormKey;
    }

    private PapyrusValue ApplyNative(QuestNative native, PapyrusValue receiver,
        IReadOnlyList<PapyrusValue> arguments, IReadOnlySet<string> owners)
    {
        var quest = QuestOf(receiver, owners);
        if (native is not (QuestNative.SetActive or QuestNative.SetStage) && arguments.Count != 0)
        {
            throw new InvalidOperationException($"Quest.{native} needs no arguments");
        }
        if (native == QuestNative.SetStage && (arguments.Count != 1 || arguments[0].Kind != PapyrusValueKind.Int
            || arguments[0].Integer is < int.MinValue or > int.MaxValue))
        {
            throw new InvalidOperationException("Quest.SetStage needs one integer representable by the Garden stage model");
        }
        if (native == QuestNative.SetActive && (arguments.Count != 1 || arguments[0].Kind != PapyrusValueKind.Bool))
        {
            throw new InvalidOperationException("Quest.SetActive needs one bool");
        }
        var stage = native == QuestNative.SetStage ? (int)arguments[0].Integer : 0;
        var applied = Apply(quest, native, stage, native != QuestNative.SetActive || arguments[0].Flag);
        return native is QuestNative.Stop or QuestNative.Reset or QuestNative.CompleteQuest or QuestNative.SetActive
            ? PapyrusValue.None : PapyrusValue.FromBool(applied);
    }
}
