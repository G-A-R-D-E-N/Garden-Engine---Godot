using System.Text;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;
using AliasKind = Radicle.Interop.Native.QuestAliasKind;
using FillType = Radicle.Interop.Native.QuestFillType;

namespace OpenCommonwealth.Services.Ui.TextTags;

public sealed record AliasFill(string Quest, uint AliasId, string AliasName, string Target, bool TargetIsReference);

public sealed record AliasBinding(string Quest, uint AliasId, string AliasName);

public static class AliasFillTable
{
    // Invariant: only a named, supported Reference-kind alias with a SpecificReference or UniqueActor fill and a target is listed; collections, location aliases and every other fill type (conditional, matching and event fills) are skipped.
    public static IEnumerable<AliasFill> From(string quest, IReadOnlyList<QuestStoredAlias> aliases)
    {
        foreach (var alias in aliases)
        {
            if (alias.Kind != AliasKind.Reference || alias.Id is not { } id || alias.NameBytes is not { Length: > 0 } name
                || !alias.Fill.Supported || alias.Fill.Target is not { } target)
                continue;
            if (alias.Fill.Type is not (FillType.SpecificReference or FillType.UniqueActor))
                continue;
            yield return new AliasFill(quest, id, Encoding.UTF8.GetString(name), RecordKey.Format(target), alias.Fill.Type == FillType.SpecificReference);
        }
    }
}

// Invariant: the fill table is static plugin data; a reference is bound to an alias only while isRunning(quest) holds, evaluated on every lookup, and only a SpecificReference target keys the reference map (a UniqueActor target is an actor base form and only supplies a display name).
public sealed class AliasBinder : ITextDisplaySource, IAliasFills
{
    private readonly ILookup<string, AliasFill> _byReference;
    private readonly Dictionary<(string Quest, uint AliasId), AliasFill> _byAlias = new();
    private readonly Func<string, bool> _isRunning;
    private readonly Func<string, string?> _displayName;

    public AliasBinder(IEnumerable<AliasFill> fills, Func<string, bool> isRunning, Func<string, string?> displayName)
    {
        ArgumentNullException.ThrowIfNull(fills);
        ArgumentNullException.ThrowIfNull(isRunning);
        ArgumentNullException.ThrowIfNull(displayName);
        var ordered = fills.OrderBy(fill => fill.Quest, StringComparer.Ordinal).ThenBy(fill => fill.AliasId).ToArray();
        foreach (var fill in ordered)
            _byAlias.TryAdd((fill.Quest, fill.AliasId), fill);
        _byReference = ordered.Where(fill => fill.TargetIsReference).ToLookup(fill => fill.Target, StringComparer.Ordinal);
        _isRunning = isRunning;
        _displayName = displayName;
    }

    public IReadOnlyList<AliasBinding> For(string refKey) =>
        _byReference[refKey].Where(fill => _isRunning(fill.Quest)).Select(fill => new AliasBinding(fill.Quest, fill.AliasId, fill.AliasName)).ToArray();

    // Invariant: a reference bound in several running quests belongs to the first quest in ordinal key order.
    public string? QuestFor(string refKey) => _byReference[refKey].FirstOrDefault(fill => _isRunning(fill.Quest))?.Quest;

    public string? DisplayName(string questKey, uint aliasId) =>
        _byAlias.TryGetValue((questKey, aliasId), out var fill) ? _displayName(fill.Target) : null;
}
