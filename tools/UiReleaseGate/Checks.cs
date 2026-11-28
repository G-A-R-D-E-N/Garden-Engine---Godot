using System.Diagnostics;
using System.Text.Json;

namespace UiReleaseGate;

internal static class Checks
{
    public static void Run()
    {
        int count = 0;
        Case("reviewed sources", _ => { }, p => p.Count == 0);
        Case("pending provenance", f => f.Rows[0]["provenance"] = "pending", Has("review pending"));
        Case("quarantined source", f => f.Rows[0]["release_status"] = "blocked", Has("review pending"));
        Case("changed source", f => f.Write(f.PathAt(0), "changed"), Has("Source changed"));
        Case("line endings", f => File.WriteAllBytes(f.FileAt(0), "fixture\r\n"u8.ToArray()), p => p.Count == 0);
        foreach (var (name, path) in new (string Name, string Path)[]
        {
            ("component tags", "opencw/scripts/Player/PlayerState.Components.cs"),
            ("component copy ownership", "opencw/scripts/Player/PlayerState.Persistence.cs"),
            ("appearance save ownership", "opencw/scripts/Player/PlayerAppearance.cs"),
            ("player input consumer", "opencw/scripts/Player/PlayerInput.cs"),
            ("program sound records", "opencw/Services/ProgramSoundRecords.cs"),
            ("native perk reader", "opencw/Services/NativePerkReader.cs"),
            ("native workshop reader", "opencw/Services/NativeWorkshopReader.cs"),
            ("workshop object owner", "opencw/scripts/WorkshopObjects.cs"),
            ("workshop placement", "opencw/scripts/WorkshopPlacement.cs"),
            ("ordinary door activation", "opencw/scripts/DoorToggle.cs"),
            ("workshop materials", "opencw/scripts/WorkshopMaterials.cs"),
            ("native materials", "opencw/Services/NativeMaterialReader.cs"),
            ("currency identity", "opencw/Services/InventoryCurrency.cs"),
            ("native inventory", "opencw/Services/NativeInventoryReader.cs"),
            ("perk progression caller", "opencw/scripts/Player/ProgressionService.cs"),
            ("native training assets", "opencw/Services/Core/GameLoader.cs"),
            ("yard state declaration", "opencw/scripts/TestLevelHud.cs"),
            ("yard persistence", "opencw/scripts/TestLevelHud.Persistence.cs"),
            ("workshop state", "opencw/scripts/WorkshopRegistry.cs"),
        })
            Case(name, f => f.Write(path, "fixture"), Has("Missing inventory entry: " + path));
        Case("new source", f => f.Write("opencw/scripts/UI/New.cs", "fixture"), Has("Missing inventory entry"));
        Case("linked UI project source", f => f.Write("opencw/OpenCw.Ui/Menus/New.cs", "fixture"), Has("Missing inventory entry: opencw/OpenCw.Ui/Menus/New.cs"));
        Case("linked UI project build output", f => f.Write("opencw/OpenCw.Ui/obj/Generated.cs", "fixture"), p => p.Count == 0);
        Case("deleted source", f => File.Delete(f.FileAt(0)), Has("Stale inventory entry"));
        Case("missing review record", f => f.Rows[0]["review_record"] = "", Has("invalid review record"));
        Case("nonexistent review record", f => f.Rows[0]["review_record"] = "docs/ui/missing.md", Has("invalid review record"));
        Case("stale review record", f => { f.Write(f.PathAt(0), "changed"); f.Rows[0]["sha256"] = Provenance.Fingerprint(f.FileAt(0)); }, Has("does not identify"));
        Case("review pairs the path with another hash", f =>
        {
            f.Write(f.PathAt(0), "changed\n");
            f.Rows[0]["sha256"] = Provenance.Fingerprint(f.FileAt(0));
            f.Write("docs/ui/fixture-review.md", f.PathAt(0) + " " + new string('0', 64) + "\nopencw/scripts/UI/Other.cs " + f.Rows[0]["sha256"] + "\n");
        }, Has("does not identify"));
        Case("review names a longer path", f => f.Write("docs/ui/fixture-review.md", f.PathAt(0) + ".bak " + f.Rows[0]["sha256"] + "\n"), Has("does not identify"));
        Case("review hash inside a longer token", f => f.Write("docs/ui/fixture-review.md", f.PathAt(0) + " " + f.Rows[0]["sha256"] + "0\n"), Has("does not identify"));
        Case("review pair in a table row", f => f.Write("docs/ui/fixture-review.md", "| `" + f.PathAt(0) + "` | `" + f.Rows[0]["sha256"] + "` |\n"), p => p.Count == 0);
        Case("parent review escape", f => f.Rows[0]["review_record"] = "../outside.md", Has("invalid review record"));
        Case("Windows rooted review", f => f.Rows[0]["review_record"] = "\\outside.md", Has("invalid review record"));
        Case("Windows drive-relative review", f => f.Rows[0]["review_record"] = "C:outside.md", Has("invalid review record"));
        Case("invalid provenance", f => f.Rows[0]["provenance"] = "assumed", Has("Invalid provenance"));
        Throws("duplicate path", f => f.Rows.Add(new(f.Rows[0])), typeof(InvalidDataException));
        Throws("case duplicate", f => { var row = new Dictionary<string, object>(f.Rows[0]); row["path"] = f.PathAt(0).ToUpperInvariant(); f.Rows.Add(row); }, typeof(InvalidDataException));
        Throws("unsupported schema", f => f.Schema = 2, typeof(InvalidDataException));
        using (var fixture = new Fixture())
        {
            fixture.Save(); File.WriteAllText(fixture.Full("docs/ui/ui-provenance.json"), "{");
            ExpectException(() => Provenance.Check(fixture.Root), typeof(JsonException), "malformed manifest"); count++;
        }
        using (var fixture = new Fixture())
        {
            fixture.Write("bytes.bin", ""); File.WriteAllBytes(fixture.Full("bytes.bin"), [255, 13, 10]);
            string hash = Provenance.Fingerprint(fixture.Full("bytes.bin"));
            File.WriteAllBytes(fixture.Full("bytes.bin"), [255, 10]);
            Require(hash == Provenance.Fingerprint(fixture.Full("bytes.bin")), "hash normalizes bytes without UTF8 decoding"); count++;
        }
        using (var fixture = new Fixture())
        using (var outside = new Fixture())
        {
            fixture.Save(); outside.Write("review.md", File.ReadAllText(fixture.Full("docs/ui/fixture-review.md")));
            string link = fixture.Full("docs/ui/linked");
            Link(link, outside.Root);
            fixture.Rows[0]["review_record"] = "docs/ui/linked/review.md"; fixture.Save();
            Require(Provenance.Check(fixture.Root).Any(p => p.Contains("invalid review record")), "linked review escape"); count++;
            Require(File.Exists(outside.Full("review.md")), "outside target preserved");
        }
        using (var fixture = new Fixture())
        {
            fixture.Save(); string link = fixture.Full("docs/ui/inside");
            fixture.Write("docs/owned/review.md", File.ReadAllText(fixture.Full("docs/ui/fixture-review.md")));
            Link(link, fixture.Full("docs/owned"));
            fixture.Rows[0]["review_record"] = "docs/ui/inside/review.md"; fixture.Save();
            Require(Provenance.Check(fixture.Root).Count == 0, "in-root linked review accepted"); count++;
        }
        using (var fixture = new Fixture())
        using (var outside = new Fixture())
        {
            fixture.Save(); Link(fixture.Full("opencw/scripts/UI/linked"), outside.Full("docs/ui"));
            ExpectException(() => Provenance.Check(fixture.Root), typeof(InvalidDataException), "linked source directory rejected"); count++;
            Require(File.Exists(outside.Full("docs/ui/fixture-review.md")), "linked source target preserved");
        }
        if (!OperatingSystem.IsWindows())
        {
            using (var fixture = new Fixture())
            using (var outside = new Fixture())
            {
                File.Delete(fixture.FileAt(0)); File.CreateSymbolicLink(fixture.FileAt(0), outside.FileAt(0)); fixture.Save();
                Require(Provenance.Check(fixture.Root).Any(p => p.Contains("Source outside repository")), "outside linked source rejected"); count++;
                Require(File.ReadAllText(outside.FileAt(0)) == "fixture\n", "outside linked source preserved");
            }
            using (var fixture = new Fixture())
            {
                fixture.Write("docs/ui/source.cs", "fixture\n"); File.Delete(fixture.FileAt(0));
                File.CreateSymbolicLink(fixture.FileAt(0), fixture.Full("docs/ui/source.cs")); fixture.Save();
                Require(Provenance.Check(fixture.Root).Count == 0, "inside linked source accepted"); count++;
            }
        }
        Console.WriteLine($"UI_PROVENANCE_SELF_TEST PASS: {count} owned inventory, hash, review, and link cases");

        void Case(string name, Action<Fixture> arrange, Func<List<string>, bool> matches)
        {
            using var fixture = new Fixture(); arrange(fixture); fixture.Save();
            Require(matches(Provenance.Check(fixture.Root)), name); count++;
        }
        void Throws(string name, Action<Fixture> arrange, Type type)
        {
            using var fixture = new Fixture(); arrange(fixture); fixture.Save();
            ExpectException(() => Provenance.Check(fixture.Root), type, name); count++;
        }
    }

