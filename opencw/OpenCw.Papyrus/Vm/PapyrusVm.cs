using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.Vm;

/// <summary>What a native call targets.</summary>
public enum NativeTargetKind
{
    /// <summary>No self. The function must be global.</summary>
    Static,

    /// <summary>The attached ScriptObject of the required script.</summary>
    ScriptInstance,

    /// <summary>An engine identity or reference, never an arbitrary attached custom script.</summary>
    EngineObject,
}

/// <summary>The natives a script can reach. The host owns the target contract.</summary>
public interface INativeTarget
{
    PapyrusValue Invoke(NativeId native, NativeTargetKind kind, PapyrusValue target, IReadOnlyList<PapyrusValue> arguments);

    PapyrusValue GetProperty(PapyrusValue target, string name);

    void SetProperty(PapyrusValue target, string name, PapyrusValue value);
}

/// <summary>An object that carries an attached script, so a method call on it can reach that script's functions.</summary>
public interface IScriptObject
{
    PapyrusScript Script { get; }
}

/// <summary>Scripts by name. Papyrus identifiers are not case sensitive.</summary>
public sealed class ScriptLibrary
{
    private readonly Dictionary<string, PapyrusScript> _scripts = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _scripts.Count;

    public IReadOnlyList<string> Names => _scripts.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();

    public void Add(PapyrusScript script)
    {
        if (string.IsNullOrWhiteSpace(script.Name))
        {
            throw new ArgumentException("a script needs a name", nameof(script));
        }
        if (!_scripts.TryAdd(script.Name, script))
        {
            throw new InvalidOperationException($"the script {script.Name} is already in the library");
        }
    }

    public bool TryGet(string name, out PapyrusScript script) => _scripts.TryGetValue(name, out script!);

    public PapyrusScript Get(string name) =>
        _scripts.TryGetValue(name, out var script)
            ? script
            : throw new KeyNotFoundException($"the library has no script named {name}");
}

public enum VmStopReason
{
    Completed,

    /// <summary>The evidence gate refused the next semantic.</summary>
    GateStopped,

    BudgetExhausted,

    /// <summary>A latent wait parked the execution. Garden approximates it and counts every reliance.</summary>
    Paused,

    /// <summary>The program is malformed, or an operation had no defined result.</summary>
    ModelError,

    NativeFailed,

    /// <summary>The explicit frame stack hit its limit. The C# stack is never used for calls.</summary>
    FrameDepthExceeded,

    Waiting,
}

public sealed record VmResult(
    VmStopReason Reason,
    PapyrusValue Value,
    string? Key,
    string? Message,
    int Instructions)
{
    public bool Completed => Reason == VmStopReason.Completed;
}

public sealed class PapyrusFrame
{
    public PapyrusFrame(PapyrusScript script, PapyrusFunction function, PapyrusValue self, int localCount)
    {
        Script = script;
        Function = function;
        Self = self;
        Locals = new PapyrusValue[localCount];
    }

    public PapyrusScript Script { get; }

    public PapyrusFunction Function { get; }

    public PapyrusValue Self { get; }

    public PapyrusValue[] Locals { get; }

    public int Pc { get; set; }

    public PapyrusValue Return { get; set; }

    public bool Returned { get; set; }

    /// <summary>Where the callee's return value lands in the caller.</summary>
    public PapyrusOperand ReturnTo { get; set; }
}

public sealed partial class PapyrusVm
{
    public const int DefaultFrameDepthLimit = 256;
    public const int DefaultBudget = PapyrusHostDefaults.StepBudget;
    private const int MinimumLocals = 16;

    private readonly ScriptLibrary _library;
    private readonly EvidenceGate _gate;
    private readonly INativeTarget? _natives;
    private readonly int _frameDepthLimit;
    private readonly int _budget;

    public PapyrusVm(
        ScriptLibrary library,
        EvidenceGate gate,
        INativeTarget? natives = null,
        int frameDepthLimit = DefaultFrameDepthLimit,
        int budget = DefaultBudget)
    {
        _library = library;
        _gate = gate;
        _natives = natives;
        _frameDepthLimit = frameDepthLimit;
        _budget = budget;
    }

    public int InstructionCount { get; private set; }

    internal int Budget => _budget;

    public VmResult Call(string scriptName, string functionName, params PapyrusValue[] arguments) =>
        _library.TryGet(scriptName, out var script)
            ? Call(script, script.FindFunction(functionName), PapyrusValue.None, arguments)
            : new VmResult(VmStopReason.ModelError, PapyrusValue.None, null, $"no script named {scriptName}", 0);

