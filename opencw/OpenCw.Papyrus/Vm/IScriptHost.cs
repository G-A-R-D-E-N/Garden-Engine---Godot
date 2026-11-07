using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public interface IScriptHost
{
    EvidenceGate Gate { get; }

    ScriptOverrideRegistry? Overrides { get; set; }

    IReadOnlyList<ScriptInstance> Instances { get; }

    int WaitingCount { get; }

    ScriptInstance Attach(string scriptName, IReadOnlyDictionary<string, PapyrusValue>? properties = null,
        PapyrusOwnerId? owner = null);

    VmResult Initialize(ScriptInstance instance);

    VmResult RunEvent(ScriptInstance instance, string functionName, params PapyrusValue[] arguments);

    void RegisterNative(NativeId native, Func<IReadOnlyList<PapyrusValue>, PapyrusValue> body,
        NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null);

    void RegisterNative(NativeId native, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> body,
        NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null);

    bool TryRegisterNative(NativeId native, Func<IReadOnlyList<PapyrusValue>, PapyrusValue> body,
        NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null);

    bool TryRegisterNative(NativeId native, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> body,
        NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null);

    bool HasScript(string name);

    bool HasInvalidParentChain(string name);

    int RunStep(int budget);

    bool CompleteLatent(long token, PapyrusValue value);
}
