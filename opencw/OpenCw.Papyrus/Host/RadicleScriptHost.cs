using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using InteropEngineHandlers = Radicle.Interop.PapyrusEngineHandlers;
using InteropHostCallbacks = Radicle.Interop.PapyrusHostCallbacks;
using InteropNativeCall = Radicle.Interop.PapyrusNativeCall;
using InteropObjectRef = Radicle.Interop.PapyrusObjectRef;
using InteropOwner = Radicle.Interop.PapyrusOwner;
using InteropRuntime = Radicle.Interop.PapyrusRuntime;
using RNative = Radicle.Interop.Native;

namespace OpenCommonwealth.Services.Papyrus.Host;

public sealed partial class RadicleScriptHost : IScriptHost, IDisposable
{
    private const int MaxRounds = 9;
    private const int MaxParentDepth = PapyrusHostDefaults.FrameDepthLimit;
    private const int MaxPublishAttempts = 3;

    private readonly EvidenceGate _gate;
    private readonly HostObjectTable _table = new();
    private readonly RadicleValueAdapter _adapter;
    private readonly InteropRuntime _runtime;
    private readonly int _stepBudget;
    private readonly List<ScriptInstance> _instances = new();
    private readonly List<ulong> _orphans = new();
    private readonly Dictionary<NativeId, NativeRegistration> _registrations = new();
    private readonly Dictionary<NativeId, PendingNative> _unpublished = new();
    private readonly Dictionary<string, string> _parents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Script, string Function), string> _nativeReturnTypes = new();
    private readonly Dictionary<NativeId, bool> _declaredNatives = new();
    private readonly Dictionary<(string Script, string Property), string> _propertyTypes = new();
    private readonly List<string> _propertyRefusals = new();
    private readonly Dictionary<long, ulong> _latentTokens = new();
    private readonly Dictionary<ulong, ScriptInstance> _initTickets = new();
    private readonly HashSet<string> _initOverrides = new(StringComparer.OrdinalIgnoreCase);
    private ulong _deferredInit;
    public const int MaxHeldEvents = 64;
    private readonly HashSet<long> _usedTokens = new();
    private ScriptOverrideRegistry? _overrides;
    private int _callbackDepth;
    private int _anonymousOwners;
    private bool _disposed;

    public RadicleScriptHost(EvidenceGate gate, IEnumerable<byte[]> pex,
        OnInitArrivalPolicy policy = OnInitArrivalPolicy.Refuse, int stepBudget = PapyrusHostDefaults.StepBudget)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(pex);
        _gate = gate;
        _stepBudget = stepBudget;
        _adapter = new RadicleValueAdapter(_table) { MemberType = StructMemberType };
        _table.Derives = ScriptInherits;
        _runtime = new InteropRuntime(new InteropHostCallbacks(
            Gate: key => gate.Gate(key).Allowed,
            Override: OverrideFromRuntime));
        try
        {
            foreach (var bytes in pex)
            {
                _runtime.AddPex(bytes);
                RecordParents(bytes);
            }
            _runtime.SetEngineCallbacks(new InteropEngineHandlers(DispatchNative, ReadEngineProperty, WriteEngineProperty));
            _runtime.SetCastHandler(CastEngineHandle);
            _runtime.SetMaxParks(PapyrusHostDefaults.DefaultMaxParksPerEvent);
            _runtime.SetOninitPolicy(policy == OnInitArrivalPolicy.QueueAsApproximation);
        }
        catch
        {
            _runtime.Dispose();
            throw;
        }
    }

    public EvidenceGate Gate => _gate;

    public InteropRuntime Runtime => _runtime;

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

    public IReadOnlyList<string> PropertyRefusals => _propertyRefusals;

    public int NativeTargetRefusals { get; private set; }

    public int RefusedCompletions { get; private set; }

    public RNative.PapyrusRuntimeStats Stats => _runtime.GetStats();

    private string? _lastNativeFailure;
    private long _failureStamp;

    public string? LastNativeFailure
    {
        get => _lastNativeFailure;
        private set { _lastNativeFailure = value; if (value is not null) _failureStamp++; }
    }

    private string? _lastNativeAnswer;

    public string? LastRegistrationFailure { get; private set; }

    public string? LastFailureSummary
    {
        get
        {
            var parts = new List<string>();
            if (LastNativeFailure is not null) parts.Add($"native: {LastNativeFailure}");
            if (_propertyRefusals.Count > 0) parts.Add($"property: {_propertyRefusals[^1]}");
            if (!_disposed && _instances.Count > 0)
            {
                var id = _table.HandleOf(_instances[^1]);
                if (_runtime.Failure(id) is { } failure) parts.Add($"runtime: {failure}");
                if (_runtime.InstanceState(id) is { } state) parts.Add($"state: {state}");
            }
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }
    }

    public ScriptInstance Attach(string scriptName, IReadOnlyDictionary<string, PapyrusValue>? properties = null,
        PapyrusOwnerId? owner = null, PapyrusOwnerId? stateIdentity = null)
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
        {
            string? structType = null;
            if (property.Value.Kind == PapyrusValueKind.Struct)
            {
                structType = DeclaredPropertyType(scriptName, property.Key);
                if (!structType.Contains('#') || structType.EndsWith("[]", StringComparison.Ordinal))
                {
                    _propertyRefusals.Add($"{scriptName}.{property.Key}: a struct value needs a struct property, declared {(structType.Length == 0 ? "unknown" : structType)}");
                    continue;
                }
            }
            else if (property.Value.Kind == PapyrusValueKind.Array && property.Value.Elements is { Count: > 0 } items
                && items[0].Kind == PapyrusValueKind.Struct)
                structType = DeclaredPropertyType(scriptName, property.Key);
            else if (property.Value.Reference is EngineObjectReference && DeclaredPropertyType(scriptName, property.Key) is { Length: > 0 } declaredType
                && _parents.ContainsKey(declaredType))
                structType = declaredType;
            radicleProperties[property.Key] = _adapter.ToRadicle(property.Value, structType);
        }
        var identity = (owner ?? stateIdentity) is { } known
            ? RadicleValueAdapter.OwnerMap(known)
            : new InteropOwner(RNative.PapyrusOwnerKind.RuntimeInstance, "anon:" + scriptName,
                (++_anonymousOwners).ToString(CultureInfo.InvariantCulture));
        var handle = _runtime.AttachWithProperties(scriptName, identity, radicleProperties);
        ScriptInstance instance;
        try
        {
            instance = Wrap(handle, scriptName, values, owner);
        }
        catch
        {
            _runtime.Detach(handle);
            throw;
        }
        if ((owner ?? stateIdentity) is not null) TrackIdentity(instance, identity);
        return instance;
    }

    // Requires: not called inside a native callback; the instance leaves the runtime, so its identity can be attached again and the capture stays unambiguous.
    public void Discard(ScriptInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_callbackDepth > 0)
            throw new InvalidOperationException("a script cannot be discarded inside a native callback");
        if (!_instances.Contains(instance)) return;
        _runtime.Detach(_table.HandleOf(instance));
        _table.Unregister(instance);
        _instances.Remove(instance);
        ForgetIdentity(instance);
        instance.Detached = true;
    }

    private ScriptInstance Wrap(ulong handle, string scriptName, IReadOnlyDictionary<string, PapyrusValue> values,
        PapyrusOwnerId? owner)
    {
        Func<string, bool> definesFunction = name => _callbackDepth > 0 || _runtime.InstanceDeclares(handle, name);
        var instance = new ScriptInstance(scriptName, definesFunction, _ => null, handle, values, owner);
        _table.Register(instance, handle);
        _instances.Add(instance);
        return instance;
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
            var tokensBefore = new HashSet<long>(_latentTokens.Keys);
            var earlier = LastNativeFailure;
            LastNativeFailure = null;
            _lastNativeAnswer = null;
            var deferInit = _runtime.InstanceDeclares(id, "OnInit");
            if (deferInit && _initOverrides.Add(instance.ScriptName)) _runtime.RegisterOverride(instance.ScriptName, "OnInit", 0);
            _deferredInit = deferInit ? id : 0;
            RNative.PapyrusStatus status;
            try { status = _runtime.Initialize(id); }
            finally { _deferredInit = 0; }
            if (deferInit && status == RNative.PapyrusStatus.Ok) return RunDeferredInit(instance, id);
            var during = LastNativeFailure;
            LastNativeFailure ??= earlier;
            if (status is RNative.PapyrusStatus.Latent or RNative.PapyrusStatus.Paused)
            {
                foreach (var added in _latentTokens.Keys.Where(key => !tokensBefore.Contains(key)).ToList())
                    _latentTokens.Remove(added);
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
                if (during is not null) instance.Failure = $"{instance.Failure}: {during}";
                else if (_lastNativeAnswer is not null) instance.Failure = $"{instance.Failure}: after the native {_lastNativeAnswer}";
                RefusedEvents += instance.Queued.Count;
                instance.Queued.Clear();
            }
            else if (instance.Lifecycle == ScriptLifecycle.Ready) ApplyAwaitingRestore(instance);
            var stopped = Stop(status, 0);
            return instance.Lifecycle == ScriptLifecycle.Failed && stopped.Message is null ? stopped with { Message = $"{instance.ScriptName}: {instance.Failure}" } : stopped;
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

    // Invariant: Radicle already counts the instance Ready; only the host-side Lifecycle says Initializing until OnInit ends.
    private VmResult RunDeferredInit(ScriptInstance instance, ulong id)
    {
        instance.Failure = null;
        var since = _failureStamp;
        var (ticket, status) = _runtime.CallFunction(id, "OnInit");
        if (ticket == 0) return FinishInit(instance, Named(Stop(status, 0), status, since));
        var result = Pump(ticket, "OnInit", since);
        if (result.Reason is not (VmStopReason.Waiting or VmStopReason.BudgetExhausted)) return FinishInit(instance, result);
        _initTickets[ticket] = instance;
        return result with { Reason = VmStopReason.Waiting, Message = $"OnInit of {instance.ScriptName} is still running: {result.Message}" };
    }

    private VmResult FinishInit(ScriptInstance instance, VmResult result)
    {
        if (result.Completed)
        {
            instance.Lifecycle = ScriptLifecycle.Ready;
            try { ApplyAwaitingRestore(instance); }
            catch (Exception failure) { _lateRestoreRefusals.Add($"{instance.ScriptName}: {failure.GetType().Name}: {failure.Message}"); }
            while (instance.Queued.TryDequeue(out var held))
            {
                try { RunEvent(instance, held.Function, held.Arguments); }
                catch (Exception failure)
                {
                    RefusedEvents++;
                    LastNativeFailure = $"{instance.ScriptName}.{held.Function}: {failure.GetType().Name}: {failure.Message}";
                }
            }
            return result;
        }
        instance.Lifecycle = ScriptLifecycle.Failed;
        var reason = result.Message ?? result.Reason.ToString();
        instance.Failure = LastNativeFailure is null || reason.Contains(LastNativeFailure, StringComparison.Ordinal) ? reason : $"{reason}: {LastNativeFailure}";
        RefusedEvents += instance.Queued.Count;
        instance.Queued.Clear();
        return result with { Message = $"{instance.ScriptName}: {instance.Failure}" };
    }

    private void SweepInits()
    {
        foreach (var (ticket, instance) in _initTickets.ToArray())
        {
            try
            {
                var info = _runtime.CallResult(ticket, false);
                if (info.Found && info.State is RNative.PapyrusCallState.Waiting or RNative.PapyrusCallState.Pending) continue;
                _initTickets.Remove(ticket);
                _orphans.Remove(ticket);
                if (instance.Detached) continue;
                if (!info.Found)
                {
                    FinishInit(instance, Budget("OnInit vanished before it finished"));
                    continue;
                }
                var done = _runtime.CallResult(ticket, true);
                FinishInit(instance, info.State == RNative.PapyrusCallState.Done
                    ? new VmResult(VmStopReason.Completed, PapyrusValue.None, null, null, Instructions(done.Instructions))
                    : Named(Stop(done.Status, Instructions(done.Instructions)), done.Status, long.MaxValue, done.Detail));
            }
            catch (Exception failure)
            {
                _initTickets.Remove(ticket);
                _orphans.Remove(ticket);
                if (!instance.Detached && instance.Lifecycle == ScriptLifecycle.Initializing)
                    FinishInit(instance, new VmResult(VmStopReason.ModelError, PapyrusValue.None, null,
                        $"finishing OnInit of {instance.ScriptName} threw {failure.GetType().Name}: {failure.Message}", 0));
            }
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
            if (instance.Lifecycle == ScriptLifecycle.Initializing && _initTickets.ContainsValue(instance))
            {
                if (instance.Queued.Count >= MaxHeldEvents)
                    return Rejected($"the script {instance.ScriptName} is still in OnInit and {MaxHeldEvents} events are already held", EvidenceKeys.OnInitBarrier);
                instance.Queued.Enqueue((functionName, arguments));
                return new VmResult(VmStopReason.Waiting, PapyrusValue.None, null, $"{functionName} is held until {instance.ScriptName} finishes OnInit", 0);
            }

            ReplayUnpublished();
            LastNativeFailure = null;
            _lastNativeAnswer = null;
            var since = _failureStamp;
            var (callTicket, status) = _runtime.CallFunction(id, functionName,
                arguments.Select(value => _adapter.ToRadicle(value, EventArgumentType(value))).ToArray());
            ticket = callTicket;
            if (ticket == 0) return Named(Stop(status, 0), status, since);
            return Pump(ticket, functionName, since);
        }
        catch (Exception failure)
        {
            if (ticket != 0) _orphans.Add(ticket);
            RefusedEvents++;
            return new VmResult(VmStopReason.NativeFailed, PapyrusValue.None, null, failure.Message, 0);
        }
    }

    private string? EventArgumentType(PapyrusValue value)
    {
        if (value.Kind != PapyrusValueKind.Object || value.Reference is not EngineObjectReference engine) return null;
        return engine.ScriptType is { } named && _parents.ContainsKey(named) ? TypeOfLookedUp(engine, named) : _table.KnownType(engine);
    }

    private VmResult Pump(ulong ticket, string functionName, long since)
    {
        try
        {
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
                        return Named(Stop(failed.Status, Instructions(failed.Instructions)), failed.Status, since, failed.Detail);
                    }
                    case RNative.PapyrusCallState.Dropped:
                        _runtime.CallResult(ticket, true);
                        return Budget($"{functionName} was parked too long and dropped");
                    case RNative.PapyrusCallState.Waiting:
                        _orphans.Add(ticket);
                        return new VmResult(VmStopReason.Waiting, PapyrusValue.None, null,
                            $"{functionName} waits on a native latent call", Instructions(pending.Instructions));
                    default:
                        if (_runtime.RunCall(ticket, _stepBudget) is null)
                        {
                            _orphans.Add(ticket);
                            return Budget($"{functionName} is not ready to run");
                        }
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
            SweepInits();
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
        var accepted = _callbackDepth == 0 && _latentTokens.TryGetValue(token, out var radicleToken)
            && _runtime.CompleteLatent(radicleToken, _adapter.ToRadicle(value));
        if (accepted) _latentTokens.Remove(token);
        else RefusedCompletions++;
        return accepted;
    }

    public void Dispose()
    {
        if (_callbackDepth > 0)
            throw new InvalidOperationException("the host cannot be disposed inside a native callback");
        if (_disposed) return;
        _disposed = true;
        if (_overrides is not null) _overrides.Registered -= OnOverrideRegistered;
        _lifecycle?.Dispose();
        _runtime.Dispose();
    }

    private void OnOverrideRegistered(string script, string function, int parameterCount) =>
        _runtime.RegisterOverride(script, function, parameterCount);

    private bool OverrideFromRuntime(ulong instance, string function, IReadOnlyList<object?> arguments,
        out RNative.PapyrusOverrideOutcome outcome, out object? result)
    {
        outcome = RNative.PapyrusOverrideOutcome.Refused;
        result = null;
        if (function.Equals("OnInit", StringComparison.OrdinalIgnoreCase) && instance == _deferredInit && instance != 0)
        {
            outcome = RNative.PapyrusOverrideOutcome.Handled;
            return true;
        }
        var registry = _overrides;
        if (registry is null)
        {
            if (function.Equals("OnInit", StringComparison.OrdinalIgnoreCase)) outcome = RNative.PapyrusOverrideOutcome.NotRegistered;
            return false;
        }
        _callbackDepth++;
        try
        {
            if (!_table.TryGetInstance(instance, out var target) || target is null) target = TryAdopt(instance);
            if (target is null) return true;
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

    private bool DispatchNative(InteropNativeCall call)
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

    private bool Dispatch(InteropNativeCall call)
    {
        var name = $"{call.Script}.{call.Function}";
        var gate = _gate.Gate(EvidenceKeys.NativeTarget);
        if (!gate.Allowed) return Fail(call, RNative.PapyrusStatus.GateRefused, gate.Reason);
        var registration = FindRegistration(new NativeId(call.Script, call.Function));
        if (registration is null)
            return Refuse(call, $"the native {name} is declared by the script library but the engine has no implementation for it");

        var target = PapyrusValue.None;
        switch (registration.Kind)
        {
            case NativeTargetKind.ScriptInstance:
                if (Resolve(call.SelfRef) is not ScriptInstance instance)
                    return Refuse(call, $"the native {name} needs an attached script instance");
                if (registration.RequiredScript is { } needed && !ScriptInherits(instance.ScriptName, needed))
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
            var declared = result.Kind == PapyrusValueKind.Object && result.Reference is EngineObjectReference engineResult
                ? engineResult.ScriptType is { } known && _parents.ContainsKey(known) ? TypeOfLookedUp(engineResult, known) : FindNativeReturnType(call.Script, call.Function)
                : null;
            if (call.SetResult(_adapter.ToRadicle(result, declared)))
            {
                _lastNativeAnswer = $"{name} answered {(result.Kind == PapyrusValueKind.Object && result.Reference is EngineObjectReference answered ? answered.Identity + UntypedHandleNote(answered) : result.Kind.ToString())}";
                return true;
            }
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

    private bool Suspend(InteropNativeCall call, string name, NativeWait wait)
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

    private bool Refuse(InteropNativeCall call, string message)
    {
        NativeTargetRefusals++;
        return Fail(call, RNative.PapyrusStatus.HostError, message);
    }

    private bool Fail(InteropNativeCall call, RNative.PapyrusStatus status, string message)
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

    private string? FindNativeReturnType(string script, string function)
    {
        var current = script;
        for (var depth = 0; depth < MaxParentDepth; ++depth)
        {
            if (_nativeReturnTypes.TryGetValue((current.ToLowerInvariant(), function.ToLowerInvariant()), out var returnType))
                return _parents.ContainsKey(returnType) ? returnType : null;
            if (!_parents.TryGetValue(current, out var parent) || string.IsNullOrEmpty(parent)) return null;
            current = parent;
        }
        return null;
    }

    private bool ScriptInherits(string script, string required)
    {
        var current = script;
        for (var depth = 0; depth < MaxParentDepth; ++depth)
        {
            if (string.Equals(current, required, StringComparison.OrdinalIgnoreCase)) return true;
            if (!_parents.TryGetValue(current, out var parent) || string.IsNullOrEmpty(parent)) return false;
            current = parent;
        }
        return false;
    }

    private object? Resolve(InteropObjectRef? reference)
    {
        if (reference is null) return null;
        try
        {
            return _table.Resolve(reference);
        }
        catch (KeyNotFoundException)
        {
            return TryAdopt(reference.Handle);
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
            var flags = registration.Kind == NativeTargetKind.EngineObject
                ? RNative.PapyrusNativeFlags.MayWait | RNative.PapyrusNativeFlags.AcceptScriptInstance
                : RNative.PapyrusNativeFlags.MayWait;
            _runtime.RegisterNativeEx(native.Script, native.Function, (RNative.PapyrusNativeKind)(int)registration.Kind,
                registration.RequiredScript ?? string.Empty, (uint)flags);
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
        {
            var script = Encoding.Latin1.GetString(pex.Objects[index].Name);
            _parents[script] = Encoding.Latin1.GetString(pex.Objects[index].ParentClassName);
            var code = pex.ReadCode(index);
            RecordNativeReturnTypes(pex, code, script);
            RecordScalarProperties(pex, code, script);
            foreach (var structure in code.Structs)
                foreach (var member in structure.Members)
                    if (structure.Name >= 0 && member.Name >= 0 && member.Type >= 0 && structure.Name < pex.StringTable.Count && member.Name < pex.StringTable.Count && member.Type < pex.StringTable.Count)
                        _structMembers[($"{script}#{Encoding.Latin1.GetString(pex.StringTable[structure.Name])}".ToLowerInvariant(), Encoding.Latin1.GetString(pex.StringTable[member.Name]).ToLowerInvariant())] =
                            Encoding.Latin1.GetString(pex.StringTable[member.Type]);
            foreach (var property in code.Properties)
                if (property.Name >= 0 && property.Type >= 0 && property.Name < pex.StringTable.Count && property.Type < pex.StringTable.Count)
                    _propertyTypes[(script.ToLowerInvariant(), Encoding.Latin1.GetString(pex.StringTable[property.Name]).ToLowerInvariant())] =
                        Encoding.Latin1.GetString(pex.StringTable[property.Type]);
        }
    }

    private readonly Dictionary<(string Struct, string Member), string> _structMembers = new();

    // Invariant: only a script that is loaded, or a struct type, is passed on as a handle or struct type; anything else keeps the untyped form.
    private string? StructMemberType(string structType, string member) =>
        _structMembers.TryGetValue((structType.ToLowerInvariant(), member.ToLowerInvariant()), out var declared)
        && (_parents.ContainsKey(declared) || declared.Contains('#')) ? declared : null;

    private string DeclaredPropertyType(string script, string property)
    {
        var current = script;
        for (var hop = 0; hop < 64 && !string.IsNullOrEmpty(current); ++hop, _parents.TryGetValue(current, out current))
            if (_propertyTypes.TryGetValue((current.ToLowerInvariant(), property.ToLowerInvariant()), out var type)) return type;
        return "";
    }

    public bool? IsDeclaredGlobal(NativeId native) => _declaredNatives.TryGetValue(native, out var isGlobal) ? isGlobal : null;

    private void RecordNativeReturnTypes(Radicle.Interop.PexFile pex, Radicle.Interop.PexObjectCode code, string script)
    {
        foreach (var function in code.Functions)
        {
            if (!function.IsNative || function.Name < 0 || function.ReturnType < 0 ||
                function.Name >= pex.StringTable.Count || function.ReturnType >= pex.StringTable.Count) continue;
            _declaredNatives[new NativeId(script, Encoding.Latin1.GetString(pex.StringTable[function.Name]))] = function.IsGlobal;
            var returnType = Encoding.Latin1.GetString(pex.StringTable[function.ReturnType]);
            if (IsValueReturnType(returnType)) continue;
            var name = Encoding.Latin1.GetString(pex.StringTable[function.Name]);
            _nativeReturnTypes[(script.ToLowerInvariant(), name.ToLowerInvariant())] = returnType;
        }
    }

    private static bool IsValueReturnType(string type) =>
        type.Length == 0 || type.EndsWith("[]", StringComparison.Ordinal) ||
        type.ToLowerInvariant() is "int" or "float" or "bool" or "string" or "var" or "none";

    private VmResult Rejected(string message, string? key = null)
    {
        RefusedEvents++;
        return new VmResult(VmStopReason.ModelError, PapyrusValue.None, key, message, 0);
    }

    private static VmResult Budget(string message) =>
        new(VmStopReason.BudgetExhausted, PapyrusValue.None, null, message, 0);

    // Requires: since is the failure stamp taken when this call began, so a failure from an earlier call is never attributed to it.
    private VmResult Named(VmResult result, RNative.PapyrusStatus status, long since, string? detail = null) =>
        result.Message is null && !result.Completed ? result with { Message = $"the VM stopped with status {status}{(detail is null ? "" : $" ({detail})")}{(_lastNativeAnswer is null ? "" : $" after the native {_lastNativeAnswer}")}{(LastNativeFailure is null || _failureStamp <= since ? "" : $"; native failure: {LastNativeFailure}")}" } : result;

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
