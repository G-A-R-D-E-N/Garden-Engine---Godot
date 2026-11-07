using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Vm;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Papyrus;

public enum CallOperandRole
{
    FunctionName,
    Receiver,
    OwningScript,
    Result,
}

public static class RadicleScriptLowering
{
    public const int Version = 1;

    private static readonly string[] Mnemonics =
    {
        "nop", "iadd", "fadd", "isub", "fsub", "imul", "fmul", "idiv", "fdiv", "imod", "not", "ineg", "fneg",
        "assign", "cast", "cmp_eq", "cmp_lt", "cmp_lte", "cmp_gt", "cmp_gte", "jmp", "jmpt", "jmpf",
        "callmethod", "callparent", "callstatic", "return", "strcat", "propget", "propset", "array_create",
        "array_length", "array_getelement", "array_setelement", "array_findelement", "array_rfindelement", "is",
        "struct_create", "struct_get", "struct_set", "array_findstruct", "array_rfindstruct", "array_add",
        "array_insert", "array_removelast", "array_remove", "array_clear",
    };

    public static string Mnemonic(byte opcode) => opcode < Mnemonics.Length ? Mnemonics[opcode] : $"opcode_{opcode}";

    public static IReadOnlyList<CallOperandRole> Roles(string mnemonic) => mnemonic switch
    {
        "callmethod" => new[] { CallOperandRole.FunctionName, CallOperandRole.Receiver, CallOperandRole.Result },
        "callparent" => new[] { CallOperandRole.FunctionName, CallOperandRole.Result },
        "callstatic" => new[] { CallOperandRole.OwningScript, CallOperandRole.FunctionName, CallOperandRole.Result },
        _ => Array.Empty<CallOperandRole>(),
    };

    public static int OperandOf(string mnemonic, CallOperandRole role)
    {
        var roles = Roles(mnemonic);
        for (var position = 0; position < roles.Count; position++)
        {
            if (roles[position] == role)
            {
                return position;
            }
        }
        return -1;
    }

    public static IReadOnlyList<PapyrusScript> Lower(byte[] payload)
    {
        using var pex = PexFile.FromBytes(payload);
        return Lower(pex);
    }

    public static IReadOnlyList<PapyrusScript> Lower(PexFile pex) =>
        Enumerable.Range(0, pex.Objects.Count).Select(index => LowerObject(
            Encoding.Latin1.GetString(pex.Objects[index].Name),
            Encoding.Latin1.GetString(pex.Objects[index].ParentClassName),
            pex.ReadCode(index), pex.StringTable)).ToList();

    public static ScriptCacheConverter Converter { get; } =
        new(PapyrusIr.FormatVersion, Version, payload => PapyrusIr.ToJson(Lower(payload)));

    public static PapyrusScript LowerObject(string name, string parent, PexObjectCode code,
        IReadOnlyList<byte[]> stringTable)
    {
        string Text(int index) => index < 0 ? "" : Encoding.Latin1.GetString(stringTable[index]);
        var variables = code.Variables.ToDictionary(variable => Text(variable.Name), variable => Text(variable.Type),
            StringComparer.OrdinalIgnoreCase);
        var handlers = new Dictionary<int, string>();
        foreach (var property in code.Properties)
        {
            if (property.ReadFunction >= 0) handlers[property.ReadFunction] = Text(property.Name) + ".get";
            if (property.WriteFunction >= 0) handlers[property.WriteFunction] = Text(property.Name) + ".set";
        }

        var functions = code.Functions.Select((function, index) => LowerFunction(function,
            handlers.TryGetValue(index, out var handler) ? handler : Text(function.Name),
            function.State < 0 ? null : Text(code.States[function.State].Name),
            name, variables, Text)).ToList();
        return new PapyrusScript(name, functions, code.Properties.Select(property => Text(property.Name)).ToList(),
            parent.Length == 0 ? null : parent, Text(code.AutoStateName));
    }

