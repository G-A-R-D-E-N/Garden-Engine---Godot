using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.Vm;

/// <summary>The Papyrus value kinds the VM models.</summary>
public enum PapyrusValueKind
{
    None,
    Int,
    Float,
    Bool,
    String,
    Object,
}

/// <summary>
/// One Papyrus value.
/// </summary>
/// <remarks>
/// Object values carry a reference identity, never an attached script: the VM refuses to guess an
/// attached script from a bare reference, which is the native target contract.
/// </remarks>
public readonly record struct PapyrusValue
{
    private PapyrusValue(PapyrusValueKind kind, long integer, double real, bool flag, string? text, object? reference)
    {
        Kind = kind;
        Integer = integer;
        Real = real;
        Flag = flag;
        Text = text;
        Reference = reference;
    }

    public PapyrusValueKind Kind { get; }

    public long Integer { get; }

    public double Real { get; }

    public bool Flag { get; }

    public string? Text { get; }

    public object? Reference { get; }

    public static PapyrusValue None => default;

    public static PapyrusValue FromInt(long value) => new(PapyrusValueKind.Int, value, 0, false, null, null);

    public static PapyrusValue FromFloat(double value) => new(PapyrusValueKind.Float, 0, value, false, null, null);

    public static PapyrusValue FromBool(bool value) => new(PapyrusValueKind.Bool, 0, 0, value, null, null);

    public static PapyrusValue FromString(string value) =>
        new(PapyrusValueKind.String, 0, 0, false, value ?? "", null);

    public static PapyrusValue FromObject(object reference) =>
        new(PapyrusValueKind.Object, 0, 0, false, null, reference);

    public bool IsNumeric => Kind is PapyrusValueKind.Int or PapyrusValueKind.Float;

    public double AsNumber() => Kind switch
    {
        PapyrusValueKind.Int => Integer,
        PapyrusValueKind.Float => Real,
        _ => throw new InvalidOperationException($"a {Kind} value is not numeric"),
    };

    public bool AsBool() => Kind switch
    {
        PapyrusValueKind.Bool => Flag,
        PapyrusValueKind.None => false,
        _ => throw new InvalidOperationException($"a {Kind} value is not a boolean"),
    };

    public override string ToString() => Kind switch
    {
        PapyrusValueKind.None => "None",
        PapyrusValueKind.Int => Integer.ToString(CultureInfo.InvariantCulture),
        PapyrusValueKind.Float => Real.ToString("R", CultureInfo.InvariantCulture),
        PapyrusValueKind.Bool => Flag ? "True" : "False",
        PapyrusValueKind.String => Text ?? "",
        _ => Reference?.ToString() ?? "Object",
    };
}

/// <summary>The Garden IR operations the VM executes. None of them is a PEX opcode number or layout.</summary>
/// <remarks>
/// Which of them may run is decided by the evidence gate per semantic family, so an operation being
/// present here is not a claim that Garden runs it: an unproven family stops the VM and is counted.
/// </remarks>
public enum PapyrusOpcode
{
    Nop,
    Jump,
    JumpIfFalse,
    JumpIfTrue,
    Return,
    CallStatic,
    CallMethod,
    CallParent,
    Assign,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Negate,
    Not,
    CompareEqual,
    CompareLess,
    CompareLessOrEqual,
    CompareGreater,
    CompareGreaterOrEqual,
    Cast,
    Is,
    PropertyGet,
    PropertySet,
    ArrayCreate,
    ArrayLength,
    ArrayGetElement,
    ArraySetElement,
    StructCreate,
    StructGet,
    StructSet,
    Wait,
    Unsupported,
}

public enum PapyrusOperandKind
{
    /// <summary>Reads as None; a write to it is dropped.</summary>
    None,
    Slot,
    Constant,
    Self,
    /// <summary>A script instance variable. Its storage is UNPROVEN, so the VM gates it.</summary>
    Variable,
}