    private static Func<List<string>, bool> Has(string text) => problems => problems.Any(p => p.Contains(text, StringComparison.Ordinal));
    private static void Require(bool value, string name) { if (!value) throw new InvalidOperationException("Fixture failed: " + name); }
    private static void ExpectException(Action action, Type type, string name)
    {
        try { action(); }
        catch (Exception error) when (type.IsInstanceOfType(error)) { return; }
        throw new InvalidOperationException("Fixture failed to reject: " + name);
    }

    private static void Link(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var info = new ProcessStartInfo("cmd.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "/c", "mklink", "/J", link, target }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot create owned junction fixture");
        string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(); Require(process.ExitCode == 0, "junction creation: " + stdout + stderr);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "garden-ui-gate-" + Guid.NewGuid().ToString("N"));
        public List<Dictionary<string, object>> Rows { get; } = [];
        public int Schema { get; set; } = 1;
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Add("opencw/scripts/UI/Menu.cs", false);
            Write("docs/ui/fixture-review.md", "Owned fixture only; no distribution approval\n" + string.Join("\n", Rows.Select(row => row["path"] + " " + row["sha256"])));
        }
        public string Full(string path) => Path.GetFullPath(Path.Combine(Root, path));
        public string PathAt(int index) => (string)Rows[index]["path"];
        public string FileAt(int index) => Full(PathAt(index));
        public void Write(string path, string text) { string file = Full(path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text); }
        private void Add(string path, bool pending)
        {
            Write(path, "fixture\n");
            Rows.Add(new() { ["path"] = path, ["sha256"] = Provenance.Fingerprint(Full(path)), ["provenance"] = pending ? "pending" : "original",
                ["release_status"] = pending ? "blocked" : "reviewed", ["review_record"] = pending ? "" : "docs/ui/fixture-review.md" });
        }
        public void Save() => Write("docs/ui/ui-provenance.json", JsonSerializer.Serialize(new { schema = Schema, files = Rows }));
        public void Dispose() => Remove(Root);
        private void Remove(string folder)
        {
            string full = Path.GetFullPath(folder);
            if (full != Root && !Paths.Within(full, Root)) throw new InvalidOperationException("Cleanup escaped owned fixture");
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(folder, false); return; }
            foreach (string file in Directory.EnumerateFiles(folder)) File.Delete(file);
            foreach (string child in Directory.EnumerateDirectories(folder)) Remove(child);
            Directory.Delete(folder, false);
        }
    }
}
