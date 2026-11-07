using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;

namespace OpenCommonwealth.Services.Papyrus.Vm;

/// <summary>
/// The one script host the runtime uses.
/// </summary>
/// <remarks>
/// The host owns the library, the VM, the attached instances and the native target contract. It
/// enforces the OnInit barrier: properties are supplied at attach time, OnInit runs once, and no later
/// event may run before it completes. An event that arrives while OnInit is running is refused by
/// default and may only be queued when the caller accepts the counted approximation the registry
/// records. A failed OnInit leaves the script failed closed. Native calls resolve through an explicit
/// target contract and are refused rather than guessed.
/// </remarks>
public sealed class ScriptHost : INativeTarget, IScriptHost
{
    private readonly ScriptLibrary _library;
    private readonly EvidenceGate _gate;
    private readonly INativeTarget? _natives;
    private readonly OnInitArrivalPolicy _onInitArrivalPolicy;
    private readonly List<ScriptInstance> _instances = new();
    private readonly Dictionary<NativeId, NativeRegistration> _registrations = new();
    private ScriptScheduler? _scheduler;
    private ScriptScheduler Scheduler => _scheduler ??= new ScriptScheduler(this);
    public EvidenceGate Gate => _gate;

    public ScriptHost(
        ScriptLibrary library,
        EvidenceGate gate,
        INativeTarget? natives = null,
        OnInitArrivalPolicy onInitArrivalPolicy = OnInitArrivalPolicy.Refuse,
        int budget = PapyrusVm.DefaultBudget)
    {
        _library = library;
        _gate = gate;
        _natives = natives;
        _onInitArrivalPolicy = onInitArrivalPolicy;
        Vm = new PapyrusVm(library, gate, this, budget: budget);
    }

    public PapyrusVm Vm { get; }

    /// <summary>Optional per-script overrides, consulted at bytecode entry points only.</summary>
    public ScriptOverrideRegistry? Overrides { get; set; }

    public IReadOnlyList<ScriptInstance> Instances => _instances;

    public int RefusedEvents { get; private set; }

    public int QueuedDuringOnInit { get; private set; }

    public int NativeTargetRefusals { get; private set; }

    public int ParkedEvents => Scheduler.ParkedEvents;

    public int ReadyCount => Scheduler.ReadyCount;

    public int WaitingCount => Scheduler.WaitingCount;

    public int WaitingEvents => Scheduler.WaitingEvents;

    public int RefusedCompletions => Scheduler.RefusedCompletions;

    public string? LastCompletionRefusal => Scheduler.LastCompletionRefusal;

    public bool CompleteLatent(long token, PapyrusValue value) => Scheduler.CompleteLatent(token, value);

    public IReadOnlyList<Save.SavedThread> CaptureWaitingThreads(Func<ScriptInstance, string> ownerKey,
        Func<PapyrusScript, string> fingerprint) => Scheduler.CaptureWaitingThreads(ownerKey, fingerprint);

    public bool HasScript(string name) => _library.TryGet(name, out _);

