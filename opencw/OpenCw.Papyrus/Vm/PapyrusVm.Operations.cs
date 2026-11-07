using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed partial class PapyrusVm
{
    private string? Arithmetic(PapyrusFrame frame, PapyrusInstruction instruction)
    {
        var left = Read(frame, instruction.Left);
        var right = Read(frame, instruction.Right);
        if (!left.IsNumeric || !right.IsNumeric)
        {
            return $"{instruction.Opcode} needs two numeric operands";
        }

        var bothInt = left.Kind == PapyrusValueKind.Int && right.Kind == PapyrusValueKind.Int;
        if (instruction.Opcode == PapyrusOpcode.Modulo)
        {
            if (!bothInt)
            {
                return "modulo needs two integer operands";
            }
            if (right.Integer == 0)
            {
                return "modulo by zero has no defined result";
            }
            Write(frame, instruction.Result, PapyrusValue.FromInt(left.Integer % right.Integer));
            return null;
        }

        if (instruction.Opcode == PapyrusOpcode.Divide && right.AsNumber() == 0)
        {
            return "division by zero has no defined result";
        }

        var result = instruction.Opcode switch
        {
            PapyrusOpcode.Add => left.AsNumber() + right.AsNumber(),
            PapyrusOpcode.Subtract => left.AsNumber() - right.AsNumber(),
            PapyrusOpcode.Multiply => left.AsNumber() * right.AsNumber(),
            _ => left.AsNumber() / right.AsNumber(),
        };
        Write(frame, instruction.Result,
            bothInt && instruction.Opcode != PapyrusOpcode.Divide
                ? PapyrusValue.FromInt((long)result)
                : bothInt
                    ? PapyrusValue.FromInt((long)Math.Truncate(result))
                    : PapyrusValue.FromFloat(result));
        return null;
    }

    private static string? Negate(PapyrusFrame frame, PapyrusInstruction instruction)
    {
        var value = Read(frame, instruction.Left);
        if (!value.IsNumeric)
        {
            return "negation needs a numeric operand";
        }
        Write(frame, instruction.Result, value.Kind == PapyrusValueKind.Int
            ? PapyrusValue.FromInt(-value.Integer)
            : PapyrusValue.FromFloat(-value.Real));
        return null;
    }

    private static string? Compare(PapyrusFrame frame, PapyrusInstruction instruction)
    {
        var left = Read(frame, instruction.Left);
        var right = Read(frame, instruction.Right);
        int order;
        if (left.IsNumeric && right.IsNumeric)
        {
            order = left.AsNumber().CompareTo(right.AsNumber());
        }
        else if (left.Kind == PapyrusValueKind.String && right.Kind == PapyrusValueKind.String)
        {
            order = string.CompareOrdinal(left.Text, right.Text);
        }
        else if (left.Kind == right.Kind && left.Kind is PapyrusValueKind.Bool or PapyrusValueKind.None)
        {
            order = left.Flag.CompareTo(right.Flag);
        }
        else
        {
            return $"{instruction.Opcode} cannot compare a {left.Kind} with a {right.Kind}";
        }

        var result = instruction.Opcode switch
        {
            PapyrusOpcode.CompareEqual => order == 0,
            PapyrusOpcode.CompareLess => order < 0,
            PapyrusOpcode.CompareLessOrEqual => order <= 0,
            PapyrusOpcode.CompareGreater => order > 0,
            _ => order >= 0,
        };
        Write(frame, instruction.Result, PapyrusValue.FromBool(result));
        return null;
    }

    private static string? Cast(PapyrusFrame frame, PapyrusInstruction instruction)
    {
        var value = Read(frame, instruction.Left);
        var target = instruction.Name ?? "";
        PapyrusValue cast;
        switch (target.ToLowerInvariant())
        {
            case "int":
                cast = value.Kind switch
                {
                    PapyrusValueKind.Int => value,
                    PapyrusValueKind.Float => PapyrusValue.FromInt((long)Math.Truncate(value.Real)),
                    PapyrusValueKind.Bool => PapyrusValue.FromInt(value.Flag ? 1 : 0),
                    PapyrusValueKind.String when long.TryParse(value.Text, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var parsed) => PapyrusValue.FromInt(parsed),
                    _ => PapyrusValue.FromInt(0),
                };
                break;
            case "float":
                cast = value.Kind switch
                {
                    PapyrusValueKind.Float => value,
                    PapyrusValueKind.Int => PapyrusValue.FromFloat(value.Integer),
                    _ => PapyrusValue.FromFloat(0),
                };
                break;
            case "bool":
                cast = PapyrusValue.FromBool(value.Kind switch
                {
                    PapyrusValueKind.Bool => value.Flag,
                    PapyrusValueKind.Int => value.Integer != 0,
                    PapyrusValueKind.Float => value.Real != 0,
                    PapyrusValueKind.String => !string.IsNullOrEmpty(value.Text),
                    _ => false,
                });
                break;
            case "string":
                cast = PapyrusValue.FromString(value.ToString());
                break;
            default:
                return $"a cast to {target} has no defined result";
        }
        Write(frame, instruction.Result, cast);
        return null;
    }

    private static string? Is(PapyrusFrame frame, PapyrusInstruction instruction)
    {
        var value = Read(frame, instruction.Left);
        var name = instruction.Name ?? "";
        var matches = name.ToLowerInvariant() switch
        {
            "int" => value.Kind == PapyrusValueKind.Int,
            "float" => value.Kind == PapyrusValueKind.Float,
            "bool" => value.Kind == PapyrusValueKind.Bool,
            "string" => value.Kind == PapyrusValueKind.String,
            "object" => value.Kind == PapyrusValueKind.Object,
            "none" => value.Kind == PapyrusValueKind.None,
            _ => value.Kind == PapyrusValueKind.Object
                 && string.Equals(value.Reference?.GetType().Name, name, StringComparison.OrdinalIgnoreCase),
        };
        Write(frame, instruction.Result, PapyrusValue.FromBool(matches));
        return null;
    }

    private static string? ArrayGet(PapyrusFrame frame, PapyrusInstruction instruction)
    {
        var array = Array(frame, instruction.Receiver);
        var index = (int)Read(frame, instruction.Left).Integer;
        if (index < 0 || index >= array.Count)
        {
            return $"an array index of {index} is outside the array";
        }
        Write(frame, instruction.Result, array[index]);
        return null;
    }

    private static string? ArraySet(PapyrusFrame frame, PapyrusInstruction instruction)
    {
        var array = Array(frame, instruction.Receiver);
        var index = (int)Read(frame, instruction.Left).Integer;
        if (index < 0 || index > array.Count)
        {
            return $"an array index of {index} is outside the array";
        }
        if (index == array.Count)
        {
            array.Add(Read(frame, instruction.Right));
            return null;
        }
        array[index] = Read(frame, instruction.Right);
        return null;
    }

    private static string? StructGet(PapyrusFrame frame, PapyrusInstruction instruction)
    {
        var fields = Struct(frame, instruction.Receiver);
        var name = instruction.Name ?? "";
        if (!fields.TryGetValue(name, out var value))
        {
            return $"the struct has no member named {name}";
        }
        Write(frame, instruction.Result, value);
        return null;
    }

    private static List<PapyrusValue> Array(PapyrusFrame frame, PapyrusOperand operand) =>
        Read(frame, operand).Reference as List<PapyrusValue>
        ?? throw new InvalidOperationException("the operand does not hold an array");

    private static Dictionary<string, PapyrusValue> Struct(PapyrusFrame frame, PapyrusOperand operand) =>
        Read(frame, operand).Reference as Dictionary<string, PapyrusValue>
        ?? throw new InvalidOperationException("the operand does not hold a struct");

    private static PapyrusValue Read(PapyrusFrame frame, PapyrusOperand operand) => operand.Kind switch
    {
        PapyrusOperandKind.None => PapyrusValue.None,
        PapyrusOperandKind.Constant => operand.Value,
        PapyrusOperandKind.Self => frame.Self,
        PapyrusOperandKind.Slot when operand.Slot >= 0 && operand.Slot < frame.Locals.Length => frame.Locals[operand.Slot],
        PapyrusOperandKind.Slot => throw new InvalidOperationException($"the slot {operand.Slot} is outside the frame"),
        _ => throw new InvalidOperationException($"the variable {operand.Name} has no storage"),
    };

    private static void Write(PapyrusFrame frame, PapyrusOperand operand, PapyrusValue value)
    {
        switch (operand.Kind)
        {
            case PapyrusOperandKind.None:
                return;
            case PapyrusOperandKind.Slot when operand.Slot >= 0 && operand.Slot < frame.Locals.Length:
                frame.Locals[operand.Slot] = value;
                return;
            case PapyrusOperandKind.Slot:
                throw new InvalidOperationException($"the slot {operand.Slot} is outside the frame");
            default:
                throw new InvalidOperationException($"a {operand.Kind} operand cannot be written");
        }
    }
}