/// <summary>Where an operation reads or writes a value.</summary>
public readonly record struct PapyrusOperand(PapyrusOperandKind Kind, int Slot = -1, PapyrusValue Value = default,
    string? Name = null)
{
    public static PapyrusOperand Discard => default;

    public static PapyrusOperand SelfRef => new(PapyrusOperandKind.Self);

    public static PapyrusOperand Local(int slot) => new(PapyrusOperandKind.Slot, slot);

    public static PapyrusOperand Constant(PapyrusValue value) => new(PapyrusOperandKind.Constant, Value: value);

    public static PapyrusOperand Variable(string name) => new(PapyrusOperandKind.Variable, Name: name);
}

/// <summary>One IR operation with named fields.</summary>
/// <remarks>
/// <see cref="Receiver"/> is the object, array or struct an operation acts on, <see cref="Left"/> its
/// first source (or condition, index, or value written), <see cref="Right"/> its second source,
/// <see cref="Name"/> the function, property, member or type it names, <see cref="Script"/> the owning
/// script of a static call or the declared receiver type of a method call, and <see cref="Target"/> an
/// absolute jump target.
/// </remarks>
public sealed record PapyrusInstruction(PapyrusOpcode Opcode)
{
    public PapyrusOperand Result { get; init; }

    public PapyrusOperand Receiver { get; init; }

    public PapyrusOperand Left { get; init; }

    public PapyrusOperand Right { get; init; }

    public string? Script { get; init; }

    public string? Name { get; init; }

    public int Target { get; init; }

    public IReadOnlyList<PapyrusOperand> Arguments { get; init; } = Array.Empty<PapyrusOperand>();

    public bool UsesVariable =>
        Result.Kind == PapyrusOperandKind.Variable || Receiver.Kind == PapyrusOperandKind.Variable
        || Left.Kind == PapyrusOperandKind.Variable || Right.Kind == PapyrusOperandKind.Variable
        || Arguments.Any(argument => argument.Kind == PapyrusOperandKind.Variable);

    public static PapyrusInstruction Nop() => new(PapyrusOpcode.Nop);

    public static PapyrusInstruction Wait() => new(PapyrusOpcode.Wait);

    public static PapyrusInstruction Jump(int target) => new(PapyrusOpcode.Jump) { Target = target };

    public static PapyrusInstruction JumpIf(PapyrusOperand condition, int target, bool whenTrue) =>
        new(whenTrue ? PapyrusOpcode.JumpIfTrue : PapyrusOpcode.JumpIfFalse) { Left = condition, Target = target };

    public static PapyrusInstruction Return() => new(PapyrusOpcode.Return);

    public static PapyrusInstruction Return(PapyrusOperand value) => new(PapyrusOpcode.Return) { Left = value };

    public static PapyrusInstruction Assign(PapyrusOperand result, PapyrusOperand source) =>
        new(PapyrusOpcode.Assign) { Result = result, Left = source };

    public static PapyrusInstruction Binary(PapyrusOpcode opcode, PapyrusOperand result, PapyrusOperand left,
        PapyrusOperand right) =>
        new(opcode) { Result = result, Left = left, Right = right };

    public static PapyrusInstruction Unary(PapyrusOpcode opcode, PapyrusOperand result, PapyrusOperand source) =>
        new(opcode) { Result = result, Left = source };

    public static PapyrusInstruction Cast(PapyrusOperand result, PapyrusOperand source, string type) =>
        new(PapyrusOpcode.Cast) { Result = result, Left = source, Name = type };

    public static PapyrusInstruction Is(PapyrusOperand result, PapyrusOperand source, string type) =>
        new(PapyrusOpcode.Is) { Result = result, Left = source, Name = type };

    public static PapyrusInstruction CallMethod(PapyrusOperand receiver, string function, PapyrusOperand result,
        IReadOnlyList<PapyrusOperand> arguments, string? receiverType = null) =>
        new(PapyrusOpcode.CallMethod)
        {
            Receiver = receiver, Name = function, Result = result, Arguments = arguments, Script = receiverType,
        };

