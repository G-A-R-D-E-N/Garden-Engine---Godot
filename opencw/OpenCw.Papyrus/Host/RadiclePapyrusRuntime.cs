#if RADICLE_HOST_AVAILABLE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Radicle.Interop.Native;

namespace OpenCommonwealth.Services.Papyrus.Host;

public sealed record PapyrusOwner(PapyrusOwnerKind Kind, string BaseFormKey, string Discriminator = "");

public sealed record PapyrusObjectRef(ulong Handle, string Type, string State);

public readonly record struct PapyrusRemoteTarget(long Handle, ulong Instance);

public delegate bool PapyrusNativeHandler(string script, string function, IReadOnlyList<object?> arguments, out object? result);

public delegate bool PapyrusOverrideHandler(ulong instance, string function, IReadOnlyList<object?> arguments,
    out PapyrusOverrideOutcome outcome, out object? result);

public delegate long PapyrusLatentHandler(string script, string function, IReadOnlyList<object?> arguments);

public sealed record PapyrusHostCallbacks(
    Func<string, bool>? Gate = null,
    PapyrusNativeHandler? CallNative = null,
    PapyrusOverrideHandler? Override = null,
    PapyrusLatentHandler? BeginLatent = null);

public sealed record PapyrusEngineHandlers(
    Func<NativeCallView, bool> CallNative,
    Func<PapyrusObjectRef, string, (bool Handled, object? Value)>? GetProperty = null,
    Func<PapyrusObjectRef, string, object?, bool>? SetProperty = null);

public sealed record CallResultInfo(bool Found, PapyrusCallState State, PapyrusStatus Status, ulong Instructions, object? Value);

public sealed unsafe class NativeCallView
{
    private readonly IntPtr _call;
    private IReadOnlyList<object?>? _args;
    private bool _open = true;

    internal NativeCallView(IntPtr call)
    {
        _call = call;
        Script = Marshal.PtrToStringUTF8(RadicleNative.PapyrusNativeCallScript(call)) ?? string.Empty;
        Function = Marshal.PtrToStringUTF8(RadicleNative.PapyrusNativeCallFunction(call)) ?? string.Empty;
        var self = RadicleNative.PapyrusNativeCallSelf(call);
        SelfRef = self == IntPtr.Zero ? null : PapyrusValues.ReadObject(self);
    }

    public string Script { get; }

    public string Function { get; }

    public PapyrusObjectRef? SelfRef { get; }

    public IReadOnlyList<object?> Args
    {
        get
        {
            Require();
            return _args ??= ReadArgs();
        }
    }

    public bool SetResult(object? value)
    {
        Require();
        var made = PapyrusValues.Make(value);
        try { return RadicleNative.PapyrusNativeCallSetResult(_call, made) == Status.Ok; }
        finally { RadicleNative.PapyrusValueDestroy(made); }
    }

    public bool TryBeginWait(out ulong token)
    {
        Require();
        ulong raw = 0;
        var status = RadicleNative.PapyrusNativeCallBeginWait(_call, &raw);
        token = raw;
        return status == Status.Ok;
    }

    public void Fail(PapyrusStatus status)
    {
        Require();
        PapyrusValues.Check(RadicleNative.PapyrusNativeCallFail(_call, status), "radicle_papyrus_native_call_fail");
    }

    internal void Close() => _open = false;

    private void Require()
    {
        if (!_open) throw new InvalidOperationException("NativeCallView is only valid inside its native callback.");
    }

    private object?[] ReadArgs()
    {
        var count = checked((int)RadicleNative.PapyrusNativeCallArgCount(_call));
        var result = new object?[count];
        for (var i = 0; i < count; ++i) result[i] = PapyrusValues.Read(RadicleNative.PapyrusNativeCallArg(_call, (nuint)i));
        return result;
    }
}

