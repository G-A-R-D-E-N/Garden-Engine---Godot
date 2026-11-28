using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using Radicle.Interop;
using QuestStartupInput = OpenCommonwealth.Services.Papyrus.Quest.QuestStartupInput;

namespace OpenCommonwealth.Services.Papyrus;

/// <summary>
/// Quest startup inputs, decoded from the winning quest records through Radicle.
/// </summary>
public static partial class RadicleQuestStartup
{
    private static readonly ConcurrentDictionary<string, byte> UnsupportedReported = new();

    /// <summary>One input per winning start-game-enabled quest, ordered by quest key; aliases counts alias definitions.</summary>
    public static IReadOnlyList<QuestStartupInput> Build(RadicleEnvironment env, IReadOnlySet<string>? alsoQuests = null)
    {
        ArgumentNullException.ThrowIfNull(env);

        var inputs = new List<QuestStartupInput>();
        foreach (var definition in QuestDefinition.ReadAll(env))
        {
            var key = definition.Key;
            var record = env.Winner(key) ?? throw new InvalidOperationException($"quest bulk row {key} is not an env winner");
            var data = definition.ReadRuntime(env);
            if (data.Unsupported.Count > 0)
            {
                var message = $"quest startup: Radicle reported unsupported data for {key}: {string.Join("; ", data.Unsupported)}";
                if (UnsupportedReported.TryAdd(message, 0)) Console.Error.WriteLine(message);
            }
            if (!data.StartGameEnabled && data.Unsupported.Count == 0 && alsoQuests?.Contains(key.ToString()) != true)
            {
                continue;
            }

            var owned = data.Scripts
                .Where(script => script.Alias == Radicle.Interop.QuestRuntime.QuestOwnedAlias)
                .Select(script => script.Script)
                .ToList();
            var unsupported = data.Unsupported.ToList();
            var metadata = Metadata(data, env, record, unsupported);
            var stages = definition.Stages.Select(stage => new QuestStartupStage(stage.Index, stage.IsStartup,
                data.Fragments.Count(fragment => fragment.Stage == stage.Index))).ToArray();
            inputs.Add(new QuestStartupInput(key.ToString(), data.StartGameEnabled, owned, data.Fragments.Count,
                data.Aliases.Count, key.Plugin, unsupported, metadata, stages));
        }

        return inputs.OrderBy(input => input.Quest, StringComparer.Ordinal).ToList();
    }

    private static QuestStartupMetadata Metadata(QuestRuntimeData data, RadicleEnvironment env, Record record,
        List<string> unsupported)
    {
        static QuestStartupValue Element(ScriptElement element) => new(element.Integer, BitConverter.SingleToInt32Bits(element.Real),
            element.Boolean, element.Text, null, element.Alias, null, element.Object?.ToString());
        QuestStartupValue Value(QuestRuntimeValue value, string property)
        {
            string? key = null;
            if (value.ObjectFormId is { } id && value.Alias == -1 && id != 0)
            {
                try
                {
                    key = env.ResolveFormId(record, id)?.ToString();
                    if (key is null) unsupported.Add($"{property}: object FormID {id:X8} did not resolve (source:RadicleEnvironment.ResolveFormId)");
                }
                catch (RadicleException error) when (error.Status != Radicle.Interop.Native.Status.ParseError)
                {
                    unsupported.Add($"{property}: object FormID {id:X8} did not resolve (source:RadicleEnvironment.ResolveFormId): {error.Message}");
                }
                catch (RadicleException) { }
            }
            return new QuestStartupValue(value.Integer, value.Real is { } real
                ? BitConverter.SingleToInt32Bits(real) : null, value.Boolean, value.Text,
                value.ObjectFormId, value.Alias, value.ElementCount, key,
                value.Elements?.Select(Element).ToArray(),
                value.Structs?.Select(item => new QuestStartupStruct(item.Members.Select(member =>
                    new QuestStartupMember(member.Name, member.Type, Element(member.Value))).ToArray())).ToArray());
        }
        QuestStartupProperty Property(QuestRuntimeProperty property) =>
            new(property.Script, property.Name, property.Type, property.Flags,
                Value(property.Value, $"{property.Script}.{property.Name}"));
        return new QuestStartupMetadata(data.ScriptProperties.Select(Property).ToArray(), data.FragmentScript,
            data.FragmentProperties.Select(Property).ToArray(), data.Fragments.Select(fragment =>
                new QuestStartupFragment(fragment.Stage, fragment.StageIndex, fragment.Script, fragment.Function)).ToArray(),
            data.Aliases.ToArray(), data.AliasScripts.Select(script =>
                new QuestStartupAliasScript(script.Alias, script.Script)).ToArray(),
            data.AliasScriptProperties.Select(property => new QuestStartupProperty(property.Script, property.Name,
                property.Type, property.Flags, Value(property.Value,
                    $"alias {property.Alias} {property.Script}.{property.Name}"), property.Alias)).ToArray());
    }
}