    public static PapyrusInstruction CallStatic(string script, string function, PapyrusOperand result,
        IReadOnlyList<PapyrusOperand> arguments) =>
        new(PapyrusOpcode.CallStatic) { Script = script, Name = function, Result = result, Arguments = arguments };

    public static PapyrusInstruction CallParent(string function, PapyrusOperand result,
        IReadOnlyList<PapyrusOperand> arguments) =>
        new(PapyrusOpcode.CallParent) { Name = function, Result = result, Arguments = arguments };

    public static PapyrusInstruction PropertyGet(PapyrusOperand result, PapyrusOperand receiver, string property) =>
        new(PapyrusOpcode.PropertyGet) { Result = result, Receiver = receiver, Name = property };

    public static PapyrusInstruction PropertySet(PapyrusOperand receiver, string property, PapyrusOperand value) =>
        new(PapyrusOpcode.PropertySet) { Receiver = receiver, Name = property, Left = value };

    public static PapyrusInstruction ArrayCreate(PapyrusOperand result, PapyrusOperand size) =>
        new(PapyrusOpcode.ArrayCreate) { Result = result, Left = size };

    public static PapyrusInstruction ArrayLength(PapyrusOperand result, PapyrusOperand array) =>
        new(PapyrusOpcode.ArrayLength) { Result = result, Receiver = array };

    public static PapyrusInstruction ArrayGet(PapyrusOperand result, PapyrusOperand array, PapyrusOperand index) =>
        new(PapyrusOpcode.ArrayGetElement) { Result = result, Receiver = array, Left = index };

    public static PapyrusInstruction ArraySet(PapyrusOperand array, PapyrusOperand index, PapyrusOperand value) =>
        new(PapyrusOpcode.ArraySetElement) { Receiver = array, Left = index, Right = value };

    public static PapyrusInstruction StructCreate(PapyrusOperand result) =>
        new(PapyrusOpcode.StructCreate) { Result = result };

    public static PapyrusInstruction StructGet(PapyrusOperand result, PapyrusOperand structure, string member) =>
        new(PapyrusOpcode.StructGet) { Result = result, Receiver = structure, Name = member };

    public static PapyrusInstruction StructSet(PapyrusOperand structure, string member, PapyrusOperand value) =>
        new(PapyrusOpcode.StructSet) { Receiver = structure, Name = member, Left = value };

    public static PapyrusInstruction Unsupported(string what) => new(PapyrusOpcode.Unsupported) { Name = what };
}

/// <summary>
/// One function. Slots 0..ParameterCount-1 are its parameters and the next LocalCount its locals.
/// <see cref="State"/> is "" for the empty state, a state name, or null for a property handler.
/// </summary>
public sealed record PapyrusFunction(
    string Name,
    IReadOnlyList<PapyrusInstruction> Instructions,
    int ParameterCount = 0,
    bool IsGlobal = false,
    int LocalCount = 0,
    bool IsNative = false,
    string? State = "");

public sealed record PapyrusScript(
    string Name,
    IReadOnlyList<PapyrusFunction> Functions,
    IReadOnlyList<string> Properties,
    string? Parent = null,
    string AutoState = "")
{
    /// <summary>The empty-state function of that name; other states and property handlers are not dispatched.</summary>
    public PapyrusFunction? FindFunction(string name) =>
        Functions.FirstOrDefault(function => function.State == ""
                                             && string.Equals(function.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The identity of a native: its owning script and its function, compared without case.</summary>
public readonly record struct NativeId(string Script, string Function)
{
    public bool Equals(NativeId other) =>
        string.Equals(Script, other.Script, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Function, other.Function, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Script ?? ""),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Function ?? ""));

    public override string ToString() => $"{Script}.{Function}";
}
