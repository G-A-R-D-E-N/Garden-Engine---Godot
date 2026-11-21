using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Papyrus.Quest;

// Invariant: the script/fragment decode is per quest and on demand; ReadAll never runs it, and text stays raw until LogTexts.
public sealed record QuestDefinition(FormKey Key, bool StartGameEnabled, QuestStoredStructure Structure)
{
    public IReadOnlyList<QuestStoredStage> Stages => Structure.Stages;

    public IReadOnlyList<QuestStoredObjective> Objectives => Structure.Objectives;

    public IReadOnlyList<QuestStoredAlias> Aliases => Structure.Aliases;

    public static IReadOnlyList<QuestDefinition> ReadAll(RadicleEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        return EnvQuestBulk.Read(env)
            .Select(row => new QuestDefinition(row.Key, row.StartGameEnabled, row.Structure))
            .ToList();
    }

    public QuestRuntimeData ReadRuntime(RadicleEnvironment env) => Radicle.Interop.QuestRuntime.Decode(env, Key);

    public IReadOnlyList<QuestRuntimePropertyEntry> ReadProperties(RadicleEnvironment env) =>
        Radicle.Interop.QuestRuntime.Properties(env, Key);

    // Requires: pluginIndex is the index of the plugin that holds the winning quest record.
    public IReadOnlyList<(ushort Stage, string Text)> LogTexts(StringTables strings, int pluginIndex, Encoding encoding)
    {
        var source = StringTables.SourceFor("QUST", "CNAM");
        string? Resolve(QuestStoredText? stored) =>
            stored switch
            {
                { StringId: { } id } => strings.Lookup(pluginIndex, source, id) is { } bytes ? encoding.GetString(bytes) : null,
                { InlineBytes: { } inline } => encoding.GetString(inline),
                _ => null,
            };
        var logs = new List<(ushort Stage, string Text)>();
        foreach (var stage in Stages)
            foreach (var log in stage.Logs)
                if (Resolve(log.Text) is { Length: > 0 } text)
                    logs.Add((stage.Index, text));
        return logs;
    }
}
