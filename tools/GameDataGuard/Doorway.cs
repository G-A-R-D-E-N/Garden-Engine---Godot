using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GameDataGuard;

internal static class Doorway
{
    internal const string BaselinePath = "tools/radicle-doorway.baseline.txt";
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private const string Code = @"\.(cs|gd|py|ps1|yml|csproj|sh)$";
    private const string Cs = @"\.cs$";
    private const string Own = @"^(tools/GameDataGuard|tests/GuardTools\.Tests)/";

    internal sealed record Rule(string Id, string Why, string Pattern, string Scope, string? Allow, string Use, string? Needs = null);

    internal static readonly Rule[] Rules =
    [
        new("G1", "BA2 archive opened directly", @"Ba2Archive\.Open\(", Code,
            @"^opencw/Services/Ba2/Ba2ExtractorService\.cs$|(^|/)Radicle\.Interop(\.Tests)?/",
            "Ba2ExtractorService or TextureService archive lookup (load order), or Radicle.Interop"),
        new("G2", "directory scan for BA2 archives ignores the load order", @"(?i)(GetFiles|EnumerateFiles)\([^)]*\.ba2", Code, null,
            "the archive list from GameLoader / Mo2ProfileLoader, never a directory scan"),
        new("G3", "vanilla or DLC archive name written by hand", @"\x22[^\x22]*(Fallout4|DLC[A-Za-z0-9]+) - [A-Za-z0-9 *]+\.ba2", Code, null,
            "resolve the archive through the load order (TextureService / Ba2ExtractorService), not by name"),
        new("G4", "RadicleEnvironment opened outside the loader", @"RadicleEnvironment\.(Load|Open)\(", Code,
            @"^opencw/Services/Core/GameLoader\.cs$|^opencw/Services/Mo2ProfileLoader\.cs$|^opencw/(tools|tests)/|^tests/",
            "GameLoader.ActiveRecords / RecordSource (the one loaded environment)"),
        new("G5", "game file magic or Radicle cache magic written by hand", @"\x22(BTDX|TES4|GRUP|Gamebryo File Format)|0xFA57C0DE", Code, null,
            "the Radicle typed decoders (BA2, plugin, NIF), not hand-parsed headers"),
        new("G6", "native Radicle library loaded outside the one resolver", @"(?i)(DllImport|LibraryImport|NativeLibrary\.\w+).*radicle|\x22(lib)?radicle\.(dll|so)\x22", Cs,
            @"^opencw/Services/Records/RadicleNativePath\.cs$|(^|/)Radicle\.Interop(\.Tests)?/",
            "RadicleNativePath (opencw/Services/Records) or Radicle.Interop"),
        new("G7", "game file read as raw bytes", @"(?i)ReadAllBytes\([^)]*\.(nif|hkx|swf|pex|esm|esp|ba2|bgsm|strings)", Code, null,
            "TextureService.Resolve* or the Radicle.Interop readers"),
        new("G8", "external converter or compiler for game data", @"(?i)PapyrusCompiler\.exe|texconv|\.jar\b", Code, null,
            "the Radicle decoders and OpenCw.Papyrus; no external texconv, Java or PapyrusCompiler"),
        new("G9", "hard-coded Steam library path", @"(?i)[A-Z]:[/\\]+(SteamLibrary|Program Files( \(x86\))?[/\\]+Steam)", Code, null,
            "FO4_DATA_DIR / ToolPaths.Fallout4Root()"),
        new("G10", "game data location from an unapproved environment variable",
            @"(?i)(GetEnvironmentVariable\(\s*\x22|SetEnvironmentVariable\(\s*\x22|OS\.GetEnvironment\(\s*\x22|os\.environ(\.get)?[\[(]\s*[\x22']|os\.getenv\(\s*[\x22']|\$env:)(?!FO4_DATA_DIR\b)\w*(FO4|FALLOUT)",
            Code, null, "FO4_DATA_DIR only"),
        new("G11", "package outside the approved set", @"PackageReference\b[^>]*\bInclude=\x22(?!(xunit|xunit\.runner\.visualstudio|Microsoft\.NET\.Test\.Sdk|Newtonsoft\.Json|LibGit2Sharp|GodotSharp|Avalonia|Avalonia\.Desktop|Avalonia\.Themes\.Fluent)\x22)",
            @"\.(csproj|props|targets)$", null, "an approved package; extend the G11 set in Doorway.cs only with owner approval"),
        new("G12", "game record keys committed in shipped data", @"Fallout4_esm|:Fallout4\.esm", @"^opencw/(data|assets)/.*\.(json|tsv|csv|txt|xml)$", null,
            "derive the keys from Radicle at load time; do not ship Fallout4.esm-keyed data"),
        new("G13", "hand-rolled binary parsing in a file that names a game format",
            @"BinaryPrimitives\.|BitConverter\.To|MemoryMarshal\.(Read|Cast)|new BinaryReader\(", Cs,
            @"(^|/)(GrassMap|GeoTiff|WaterStore|Hlod|FarShadow|TerrainMaterialPackage)[^/]*(/|\.cs$)",
            "the Radicle typed decoders; own cache formats belong in the G13 allow-listed files",
            @"(?i)\.(esm|esp|ba2|nif|dds|hkx|swf)\b"),
        new("G14", "regex over game file names", @"(?i)(regex|\bre\.(compile|match|search|findall|sub)).*[._]\(?(\?:)?(hkx|nif|esm|ba2)\b", Code, null,
            "Radicle asset and record lookup instead of matching game file names"),
    ];

