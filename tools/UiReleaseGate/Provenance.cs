using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UiReleaseGate;

internal static class Provenance
{
    public static string Fingerprint(string path) => Convert.ToHexString(SHA256.HashData(
        Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(File.ReadAllBytes(path)).Replace("\r\n", "\n", StringComparison.Ordinal)))).ToLowerInvariant();

    public static List<string> Check(string root)
    {
        root = Path.GetFullPath(root);
        string canonicalRoot = Paths.Resolve(root);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/ui/ui-provenance.json")));
        var manifest = document.RootElement;
        if (manifest.GetProperty("schema").GetInt32() != 1) throw new InvalidDataException("Unsupported UI provenance schema");
        var rows = manifest.GetProperty("files").EnumerateArray().ToArray();
        var allPaths = rows.Select(row => Text(row, "path")).ToArray();
        if (allPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != allPaths.Length) throw new InvalidDataException("Duplicate UI provenance path");
        var paths = allPaths.ToHashSet(StringComparer.Ordinal);
        var expected = new HashSet<string>(StringComparer.Ordinal);
        Sources("opencw/scripts/UI");
        Sources("opencw/scripts/Save");
        Sources("opencw/scripts/Settings");
        Sources("opencw/OpenCw.Ui");
        foreach (string path in new[] { "opencw/Services/ProgramSoundRecords.cs", "opencw/Services/NativePerkReader.cs", "opencw/Services/NativeWorkshopReader.cs", "opencw/Services/NativeWorkbenchReader.cs", "opencw/Services/NativeMaterialReader.cs", "opencw/Services/NativeInventoryReader.cs", "opencw/Services/InventoryCurrency.cs", "opencw/Services/NativeBookReader.cs", "opencw/Services/NativeHolotapeReader.cs", "opencw/Services/NativeMessageReader.cs", "opencw/Services/NativeTerminalReader.cs", "opencw/Services/CellService.Books.cs", "opencw/Services/CellService.Inventory.cs", "opencw/scripts/Player/ProgressionService.cs", "opencw/scripts/Player/PlayerState.Components.cs", "opencw/scripts/Player/PlayerState.Persistence.cs", "opencw/scripts/MapData.cs", "opencw/scripts/Player/PlayerController.cs", "opencw/scripts/Player/PlayerRadio.cs", "opencw/scripts/Player/PlayerHolotape.cs", "opencw/scripts/Player/PlayerInput.cs", "opencw/scripts/Player/PlayerAppearance.cs","opencw/Services/Core/GameLoader.cs", "opencw/scripts/TestLevelHud.cs", "opencw/scripts/TestLevelHud.Persistence.cs", "opencw/scripts/WorkshopRegistry.cs", "opencw/scripts/WorkshopObjects.cs", "opencw/scripts/WorkshopPlacement.cs", "opencw/scripts/WorkshopMaterials.cs", "opencw/scripts/DoorToggle.cs", "opencw/scripts/WorldspaceStreamer.cs", "opencw/scripts/WorldspaceStreamer.Doors.cs" })
            if (File.Exists(Path.Combine(root, path))) expected.Add(path);
        var problems = expected.Except(paths).Order(StringComparer.Ordinal).Select(path => "Missing inventory entry: " + path).ToList();
        problems.AddRange(paths.Except(expected).Order(StringComparer.Ordinal).Select(path => "Stale inventory entry: " + path));
        foreach (var row in rows)
        {
            string path = Text(row, "path");
            if (!expected.Contains(path)) continue;
            string source = Path.Combine(root, path);
            if (!File.Exists(source)) { problems.Add("Missing source: " + path); continue; }
            if (!Paths.Within(Paths.Resolve(source), canonicalRoot)) { problems.Add("Source outside repository: " + path); continue; }
            string hash = Text(row, "sha256");
            if (Fingerprint(source) != hash) problems.Add("Source changed since inventory/review: " + path);
            string provenance = Text(row, "provenance"), status = Text(row, "release_status");
            if (provenance is not ("original" or "source-informed" or "third-party" or "pending")) problems.Add("Invalid provenance: " + path);
            if (status != "reviewed" || provenance == "pending") problems.Add("UI release review pending: " + path);
            if (status != "reviewed") continue;
            string record = row.TryGetProperty("review_record", out var recordValue) ? recordValue.GetString() ?? "" : "";
            if (!Paths.Review(root, canonicalRoot, record, out string reviewPath)) { problems.Add("Missing or invalid review record: " + path); continue; }
            string review = File.ReadAllText(reviewPath);
            if (!Identifies(review, path, hash))
                problems.Add("Review record does not identify this source/hash: " + path);
        }
        return problems;

        string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
        void Sources(string relative)
        {
            string directory = Path.Combine(root, relative);
            if (!Directory.Exists(directory)) return;
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked source directory cannot be inventoried: " + relative);
            foreach (string file in Directory.EnumerateFiles(directory))
                if (Path.GetExtension(file) == ".cs") expected.Add(Relative(file));
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (relative == "opencw/OpenCw.Ui" && Path.GetFileName(child) is "bin" or "obj") continue;
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked source directory cannot be inventoried: " + Relative(child));
                Sources(Relative(child));
            }
        }
    }
    internal static bool Identifies(string review, string path, string hash)
    {
        foreach (string line in review.Split('\n'))
        {
            string[] tokens = line.Split([' ', '\t', '\r', '|', '`'], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < tokens.Length; i++)
                if (tokens[i] == path && string.Equals(tokens[i + 1], hash, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string Text(JsonElement row, string key) => row.GetProperty(key).GetString() ?? throw new InvalidDataException("Missing provenance value: " + key);
}
