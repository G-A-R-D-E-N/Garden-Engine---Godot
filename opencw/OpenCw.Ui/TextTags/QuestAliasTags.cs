using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;
using AliasKind = Radicle.Interop.Native.QuestAliasKind;

namespace OpenCommonwealth.Services.Ui.TextTags;

public sealed record QuestAliasName(uint Id, string Name);

public interface IQuestAliasNames
{
    IReadOnlyList<QuestAliasName>? Aliases(string questKey);
}

public interface IAliasFills
{
    string? DisplayName(string questKey, uint aliasId);
}

public interface ITextDisplaySource
{
    string? QuestFor(string refKey);

    IReadOnlyList<string> QuestsFor(string refKey) => QuestFor(refKey) is { Length: > 0 } quest ? new[] { quest } : Array.Empty<string>();
}

public sealed class QuestAliasResolver(IQuestAliasNames names, IAliasFills fills, string questKey) : ITagResolver
{
    public const string AliasTag = "Alias";

    // Requires: only the plain Alias=Name form resolves; any sub, any other kind, an unknown alias name and an unfilled alias answer null so the tag stays verbatim.
    public string? Resolve(string kind, string? value, string? sub)
    {
        if (!kind.Equals(AliasTag, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(value) || sub is not null)
            return null;
        if (names.Aliases(questKey)?.FirstOrDefault(a => a.Name.Equals(value, StringComparison.OrdinalIgnoreCase)) is not { } alias)
            return null;
        return fills.DisplayName(questKey, alias.Id) is { Length: > 0 } display ? display : null;
    }
}

public sealed class ReferenceTagResolvers(ITextDisplaySource display, IQuestAliasNames names, IAliasFills fills)
{
    // Requires: a reference bound in several running quests resolves a tag only when the quests that know the alias agree on one display name; a conflict stays literal.
    public ITagResolver? For(string refKey)
    {
        var quests = display.QuestsFor(refKey).Where(quest => quest.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        return quests.Length switch
        {
            0 => null,
            1 => new QuestAliasResolver(names, fills, quests[0]),
            _ => new SharedReferenceResolver(quests.Select(quest => new QuestAliasResolver(names, fills, quest)).ToArray()),
        };
    }
}

public sealed class SharedReferenceResolver(IReadOnlyList<ITagResolver> inner) : ITagResolver
{
    public string? Resolve(string kind, string? value, string? sub)
    {
        string? answer = null;
        foreach (var resolver in inner)
        {
            if (resolver.Resolve(kind, value, sub) is not { } text) continue;
            if (answer is not null && !answer.Equals(text, StringComparison.Ordinal)) return null;
            answer = text;
        }
        return answer;
    }
}

public sealed class RadicleQuestAliasNames : IQuestAliasNames
{
    private sealed record AliasIndex(Dictionary<string, IReadOnlyList<QuestAliasName>> Names, List<AliasFill> Fills);

    private readonly Func<IEnumerable<(string Quest, IReadOnlyList<QuestStoredAlias> Aliases)>> _read;
    private AliasIndex? _index;

    public RadicleQuestAliasNames(IRecordSource source)
        : this(() => EnvQuestBulk.Read(source.Environment).Select(row => (RecordKey.Format(row.Key), (IReadOnlyList<QuestStoredAlias>)row.Structure.Aliases)))
    {
    }

    public RadicleQuestAliasNames(Func<IEnumerable<(string Quest, IReadOnlyList<QuestStoredAlias> Aliases)>> read) => _read = read;

    // Constraint: a failed bulk read answers null here and is retried on the next call; nothing partial is kept.
    public IReadOnlyList<QuestAliasName>? Aliases(string questKey)
    {
        try { return Index().Names.GetValueOrDefault(questKey); }
        catch (Exception ex) when (IsReadFailure(ex)) { return null; }
    }

    // Requires: callers publish the fill table only when this returns; a failed bulk read throws so no partial table is mistaken for the whole one.
    public IReadOnlyList<AliasFill> Fills() => Index().Fills;

    private static bool IsReadFailure(Exception ex) => ex is RadicleException or ArgumentException or InvalidOperationException or ObjectDisposedException;

    private AliasIndex Index() => _index ??= Load(_read());

    // Invariant: one bulk read feeds both tables; collections carry no id and are skipped; an alias with no ALID name is skipped; the index is cached only when the whole read succeeded.
    private static AliasIndex Load(IEnumerable<(string Quest, IReadOnlyList<QuestStoredAlias> Aliases)> rows)
    {
        var quests = new Dictionary<string, IReadOnlyList<QuestAliasName>>(StringComparer.Ordinal);
        var fills = new List<AliasFill>();
        foreach (var (quest, aliases) in rows)
        {
            quests[quest] = aliases
                .Where(a => a.Kind != AliasKind.Collection && a.Id is not null && a.NameBytes is { Length: > 0 })
                .Select(a => new QuestAliasName(a.Id!.Value, System.Text.Encoding.UTF8.GetString(a.NameBytes!)))
                .ToArray();
            fills.AddRange(AliasFillTable.From(quest, aliases));
        }
        return new AliasIndex(quests, fills);
    }
}