public static class EvidenceKeys
{
    public const string Frames = "vm.frames.explicit_stack";
    public const string Return = "vm.control_flow.return";
    public const string Parameters = "vm.parameters.passing";
    public const string NativeTarget = "vm.native.target_contract";
    public const string Assignment = "vm.assignment.defaults";
    public const string Arithmetic = "vm.arithmetic.comparisons";
    public const string Types = "vm.types.casts_and_tests";
    public const string Dispatch = "vm.dispatch.property_and_function";
    public const string Arrays = "vm.arrays.semantics";
    public const string Structs = "vm.structs.semantics";
    public const string Waits = "vm.latent.waits";
    public const string States = "vm.states.gotostate";
    public const string Variables = "vm.variables.instance_storage";
    public const string Unsupported = "vm.ir.unsupported_operation";
    public const string OnInitBarrier = "vm.lifecycle.oninit_barrier";
    public const string EventDuringOnInit = "vm.lifecycle.event_during_oninit";
    public const string SchedulerBudget = "vm.scheduler.instruction_budget";
    public const string Host = "vm.host.single_script_host";
    public const string FailClosed = "vm.errors.fail_closed";

    public static string For(PapyrusOpcode opcode) => opcode switch
    {
        PapyrusOpcode.Nop => Frames,
        PapyrusOpcode.Jump or PapyrusOpcode.JumpIfFalse or PapyrusOpcode.JumpIfTrue or PapyrusOpcode.Return => Return,
        PapyrusOpcode.CallStatic or PapyrusOpcode.CallMethod => NativeTarget,
        PapyrusOpcode.Assign => Assignment,
        PapyrusOpcode.Add or PapyrusOpcode.Subtract or PapyrusOpcode.Multiply or PapyrusOpcode.Divide
            or PapyrusOpcode.Modulo or PapyrusOpcode.Negate or PapyrusOpcode.Not
            or PapyrusOpcode.CompareEqual or PapyrusOpcode.CompareLess or PapyrusOpcode.CompareLessOrEqual
            or PapyrusOpcode.CompareGreater or PapyrusOpcode.CompareGreaterOrEqual => Arithmetic,
        PapyrusOpcode.Cast or PapyrusOpcode.Is => Types,
        PapyrusOpcode.PropertyGet or PapyrusOpcode.PropertySet or PapyrusOpcode.CallParent => Dispatch,
        PapyrusOpcode.ArrayCreate or PapyrusOpcode.ArrayLength or PapyrusOpcode.ArrayGetElement
            or PapyrusOpcode.ArraySetElement => Arrays,
        PapyrusOpcode.StructCreate or PapyrusOpcode.StructGet or PapyrusOpcode.StructSet => Structs,
        PapyrusOpcode.Wait => Waits,
        _ => Unsupported,
    };
}
