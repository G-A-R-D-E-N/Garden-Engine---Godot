using System.Text.Json;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using AssetResolver = Radicle.Interop.AssetResolver;
using FormKey = Radicle.Interop.FormKey;
using FurnitureClipDirs = OpenCommonwealth.Services.FurnitureClipDirs;
using FurnitureClips = OpenCommonwealth.Services.FurnitureClips;
using FurnitureMarkerSides = Radicle.Interop.FurnitureMarkerSides;
using RaceSubgraphRow = Radicle.Interop.RaceSubgraphRow;
using Record = Radicle.Interop.Record;
using RadicleEnvironment = Radicle.Interop.RadicleEnvironment;
using OpenCommonwealth.Tests;
using Xunit;

namespace OpenCw.Papyrus.Tests;

// Requires: FO4_DATA_DIR names an existing Fallout 4 Data folder; without it every test is a skip, so the public Linux run never measures anything.
public sealed class RealDataFactAttribute : FactAttribute
{
    public RealDataFactAttribute()
    {
        if (!Directory.Exists(RegistryCorpusRealTests.DataDirectory))
        {
            Skip = "FO4_DATA_DIR is not set to an existing Fallout 4 Data folder";
        }
    }
}

// Invariant: every figure is read through Radicle (QuestDefinition over RadicleEnvironment, AssetResolver) and compared with the number written in the registry row's source_detail; the figures are written as counts only to REGISTRY_MEASUREMENTS_OUT.
[Trait("Category", "RealData")]
public sealed class RegistryCorpusRealTests
{
    private static readonly Regex Figure = new(@"(?<![A-Za-z0-9_])(?:\d{1,3}(?:,\d{3})+|\d+)(?![A-Za-z0-9_])");

    private static readonly Lazy<IReadOnlyDictionary<string, int?[]>> Measured = new(Measure);

    internal static string? DataDirectory => Fo4Data.Folder;

    // Detects: a registry edit or a Radicle decode change that moves the QUST count of Fallout4.esm away from the 1,336 every quest row cites; the three quest rows must agree with the data.
    [RealDataFact]
    public void EveryQuestRowCitesTheQuestCountOfTheMaster()
    {
        AssertFigure("quest.vmad.scripts_and_properties", 0);
        AssertFigure("quest.vmad.fragment_names", 0);
        AssertFigure("quest.vmad.alias_scripts", 0);
    }

    // Detects: the quest VMAD decode dropping or double counting stage fragment entries; the count over all Fallout4.esm quests must equal the registry figure.
    [RealDataFact]
    public void TheStageFragmentCountMatchesTheRegistry() => AssertFigure("quest.vmad.fragment_names", 1);

    // Detects: the alias script decode losing or repeating an attachment; the count over all Fallout4.esm quests must equal the registry figure.
    [RealDataFact]
    public void TheAliasScriptCountMatchesTheRegistry() => AssertFigure("quest.vmad.alias_scripts", 1);

    // Detects: Radicle's BA2 reader skipping or inventing .pex entries in the load-order archives of the five official plugins; the total must equal the 8,533 the extraction row cites.
    [RealDataFact]
    public void TheOfficialArchivesHoldTheCitedPexEntries() => AssertFigure("cache.archive_extraction", 0);

    private static void AssertFigure(string key, int position)
    {
        var measured = Measured.Value;
        var figures = RegistryFigures(key);
        Assert.True(figures.Length > position, $"{key}: source_detail holds {figures.Length} figures");
        Assert.Equal(figures[position], measured[key][position]);
    }

    private static int[] RegistryFigures(string key)
    {
        using var resource = typeof(GameSession).Assembly.GetManifestResourceStream("OpenCw.Papyrus.EvidenceRegistry")
            ?? throw new InvalidOperationException("the evidence registry is not embedded");
        using var registry = JsonDocument.Parse(resource);
        foreach (var row in registry.RootElement.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("key").GetString() != key) continue;
            return Figure.Matches(row.GetProperty("source_detail").GetString()!)
                .Select(match => int.Parse(match.Value.Replace(",", ""))).ToArray();
        }
        throw new InvalidOperationException($"no registry row {key}");
    }

    private static IReadOnlyDictionary<string, int?[]> Measure()
    {
        var data = DataDirectory!;
        using var environment = RadicleEnvironment.Load(new[] { Path.Combine(data, "Fallout4.esm") });
        var quests = QuestDefinition.ReadAll(environment);
        var fragments = 0;
        var aliasScripts = 0;
        foreach (var quest in quests)
        {
            var runtime = quest.ReadRuntime(environment);
            fragments += runtime.Fragments.Count;
            aliasScripts += runtime.AliasScripts.Count;
        }

        using var assets = Fo4Data.Assets()!;
        var pex = assets.List(null, ".pex", includeShadowed: true).Count(listing => listing.Location.Origin == Radicle.Interop.Native.AssetOrigin.Archive);

        var measured = new Dictionary<string, int?[]>
        {
            ["quest.vmad.scripts_and_properties"] = new int?[] { quests.Count },
            ["quest.vmad.fragment_names"] = new int?[] { quests.Count, fragments },
            ["quest.vmad.alias_scripts"] = new int?[] { quests.Count, aliasScripts },
            ["cache.archive_extraction"] = new int?[] { pex },
        };
        var output = Environment.GetEnvironmentVariable("REGISTRY_MEASUREMENTS_OUT");
        if (string.IsNullOrEmpty(output)) output = Path.Combine(AppContext.BaseDirectory, "registry-measurements.json");
        File.WriteAllText(output, JsonSerializer.Serialize(measured, new JsonSerializerOptions { WriteIndented = true }));
        return measured;
    }
}