    public bool HasInvalidParentChain(string name)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (_library.TryGet(name, out var script))
        {
            if (!visited.Add(script.Name)) return true;
            if (string.IsNullOrEmpty(script.Parent) || string.Equals(script.Parent, "ScriptObject", StringComparison.OrdinalIgnoreCase)) return false;
            name = script.Parent;
        }
        return true;
    }

    public void RegisterNative(NativeId native, Func<IReadOnlyList<PapyrusValue>, PapyrusValue> body,
        NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) =>
        RegisterNative(native, (_, arguments) => body(arguments), kind, requiredScript);

    public void RegisterNative(NativeId native,
        Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> body,
        NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null)
    {
        _registrations[native] = new NativeRegistration(body, kind, requiredScript);
    }

    public bool TryRegisterNative(NativeId native, Func<IReadOnlyList<PapyrusValue>, PapyrusValue> body,
        NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) =>
        TryRegisterNative(native, (_, arguments) => body(arguments), kind, requiredScript);

    public bool TryRegisterNative(NativeId native,
        Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> body,
        NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) =>
        _registrations.TryAdd(native, new NativeRegistration(body, kind, requiredScript));

    public ScriptInstance Attach(string scriptName, IReadOnlyDictionary<string, PapyrusValue>? properties = null,
        PapyrusOwnerId? owner = null)
    {
        var instance = new ScriptInstance(_library.Get(scriptName),
            properties ?? new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase), owner);
        _instances.Add(instance);
        return instance;
    }

    /// <summary>Runs OnInit once, behind the barrier, and leaves a failure closed.</summary>
    public VmResult Initialize(ScriptInstance instance)
    {
        try
        {
            var decision = _gate.Gate(EvidenceKeys.OnInitBarrier);
            if (!decision.Allowed)
            {
                return new VmResult(VmStopReason.GateStopped, PapyrusValue.None, decision.Key, decision.Reason, 0);
            }

            if (instance.Lifecycle == ScriptLifecycle.Ready)
            {
                return new VmResult(VmStopReason.Completed, PapyrusValue.None, null, null, 0);
            }

            if (instance.Lifecycle == ScriptLifecycle.Failed)
                return new VmResult(VmStopReason.ModelError, PapyrusValue.None, EvidenceKeys.OnInitBarrier,
                    $"the script {instance.Script.Name} failed OnInit and stays closed: {instance.Failure}", 0);

            if (instance.Lifecycle == ScriptLifecycle.Initializing)
                return new VmResult(VmStopReason.ModelError, PapyrusValue.None, EvidenceKeys.OnInitBarrier,
                    "initialization is already running", 0);

            instance.Lifecycle = ScriptLifecycle.Initializing;
            var overridden = Overrides?.TryInvoke(instance, "OnInit", Array.Empty<PapyrusValue>());
            if (overridden is { Outcome: OverrideOutcome.Handled })
            {
                instance.Lifecycle = ScriptLifecycle.Ready;
                DrainQueued(instance);
                return new VmResult(VmStopReason.Completed, overridden.Value, ScriptOverrideRegistry.Key,
                    "a synchronous override handled OnInit", 0);
            }
            if (overridden is { Outcome: OverrideOutcome.Refused })
            {
                instance.Lifecycle = ScriptLifecycle.Failed;
                instance.Failure = overridden.Reason;
                RefusedEvents += instance.Queued.Count;
                instance.Queued.Clear();
                return new VmResult(VmStopReason.ModelError, PapyrusValue.None, ScriptOverrideRegistry.Key,
                    overridden.Reason, 0);
            }

            var onInit = instance.Script.FindFunction("OnInit");
            var result = onInit is null
                ? new VmResult(VmStopReason.Completed, PapyrusValue.None, null, null, 0)
                : Vm.Call(instance.Script, onInit, PapyrusValue.FromObject(instance), Array.Empty<PapyrusValue>());
            if (result.Completed)
            {
                instance.Lifecycle = ScriptLifecycle.Ready;
                DrainQueued(instance);
                return result;
            }

            instance.Lifecycle = ScriptLifecycle.Failed;
            instance.Failure = result.Key ?? result.Message ?? result.Reason.ToString();
            RefusedEvents += instance.Queued.Count;
            instance.Queued.Clear();
            return result;
        }
        catch (Exception failure)
        {
            instance.Lifecycle = ScriptLifecycle.Failed;
            instance.Failure = failure.Message;
            RefusedEvents += instance.Queued.Count;
            instance.Queued.Clear();
            return new VmResult(VmStopReason.ModelError, PapyrusValue.None, null, failure.Message, 0);
        }
    }

    public VmResult RunEvent(ScriptInstance instance, string functionName, params PapyrusValue[] arguments) =>
        Scheduler.RunEvent(instance, functionName, arguments);

    internal PapyrusExecution PrepareEvent(ScriptInstance instance, string functionName, PapyrusValue[] arguments)
    {
        PapyrusExecution Stopped(VmResult result) => new() { Result = result };
        try
        {
            if (instance.Detached)
            {
                RefusedEvents++;
                return Stopped(new VmResult(VmStopReason.ModelError, PapyrusValue.None, null,
                    $"the script {instance.Script.Name} is detached, so the event was refused", 0));
            }
            switch (instance.Lifecycle)
            {
                case ScriptLifecycle.Failed:
                    RefusedEvents++;
                    return Stopped(new VmResult(VmStopReason.ModelError, PapyrusValue.None, EvidenceKeys.OnInitBarrier,
                        $"the script {instance.Script.Name} failed OnInit and stays closed", 0));
                case ScriptLifecycle.NotInitialized:
                    RefusedEvents++;
                    return Stopped(new VmResult(VmStopReason.ModelError, PapyrusValue.None, EvidenceKeys.OnInitBarrier,
                        $"the script {instance.Script.Name} has not been initialized", 0));
                case ScriptLifecycle.Initializing:
                    return Stopped(QueueOrRefuse(instance, functionName, arguments));
                default:
                    var declared = instance.Script.FindFunction(functionName);
                    var overridden = Overrides?.TryInvoke(instance, functionName, arguments);
                    if (overridden is { Outcome: OverrideOutcome.Handled })
                    {
                        return Stopped(new VmResult(VmStopReason.Completed, overridden.Value, ScriptOverrideRegistry.Key,
                            "a synchronous override handled this call", 0));
                    }
                    if (overridden is { Outcome: OverrideOutcome.Refused })
                    {
                        RefusedEvents++;
                        return Stopped(new VmResult(VmStopReason.ModelError, PapyrusValue.None, ScriptOverrideRegistry.Key,
                            overridden.Reason, 0));
                    }
                    if (declared is null)
                    {
                        RefusedEvents++;
                        return Stopped(new VmResult(VmStopReason.ModelError, PapyrusValue.None, null,
                            $"the script {instance.Script.Name} has no event named {functionName}", 0));
                    }
                    return Vm.Prepare(instance.Script, declared, PapyrusValue.FromObject(instance), arguments);
            }
        }
        catch (Exception failure)
        {
            RefusedEvents++;
            return Stopped(new VmResult(VmStopReason.ModelError, PapyrusValue.None, null, failure.Message, 0));
        }
    }

    public void Enqueue(ScriptInstance instance, string functionName, params PapyrusValue[] arguments) =>
        Scheduler.Enqueue(instance, functionName, arguments);

    /// <summary>
    /// Runs ready events under a deterministic instruction budget. An event that cannot finish inside
    /// the budget is parked and counted, so a runaway script cannot starve the rest of the ready set.
    /// </summary>
    public int RunStep(int budget) => Scheduler.RunStep(budget).Spent;

    PapyrusValue INativeTarget.Invoke(NativeId native, NativeTargetKind kind, PapyrusValue target,
        IReadOnlyList<PapyrusValue> arguments)
    {
        var decision = _gate.Gate(EvidenceKeys.NativeTarget);
        if (!decision.Allowed)
        {
            throw new InvalidOperationException($"{decision.Key}: {decision.Reason}");
        }
        var name = native.ToString();
        if (Registration(native) is not { } found)
        {
            NativeTargetRefusals++;
            throw new InvalidOperationException($"no native named {name} is registered");
        }
        var (owner, registration) = found;

        if (registration.Kind == NativeTargetKind.EngineObject && kind == NativeTargetKind.ScriptInstance
            && target.Reference is ScriptInstance { Owner: { } identity })
        {
            var ownerDecision = _gate.Gate("owner.engine_identity");
            if (!ownerDecision.Allowed)
                throw new InvalidOperationException($"{ownerDecision.Key}: {ownerDecision.Reason}");
            kind = NativeTargetKind.EngineObject;
            target = PapyrusValue.FromObject(new EngineObjectReference(identity));
        }

        var refusal = RefuseTarget(name, kind, target, registration);
        if (refusal is not null)
        {
            NativeTargetRefusals++;
            throw new InvalidOperationException(refusal);
        }

        if (_natives is not null)
        {
            return _natives.Invoke(owner, kind, target, arguments);
        }
        return registration.Body(target, arguments);
    }

    PapyrusValue INativeTarget.GetProperty(PapyrusValue target, string name)
    {
        if (target.Reference is not ScriptInstance instance)
        {
            NativeTargetRefusals++;
            throw new InvalidOperationException("a property read needs an attached script instance");
        }
        if (!instance.Properties.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"the script {instance.Script.Name} has no property {name}");
        }
        return value;
    }

    void INativeTarget.SetProperty(PapyrusValue target, string name, PapyrusValue value)
    {
        NativeTargetRefusals++;
        if (target.Reference is not ScriptInstance)
        {
            throw new InvalidOperationException("a property write needs an attached script instance");
        }
        throw new InvalidOperationException("script property writes have no proven storage or handler semantics");
    }

    private (NativeId Owner, NativeRegistration Registration)? Registration(NativeId native)
    {
        var script = native.Script;
        for (var depth = 0; depth < PapyrusVm.DefaultFrameDepthLimit; ++depth)
        {
            var id = new NativeId(script, native.Function);
            if (_registrations.TryGetValue(id, out var registration))
            {
                return (id, registration);
            }
            if (!_library.TryGet(script, out var owner) || owner.Parent is null)
            {
                return null;
            }
            script = owner.Parent;
        }
        return null;
    }

    private string? RefuseTarget(string name, NativeTargetKind kind, PapyrusValue target,
        NativeRegistration registration)
    {
        if (registration.Kind == NativeTargetKind.ScriptInstance && target.Reference is EngineObjectReference)
            return $"the native {name} needs an attached script instance; the VM never guesses one from a bare reference";
        if (registration.Kind != kind)
        {
            return $"the native {name} is registered as {registration.Kind}, not {kind}";
        }
        switch (kind)
        {
            case NativeTargetKind.Static:
                return null;
            case NativeTargetKind.ScriptInstance:
                if (target.Reference is not ScriptInstance instance)
                {
                    return $"the native {name} needs an attached script instance; the VM never guesses one from a bare reference";
                }
                if (registration.RequiredScript is not null
                    && !string.Equals(registration.RequiredScript, instance.Script.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return $"the native {name} needs the script {registration.RequiredScript}";
                }
                return null;
            default:
                return target.Reference is EngineObjectReference
                    ? null
                    : $"the native {name} needs an engine identity, not an attached script";
        }
    }

    private void DrainQueued(ScriptInstance instance)
    {
        while (instance.Queued.Count > 0)
        {
            var queued = instance.Queued.Dequeue();
            Enqueue(instance, queued.Function, queued.Arguments);
        }
    }

    private VmResult QueueOrRefuse(ScriptInstance instance, string functionName, PapyrusValue[] arguments)
    {
        var decision = _gate.Gate(EvidenceKeys.EventDuringOnInit);
        if (decision.Outcome == GateOutcome.Approximated && _onInitArrivalPolicy == OnInitArrivalPolicy.QueueAsApproximation)
        {
            instance.Queued.Enqueue((functionName, arguments));
            QueuedDuringOnInit++;
            return new VmResult(VmStopReason.Completed, PapyrusValue.None, decision.Key, decision.Reason, 0);
        }

        RefusedEvents++;
        return new VmResult(VmStopReason.GateStopped, PapyrusValue.None, decision.Key,
            $"an event arrived during OnInit and the policy is {_onInitArrivalPolicy}: {decision.Reason}", 0);
    }

    private sealed record NativeRegistration(
        Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> Body,
        NativeTargetKind Kind,
        string? RequiredScript);
}