    public VmResult Call(PapyrusScript script, PapyrusFunction? function, PapyrusValue self,
        IReadOnlyList<PapyrusValue> arguments)
    {
        var result = RunSlice(Prepare(script, function, self, arguments), _budget);
        return result.Reason == VmStopReason.Waiting
            ? result with { Reason = VmStopReason.ModelError, Message = "latent waits require the ScriptHost event scheduler" }
            : result;
    }

    internal PapyrusExecution Prepare(PapyrusScript script, PapyrusFunction? function, PapyrusValue self,
        IReadOnlyList<PapyrusValue> arguments)
    {
        var execution = new PapyrusExecution();
        if (function is null)
        {
            execution.Result = new VmResult(VmStopReason.ModelError, PapyrusValue.None, null,
                $"the script {script.Name} has no requested function to run", 0);
            return execution;
        }
        if (!string.IsNullOrEmpty(script.AutoState))
        {
            var state = _gate.Gate(EvidenceKeys.States);
            if (!state.Allowed)
            {
                execution.Result = new VmResult(VmStopReason.GateStopped, PapyrusValue.None, state.Key,
                    $"the script {script.Name} starts in state {script.AutoState}: {state.Reason}", 0);
                return execution;
            }
        }
        var error = Push(execution, script, function, self, arguments, PapyrusOperand.Discard);
        if (error is not null && execution.Result is null)
            execution.Result = new VmResult(execution.FailureReason, PapyrusValue.None, null, error, 0);
        return execution;
    }

    internal VmResult RunSlice(PapyrusExecution execution, int budget)
    {
        if (execution.Result is not null) return execution.Result;
        var frames = execution.Frames;
        var instructions = 0;
        PapyrusValue completed = PapyrusValue.None;

        VmResult Finish(VmStopReason reason, PapyrusValue value, string? key = null, string? message = null)
        {
            InstructionCount += instructions;
            var result = new VmResult(reason, value, key, message, instructions);
            if (reason != VmStopReason.BudgetExhausted) execution.Result = result;
            return result;
        }

        try
        {
            while (frames.Count > 0)
            {
                var frame = frames[^1];
                if (frame.Pc >= frame.Function.Instructions.Count)
                {
                    if (!frame.Returned)
                    {
                        var returns = _gate.Gate(EvidenceKeys.Return);
                        if (!returns.Allowed)
                            return Finish(VmStopReason.GateStopped, PapyrusValue.None, returns.Key, returns.Reason);
                    }
                    completed = Pop(frames);
                    continue;
                }
                if (instructions >= budget)
                {
                    return Finish(VmStopReason.BudgetExhausted, PapyrusValue.None, message:
                        $"the script {frame.Script.Name} reached its instruction budget of {budget}");
                }

                var instruction = frame.Function.Instructions[frame.Pc++];
                var decision = _gate.Gate(EvidenceKeys.For(instruction.Opcode));
                if (decision.Allowed && instruction.Arguments.Count > 0)
                    decision = _gate.Gate(EvidenceKeys.Parameters);
                if (decision.Allowed && instruction.UsesVariable)
                {
                    decision = _gate.Gate(EvidenceKeys.Variables);
                }
                if (!decision.Allowed)
                {
                    return Finish(VmStopReason.GateStopped, PapyrusValue.None, decision.Key,
                        instruction.Name is null
                            ? $"{instruction.Opcode}: {decision.Reason}"
                            : $"{instruction.Opcode} {instruction.Name}: {decision.Reason}");
                }

                if (instruction.Opcode == PapyrusOpcode.Wait)
                {
                    return Finish(VmStopReason.Paused, PapyrusValue.None, decision.Key,
                        "a latent wait stops this execution; timed resumption is unproven");
                }

                instructions++;
                var error = Execute(execution, frame, instruction);
                if (error is not null)
                {
                    if (execution.Result is { } stopped)
                        return Finish(stopped.Reason, stopped.Value, stopped.Key, stopped.Message);
                    return Finish(execution.FailureReason, PapyrusValue.None, message: error);
                }
            }
        }
        catch (Exception failure)
        {
            return Finish(VmStopReason.ModelError, PapyrusValue.None, message: failure.Message);
        }
        return Finish(VmStopReason.Completed, completed);
    }

