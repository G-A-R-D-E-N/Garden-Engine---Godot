using System.Text.Json;

namespace OpenCommonwealth.Services.Papyrus.Quest;

public sealed record QuestStartupValue(int? Integer, int? RealBits, bool? Boolean, string? Text,
    uint? ObjectFormId, short? Alias, uint? ElementCount, string? ObjectKey);

public sealed record QuestStartupProperty(string Script, string Name, byte Type, byte Flags,
    QuestStartupValue Value, int Alias = -1);

public sealed record QuestStartupFragment(ushort Stage, int StageIndex, string Script, string Function);
public sealed record QuestStartupAliasScript(int Alias, string Script);

public sealed record QuestStartupMetadata(IReadOnlyList<QuestStartupProperty> ScriptProperties,
    string FragmentScript, IReadOnlyList<QuestStartupProperty> FragmentProperties,
    IReadOnlyList<QuestStartupFragment> Fragments, IReadOnlyList<int> Aliases,
    IReadOnlyList<QuestStartupAliasScript> AliasScripts, IReadOnlyList<QuestStartupProperty> AliasProperties);

public sealed record QuestStartupDatabase(int FormatVersion, IReadOnlyList<QuestStartupInput> Inputs)
{
    public const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static QuestStartupDatabase FromJson(string json)
    {
        var data = JsonSerializer.Deserialize<QuestStartupDatabase>(json, Options)
            ?? throw new InvalidOperationException("the converted quest startup payload is null");
        data.Validate();
        return data;
    }

    internal void Validate()
    {
        if (FormatVersion != CurrentFormatVersion || Inputs is null
            || Inputs.Any(input => input is null || string.IsNullOrWhiteSpace(input.Quest)
                || input.Scripts is null || input.Scripts.Any(string.IsNullOrWhiteSpace)
                || input.Fragments < 0 || input.Aliases < 0 || !Complete(input)))
            throw new InvalidOperationException("the converted quest startup payload has an unsupported version or shape");
    }

    private static bool Complete(QuestStartupInput input) => input.Metadata is { } metadata
        && metadata.ScriptProperties is not null && metadata.FragmentProperties is not null
        && metadata.Fragments is not null && metadata.Fragments.Count == input.Fragments
        && metadata.Aliases is not null && metadata.Aliases.Count == input.Aliases
        && metadata.AliasScripts is not null && metadata.AliasProperties is not null
        && metadata.FragmentScript is not null
        && metadata.Fragments.All(fragment => fragment is not null && fragment.Script is not null
            && fragment.Function is not null)
        && metadata.AliasScripts.All(script => script is not null && script.Script is not null)
        && metadata.ScriptProperties.Concat(metadata.FragmentProperties).Concat(metadata.AliasProperties)
            .All(property => property is not null && property.Script is not null
                && property.Name is not null && property.Value is not null);
}
