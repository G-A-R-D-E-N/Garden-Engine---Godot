#if RADICLE_HOST_AVAILABLE
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Radicle.Interop.Native;

namespace OpenCommonwealth.Services.Papyrus.Host;

// Invariant: the Host skeleton creates a runtime with the default-allow gate, loads a PEX set, runs a function and serves the engine callbacks; it carries no engine state of its own.
public sealed class RadiclePapyrusHost : IDisposable
{
    // Requires: a native call handler returns an owned value that the runtime frees, or null when unhandled.
    public delegate RadicleValue? NativeCallHandler(
        string script, string function, IReadOnlyList<RadicleValue> arguments);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GateCallback(IntPtr user, IntPtr key);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int GetValueCallback(IntPtr user, IntPtr target, IntPtr name, IntPtr* outValue);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetValueCallback(IntPtr user, IntPtr target, IntPtr name, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int CallNativeCallback(
        IntPtr user, IntPtr script, IntPtr function, IntPtr* arguments, nuint argumentCount, IntPtr* outValue);

    private readonly IntPtr _runtime;
    private readonly List<IntPtr> _pexFiles = new();
    private readonly GateCallback _gate;
    private readonly GetValueCallback _getValue;
    private readonly SetValueCallback _setValue;
    private readonly CallNativeCallback _callNative;
    private GCHandle _self;
    private bool _disposed;

    // Requires: null means "not handled", which the interpreter reports as a host error.
    public NativeCallHandler? NativeCall { get; set; }

    private unsafe RadiclePapyrusHost()
    {
        _self = GCHandle.Alloc(this);
        _gate = Gate;
        _getValue = GetValue;
        _setValue = SetValue;
        _callNative = CallNative;

        var callbacks = new PapyrusCallbacks
        {
            User = GCHandle.ToIntPtr(_self),
            Gate = Marshal.GetFunctionPointerForDelegate(_gate),
            GetVariable = Marshal.GetFunctionPointerForDelegate(_getValue),
            SetVariable = Marshal.GetFunctionPointerForDelegate(_setValue),
            GetProperty = Marshal.GetFunctionPointerForDelegate(_getValue),
            SetProperty = Marshal.GetFunctionPointerForDelegate(_setValue),
            CallNative = Marshal.GetFunctionPointerForDelegate(_callNative),
        };

        IntPtr runtime;
        var status = RadicleNative.PapyrusCreate(&callbacks, &runtime);
        if (status != Status.Ok || runtime == IntPtr.Zero)
        {
            _self.Free();
            throw new InvalidOperationException($"radicle_papyrus_create failed: {status}");
        }
        _runtime = runtime;
    }

    public static RadiclePapyrusHost Create() => new();

    public int PexCount => (int)RadicleNative.PapyrusPexCount(_runtime);

    // Requires: the payload parses with radicle_pex_parse; the runtime borrows the parsed handle until Dispose.
    public unsafe void AddPex(byte[] bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bytes);

        IntPtr pex;
        fixed (byte* data = bytes)
        {
            var parse = RadicleNative.PexParse(data, (nuint)bytes.Length, &pex);
            if (parse != Status.Ok || pex == IntPtr.Zero)
            {
                throw new InvalidOperationException($"radicle_pex_parse failed: {parse}");
            }
        }

        var add = RadicleNative.PapyrusAddPex(_runtime, pex);
        if (add != Status.Ok)
        {
            RadicleNative.PexDestroy(pex);
            throw new InvalidOperationException($"radicle_papyrus_add_pex failed: {add}");
        }

        _pexFiles.Add(pex);
    }

    public unsafe RadicleRunResult Run(string script, string function)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(function);

        IntPtr result = IntPtr.Zero;
        PapyrusStatus scriptStatus = PapyrusStatus.Ok;
        nuint instructions = 0;
        var status = RadicleNative.PapyrusRun(
            _runtime, script, function, null, 0, &result, &scriptStatus, &instructions);
        if (status != Status.Ok)
        {
            throw new InvalidOperationException($"radicle_papyrus_run failed: {status}");
        }

        var value = result == IntPtr.Zero ? null : new RadicleValue(result, owned: true);
        return new RadicleRunResult(scriptStatus, value, instructions);
    }

    private static int Gate(IntPtr user, IntPtr key) => 1;

    private static unsafe int GetValue(IntPtr user, IntPtr target, IntPtr name, IntPtr* outValue) => 0;

    private static int SetValue(IntPtr user, IntPtr target, IntPtr name, IntPtr value) => 0;

    private unsafe int CallNative(IntPtr user, IntPtr script, IntPtr function, IntPtr* arguments, nuint argumentCount,
        IntPtr* outValue)
    {
        var handler = NativeCall;
        if (handler is null) return 0;

        var values = new RadicleValue[(int)argumentCount];
        for (var i = 0; i < values.Length; ++i)
        {
            values[i] = new RadicleValue(arguments[i], owned: false);
        }

        var produced = handler(
            Marshal.PtrToStringUTF8(script) ?? string.Empty,
            Marshal.PtrToStringUTF8(function) ?? string.Empty,
            values);
        if (produced is null) return 0;

        *outValue = produced.TakeHandle();
        return 1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        RadicleNative.PapyrusDestroy(_runtime);
        foreach (var pex in _pexFiles)
        {
            RadicleNative.PexDestroy(pex);
        }
        _pexFiles.Clear();
        if (_self.IsAllocated) _self.Free();
    }
}

// Invariant: a run result owns its value handle and reports the script status and instruction count.
public sealed record RadicleRunResult(PapyrusStatus Status, RadicleValue? Value, nuint Instructions)
{
    public bool Ok => Status == PapyrusStatus.Ok;
}

// Invariant: an owned value handle is destroyed once, on Dispose or when TakeHandle transfers it.
public sealed class RadicleValue : IDisposable
{
    private IntPtr _handle;
    private readonly bool _owned;

    internal RadicleValue(IntPtr handle, bool owned)
    {
        _handle = handle;
        _owned = owned;
    }

    public bool IsValid => _handle != IntPtr.Zero;

    public PapyrusValueKind Kind => RadicleNative.PapyrusValueKindOf(_handle);

    public int AsInt() => RadicleNative.PapyrusValueIntOf(_handle);

    public float AsFloat() => RadicleNative.PapyrusValueFloatOf(_handle);

    public bool AsBool() => RadicleNative.PapyrusValueBoolOf(_handle) != 0;

    public string? AsText() => Marshal.PtrToStringUTF8(RadicleNative.PapyrusValueText(_handle));

    public static unsafe RadicleValue Int(int number)
    {
        IntPtr handle;
        var status = RadicleNative.PapyrusValueInt(number, &handle);
        if (status != Status.Ok || handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"radicle_papyrus_value_int failed: {status}");
        }
        return new RadicleValue(handle, owned: true);
    }

    internal IntPtr TakeHandle()
    {
        var handle = _handle;
        _handle = IntPtr.Zero;
        return handle;
    }

    public void Dispose()
    {
        if (_owned && _handle != IntPtr.Zero)
        {
            RadicleNative.PapyrusValueDestroy(_handle);
        }
        _handle = IntPtr.Zero;
    }
}
#endif