// Invariant: the table is written before any assertion and holds editor IDs in sorted or stored order, with no time or host path.
[Trait("Category", "RealData")]
public sealed class FurnitureClipRealTests
{
    private const ushort FurnitureRole = 2;
    private const ushort ThirdPerson = 0;
    private const string TemplateRacePath = @"RACE\SubgraphTemplateRace";
    private const string AdditiveRacePath = @"RACE\SubgraphAdditiveRace";
    private const FurnitureMarkerSides AllSides = FurnitureMarkerSides.Front | FurnitureMarkerSides.Rear | FurnitureMarkerSides.Right | FurnitureMarkerSides.Left;

    private static readonly (string Label, string Needle)[] Samples = { ("chair", "Chair"), ("bed", "Bed"), ("workbench", "Workbench") };

    private sealed record Resolved(string Id, IReadOnlyList<string> Dirs, FurnitureClips Clips, bool WidenedChangesDirs);

    // Detects a regression: some FURN must resolve a clip directory from the race keywords FurnitureClipSource.Build passes.
    [RealDataFact]
    public void HumanRaceFurnitureRowsResolveClipFilesForAChairABedAndAWorkbench()
    {
        var data = RegistryCorpusRealTests.DataDirectory!;
        using var environment = RadicleEnvironment.Load(new[] { Path.Combine(data, "Fallout4.esm") });
        using var assets = AssetResolver.Open(data, AssetResolver.ArchiveLoadOrder(data, Array.Empty<string>(), new[] { "Fallout4.esm" }));
        var human = environment.FindEditorId("HumanRace", "RACE") ?? throw new InvalidOperationException("HumanRace is not in Fallout4.esm");
        var humanKey = environment.Info(human).FormKey;

        var reads = new List<(FormKey Race, IReadOnlyList<RaceSubgraphRow> Rows)>();
        var parent = new Dictionary<FormKey, FormKey>();
        var chain = FurnitureClipDirs.RaceChain(key =>
        {
            var race = ReadRace(environment, key);
            reads.Add((key, race.Rows));
            foreach (var link in race.Additive.Cast<FormKey?>().Append(race.Template))
                if (link is { } child && !child.Equals(humanKey)) parent.TryAdd(child, key);
            return race;
        }, humanKey);
        int Depth(FormKey key)
        {
            var depth = 1;
            while (parent.TryGetValue(key, out var up)) { key = up; depth++; }
            return depth;
        }

        var names = new Dictionary<FormKey, string>();
        string Name(FormKey? key)
        {
            if (key is not { } k) return "none";
            if (!names.TryGetValue(k, out var name))
                names[k] = name = environment.Winner(k) is { } record ? environment.Info(record).EditorId ?? k.ToString() : k.ToString();
            return name;
        }
        string Names(IEnumerable<FormKey?> keys) => string.Join(",", keys.Select(Name).OrderBy(n => n, StringComparer.Ordinal));

        var raceKeywords = environment.RecordKeywords(human).Cast<FormKey?>().ToArray();
        var furnitureRows = reads.SelectMany(r => r.Rows.Select(row => (r.Race, Row: row)))
            .Where(r => r.Row.Role == FurnitureRole && r.Row.Perspective == ThirdPerson).ToList();
        var everyActorKeyword = raceKeywords.Concat(furnitureRows.SelectMany(r => r.Row.ActorKeywords)).Where(k => k is not null).Distinct().ToArray();
        var furniture = environment.WinnersOf("FURN").Select(record =>
        {
            var info = environment.Info(record);
            return (Id: info.EditorId ?? info.FormKey.ToString(), Key: info.FormKey.ToString(), Keywords: environment.FurnitureKeywords(record));
        }).OrderBy(f => f.Id, StringComparer.Ordinal).ThenBy(f => f.Key, StringComparer.Ordinal).ToList();

        var listed = new Dictionary<string, IReadOnlyCollection<string>>();
        IReadOnlyCollection<string> Files(string dir)
        {
            if (!listed.TryGetValue(dir, out var files))
                listed[dir] = files = FurnitureClips.DirectFiles(dir, assets.List(dir + "/", ".hkx").Select(l => l.Path));
            return files;
        }
        var resolved = furniture.Select(f =>
        {
            var dirs = FurnitureClipDirs.Directories(chain, f.Keywords, raceKeywords);
            var widened = FurnitureClipDirs.Directories(chain, f.Keywords, everyActorKeyword);
            var clips = FurnitureClips.Pick(dirs, Files, FurnitureClips.SidesOf(AllSides));
            return new Resolved(f.Id, dirs, clips, !dirs.SequenceEqual(widened));
        }).ToList();

        var lines = new List<string>
        {
            "furniture clip table, HumanRace, Fallout4.esm",
            "race keywords: " + Names(raceKeywords),
            "races read: " + string.Join("; ", reads.Select(r => $"{Name(r.Race)} depth {Depth(r.Race)} rows {r.Rows.Count}")),
            "furniture rows: " + furnitureRows.Count,
            string.Empty,
            "table A: furniture third person rows in read order (index | race | depth | target keywords | actor keywords | actor keywords covered by race keywords | prefixes | FURN matched)",
        };
        for (var i = 0; i < furnitureRows.Count; i++)
        {
            var (race, row) = furnitureRows[i];
            var actors = row.ActorKeywords.Where(k => k is not null).ToList();
            var covered = actors.Count == 0 ? "none" : actors.Any(k => raceKeywords.Contains(k)) ? "yes" : "NO";
            var matched = furniture.Count(f => row.TargetKeywords.Any(t => t is { } key && f.Keywords.Contains(key)));
            lines.Add($"{i} | {Name(race)} | {Depth(race)} | {Names(row.TargetKeywords)} | {Names(actors)} | {covered} | {string.Join(";", row.PathPrefixes)} | {matched}");
        }
        lines.Add(string.Empty);
        lines.Add($"table B: FURN with a directory or a sample name (of {resolved.Count} FURN, {resolved.Count(r => r.Dirs.Count > 0)} with a directory, {resolved.Count(r => r.Clips.Entries().Any())} with a clip, {resolved.Count(r => r.WidenedChangesDirs)} whose directories change with every actor keyword) (editor ID | directories | enter | pose | exit | actor keywords change the directories)");
        foreach (var r in resolved.Where(r => r.Dirs.Count > 0 || Samples.Any(s => r.Id.Contains(s.Needle, StringComparison.OrdinalIgnoreCase))))
            lines.Add($"{r.Id} | {string.Join(";", r.Dirs)} | {r.Clips.Enter ?? "-"} | {r.Clips.Pose ?? "-"} | {r.Clips.Exit ?? "-"} | {(r.WidenedChangesDirs ? "YES" : "no")}");
        var output = Environment.GetEnvironmentVariable("FURNITURE_CLIP_TABLE_OUT");
        if (string.IsNullOrEmpty(output)) output = Path.Combine(AppContext.BaseDirectory, "furniture-clip-table.txt");
        File.WriteAllText(output, string.Join("\n", lines) + "\n");

        Assert.NotEmpty(furnitureRows);
        Assert.Contains(resolved, r => r.Dirs.Count > 0);
    }

    private static (IReadOnlyList<RaceSubgraphRow> Rows, FormKey? Template, IReadOnlyList<FormKey> Additive) ReadRace(RadicleEnvironment environment, FormKey race)
    {
        if (environment.Winner(race) is not { } record) return (Array.Empty<RaceSubgraphRow>(), null, Array.Empty<FormKey>());
        var additive = new List<FormKey>();
        if (environment.TryListSize(record, AdditiveRacePath, out var count))
            for (var i = 0; i < count; i++)
                if (Link(environment, record, AdditiveRacePath, i) is { } added) additive.Add(added);
        return (environment.RaceSubgraphs(record), Link(environment, record, TemplateRacePath, 0), additive);
    }

    private static FormKey? Link(RadicleEnvironment environment, Record record, string path, int index)
    {
        if (environment.GetField(record, path, index) is not { Length: > 0 } text) return null;
        var colon = text.IndexOf(':');
        if (colon <= 0 || colon == text.Length - 1 ||
            !uint.TryParse(text.AsSpan(0, colon), System.Globalization.NumberStyles.HexNumber, null, out var id)) return null;
        return new FormKey(text[(colon + 1)..], id);
    }
}
