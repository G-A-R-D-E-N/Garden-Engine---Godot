#if RADICLE_HOST_AVAILABLE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using RNative = Radicle.Interop.Native;

namespace OpenCommonwealth.Services.Papyrus.Host;

public sealed class RadicleScriptHost : IScriptHost, IDisposable
{
    private const int MaxRounds = 9;
    private const int MaxParentDepth = PapyrusVm.DefaultFrameDepthLimit;
    private const int MaxPublishAttempts = 3;

    private readonly EvidenceGate _gate;
    private readonly HostObjectTable _table = new();
    private readonly RadicleValueAdapter _adapter;
    private readonly RadiclePapyrusRuntime _runtime;
    private readonly int _stepBudget;
    private readonly List<ScriptInstance> _instances = new();
    private readonly List<ulong> _orphans = new();
    private readonly Dictionary<NativeId, NativeRegistration> _registrations = new();
    private readonly Dictionary<NativeId, PendingNative> _unpublished = new();
    private readonly Dictionary<string, string> _parents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, ulong> _latentTokens = new();
    private readonly HashSet<long> _usedTokens = new();
    private ScriptOverrideRegistry? _overrides;
    private int _callbackDepth;
    private int _anonymousOwners;
    private bool _disposed;

    public RadicleScriptHost(EvidenceGate gate, IEnumerable<byte[]> pex,
        OnInitArrivalPolicy policy = OnInitArrivalPolicy.Refuse, int stepBudget = PapyrusVm.DefaultBudget)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(pex);
        _gate = gate;
        _stepBudget = stepBudget;
        _adapter = new RadicleValueAdapter(_table);
        _runtime = new RadiclePapyrusRuntime(new PapyrusHostCallbacks(
            Gate: key => gate.Gate(key).Allowed,
            Override: OverrideFromRuntime));
        try
        {
            foreach (var bytes in pex)
            {
                _runtime.AddPex(bytes);
                RecordParents(bytes);
            }
            _runtime.SetEngineCallbacks(new PapyrusEngineHandlers(DispatchNative));
            _runtime.SetMaxParks(ScriptScheduler.DefaultMaxParksPerEvent);
            _runtime.SetOninitPolicy(policy == OnInitArrivalPolicy.QueueAsApproximation);
        }
        catch
        {
            _runtime.Dispose();
            throw;
        }
    }

    public EvidenceGate Gate => _gate;

    public ScriptOverrideRegistry? Overrides
    {
        get => _overrides;
        set
        {
            if (_callbackDepth > 0)
                throw new InvalidOperationException("the override registry cannot change inside a native callback");
            if (value is not null)
                foreach (var (script, function, parameterCount) in value.Entries)
                    _runtime.RegisterOverride(script, function, parameterCount);
            if (_overrides is not null) _overrides.Registered -= OnOverrideRegistered;
            _overrides = value;
            if (value is not null) value.Registered += OnOverrideRegistered;
        }
    }

    public IReadOnlyList<ScriptInstance> Instances => _instances;

    public int WaitingCount => checked((int)_runtime.GetStats().Waiting);

    public int RefusedEvents { get; private set; }

    public int NativeTargetRefusals { get; private set; }

    public string? LastNativeFailure { get; private set; }

    public string? LastRegistrationFailure { get; private set; }

    public ScriptInstance Attach(string scriptName, IReadOnlyDictionary<string, PapyrusValue>? properties = null,
        PapyrusOwnerId? owner = null)
    {
        if (_callbackDepth > 0)
            throw new InvalidOperationException("a script cannot attach inside a native callback");
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptName);
        if (owner is { } checkedOwner && string.IsNullOrWhiteSpace(checkedOwner.BaseFormKey))
            throw new ArgumentException("a script owner needs a base form identity", nameof(owner));
        if (!_runtime.HasScript(scriptName))
            throw new KeyNotFoundException($"the library has no script named {scriptName}");
        IReadOnlyDictionary<string, PapyrusValue> values =
            properties ?? new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase);
        var radicleProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in values)
            radicleProperties[property.Key] = _adapter.ToRadicle(property.Value);
        var identity = owner is { } known
            ? RadicleValueAdapter.OwnerMap(known)
            : new PapyrusOwner(RNative.PapyrusOwnerKind.RuntimeInstance, "anon:" + scriptName,
                (++_anonymousOwners).ToString(CultureInfo.InvariantCulture));
        var handle = _runtime.AttachWithProperties(scriptName, identity, radicleProperties);
        var declared = new Dictionary<string, bool>(StringComparer.Ordinal);
        Func<string, bool> definesFunction = name =>
        {
            if (declared.TryGetValue(name, out var cached)) return cached;
            if (_callbackDepth > 0) return true;
            var answer = _runtime.InstanceDeclares(handle, name);
            declared[name] = answer;
            return answer;
        };
        try
        {
            var instance = new ScriptInstance(scriptName, definesFunction, _ => null, handle, values, owner);
            _table.Register(instance, handle);
            _instances.Add(instance);
            return instance;
        }
        catch
        {
            _runtime.Detach(handle);
            throw;
        }
    }

    public VmResult Initialize(ScriptInstance instance)
    {
        try
        {
            var id = _table.HandleOf(instance);
            var decision = _gate.Gate(EvidenceKeys.OnInitBarrier);
            if (!decision.Allowed)
                return new VmResult(VmStopReason.GateStopped, PapyrusValue.None, decision.Key, decision.Reason, 0);
            if (instance.Lifecycle == ScriptLifecycle.Ready)
                return new VmResult(VmStopReason.Completed, PapyrusValue.None, null, null, 0);
            if (instance.Lifecycle == ScriptLifecycle.Failed)
                return new VmResult(VmStopReason.ModelError, PapyrusValue.None, EvidenceKeys.OnInitBarrier,
                    $"the script {instance.ScriptName} failed OnInit and stays closed: {instance.Failure}", 0);
            if (instance.Lifecycle == ScriptLifecycle.Initializing)
                return new VmResult(VmStopReason.ModelError, PapyrusValue.None, EvidenceKeys.OnInitBarrier,
                    "initialization is already running", 0);
            if (_callbackDepth > 0)
                return new VmResult(VmStopReason.ModelError, PapyrusValue.None, EvidenceKeys.OnInitBarrier,
                    "a host call cannot run inside a native callback", 0);

            ReplayUnpublished();
            instance.Lifecycle = ScriptLifecycle.Initializing;
            var status = _runtime.Initialize(id);
            if (status is RNative.PapyrusStatus.Latent or RNative.PapyrusStatus.Paused)
            {
                instance.Lifecycle = ScriptLifecycle.Failed;
                instance.Failure = "OnInit cannot wait";
                RefusedEvents += instance.Queued.Count;
                instance.Queued.Clear();
                return new VmResult(VmStopReason.ModelError, PapyrusValue.None, EvidenceKeys.OnInitBarrier,
                    "OnInit cannot wait", 0);
            }
            instance.Lifecycle = ToLifecycle(_runtime.Lifecycle(id));
            instance.Failure = _runtime.Failure(id);
            if (instance.Lifecycle == ScriptLifecycle.Failed)
            {
                instance.Failure ??= status.ToString();
                RefusedEvents += instance.Queued.Count;
                instance.Queued.Clear();
            }
            return Stop(status, 0);
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

    public VmResult RunEvent(ScriptInstance instance, string functionName, params PapyrusValue[] arguments)
    {
        ulong ticket = 0;
        try
        {
            if (_callbackDepth > 0) return Rejected("a host call cannot run inside a native callback");
            var id = _table.HandleOf(instance);
            if (instance.Detached)
                return Rejected($"the script {instance.ScriptName} is detached, so the event was refused");
            switch (instance.Lifecycle)
            {
                case ScriptLifecycle.Failed:
                    return Rejected($"the script {instance.ScriptName} failed OnInit and stays closed", EvidenceKeys.OnInitBarrier);
                case ScriptLifecycle.NotInitialized:
                    return Rejected($"the script {instance.ScriptName} has not been initialized", EvidenceKeys.OnInitBarrier);
            }
            if (!_runtime.InstanceDeclares(id, functionName))
                return Rejected($"the script {instance.ScriptName} has no event named {functionName}");

            ReplayUnpublished();
            var (callTicket, status) = _runtime.CallFunction(id, functionName,
                arguments.Select(value => _adapter.ToRadicle(value)).ToArray());
            ticket = callTicket;
            if (ticket == 0) return Stop(status, 0);

            for (var round = 0; round < MaxRounds; ++round)
            {
                var pending = _runtime.CallResult(ticket, false);
                if (!pending.Found) return Budget($"{functionName} vanished before it finished");
                switch (pending.State)
                {
                    case RNative.PapyrusCallState.Done:
                    {
                        var done = _runtime.CallResult(ticket, true);
                        return new VmResult(VmStopReason.Completed, _adapter.FromRadicle(done.Value), null, null,
                            Instructions(done.Instructions));
                    }
                    case RNative.PapyrusCallState.Failed:
                    {
                        var failed = _runtime.CallResult(ticket, true);
                        return Stop(failed.Status, Instructions(failed.Instructions));
                    }
                    case RNative.PapyrusCallState.Dropped:
                        _runtime.CallResult(ticket, true);
                        return Budget($"{functionName} was parked too long and dropped");
                    case RNative.PapyrusCallState.Waiting:
                        _orphans.Add(ticket);
                        return new VmResult(VmStopReason.Waiting, PapyrusValue.None, null,
                            $"{functionName} waits on a native latent call", Instructions(pending.Instructions));
                    default:
                        _runtime.Step(_stepBudget);
                        break;
                }
            }
            _orphans.Add(ticket);
            return Budget($"{functionName} did not finish inside {MaxRounds} steps");
        }
        catch (Exception failure)
        {
            if (ticket != 0) _orphans.Add(ticket);
            RefusedEvents++;
            return new VmResult(VmStopReason.NativeFailed, PapyrusValue.None, null, failure.Message, 0);
        }
    }

    public void RegisterNative(NativeId native, Func<IReadOnlyList<PapyrusValue>, PapyrusValue> body,
        NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) =>
        RegisterNative(native, (_, arguments) => body(arguments), kind, requiredScript);

    public void RegisterNative(NativeId native, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> body,
        NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null)
    {
        var registration = new NativeRegistration(body, kind, requiredScript);
        _registrations[native] = registration;
        Publish(native, registration);
    }

    public bool TryRegisterNative(NativeId native, Func<IReadOnlyList<PapyrusValue>, PapyrusValue> body,
        NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) =>
        TryRegisterNative(native, (_, arguments) => body(arguments), kind, requiredScript);

    public bool TryRegisterNative(NativeId native, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> body,
        NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null)
    {
        var registration = new NativeRegistration(body, kind, requiredScript);
        if (!_registrations.TryAdd(native, registration)) return false;
        Publish(native, registration);
        return true;
    }

    public bool HasScript(string name) =>
        (_callbackDepth == 0 && _runtime.HasScript(name)) || _parents.ContainsKey(name);

    public bool HasInvalidParentChain(string name)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (_parents.TryGetValue(name, out var parent))
        {
            if (!visited.Add(name)) return true;
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, "ScriptObject", StringComparison.OrdinalIgnoreCase)) return false;
            name = parent;
        }
        return true;
    }

    public int RunStep(int budget)
    {
        if (_callbackDepth > 0) return 0;
        try
        {
            ReplayUnpublished();
            var step = _runtime.Step(budget);
            SweepOrphans();
            return checked((int)step.Spent);
        }
        catch (Exception failure)
        {
            LastNativeFailure = failure.Message;
            return 0;
        }
    }

    public bool CompleteLatent(long token, PapyrusValue value)
    {
        if (_callbackDepth > 0 || !_latentTokens.TryGetValue(token, out var radicleToken)) return false;
        var accepted = _runtime.CompleteLatent(radicleToken, _adapter.ToRadicle(value));
        if (accepted) _latentTokens.Remove(token);
        return accepted;
    }

    public void Dispose()
    {
        if (_callbackDepth > 0)
            throw new InvalidOperationException("the host cannot be disposed inside a native callback");
        if (_disposed) return;
        _disposed = true;
        if (_overrides is not null) _overrides.Registered -= OnOverrideRegistered;
        _runtime.Dispose();
    }

    private void OnOverrideRegistered(string script, string function, int parameterCount) =>
        _runtime.RegisterOverride(script, function, parameterCount);

    private bool OverrideFromRuntime(ulong instance, string function, IReadOnlyList<object?> arguments,
        out RNative.PapyrusOverrideOutcome outcome, out object? result)
    {
        outcome = RNative.PapyrusOverrideOutcome.Refused;
        result = null;
        var registry = _overrides;
        if (registry is null) return false;
        _callbackDepth++;
        try
        {
            if (!_table.TryGetInstance(instance, out var target) || target is null) return true;
            var invoked = registry.TryInvoke(target, function,
                arguments.Select(argument => _adapter.FromRadicle(argument)).ToArray());
            switch (invoked.Outcome)
            {
                case OverrideOutcome.NotRegistered:
                    outcome = RNative.PapyrusOverrideOutcome.NotRegistered;
                    return false;
                case OverrideOutcome.Declined:
                    outcome = RNative.PapyrusOverrideOutcome.Declined;
                    return true;
                case OverrideOutcome.Handled:
                    outcome = RNative.PapyrusOverrideOutcome.Handled;
                    result = _adapter.ToRadicle(invoked.Value);
                    return true;
                default:
                    return true;
            }
        }
        finally
        {
            _callbackDepth--;
        }
    }

    private bool DispatchNative(NativeCallView call)
    {
        _callbackDepth++;
        try
        {
            return Dispatch(call);
        }
        finally
        {
            _callbackDepth--;
        }
    }

    private bool Dispatch(NativeCallView call)
    {
        var name = $"{call.Script}.{call.Function}";
        var gate = _gate.Gate(EvidenceKeys.NativeTarget);
        if (!gate.Allowed) return Fail(call, RNative.PapyrusStatus.GateRefused, gate.Reason);
        var registration = FindRegistration(new NativeId(call.Script, call.Function));
        if (registration is null) return Refuse(call, $"no native named {name} is registered");

        var target = PapyrusValue.None;
        switch (registration.Kind)
        {
            case NativeTargetKind.ScriptInstance:
                if (Resolve(call.SelfRef) is not ScriptInstance instance)
                    return Refuse(call, $"the native {name} needs an attached script instance");
                if (registration.RequiredScript is { } needed
                    && !string.Equals(instance.ScriptName, needed, StringComparison.OrdinalIgnoreCase))
                    return Refuse(call, $"the native {name} needs the script {needed}");
                target = PapyrusValue.FromObject(instance);
                break;
            case NativeTargetKind.EngineObject:
                switch (Resolve(call.SelfRef))
                {
                    case EngineObjectReference engine:
                        target = PapyrusValue.FromObject(engine);
                        break;
                    case ScriptInstance { Owner: { } owner }:
                        var ownerDecision = _gate.Gate("owner.engine_identity");
                        if (!ownerDecision.Allowed)
                            return Fail(call, RNative.PapyrusStatus.GateRefused,
                                $"{ownerDecision.Key}: {ownerDecision.Reason}");
                        target = PapyrusValue.FromObject(new EngineObjectReference(owner));
                        break;
                    default:
                        return Refuse(call, $"the native {name} needs an engine identity, not an attached script");
                }
                break;
        }

        try
        {
            var arguments = call.Args.Select(argument => _adapter.FromRadicle(argument)).ToArray();
            var result = registration.Body(target, arguments);
            if (call.SetResult(_adapter.ToRadicle(result))) return true;
            return Fail(call, RNative.PapyrusStatus.HostError, $"the native {name} result could not be set");
        }
        catch (NativeWait wait)
        {
            return Suspend(call, name, wait);
        }
        catch (Exception failure)
        {
            return Fail(call, RNative.PapyrusStatus.HostError, failure.Message);
        }
    }

    private bool Suspend(NativeCallView call, string name, NativeWait wait)
    {
        var gate = _gate.Gate(NativeWait.Key);
        if (!gate.Allowed) return Fail(call, RNative.PapyrusStatus.GateRefused, gate.Reason);
        if (_latentTokens.ContainsKey(wait.Token) || !_usedTokens.Add(wait.Token))
            return Fail(call, RNative.PapyrusStatus.HostError,
                $"the native {name} reused latent token {wait.Token.ToString(CultureInfo.InvariantCulture)}");
        if (!call.TryBeginWait(out var token))
            return Fail(call, RNative.PapyrusStatus.HostError, $"the native {name} could not begin its latent wait");
        _latentTokens[wait.Token] = token;
        return true;
    }

    private bool Refuse(NativeCallView call, string message)
    {
        NativeTargetRefusals++;
        return Fail(call, RNative.PapyrusStatus.HostError, message);
    }

    private bool Fail(NativeCallView call, RNative.PapyrusStatus status, string message)
    {
        LastNativeFailure = message;
        call.Fail(status);
        return false;
    }

    private NativeRegistration? FindRegistration(NativeId native)
    {
        var script = native.Script;
        for (var depth = 0; depth < MaxParentDepth; ++depth)
        {
            if (_registrations.TryGetValue(new NativeId(script, native.Function), out var registration)) return registration;
            if (!_parents.TryGetValue(script, out var parent) || string.IsNullOrEmpty(parent)) return null;
            script = parent;
        }
        return null;
    }

    private object? Resolve(PapyrusObjectRef? reference)
    {
        if (reference is null) return null;
        try
        {
            return _table.Resolve(reference);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private void Publish(NativeId native, NativeRegistration registration)
    {
        _unpublished.Remove(native);
        if (_callbackDepth == 0 && TryPublish(native, registration)) return;
        if (HasScript(native.Script))
            _unpublished[native] = new PendingNative(registration, _callbackDepth == 0 ? 1 : 0);
        else
            LastRegistrationFailure = $"the library has no script named {native.Script}";
    }

    private bool TryPublish(NativeId native, NativeRegistration registration)
    {
        try
        {
            _runtime.RegisterNativeEx(native.Script, native.Function, (RNative.PapyrusNativeKind)(int)registration.Kind,
                registration.RequiredScript ?? string.Empty, (uint)RNative.PapyrusNativeFlags.Wait);
            return true;
        }
        catch (Exception failure)
        {
            LastRegistrationFailure = failure.Message;
            return false;
        }
    }

    private void ReplayUnpublished()
    {
        if (_callbackDepth > 0) return;
        foreach (var (native, pending) in _unpublished.ToArray())
        {
            if (TryPublish(native, pending.Registration))
            {
                _unpublished.Remove(native);
                continue;
            }
            var attempts = pending.Attempts + 1;
            if (attempts >= MaxPublishAttempts) _unpublished.Remove(native);
            else _unpublished[native] = pending with { Attempts = attempts };
        }
    }

    private void SweepOrphans()
    {
        for (var index = _orphans.Count - 1; index >= 0; --index)
        {
            var info = _runtime.CallResult(_orphans[index], false);
            if (!info.Found)
            {
                _orphans.RemoveAt(index);
                continue;
            }
            if (info.State is RNative.PapyrusCallState.Waiting or RNative.PapyrusCallState.Pending) continue;
            _runtime.CallResult(_orphans[index], true);
            _orphans.RemoveAt(index);
        }
    }

    private void RecordParents(byte[] bytes)
    {
        using var pex = Radicle.Interop.PexFile.FromBytes(bytes);
        for (var index = 0; index < pex.Objects.Count; ++index)
            _parents[Encoding.Latin1.GetString(pex.Objects[index].Name)] =
                Encoding.Latin1.GetString(pex.Objects[index].ParentClassName);
    }

    private VmResult Rejected(string message, string? key = null)
    {
        RefusedEvents++;
        return new VmResult(VmStopReason.ModelError, PapyrusValue.None, key, message, 0);
    }

    private static VmResult Budget(string message) =>
        new(VmStopReason.BudgetExhausted, PapyrusValue.None, null, message, 0);

    private static VmResult Stop(RNative.PapyrusStatus status, int instructions) =>
        status == RNative.PapyrusStatus.Ok
            ? new VmResult(VmStopReason.Completed, PapyrusValue.None, null, null, instructions)
            : RadicleValueAdapter.StatusMap(status, instructions);

    private static int Instructions(ulong count) => checked((int)count);

    private static ScriptLifecycle ToLifecycle(RNative.PapyrusLifecycle lifecycle) => lifecycle switch
    {
        RNative.PapyrusLifecycle.NotInitialized => ScriptLifecycle.NotInitialized,
        RNative.PapyrusLifecycle.Initializing => ScriptLifecycle.Initializing,
        RNative.PapyrusLifecycle.Ready => ScriptLifecycle.Ready,
        RNative.PapyrusLifecycle.Failed => ScriptLifecycle.Failed,
        _ => throw new NotSupportedException($"Papyrus lifecycle {lifecycle} has no host form."),
    };

    private sealed record NativeRegistration(
        Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> Body,
        NativeTargetKind Kind,
        string? RequiredScript);

    private sealed record PendingNative(NativeRegistration Registration, int Attempts);
}
#endif