    internal static Dictionary<(string Id, string Path), int> Baseline(IEnumerable<string> lines)
    {
        var baseline = new Dictionary<(string Id, string Path), int>();
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int first = line.IndexOf(' '), last = line.LastIndexOf(' ');
            if (first <= 0 || last <= first || !int.TryParse(line[(last + 1)..], out int count) || count < 1)
                throw new InvalidDataException("Bad doorway baseline line: " + line);
            if (!baseline.TryAdd((line[..first], line[(first + 1)..last].Trim()), count))
                throw new InvalidDataException("Duplicate doorway baseline line: " + line);
        }
        return baseline;
    }

    internal static Dictionary<string, string> Renames(IEnumerable<string> lines)
    {
        var renames = new Dictionary<string, string>();
        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            string[] parts = line.Split('\t');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
                throw new InvalidDataException("Bad doorway rename line: " + line);
            if (!renames.TryAdd(parts[1], parts[0]))
                throw new InvalidDataException("Duplicate doorway rename target: " + line);
        }
        return renames;
    }

    internal static List<string> Compare(IReadOnlyDictionary<(string Id, string Path), int> baseline, IReadOnlyDictionary<(string Id, string Path), int> head, IReadOnlyDictionary<string, string>? renames = null)
    {
        var problems = new List<string>();
        foreach (var entry in head.OrderBy(row => row.Key.Id, StringComparer.Ordinal).ThenBy(row => row.Key.Path, StringComparer.Ordinal))
        {
            string id = entry.Key.Id, path = entry.Key.Path;
            string origin = path;
            if (renames is not null && renames.TryGetValue(path, out string? moved)) origin = moved;
            if (!baseline.TryGetValue((id, origin), out int allowed))
                problems.Add($"{id} {path}: not in the trusted base baseline; a new entry may not be added (owner decision)");
            else if (entry.Value > allowed)
                problems.Add($"{id} {path}: baseline raised to {entry.Value} from {allowed}{(origin == path ? "" : " at " + origin)} on the trusted base; a raise may not be added (owner decision)");
        }
        return problems;
    }

    internal static List<string> Check(IEnumerable<string> paths, Func<string, string> read, IReadOnlyDictionary<(string Id, string Path), int> baseline)
    {
        static Regex Make(string pattern) => new(pattern, RegexOptions.None, Timeout);
        var own = Make(Own);
        var rules = Rules.Select(rule => (Rule: rule, Hit: Make(rule.Pattern), Scope: Make(rule.Scope),
            Allow: rule.Allow is null ? null : Make(rule.Allow), Needs: rule.Needs is null ? null : Make(rule.Needs))).ToList();
        var problems = new List<string>();
        var seen = new HashSet<(string Id, string Path)>();
        foreach (string path in paths.OrderBy(path => path, StringComparer.Ordinal))
        {
            if (own.IsMatch(path)) continue;
            var active = rules.Where(r => r.Scope.IsMatch(path) && !(r.Allow?.IsMatch(path) ?? false)).ToList();
            if (active.Count == 0) continue;
            string text = read(path);
            string[] lines = text.Split('\n');
            foreach (var r in active)
            {
                var found = new List<int>();
                int count = 0, index = 0;
                try
                {
                    if (r.Needs != null && !r.Needs.IsMatch(text)) continue;
                    for (; index < lines.Length; index++)
                    {
                        int hits = r.Hit.Matches(lines[index].TrimEnd('\r')).Count;
                        if (hits == 0) continue;
                        count += hits;
                        found.Add(index + 1);
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    seen.Add((r.Rule.Id, path));
                    problems.Add($"{r.Rule.Id} {path}:{index + 1}: regex timed out after {Timeout.TotalSeconds:0}s on this line; split the line or ask the owner");
                    continue;
                }
                if (count > 0) seen.Add((r.Rule.Id, path));
                int allowed = baseline.GetValueOrDefault((r.Rule.Id, path));
                if (count > allowed)
                    problems.Add($"{r.Rule.Id} {path}:{string.Join(',', found.Take(5))}{(found.Count > 5 ? ",..." : "")}: {r.Rule.Why}; {count} hit(s), baseline allows {allowed}; use Radicle/the wrapper: {r.Rule.Use}");
                else if (count > 0 && count < allowed)
                    problems.Add($"{r.Rule.Id} {path}: {count} hit(s), baseline has {allowed}; lower the baseline to {count}");
            }
        }
        foreach (var (id, path) in baseline.Keys.Where(key => !seen.Contains(key)).OrderBy(key => key.Id, StringComparer.Ordinal).ThenBy(key => key.Path, StringComparer.Ordinal))
            problems.Add($"{id} {path}: no hits; remove stale baseline entry");
        return problems;
    }

    private static string[] Tracked(string root)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8, UseShellExecute = false };
        foreach (string argument in new[] { "-C", root, "ls-files", "-z" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string listing = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git ls-files failed in " + root);
        return listing.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    internal static List<string> Repo(string root, string? baseFile = null, string? renamesFile = null)
    {
        string file = Path.Combine(root, BaselinePath);
        if (!File.Exists(file)) throw new FileNotFoundException("Doorway baseline not found: " + BaselinePath);
        var baseline = Baseline(File.ReadAllLines(file));
        var trusted = baseFile is null ? null : Baseline(File.ReadAllLines(baseFile));
        var renames = renamesFile is null ? null : Renames(File.ReadAllLines(renamesFile));
        var paths = Tracked(root).Where(name =>
        {
            string full = Path.Combine(root, name);
            return File.Exists(full) && !File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint);
        });
        var problems = Check(paths, name => File.ReadAllText(Path.Combine(root, name)), baseline);
        if (trusted is not null) problems.AddRange(Compare(trusted, baseline, renames));
        return problems;
    }
}
