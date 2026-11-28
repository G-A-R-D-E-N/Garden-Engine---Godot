using System.Collections.Concurrent;
using Radicle.Interop;
using Radicle.Interop.Native;

namespace OpenCommonwealth.Services;

// Constraint: the identifier tables live in Radicle (Vocabulary exports); this class only answers the signature or numeric fallback when Radicle has no row.
public static class RecordVocabulary
{
    private static readonly ConcurrentDictionary<string, string> ClassNames = new();
    private static readonly ConcurrentDictionary<string, SignatureTraits> TraitsBySignature = new();

    public static string ClassName(string signature) =>
        ClassNames.GetOrAdd(signature, static s => IsPackable(s) ? Vocabulary.ClassName(s) ?? s : s);

    public static bool IsNamed(string signature) => Traits(signature).Named;

    public static bool IsTrapLike(string signature) => Traits(signature).TrapLike;

    public static bool IsPlaced(string signature) => signature is "REFR" or "ACHR" || IsTrapLike(signature);

    public static string Level(int value) => value >= 0 ? Name(VocabTable.Level, (uint)value) : value.ToString();

    public static string PrimitiveType(uint value) => Vocabulary.Name(VocabTable.PrimitiveType, value) ?? ((int)value).ToString();

    public static string MarkerType(byte value) => Name(VocabTable.MarkerType, value);

    public static string ScriptPropertyType(byte value) => Name(VocabTable.ScriptPropertyType, value);

    public static string ActivateParentFlags(byte value) => Name(VocabTable.ActivateParentFlags, value);

    private static SignatureTraits Traits(string signature) =>
        TraitsBySignature.GetOrAdd(signature, static s => IsPackable(s) ? Vocabulary.Traits(s) : default);

    private static string Name(VocabTable table, uint value) => Vocabulary.Name(table, value) ?? value.ToString();

    private static bool IsPackable(string signature) => signature.Length == 4 && signature.All(c => c <= 0x7F);
}
