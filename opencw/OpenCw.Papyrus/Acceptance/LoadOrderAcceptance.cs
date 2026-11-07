using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Quest;

namespace OpenCommonwealth.Services.Papyrus.Acceptance;

/// <summary>One decoded script of the effective set, with the parent it declares.</summary>
public sealed record AcceptanceScript(string Name, string? Parent, int InstructionCount);

public enum ChainState
{
    Resolved,
    MissingParent,
    Cycle,
    DepthExceeded,
}

public sealed record ChainOutcome(string Script, ChainState State, string? Detail, int Depth);

public sealed record AcceptanceReport(
    int Scripts,
    int Resolved,
    int MissingParent,
    int Cycle,
    int DepthExceeded,
    int SeedTypes,
    int StartupEntries,
    int UnknownStartupPlugins,
    int MissingStartupScripts,
    IReadOnlyList<ChainOutcome> Chains,
    IReadOnlyList<string> Failures)
{
    public bool Clean => Failures.Count == 0;

    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"scripts={Scripts} resolved={Resolved} missing-parent={MissingParent} cycle={Cycle} depth={DepthExceeded} seed-types={SeedTypes} startup={StartupEntries} unknown-plugins={UnknownStartupPlugins} missing-startup-scripts={MissingStartupScripts} failures={Failures.Count}\n");
        foreach (var chain in Chains)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {chain.State.ToString().ToLowerInvariant()} {chain.Script} depth={chain.Depth} {chain.Detail}\n");
        }
        foreach (var failure in Failures)
        {
            text.Append(CultureInfo.InvariantCulture, $"  failure {failure}\n");
        }
        return text.ToString();
    }

    /// <summary>A stable digest of the report, so two runs can be compared byte for byte.</summary>
    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// The real-load-order acceptance run.
/// </summary>
/// <remarks>
/// The run exercises the effective script set, each script's parent chain, the engine-type seed list
/// and the quest startup data. A chain that ends at an engine seed resolves; a parent that is neither
/// in the library nor a seed, a parent chain that loops and a chain deeper than the limit are each
/// reported as their own failure, so nothing is hidden in a summary. Every list is ordinally ordered
/// and the report carries a digest, so the same input always produces the same result.
/// </remarks>
public static class LoadOrderAcceptance
{
    public const int DefaultDepthLimit = 64;

    public static AcceptanceReport Run(
        IEnumerable<AcceptanceScript> scripts,
        IEnumerable<string> engineTypes,
        IEnumerable<QuestStartupInput> startup,
        int depthLimit = DefaultDepthLimit,
        IEnumerable<string>? loadOrder = null)
    {
        var seeds = new HashSet<string>(engineTypes, StringComparer.OrdinalIgnoreCase);
        var byName = scripts
            .GroupBy(script => script.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var ordered = byName.Values.OrderBy(script => script.Name, StringComparer.Ordinal).ToList();

        var chains = new List<ChainOutcome>(ordered.Count);
        var failures = new List<string>();
        var resolved = 0;
        var missing = 0;
        var cycles = 0;
        var tooDeep = 0;

        foreach (var script in ordered)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { script.Name };
            var current = script;
            var depth = 0;
            var state = ChainState.Resolved;
            string? detail = null;

            while (true)
            {
                if (current.Parent is null || current.Parent.Length == 0 || seeds.Contains(current.Parent))
                {
                    break;
                }
                if (depth >= depthLimit)
                {
                    state = ChainState.DepthExceeded;
                    detail = $"the chain exceeded {depthLimit} parents";
                    break;
                }
                if (!byName.TryGetValue(current.Parent, out var parent))
                {
                    state = ChainState.MissingParent;
                    detail = $"the parent {current.Parent} is neither in the library nor an engine type";
                    break;
                }
                if (!visited.Add(parent.Name))
                {
                    state = ChainState.Cycle;
                    detail = $"the chain returns to {parent.Name}";
                    break;
                }
                current = parent;
                depth++;
            }

            switch (state)
            {
                case ChainState.Resolved:
                    resolved++;
                    break;
                case ChainState.MissingParent:
                    missing++;
                    break;
                case ChainState.Cycle:
                    cycles++;
                    break;
                default:
                    tooDeep++;
                    break;
            }
            chains.Add(new ChainOutcome(script.Name, state, detail, depth));
            if (state != ChainState.Resolved)
            {
                failures.Add($"{script.Name}: {detail}");
            }
        }

        var startupList = startup.OrderBy(input => input.Quest, StringComparer.Ordinal).ToList();
        var plugins = loadOrder is null
            ? null
            : new HashSet<string>(loadOrder, StringComparer.OrdinalIgnoreCase);
        var unambiguousStems = plugins?.GroupBy(PluginIdentity, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1).Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownPlugins = 0;
        var missingScripts = 0;
        foreach (var input in startupList)
        {
            if (plugins is not null && !(PluginIdentity(input.Plugin) == input.Plugin
                ? unambiguousStems!.Contains(input.Plugin) : plugins.Contains(input.Plugin)))
            {
                unknownPlugins++;
                failures.Add($"{input.Quest}: the plugin {input.Plugin} is not in the load order");
            }
            foreach (var script in input.Scripts)
            {
                if (byName.ContainsKey(script))
                {
                    continue;
                }
                missingScripts++;
                failures.Add($"{input.Quest}: the attached script {script} is not in the effective script set");
            }
        }

        return new AcceptanceReport(ordered.Count, resolved, missing, cycles, tooDeep, seeds.Count,
            startupList.Count, unknownPlugins, missingScripts, chains, failures);
    }

    /// <summary>The plugin a name denotes, with a plugin extension removed.</summary>
    /// <remarks>
    /// A SEQ file is named after its plugin without the extension — the one measured file is
    /// `FW_Locksmith.seq` beside `FW_Locksmith.esp` — while a load order names the plugin with it, so
    /// the comparison normalizes the extension on both sides instead of failing on one plugin spelled
    /// two ways.
    /// </remarks>
    public static string PluginIdentity(string name)
    {
        foreach (var extension in PluginExtensions)
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^extension.Length];
            }
        }
        return name;
    }

    private static readonly string[] PluginExtensions = { ".esm", ".esp", ".esl" };
}