    private static PapyrusFunction LowerFunction(Radicle.Interop.PexFunction function, string name, string? state,
        string self, IReadOnlyDictionary<string, string> variables, Func<int, string> text)
    {
        var slots = new Dictionary<string, (int Slot, string Type)>(StringComparer.OrdinalIgnoreCase);
        foreach (var typed in function.Parameters.Concat(function.Locals))
        {
            slots.TryAdd(text(typed.Name), (slots.Count, text(typed.Type)));
        }
        var context = new Context(slots, variables, self, text, function.Instructions.Count);
        var instructions = function.Instructions.Select((instruction, at) => context.Lower(instruction, at)).ToList();
        return new PapyrusFunction(name, instructions, function.Parameters.Count, function.IsGlobal,
            function.Locals.Count, function.IsNative, state);
    }

    private sealed class LoweringException(string message) : Exception(message);

    private sealed record Context(
        IReadOnlyDictionary<string, (int Slot, string Type)> Slots,
        IReadOnlyDictionary<string, string> Variables,
        string Self,
        Func<int, string> Text,
        int Count)
    {
        public PapyrusInstruction Lower(Radicle.Interop.PexInstruction instruction, int at)
        {
            var mnemonic = Mnemonic(instruction.Opcode);
            try
            {
                return LowerKnown(mnemonic, instruction.Arguments, at)
                       ?? PapyrusInstruction.Unsupported(mnemonic);
            }
            catch (LoweringException failure)
            {
                return PapyrusInstruction.Unsupported($"{mnemonic}: {failure.Message}");
            }
        }

        private PapyrusInstruction? LowerKnown(string mnemonic, IReadOnlyList<PexValue> a, int at)
        {
            PapyrusOperand O(int position) => Operand(Arg(a, position));
            string N(int position) => Name(Arg(a, position));
            IReadOnlyList<PapyrusOperand> Rest(int first) => a.Skip(first).Select(Operand).ToList();

            return mnemonic switch
            {
                "nop" => PapyrusInstruction.Nop(),
                "iadd" or "fadd" => PapyrusInstruction.Binary(PapyrusOpcode.Add, O(0), O(1), O(2)),
                "isub" or "fsub" => PapyrusInstruction.Binary(PapyrusOpcode.Subtract, O(0), O(1), O(2)),
                "imul" or "fmul" => PapyrusInstruction.Binary(PapyrusOpcode.Multiply, O(0), O(1), O(2)),
                "idiv" or "fdiv" => PapyrusInstruction.Binary(PapyrusOpcode.Divide, O(0), O(1), O(2)),
                "imod" => PapyrusInstruction.Binary(PapyrusOpcode.Modulo, O(0), O(1), O(2)),
                "not" => PapyrusInstruction.Unary(PapyrusOpcode.Not, O(0), O(1)),
                "ineg" or "fneg" => PapyrusInstruction.Unary(PapyrusOpcode.Negate, O(0), O(1)),
                "assign" => PapyrusInstruction.Assign(O(0), O(1)),
                "cast" => PapyrusInstruction.Cast(O(0), O(1), DeclaredType(Arg(a, 0))),
                "cmp_eq" => PapyrusInstruction.Binary(PapyrusOpcode.CompareEqual, O(0), O(1), O(2)),
                "cmp_lt" => PapyrusInstruction.Binary(PapyrusOpcode.CompareLess, O(0), O(1), O(2)),
                "cmp_lte" => PapyrusInstruction.Binary(PapyrusOpcode.CompareLessOrEqual, O(0), O(1), O(2)),
                "cmp_gt" => PapyrusInstruction.Binary(PapyrusOpcode.CompareGreater, O(0), O(1), O(2)),
                "cmp_gte" => PapyrusInstruction.Binary(PapyrusOpcode.CompareGreaterOrEqual, O(0), O(1), O(2)),
                "jmp" => PapyrusInstruction.Jump(Target(Arg(a, 0), at)),
                "jmpt" => PapyrusInstruction.JumpIf(O(0), Target(Arg(a, 1), at), whenTrue: true),
                "jmpf" => PapyrusInstruction.JumpIf(O(0), Target(Arg(a, 1), at), whenTrue: false),
                "callmethod" => PapyrusInstruction.CallMethod(O(1), N(0), O(2), Rest(3), ReceiverType(Arg(a, 1))),
                "callparent" => PapyrusInstruction.CallParent(N(0), O(1), Rest(2)),
                "callstatic" => PapyrusInstruction.CallStatic(N(0), N(1), O(2), Rest(3)),
                "return" => PapyrusInstruction.Return(O(0)),
                "propget" => PapyrusInstruction.PropertyGet(O(2), O(1), N(0)),
                "propset" => PapyrusInstruction.PropertySet(O(1), N(0), O(2)),
                "array_create" => PapyrusInstruction.ArrayCreate(O(0), O(1)),
                "array_length" => PapyrusInstruction.ArrayLength(O(0), O(1)),
                "array_getelement" => PapyrusInstruction.ArrayGet(O(0), O(1), O(2)),
                "array_setelement" => PapyrusInstruction.ArraySet(O(0), O(1), O(2)),
                "is" => PapyrusInstruction.Is(O(0), O(1), N(2)),
                "struct_create" => PapyrusInstruction.StructCreate(O(0)),
                "struct_get" => PapyrusInstruction.StructGet(O(0), O(1), N(2)),
                "struct_set" => PapyrusInstruction.StructSet(O(0), N(1), O(2)),
                _ => null,
            };
        }

        private static PexValue Arg(IReadOnlyList<PexValue> arguments, int position) =>
            position < arguments.Count
                ? arguments[position]
                : throw new LoweringException($"operand {position} is missing");

        private PapyrusOperand Operand(PexValue value)
        {
            switch (value.Type)
            {
                case PexValueType.None:
                    return PapyrusOperand.Constant(PapyrusValue.None);
                case PexValueType.Integer:
                    return PapyrusOperand.Constant(PapyrusValue.FromInt(value.Integer));
                case PexValueType.Float:
                    return PapyrusOperand.Constant(PapyrusValue.FromFloat(value.Real));
                case PexValueType.Bool:
                    return PapyrusOperand.Constant(PapyrusValue.FromBool(value.Boolean));
                case PexValueType.String:
                    return PapyrusOperand.Constant(PapyrusValue.FromString(Text(value.String)));
            }
            var identifier = Text(value.String);
            if (string.Equals(identifier, "self", StringComparison.OrdinalIgnoreCase)) return PapyrusOperand.SelfRef;
            if (string.Equals(identifier, "::NoneVar", StringComparison.OrdinalIgnoreCase)) return PapyrusOperand.Discard;
            if (Slots.TryGetValue(identifier, out var slot)) return PapyrusOperand.Local(slot.Slot);
            if (Variables.ContainsKey(identifier)) return PapyrusOperand.Variable(identifier);
            throw new LoweringException($"the identifier {identifier} is not a parameter, local or variable");
        }

        private string Name(PexValue value) =>
            value.Type is PexValueType.Identifier or PexValueType.String
                ? Text(value.String)
                : throw new LoweringException($"a {value.Type} operand where a name was expected");

        private string DeclaredType(PexValue value)
        {
            var identifier = value.Type == PexValueType.Identifier ? Text(value.String) : "";
            if (Slots.TryGetValue(identifier, out var slot)) return slot.Type;
            if (Variables.TryGetValue(identifier, out var type)) return type;
            throw new LoweringException($"the declared type of {identifier} is not known");
        }

        private string? ReceiverType(PexValue value)
        {
            if (value.Type != PexValueType.Identifier) return null;
            var identifier = Text(value.String);
            if (string.Equals(identifier, "self", StringComparison.OrdinalIgnoreCase)) return Self;
            if (Slots.TryGetValue(identifier, out var slot)) return slot.Type;
            return Variables.TryGetValue(identifier, out var type) ? type : null;
        }

        private int Target(PexValue value, int at)
        {
            if (value.Type != PexValueType.Integer) throw new LoweringException("a jump offset must be an integer");
            var target = at + value.Integer;
            return target >= 0 && target <= Count
                ? target
                : throw new LoweringException($"the jump target {target} is outside the function");
        }
    }
}