// Invariant: values cross as null, int, bool, float, string, PapyrusObjectRef or IReadOnlyList<object?>; a struct value is not readable here.
internal static unsafe class PapyrusValues
{
    public static IntPtr Make(object? value)
    {
        IntPtr made = IntPtr.Zero;
        switch (value)
        {
            case null:
                Check(RadicleNative.PapyrusValueNone(&made), "radicle_papyrus_value_none");
                break;
            case int number:
                Check(RadicleNative.PapyrusValueInt(number, &made), "radicle_papyrus_value_int");
                break;
            case bool flag:
                Check(RadicleNative.PapyrusValueBool(flag ? 1 : 0, &made), "radicle_papyrus_value_bool");
                break;
            case float real:
                Check(RadicleNative.PapyrusValueFloat(real, &made), "radicle_papyrus_value_float");
                break;
            case double real:
                Check(RadicleNative.PapyrusValueFloat((float)real, &made), "radicle_papyrus_value_float");
                break;
            case string text:
                Check(RadicleNative.PapyrusValueString(text, (nuint)Encoding.UTF8.GetByteCount(text), &made), "radicle_papyrus_value_string");
                break;
            case PapyrusObjectRef reference:
                Check(RadicleNative.PapyrusValueObject(reference.Handle, reference.Type, reference.State, &made), "radicle_papyrus_value_object");
                break;
            case IReadOnlyList<object?> items:
                made = MakeArray(items);
                break;
            default:
                throw new ArgumentException($"Unsupported Papyrus value type {value.GetType().Name}.", nameof(value));
        }
        return made;
    }

    public static object? Read(IntPtr value)
    {
        switch (RadicleNative.PapyrusValueKindOf(value))
        {
            case PapyrusValueKind.None:
                return null;
            case PapyrusValueKind.String:
                return Marshal.PtrToStringUTF8(RadicleNative.PapyrusValueText(value)) ?? string.Empty;
            case PapyrusValueKind.Int:
                return RadicleNative.PapyrusValueIntOf(value);
            case PapyrusValueKind.Float:
                return RadicleNative.PapyrusValueFloatOf(value);
            case PapyrusValueKind.Bool:
                return RadicleNative.PapyrusValueBoolOf(value) != 0;
            case PapyrusValueKind.Object:
                return new PapyrusObjectRef(
                    RadicleNative.PapyrusValueObjectHandle(value),
                    Marshal.PtrToStringUTF8(RadicleNative.PapyrusValueObjectType(value)) ?? string.Empty,
                    Marshal.PtrToStringUTF8(RadicleNative.PapyrusValueObjectState(value)) ?? string.Empty);
            case PapyrusValueKind.Array:
                return ReadArray(value);
            default:
                throw new NotSupportedException("Papyrus struct values are not readable through RadiclePapyrusRuntime.");
        }
    }

    public static PapyrusObjectRef ReadObject(IntPtr value) => new(
        RadicleNative.PapyrusObjectHandle(value),
        Marshal.PtrToStringUTF8(RadicleNative.PapyrusObjectType(value)) ?? string.Empty,
        Marshal.PtrToStringUTF8(RadicleNative.PapyrusObjectState(value)) ?? string.Empty);

    public static object?[] ReadAll(IntPtr values, nuint count)
    {
        var result = new object?[checked((int)count)];
        var raw = (IntPtr*)values;
        for (var i = 0; i < result.Length; ++i) result[i] = Read(raw[i]);
        return result;
    }

    public static void Check(Status status, string operation)
    {
        if (status != Status.Ok) throw new InvalidOperationException($"{operation} failed: {status}");
    }

    private static IntPtr MakeArray(IReadOnlyList<object?> items)
    {
        IntPtr array = IntPtr.Zero;
        Check(RadicleNative.PapyrusValueArray((nuint)items.Count, &array), "radicle_papyrus_value_array");
        try
        {
            for (var i = 0; i < items.Count; ++i)
            {
                var element = Make(items[i]);
                try { Check(RadicleNative.PapyrusValueArraySet(array, (nuint)i, element), "radicle_papyrus_value_array_set"); }
                finally { RadicleNative.PapyrusValueDestroy(element); }
            }
        }
        catch
        {
            RadicleNative.PapyrusValueDestroy(array);
            throw;
        }
        return array;
    }

