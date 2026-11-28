namespace OpenCommonwealth.Services.Papyrus.Vm;

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
