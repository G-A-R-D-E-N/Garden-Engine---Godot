using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Owners;
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

public sealed record NativeReport(IReadOnlyList<NativeBinding> Natives, int Bound, int Blocked)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"natives={Natives.Count} bound={Bound} blocked={Blocked}\n");
        foreach (var native in Natives)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {native.Native} {(native.Bound ? "bound" : "blocked")} key={native.Key} {native.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// The quest lifecycle state model and its natives.
/// </summary>
/// <remarks>
/// Every targeted Quest native is either bound or blocked by a named key: `SetActive` is blocked
/// outright because whether it changes the running state is unproven, and `SetStage` is bound only when
/// the fragment dispatch timing is proven. A state transition therefore never depends on an unproven
/// timing rule — a refused stage leaves the state exactly as it was, and an illegal transition is
/// refused and counted rather than forced.
/// </remarks>
public sealed class QuestStateMachine
{
    public const string LifecycleKey = "quest.native.lifecycle";
    public const string SetActiveKey = "quest.native.set_active";

    private readonly EvidenceGate _gate;
    private readonly Dictionary<string, QuestState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _stages = new(StringComparer.Ordinal);

    public QuestStateMachine(EvidenceGate gate) => _gate = gate;

    public int Applied { get; private set; }

    public int Refused { get; private set; }

    public IReadOnlyDictionary<string, QuestState> States => _states;

    public QuestState StateOf(string quest) =>
        _states.TryGetValue(quest, out var state) ? state : QuestState.NotStarted;

    public int StageOf(string quest) => _stages.TryGetValue(quest, out var stage) ? stage : 0;

    public NativeReport Bind(IScriptHost host, IEnumerable<string> quests)
    {
        var bindings = new List<NativeBinding>();
        var bound = 0;
        var blocked = 0;
        var owners = quests.ToHashSet(StringComparer.Ordinal);

        foreach (var native in Enum.GetValues<QuestNative>().OrderBy(native => native.ToString(), StringComparer.Ordinal))
        {
            var key = native switch
            {
                QuestNative.SetActive => SetActiveKey,
                QuestNative.SetStage => QuestStageDispatch.TimingKey,
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
            bound++;
            bindings.Add(new NativeBinding(native, true, key, "the key allows it"));
        }

        return new NativeReport(bindings, bound, blocked);
    }

    public bool Apply(string quest, QuestNative native, int stage = 0)
    {
        if (native == QuestNative.SetActive)
        {
            Refused++;
            return false;
        }
        if (native == QuestNative.SetStage)
        {
            if (!_gate.Gate(QuestStageDispatch.TimingKey).Allowed)
            {
                Refused++;
                return false;
            }
            _stages[quest] = stage;
            Applied++;
            return true;
        }

        if (!_gate.Gate(LifecycleKey).Allowed)
        {
            Refused++;
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
            Refused++;
            return false;
        }

        _states[quest] = next.Value;
        Applied++;
        return true;
    }

    private PapyrusValue ApplyNative(QuestNative native, PapyrusValue receiver,
        IReadOnlyList<PapyrusValue> arguments, IReadOnlySet<string> owners)
    {
        if (receiver.Kind != PapyrusValueKind.Object || receiver.Reference is not EngineObjectReference reference
            || reference.Owner is not { Kind: PapyrusOwnerKind.Form } owner
            || string.IsNullOrWhiteSpace(owner.BaseFormKey) || reference.Identity != owner.Key
            || !owners.Contains(owner.BaseFormKey))
        {
            Refused++;
            throw new InvalidOperationException("the native needs a resolved, known Quest form owner");
        }
        if (native is not (QuestNative.SetActive or QuestNative.SetStage) && arguments.Count != 0)
        {
            Refused++;
            throw new InvalidOperationException($"Quest.{native} needs no arguments");
        }
        if (native == QuestNative.SetStage && (arguments.Count != 1 || arguments[0].Kind != PapyrusValueKind.Int
            || arguments[0].Integer is < int.MinValue or > int.MaxValue))
        {
            Refused++;
            throw new InvalidOperationException("Quest.SetStage needs one integer representable by the Garden stage model");
        }
        var stage = native == QuestNative.SetStage ? (int)arguments[0].Integer : 0;
        var applied = Apply(owner.BaseFormKey, native, stage);
        return native is QuestNative.Stop or QuestNative.Reset or QuestNative.CompleteQuest
            ? PapyrusValue.None : PapyrusValue.FromBool(applied);
    }
}