    private static object?[] ReadArray(IntPtr array)
    {
        var result = new object?[checked((int)RadicleNative.PapyrusValueArrayCount(array))];
        for (var i = 0; i < result.Length; ++i)
        {
            IntPtr element = IntPtr.Zero;
            Check(RadicleNative.PapyrusValueArrayGet(array, (nuint)i, &element), "radicle_papyrus_value_array_get");
            try { result[i] = Read(element); }
            finally { RadicleNative.PapyrusValueDestroy(element); }
        }
        return result;
    }
}

// Invariant: owns the handles it made and destroys them on Dispose; the runtime copies what it keeps, so they may be freed right after the call.
internal sealed class PapyrusValueSet : IDisposable
{
    public PapyrusValueSet(IReadOnlyList<object?> values)
    {
        Handles = new IntPtr[values.Count];
        try
        {
            for (var i = 0; i < Handles.Length; ++i) Handles[i] = PapyrusValues.Make(values[i]);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public IntPtr[] Handles { get; }

    public void Dispose()
    {
        for (var i = 0; i < Handles.Length; ++i)
        {
            if (Handles[i] == IntPtr.Zero) continue;
            RadicleNative.PapyrusValueDestroy(Handles[i]);
            Handles[i] = IntPtr.Zero;
        }
    }
}

// Requires: parsed pex handles outlive the runtime, so Dispose destroys the runtime before them; callback delegates stay rooted in fields for the runtime's whole life; every call and the callbacks it runs happen on one thread; a callback must not re-enter the runtime, since a nested call would consume the outer callback fault.
public sealed unsafe class RadiclePapyrusRuntime : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GateCallback(IntPtr user, IntPtr key);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeCallback(IntPtr user, IntPtr script, IntPtr function, IntPtr args, nuint count, IntPtr outValue);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OverrideCallback(IntPtr user, ulong instance, IntPtr function, IntPtr args, nuint count, IntPtr outOutcome, IntPtr outValue);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long LatentCallback(IntPtr user, IntPtr script, IntPtr function, IntPtr args, nuint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EngineCallNativeCallback(IntPtr user, IntPtr call);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EngineGetPropertyCallback(IntPtr user, IntPtr obj, IntPtr name, IntPtr outValue);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EngineSetPropertyCallback(IntPtr user, IntPtr obj, IntPtr name, IntPtr value);

    public const ulong EngineHandleBit = 0x8000000000000000UL;

    private readonly List<IntPtr> _pex = new();
    private readonly GateCallback? _gate;
    private readonly NativeCallback? _native;
    private readonly OverrideCallback? _override;
    private readonly LatentCallback? _latent;
    private EngineCallNativeCallback? _engineCallNative;
    private EngineGetPropertyCallback? _engineGetProperty;
    private EngineSetPropertyCallback? _engineSetProperty;
    private IntPtr _engineBlock;
    private IntPtr _runtime;
    private int _callbacks;
    private Exception? _fault;

    public RadiclePapyrusRuntime(PapyrusHostCallbacks? host = null)
    {
        PapyrusRuntimeCallbacks callbacks = default;
        if (host?.Gate is { } gate)
        {
            _gate = (_, key) => Guard(() => gate(Marshal.PtrToStringUTF8(key) ?? string.Empty) ? 1 : 0, 0);
            callbacks.Gate = Marshal.GetFunctionPointerForDelegate(_gate);
        }
        if (host?.CallNative is { } native)
        {
            _native = (_, script, function, args, count, outValue) => Guard(() =>
            {
                if (!native(Text(script), Text(function), PapyrusValues.ReadAll(args, count), out var result)) return 0;
                *(IntPtr*)outValue = PapyrusValues.Make(result);
                return 1;
            }, 0);
            callbacks.CallNative = Marshal.GetFunctionPointerForDelegate(_native);
        }
        if (host?.Override is { } handler)
        {
            _override = (_, instance, function, args, count, outOutcome, outValue) =>
            {
                *(int*)outOutcome = (int)PapyrusOverrideOutcome.Refused;
                *(IntPtr*)outValue = IntPtr.Zero;
                return Guard(() =>
                {
                    if (!handler(instance, Text(function), PapyrusValues.ReadAll(args, count), out var outcome, out var result))
                    {
                        *(int*)outOutcome = (int)PapyrusOverrideOutcome.NotRegistered;
                        return 0;
                    }
                    var made = outcome == PapyrusOverrideOutcome.Handled ? PapyrusValues.Make(result) : IntPtr.Zero;
                    *(IntPtr*)outValue = made;
                    *(int*)outOutcome = (int)outcome;
                    return 1;
                }, 1);
            };
            callbacks.OverrideFunction = Marshal.GetFunctionPointerForDelegate(_override);
        }
        if (host?.BeginLatent is { } latent)
        {
            _latent = (_, script, function, args, count) => Guard(
                () => latent(Text(script), Text(function), PapyrusValues.ReadAll(args, count)), 0L);
            callbacks.BeginLatent = Marshal.GetFunctionPointerForDelegate(_latent);
        }
        IntPtr runtime = IntPtr.Zero;
        PapyrusValues.Check(RadicleNative.PapyrusRuntimeCreate(&callbacks, &runtime), "radicle_papyrus_runtime_create");
        _runtime = runtime;
    }

    public int PexCount => _pex.Count;

    public void AddPex(ReadOnlySpan<byte> pexBytes)
    {
        var runtime = Runtime;
        IntPtr pex = IntPtr.Zero;
        Status parse;
        fixed (byte* data = pexBytes)
        {
            parse = RadicleNative.PexParse(data, (nuint)pexBytes.Length, &pex);
        }
        PapyrusValues.Check(parse, "radicle_pex_parse");
        var add = RadicleNative.PapyrusRuntimeAddPex(runtime, pex);
        if (add != Status.Ok)
        {
            RadicleNative.PexDestroy(pex);
            PapyrusValues.Check(add, "radicle_papyrus_runtime_add_pex");
        }
        _pex.Add(pex);
    }

    public ulong Attach(string script, PapyrusOwner owner)
    {
        ulong instance = 0;
        PapyrusValues.Check(
            RadicleNative.PapyrusRuntimeAttach(Runtime, script, owner.Kind, owner.BaseFormKey, owner.Discriminator, &instance),
            "radicle_papyrus_runtime_attach");
        return instance;
    }

    public ulong AttachWithProperties(string script, PapyrusOwner owner, IReadOnlyDictionary<string, object?> properties)
    {
        var names = properties.Keys.ToArray();
        var rawNames = new IntPtr[names.Length];
        var runtime = Runtime;
        try
        {
            for (var i = 0; i < names.Length; ++i) rawNames[i] = Marshal.StringToCoTaskMemUTF8(names[i]);
            using var values = new PapyrusValueSet(names.Select(name => properties[name]).ToArray());
            ulong instance = 0;
            fixed (IntPtr* n = rawNames)
            fixed (IntPtr* v = values.Handles)
            {
                PapyrusValues.Check(
                    RadicleNative.PapyrusRuntimeAttachWithProperties(runtime, script, owner.Kind, owner.BaseFormKey, owner.Discriminator,
                        n, v, (nuint)names.Length, &instance),
                    "radicle_papyrus_runtime_attach_with_properties");
            }
            return instance;
        }
        finally
        {
            foreach (var raw in rawNames)
            {
                if (raw != IntPtr.Zero) Marshal.FreeCoTaskMem(raw);
            }
        }
    }

    public bool Detach(ulong instance) => RadicleNative.PapyrusRuntimeDetach(Runtime, instance) != 0;

    public PapyrusLifecycle Lifecycle(ulong instance) => RadicleNative.PapyrusRuntimeLifecycle(Runtime, instance);

    public string? Failure(ulong instance)
    {
        var text = Marshal.PtrToStringUTF8(RadicleNative.PapyrusRuntimeFailure(Runtime, instance));
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public string? InstanceState(ulong instance) => Marshal.PtrToStringUTF8(RadicleNative.PapyrusRuntimeInstanceState(Runtime, instance));

    public PapyrusStatus Initialize(ulong instance)
    {
        PapyrusStatus status = PapyrusStatus.InternalError;
        CheckAfterCallbacks(RadicleNative.PapyrusRuntimeInitialize(Runtime, instance, &status), "radicle_papyrus_runtime_initialize");
        return status;
    }

    public PapyrusStatus SendEvent(ulong instance, string function, params object?[] arguments)
    {
        using var values = new PapyrusValueSet(arguments ?? Array.Empty<object?>());
        PapyrusStatus status = PapyrusStatus.InternalError;
        fixed (IntPtr* args = values.Handles)
        {
            CheckAfterCallbacks(
                RadicleNative.PapyrusRuntimeSendEvent(Runtime, instance, function, args, (nuint)values.Handles.Length, &status),
                "radicle_papyrus_runtime_send_event");
        }
        return status;
    }

    public PapyrusStepResult Step(int budget)
    {
        PapyrusStepResult result = default;
        CheckAfterCallbacks(RadicleNative.PapyrusRuntimeStep(Runtime, budget, &result), "radicle_papyrus_runtime_step");
        return result;
    }

    public bool CompleteLatent(ulong token, object? value)
    {
        var made = PapyrusValues.Make(value);
        try
        {
            int accepted = 0;
            CheckAfterCallbacks(RadicleNative.PapyrusRuntimeCompleteLatent(Runtime, token, made, &accepted), "radicle_papyrus_runtime_complete_latent");
            return accepted != 0;
        }
        finally { RadicleNative.PapyrusValueDestroy(made); }
    }

    public void RegisterNative(string script, string function, PapyrusNativeKind kind = PapyrusNativeKind.Static,
        string requiredScript = "", bool latent = false) =>
        PapyrusValues.Check(
            RadicleNative.PapyrusRuntimeRegisterNative(Runtime, script, function, kind, requiredScript, latent ? 1 : 0),
            "radicle_papyrus_runtime_register_native");

    public void RegisterOverride(string script, string function, int parameterCount = -1) =>
        PapyrusValues.Check(
            RadicleNative.PapyrusRuntimeRegisterOverride(Runtime, script, function, parameterCount),
            "radicle_papyrus_runtime_register_override");

    public bool TryGetVariable(ulong instance, string name, out object? value)
    {
        IntPtr read = IntPtr.Zero;
        var status = RadicleNative.PapyrusRuntimeGetVariable(Runtime, instance, name, &read);
        return Take(status, read, "radicle_papyrus_runtime_get_variable", out value);
    }

    public bool TryGetProperty(ulong instance, string name, out object? value)
    {
        IntPtr read = IntPtr.Zero;
        var status = RadicleNative.PapyrusRuntimeGetProperty(Runtime, instance, name, &read);
        return Take(status, read, "radicle_papyrus_runtime_get_property", out value);
    }

    public void SetVariable(ulong instance, string name, object? value)
    {
        var made = PapyrusValues.Make(value);
        try { PapyrusValues.Check(RadicleNative.PapyrusRuntimeSetVariable(Runtime, instance, name, made), "radicle_papyrus_runtime_set_variable"); }
        finally { RadicleNative.PapyrusValueDestroy(made); }
    }

    public long RegisterCustomEvent(ulong instance, string eventName)
    {
        long handle = 0;
        PapyrusValues.Check(RadicleNative.PapyrusRuntimeRegisterCustomEvent(Runtime, instance, eventName, &handle), "radicle_papyrus_runtime_register_custom_event");
        return handle;
    }

    public long RegisterTimer(ulong instance, double intervalSeconds, int repeats, PapyrusClock clock)
    {
        long handle = 0;
        PapyrusValues.Check(RadicleNative.PapyrusRuntimeRegisterTimer(Runtime, instance, intervalSeconds, repeats, clock, &handle), "radicle_papyrus_runtime_register_timer");
        return handle;
    }

    public long RegisterRemoteEvent(ulong instance, PapyrusOwner source, string eventName)
    {
        long handle = 0;
        PapyrusValues.Check(
            RadicleNative.PapyrusRuntimeRegisterRemoteEvent(Runtime, instance, source.Kind, source.BaseFormKey, source.Discriminator, eventName, &handle),
            "radicle_papyrus_runtime_register_remote_event");
        return handle;
    }

    public IReadOnlyList<PapyrusRemoteTarget> MatchRemoteEvent(PapyrusOwner source, string eventName)
    {
        while (true)
        {
            nuint count = 0;
            PapyrusValues.Check(
                RadicleNative.PapyrusRuntimeMatchRemoteEvent(Runtime, source.Kind, source.BaseFormKey, source.Discriminator, eventName, null, null, 0, &count),
                "radicle_papyrus_runtime_match_remote_event");
            if (count == 0) return Array.Empty<PapyrusRemoteTarget>();
            var handles = new long[checked((int)count)];
            var instances = new ulong[handles.Length];
            nuint total;
            fixed (long* h = handles)
            fixed (ulong* i = instances)
            {
                PapyrusValues.Check(
                    RadicleNative.PapyrusRuntimeMatchRemoteEvent(Runtime, source.Kind, source.BaseFormKey, source.Discriminator, eventName, h, i, count, &total),
                    "radicle_papyrus_runtime_match_remote_event");
            }
            if (total > count) continue;
            return Enumerable.Range(0, (int)total).Select(index => new PapyrusRemoteTarget(handles[index], instances[index])).ToArray();
        }
    }

    public bool Unregister(long handle)
    {
        int removed = 0;
        PapyrusValues.Check(RadicleNative.PapyrusRuntimeUnregister(Runtime, handle, &removed), "radicle_papyrus_runtime_unregister");
        return removed != 0;
    }

    public int AdvanceClock(string owner, double deltaSeconds, PapyrusClock clock)
    {
        int fired = 0;
        CheckAfterCallbacks(RadicleNative.PapyrusRuntimeAdvanceClock(Runtime, owner, deltaSeconds, clock, &fired), "radicle_papyrus_runtime_advance_clock");
        return fired;
    }

    public void RegisterNativeEx(string script, string function, PapyrusNativeKind kind, string requiredScript, uint flags) =>
        PapyrusValues.Check(
            RadicleNative.PapyrusRuntimeRegisterNativeEx(Runtime, script, function, kind, requiredScript, flags),
            "radicle_papyrus_runtime_register_native_ex");

    public void SetEngineCallbacks(PapyrusEngineHandlers? handlers)
    {
        var runtime = Runtime;
        if (handlers is null)
        {
            PapyrusValues.Check(RadicleNative.PapyrusRuntimeSetEngineCallbacks(runtime, null), "radicle_papyrus_runtime_set_engine_callbacks");
            _engineCallNative = null;
            _engineGetProperty = null;
            _engineSetProperty = null;
            return;
        }
        bool fresh = _engineBlock == IntPtr.Zero;
        if (fresh) _engineBlock = Marshal.AllocHGlobal(sizeof(PapyrusEngineCallbacks));
        var previous = fresh ? default : *(PapyrusEngineCallbacks*)_engineBlock;
        PapyrusEngineCallbacks engine = default;
        engine.StructSize = (nuint)sizeof(PapyrusEngineCallbacks);
        EngineCallNativeCallback callNative = (_, call) => Guard(() => EngineCallNative(call, handlers.CallNative), 0);
        engine.CallNativeEx = Marshal.GetFunctionPointerForDelegate(callNative);
        EngineGetPropertyCallback? getProperty = null;
        if (handlers.GetProperty is { } getPropertyHandler)
        {
            getProperty = (_, obj, name, outValue) => EngineGetProperty(obj, name, outValue, getPropertyHandler);
            engine.GetProperty = Marshal.GetFunctionPointerForDelegate(getProperty);
        }
        EngineSetPropertyCallback? setProperty = null;
        if (handlers.SetProperty is { } setPropertyHandler)
        {
            setProperty = (_, obj, name, value) => EngineSetProperty(obj, name, value, setPropertyHandler);
            engine.SetProperty = Marshal.GetFunctionPointerForDelegate(setProperty);
        }
        *(PapyrusEngineCallbacks*)_engineBlock = engine;
        try
        {
            PapyrusValues.Check(RadicleNative.PapyrusRuntimeSetEngineCallbacks(runtime, (PapyrusEngineCallbacks*)_engineBlock), "radicle_papyrus_runtime_set_engine_callbacks");
        }
        catch
        {
            *(PapyrusEngineCallbacks*)_engineBlock = previous;
            throw;
        }
        _engineCallNative = callNative;
        _engineGetProperty = getProperty;
        _engineSetProperty = setProperty;
    }

    public (ulong Ticket, PapyrusStatus Status) CallFunction(ulong instance, string function, params object?[] arguments)
    {
        using var values = new PapyrusValueSet(arguments ?? Array.Empty<object?>());
        ulong ticket = 0;
        PapyrusStatus status = PapyrusStatus.InternalError;
        fixed (IntPtr* args = values.Handles)
        {
            CheckAfterCallbacks(
                RadicleNative.PapyrusRuntimeCallFunction(Runtime, instance, function, args, (nuint)values.Handles.Length, &ticket, &status),
                "radicle_papyrus_runtime_call_function");
        }
        return (ticket, status);
    }

    public CallResultInfo CallResult(ulong ticket, bool consume)
    {
        var runtime = Runtime;
        PapyrusCallInfo info = default;
        info.StructSize = (nuint)sizeof(PapyrusCallInfo);
        IntPtr value = IntPtr.Zero;
        var status = RadicleNative.PapyrusRuntimeCallResult(runtime, ticket, consume ? 1 : 0, &info, &value);
        if (status == Status.NotFound) return new CallResultInfo(false, default, default, 0, null);
        PapyrusValues.Check(status, "radicle_papyrus_runtime_call_result");
        try
        {
            var read = value != IntPtr.Zero ? PapyrusValues.Read(value) : null;
            return new CallResultInfo(true, info.State, info.Status, info.Instructions, read);
        }
        finally
        {
            if (value != IntPtr.Zero) RadicleNative.PapyrusValueDestroy(value);
        }
    }

    public bool HasScript(string name) => RadicleNative.PapyrusRuntimeHasScript(Runtime, name) != 0;

    public bool InstanceDeclares(ulong instance, string function) => RadicleNative.PapyrusRuntimeInstanceDeclares(Runtime, instance, function) != 0;

    public string? InstanceScript(ulong instance) => Marshal.PtrToStringUTF8(RadicleNative.PapyrusRuntimeInstanceScript(Runtime, instance));

    public IReadOnlyList<ulong> ListInstances()
    {
        var runtime = Runtime;
        while (true)
        {
            nuint count = 0;
            PapyrusValues.Check(RadicleNative.PapyrusRuntimeListInstances(runtime, null, 0, &count), "radicle_papyrus_runtime_list_instances");
            if (count == 0) return Array.Empty<ulong>();
            var ids = new ulong[checked((int)count)];
            nuint total;
            fixed (ulong* p = ids)
            {
                PapyrusValues.Check(RadicleNative.PapyrusRuntimeListInstances(runtime, p, count, &total), "radicle_papyrus_runtime_list_instances");
            }
            if (total > count) continue;
            return total == count ? ids : ids.AsSpan(0, (int)total).ToArray();
        }
    }

    public PapyrusRuntimeStats GetStats()
    {
        PapyrusRuntimeStats stats = default;
        stats.StructSize = (nuint)sizeof(PapyrusRuntimeStats);
        PapyrusValues.Check(RadicleNative.PapyrusRuntimeGetStats(Runtime, &stats), "radicle_papyrus_runtime_get_stats");
        return stats;
    }

    public void SetOninitPolicy(bool queue) =>
        PapyrusValues.Check(
            RadicleNative.PapyrusRuntimeSetOninitPolicy(Runtime, queue ? PapyrusOninitPolicy.Queue : PapyrusOninitPolicy.Refuse),
            "radicle_papyrus_runtime_set_oninit_policy");

    public void SetMaxParks(int parks) =>
        PapyrusValues.Check(RadicleNative.PapyrusRuntimeSetMaxParks(Runtime, parks), "radicle_papyrus_runtime_set_max_parks");

    public void Dispose()
    {
        if (_callbacks > 0) throw new InvalidOperationException("RadiclePapyrusRuntime cannot be disposed from one of its callbacks.");
        if (_runtime != IntPtr.Zero) RadicleNative.PapyrusRuntimeDestroy(_runtime);
        _runtime = IntPtr.Zero;
        if (_engineBlock != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_engineBlock);
            _engineBlock = IntPtr.Zero;
        }
        foreach (var pex in _pex) RadicleNative.PexDestroy(pex);
        _pex.Clear();
        GC.KeepAlive(_gate);
        GC.KeepAlive(_native);
        GC.KeepAlive(_override);
        GC.KeepAlive(_latent);
        GC.KeepAlive(_engineCallNative);
        GC.KeepAlive(_engineGetProperty);
        GC.KeepAlive(_engineSetProperty);
    }

    private static int EngineCallNative(IntPtr call, Func<NativeCallView, bool> callNative)
    {
        var view = new NativeCallView(call);
        try { return callNative(view) ? 1 : 0; }
        finally { view.Close(); }
    }

    private int EngineGetProperty(IntPtr obj, IntPtr name, IntPtr outValue, Func<PapyrusObjectRef, string, (bool Handled, object? Value)> getProperty)
    {
        *(IntPtr*)outValue = IntPtr.Zero;
        return Guard(() =>
        {
            var (handled, value) = getProperty(PapyrusValues.ReadObject(obj), Text(name));
            if (!handled) return 0;
            *(IntPtr*)outValue = PapyrusValues.Make(value);
            return 1;
        }, 0);
    }

    private int EngineSetProperty(IntPtr obj, IntPtr name, IntPtr value, Func<PapyrusObjectRef, string, object?, bool> setProperty) =>
        Guard(() => setProperty(PapyrusValues.ReadObject(obj), Text(name), PapyrusValues.Read(value)) ? 1 : 0, 0);

    private IntPtr Runtime => _runtime != IntPtr.Zero ? _runtime : throw new ObjectDisposedException(nameof(RadiclePapyrusRuntime));

    private static string Text(IntPtr pointer) => Marshal.PtrToStringUTF8(pointer) ?? string.Empty;

    private static bool Take(Status status, IntPtr read, string operation, out object? value)
    {
        value = null;
        if (status == Status.NotFound) return false;
        PapyrusValues.Check(status, operation);
        try { value = PapyrusValues.Read(read); }
        finally { RadicleNative.PapyrusValueDestroy(read); }
        return true;
    }

    private void CheckAfterCallbacks(Status status, string operation)
    {
        if (_fault is { } fault)
        {
            _fault = null;
            ExceptionDispatchInfo.Throw(fault);
        }
        PapyrusValues.Check(status, operation);
    }

    private T Guard<T>(Func<T> body, T failed)
    {
        _callbacks += 1;
        try { return body(); }
        catch (Exception fault)
        {
            _fault ??= fault;
            return failed;
        }
        finally { _callbacks -= 1; }
    }
}
#endif
