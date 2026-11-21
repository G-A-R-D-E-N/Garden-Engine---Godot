using System.Globalization;

namespace OpenCommonwealth.Services.Papyrus.Vm;

/// <summary>
/// One Papyrus value.
/// </summary>
/// <remarks>
/// Object values carry a reference identity, never an attached script: the VM refuses to guess an
/// attached script from a bare reference, which is the native target contract.
/// </remarks>
public readonly record struct PapyrusValue
{
    private PapyrusValue(PapyrusValueKind kind, long integer, double real, bool flag, string? text, object? reference,
        IReadOnlyList<PapyrusValue>? elements = null, IReadOnlyDictionary<string, PapyrusValue>? members = null)
    {
        Kind = kind;
        Integer = integer;
        Real = real;
        Flag = flag;
        Text = text;
        Reference = reference;
        Elements = elements;
        Members = members;
    }

    public PapyrusValueKind Kind { get; }

    public long Integer { get; }

    public double Real { get; }

    public bool Flag { get; }

    public string? Text { get; }

    public object? Reference { get; }

    public IReadOnlyList<PapyrusValue>? Elements { get; }

    public IReadOnlyDictionary<string, PapyrusValue>? Members { get; }

    public static PapyrusValue None => default;

    // Invariant: Papyrus Integer is 32-bit, so Integer always holds an int32; wider input wraps modulo 2^32 like Radicle's parseInteger and iadd.
    public static PapyrusValue FromInt(long value) => new(PapyrusValueKind.Int, unchecked((int)value), 0, false, null, null);

    public static PapyrusValue FromFloat(double value) => new(PapyrusValueKind.Float, 0, value, false, null, null);

    public static PapyrusValue FromBool(bool value) => new(PapyrusValueKind.Bool, 0, 0, value, null, null);

    public static PapyrusValue FromString(string value) =>
        new(PapyrusValueKind.String, 0, 0, false, value ?? "", null);

    public static PapyrusValue FromObject(object reference) =>
        new(PapyrusValueKind.Object, 0, 0, false, null, reference);

    public static PapyrusValue FromArray(IReadOnlyList<PapyrusValue> elements) =>
        new(PapyrusValueKind.Array, 0, 0, false, null, null, elements);

    public static PapyrusValue FromStruct(IReadOnlyDictionary<string, PapyrusValue> members) =>
        new(PapyrusValueKind.Struct, 0, 0, false, null, null, null, members);

    public override string ToString() => Kind switch
    {
        PapyrusValueKind.None => "None",
        PapyrusValueKind.Int => Integer.ToString(CultureInfo.InvariantCulture),
        PapyrusValueKind.Float => Real.ToString("R", CultureInfo.InvariantCulture),
        PapyrusValueKind.Bool => Flag ? "True" : "False",
        PapyrusValueKind.String => Text ?? "",
        PapyrusValueKind.Array => $"Array[{Elements?.Count ?? 0}]",
        PapyrusValueKind.Struct => $"Struct[{Members?.Count ?? 0}]",
        _ => Reference?.ToString() ?? "Object",
    };
}
