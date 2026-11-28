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
    public ITagResolver? For(string refKey) =>
        display.QuestFor(refKey) is { Length: > 0 } quest ? new QuestAliasResolver(names, fills, quest) : null;
}

public sealed class RadicleQuestAliasNames(IRecordSource source) : IQuestAliasNames
{
    private readonly Lazy<(Dictionary<string, IReadOnlyList<QuestAliasName>> Names, List<AliasFill> Fills)> _quests = new(() => Load(source.Environment));

    public IReadOnlyList<QuestAliasName>? Aliases(string questKey) => _quests.Value.Names.GetValueOrDefault(questKey);

    public IReadOnlyList<AliasFill> Fills() => _quests.Value.Fills;

    // Invariant: one bulk read feeds both tables; collections carry no id and are skipped; an alias with no ALID name is skipped; a failed bulk read yields no aliases and no fills and is not retried.
    private static (Dictionary<string, IReadOnlyList<QuestAliasName>> Names, List<AliasFill> Fills) Load(RadicleEnvironment env)
    {
        var quests = new Dictionary<string, IReadOnlyList<QuestAliasName>>(StringComparer.Ordinal);
        var fills = new List<AliasFill>();
        try
        {
            foreach (var row in EnvQuestBulk.Read(env))
            {
                string quest = RecordKey.Format(row.Key);
                quests[quest] = row.Structure.Aliases
                    .Where(a => a.Kind != AliasKind.Collection && a.Id is not null && a.NameBytes is { Length: > 0 })
                    .Select(a => new QuestAliasName(a.Id!.Value, System.Text.Encoding.UTF8.GetString(a.NameBytes!)))
                    .ToArray();
                fills.AddRange(AliasFillTable.From(quest, row.Structure.Aliases));
            }
        }
        catch (Exception ex) when (ex is RadicleException or ArgumentException or InvalidOperationException or ObjectDisposedException)
        {
        }
        return (quests, fills);
    }
}