    private string? Execute(PapyrusExecution execution, PapyrusFrame frame, PapyrusInstruction instruction)
    {
        switch (instruction.Opcode)
        {
            case PapyrusOpcode.Nop:
                return null;
            case PapyrusOpcode.Jump:
                frame.Pc = instruction.Target;
                return null;
            case PapyrusOpcode.JumpIfFalse:
            case PapyrusOpcode.JumpIfTrue:
                if (Read(frame, instruction.Left).AsBool() == (instruction.Opcode == PapyrusOpcode.JumpIfTrue))
                {
                    frame.Pc = instruction.Target;
                }
                return null;
            case PapyrusOpcode.Assign:
                Write(frame, instruction.Result, Read(frame, instruction.Left));
                return null;
            case PapyrusOpcode.Add:
            case PapyrusOpcode.Subtract:
            case PapyrusOpcode.Multiply:
            case PapyrusOpcode.Divide:
            case PapyrusOpcode.Modulo:
                return Arithmetic(frame, instruction);
            case PapyrusOpcode.Negate:
                return Negate(frame, instruction);
            case PapyrusOpcode.Not:
                Write(frame, instruction.Result, PapyrusValue.FromBool(!Read(frame, instruction.Left).AsBool()));
                return null;
            case PapyrusOpcode.CompareEqual:
            case PapyrusOpcode.CompareLess:
            case PapyrusOpcode.CompareLessOrEqual:
            case PapyrusOpcode.CompareGreater:
            case PapyrusOpcode.CompareGreaterOrEqual:
                return Compare(frame, instruction);
            case PapyrusOpcode.Cast:
                return Cast(frame, instruction);
            case PapyrusOpcode.Is:
                return Is(frame, instruction);
            case PapyrusOpcode.Return:
                frame.Return = Read(frame, instruction.Left);
                frame.Returned = true;
                frame.Pc = frame.Function.Instructions.Count;
                return null;
            case PapyrusOpcode.CallMethod:
                return CallMethod(execution, frame, instruction);
            case PapyrusOpcode.CallStatic:
                return CallStatic(execution, frame, instruction);
            case PapyrusOpcode.CallParent:
                return CallParent(execution, frame, instruction);
            case PapyrusOpcode.PropertyGet:
                Write(frame, instruction.Result,
                    Natives().GetProperty(Read(frame, instruction.Receiver), instruction.Name ?? ""));
                return null;
            case PapyrusOpcode.PropertySet:
                Natives().SetProperty(Read(frame, instruction.Receiver), instruction.Name ?? "",
                    Read(frame, instruction.Left));
                return null;
            case PapyrusOpcode.ArrayCreate:
                Write(frame, instruction.Result, PapyrusValue.FromObject(new List<PapyrusValue>()));
                return null;
            case PapyrusOpcode.ArrayLength:
                Write(frame, instruction.Result, PapyrusValue.FromInt(Array(frame, instruction.Receiver).Count));
                return null;
            case PapyrusOpcode.ArrayGetElement:
                return ArrayGet(frame, instruction);
            case PapyrusOpcode.ArraySetElement:
                return ArraySet(frame, instruction);
            case PapyrusOpcode.StructCreate:
                Write(frame, instruction.Result,
                    PapyrusValue.FromObject(new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase)));
                return null;
            case PapyrusOpcode.StructGet:
                return StructGet(frame, instruction);
            case PapyrusOpcode.StructSet:
                Struct(frame, instruction.Receiver)[instruction.Name ?? ""] = Read(frame, instruction.Left);
                return null;
            default:
                return $"the operation {instruction.Opcode} has no implementation";
        }
    }

    private string? CallMethod(PapyrusExecution execution, PapyrusFrame frame, PapyrusInstruction instruction)
    {
        var name = instruction.Name ?? "";
        var receiver = Read(frame, instruction.Receiver);
        var arguments = Arguments(frame, instruction);
        if (receiver.Reference is IScriptObject owner && FindDeclared(owner.Script, name) is { } declared)
        {
            var dispatch = _gate.Gate(EvidenceKeys.Dispatch);
            if (!dispatch.Allowed)
            {
                return $"{dispatch.Key}: a method call to the script function {declared.Script.Name}.{name}: {dispatch.Reason}";
            }
            return Push(execution, declared.Script, declared.Function, receiver, arguments, instruction.Result);
        }
        var script = instruction.Script ?? (receiver.Reference as IScriptObject)?.Script.Name ?? "";
        var kind = receiver.Reference is EngineObjectReference ? NativeTargetKind.EngineObject : NativeTargetKind.ScriptInstance;
        return Native(execution, frame, instruction, new NativeId(script, name), kind, receiver, arguments);
    }

    private string? CallStatic(PapyrusExecution execution, PapyrusFrame frame, PapyrusInstruction instruction)
    {
        var script = instruction.Script ?? "";
        var name = instruction.Name ?? "";
        var arguments = Arguments(frame, instruction);
        if (_library.TryGet(script, out var owner) && owner.FindFunction(name) is { IsNative: false } declared)
        {
            var dispatch = _gate.Gate(EvidenceKeys.Dispatch);
            if (!dispatch.Allowed)
            {
                return $"{dispatch.Key}: a static call to the script function {script}.{name}: {dispatch.Reason}";
            }
            return Push(execution, owner, declared, PapyrusValue.None, arguments, instruction.Result);
        }
        return Native(execution, frame, instruction, new NativeId(script, name), NativeTargetKind.Static, frame.Self, arguments);
    }

    private string? CallParent(PapyrusExecution execution, PapyrusFrame frame, PapyrusInstruction instruction)
    {
        var name = instruction.Name ?? "";
        if (frame.Script.Parent is null || !_library.TryGet(frame.Script.Parent, out var parent))
        {
            return $"the script {frame.Script.Name} has no known parent script to call {name} on";
        }
        var callee = parent.FindFunction(name);
        if (callee is null)
        {
            return $"the parent script {parent.Name} has no function named {name}";
        }
        return callee.IsNative
            ? Native(execution, frame, instruction, new NativeId(parent.Name, name),
                NativeTargetKind.ScriptInstance, frame.Self, Arguments(frame, instruction))
            : Push(execution, parent, callee, frame.Self, Arguments(frame, instruction), instruction.Result);
    }

    private (PapyrusScript Script, PapyrusFunction Function)? FindDeclared(PapyrusScript script, string name)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var current = script; current is not null;)
        {
            if (!visited.Add(current.Name))
                throw new InvalidOperationException($"cyclic parent chain while resolving {script.Name}.{name}");
            if (current.FindFunction(name) is { IsNative: false } function)
            {
                return (current, function);
            }
            if (current.Parent is null || !_library.TryGet(current.Parent, out var parent) || ReferenceEquals(parent, current))
            {
                return null;
            }
            current = parent;
        }
        return null;
    }

    private static IReadOnlyList<PapyrusValue> Arguments(PapyrusFrame frame, PapyrusInstruction instruction) =>
        instruction.Arguments.Select(argument => Read(frame, argument)).ToList();

    private string? Push(PapyrusExecution execution, PapyrusScript script, PapyrusFunction function,
        PapyrusValue self, IReadOnlyList<PapyrusValue> arguments, PapyrusOperand returnTo)
    {
        if (function.IsNative)
            return $"the native declaration {script.Name}.{function.Name} has no bytecode body to run";
        var frames = execution.Frames;
        if (arguments.Count > 0 || function.ParameterCount > 0)
        {
            var parameters = _gate.Gate(EvidenceKeys.Parameters);
            if (!parameters.Allowed)
            {
                execution.Result = new VmResult(VmStopReason.GateStopped, PapyrusValue.None,
                    parameters.Key, parameters.Reason, 0);
                return parameters.Reason;
            }
        }
        if (frames.Count >= _frameDepthLimit)
        {
            execution.FailureReason = VmStopReason.FrameDepthExceeded;
            return $"the call chain exceeded {_frameDepthLimit} frames";
        }
        var localCount = Math.Max(Math.Max(function.ParameterCount + function.LocalCount, arguments.Count), MinimumLocals);
        var frame = new PapyrusFrame(script, function, self, localCount) { ReturnTo = returnTo };
        for (var index = 0; index < arguments.Count && index < frame.Locals.Length; ++index)
        {
            frame.Locals[index] = arguments[index];
        }
        frames.Add(frame);
        return null;
    }

    private static PapyrusValue Pop(List<PapyrusFrame> frames)
    {
        var finished = frames[^1];
        frames.RemoveAt(frames.Count - 1);
        if (frames.Count > 0)
        {
            Write(frames[^1], finished.ReturnTo, finished.Return);
        }
        return finished.Return;
    }

    private INativeTarget Natives() =>
        _natives ?? throw new InvalidOperationException("this VM has no native target host");
}
