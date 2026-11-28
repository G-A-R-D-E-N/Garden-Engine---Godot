using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace GameDataGuard;

internal static class Checks
{
    private static void Require(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("Fixture failed: " + name);
    }

    public static void Run()
    {
        foreach (string bad in new[] { "opencw/.garden-cache/a.svg", "x/Interface.SWF", "Data/Fallout4 - Interface.ba2", "m/Fallout4.esm", "t/a.DDS" })
            Require(Guard.PathProblem(bad) != null, "forbidden path " + bad);
        foreach (string good in new[] { "opencw/assets/UI/Fonts/RobotoCondensed-Bold.ttf", "docs/ui/converted-notes.md", "opencw/scripts/UI/LocalArt.cs" })
            Require(Guard.PathProblem(good) == null, "allowed path " + good);
        foreach (string magic in new[] { "BTDX....", "TES4....", "FWS\u000f..", "CWS\u000f..", "DDS |...", "Gamebryo File Format, Version 20.2.0.7" })
            Require(Guard.HeadProblem(Encoding.Latin1.GetBytes(magic)) != null, "forbidden header " + magic);
        Require(Guard.HeadProblem("using System;"u8) == null && Guard.HeadProblem([]) == null, "ordinary header");
        foreach (string audio in new[] { "sounds/a.WAV", "music/a.ogg", "a.mp3" })
            Require(Guard.PathProblem(audio) != null, "local audio never tracked or packaged " + audio);
        foreach (string audio in new[] { "OggS0000", "RIFF0000WAVE", "RIFF0000XWMA" })
            Require(Guard.HeadProblem(Encoding.ASCII.GetBytes(audio)) != null, "renamed local audio rejected");
        Require(Guard.HeadProblem("RIFF0000WEBP"u8) == null, "unrelated RIFF format retained");
        foreach (string movie in new[] { "Video/intro.BK2", "video/logo.bik", "cache/movie.ogv", "video/movie.avi", "video/movie.mp4", "video/movie.webm" })
            Require(Guard.PathProblem(movie) != null, "local video never tracked or packaged " + movie);
        foreach (string path in new[] { "Interface/Translate_en.txt", "data/TRANSLATE_fr.TXT" })
            Require(Guard.PathProblem(path) != null, "installed translation cannot be tracked or packaged");

        string directory = Path.Combine(Path.GetTempPath(), "garden-guard-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckRepo(Path.Combine(directory, "repo"));
            CheckExport(Path.Combine(directory, "export"));
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(directory, true);
        }
        Console.WriteLine("GAME_DATA_GUARD_SELF_TEST PASS: path, header, repository, directory, zip, pack and embedded-pack fixtures");
    }

    private static void CheckRepo(string root)
    {
        Directory.CreateDirectory(root);
        Git(root, "init", "-q");
        Write(Path.Combine(root, "src", "Main.cs"), "class Main {}");
        Require(Guard.Repo(root).Count == 0, "clean repository");
        Write(Path.Combine(root, "opencw", ".garden-cache", "a.svg"), "<svg/>");
        File.WriteAllBytes(Path.Combine(root, "data.bin"), "BTDX0000"u8.ToArray());
        Git(root, "add", "-A", "-f");
        var found = Guard.Repo(root);
        Require(found.Count == 2 && found.Any(row => row.Contains("local runtime cache path")) && found.Any(row => row.Contains("header BTDX")), "tracked game data is reported");
    }

    private static void CheckExport(string root)
    {
        Write(Path.Combine(root, "plain", "scene.tscn"), "[ext_resource path=\"res://Converted/UI/x.svg\"]");
        byte[] clean = Pack(4, "res://scenes/main.tscn", "scripts/Player.cs", ".godot/exported/x/a.scn");
        File.WriteAllBytes(Path.Combine(root, "plain", "clean.pck"), clean);
        Require(Guard.Export(Path.Combine(root, "plain")).Count == 0, "clean export, scene text is not scanned");
        string pack = Path.Combine(root, "dirty.pck");
        File.WriteAllBytes(pack, Pack(4, "Sounds/focus.wav", "sounds/theme.ogg", "Interface/Translate_en.txt", "Video/intro.bk2", "movie.ogv"));
        Require(Guard.Export(pack).Count == 5, "local sounds, video and translation rejected inside pack");
        foreach (uint version in new uint[] { 2, 3, 4 })
        {
            File.WriteAllBytes(pack, Pack(version, "res://scenes/main.tscn", "res://.garden-cache/UI/Animations/A/1.svg", "Data/menu.swf", "converted/meshes/a.nif"));
            var packed = Guard.Export(pack);
            Require(packed.Count == 3 && packed.All(row => row.Contains("dirty.pck!")), "pack v" + version + " directory entries are reported with or without res://");
        }
        string game = Path.Combine(root, "Game.exe");
        byte[] embedded = Pack(4, ".garden-cache/UI/a.svg");
        File.WriteAllBytes(game, new byte[]{ 77, 90, 0, 0 }.Concat(embedded).Concat(BitConverter.GetBytes((ulong)embedded.Length)).Concat("GDPC"u8.ToArray()).ToArray());
        Require(Guard.Export(game).Count == 1, "pack embedded in an executable is inspected");
        Write(Path.Combine(root, "owned-library", "Plugin.dll"), "owned assembly fixture");
        Require(Guard.Export(Path.Combine(root, "owned-library")).Count == 0, "assembly with a clean header allowed");
        File.WriteAllBytes(Path.Combine(root, "owned-library", "Plugin.dll"), "FWS00000"u8.ToArray());
        Require(Guard.Export(Path.Combine(root, "owned-library")).Single().Contains("game data header"), "assembly header is still checked");
        File.WriteAllBytes(Path.Combine(root, "Plain.exe"), new byte[] { 77, 90, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 });
        Require(Guard.Export(Path.Combine(root, "Plain.exe")).Count == 0, "executable without a pack has nothing to inspect");
        File.Delete(game); File.Delete(Path.Combine(root, "Plain.exe"));
        byte[] unknown = Pack(9, "res://a");
        File.WriteAllBytes(pack, unknown);
        try { Guard.Export(pack); throw new InvalidOperationException("Unknown pack format accepted"); } catch (InvalidDataException) { }
        File.WriteAllBytes(pack, Pack(4, "res://a")[..110]);
        try { Guard.Export(pack); throw new InvalidOperationException("Truncated pack accepted"); } catch (Exception error) when (error is InvalidDataException or EndOfStreamException) { }
        File.WriteAllBytes(pack, Pack(3, "res://.garden-cache/a")); File.SetAttributes(pack, FileAttributes.Normal);
        string clone = Path.Combine(root, "copy", "dirty.pck");
        Directory.CreateDirectory(Path.GetDirectoryName(clone)!);
        File.Copy(pack, clone);
        string zipPath = Path.Combine(root, "export.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("game/ok.txt").Open())) writer.Write("fine");
            zip.CreateEntry("game/.garden-cache/a.svg").Open().Dispose();
            using (var writer = new BinaryWriter(zip.CreateEntry("game/blob.dat").Open())) writer.Write("TES4...."u8.ToArray());
        }
        Require(Guard.Export(zipPath).Count == 2, "zip entries are reported by name and header");
        Require(Guard.Export(root).Count >= 4, "directory export scans nested files and packs");
        try { Guard.Export(Path.Combine(root, "absent")); throw new InvalidOperationException("Missing export accepted"); }
        catch (FileNotFoundException) { }
    }

    private static byte[] Pack(uint version, params string[] paths)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("GDPC"u8.ToArray()); writer.Write(version); writer.Write(4u); writer.Write(8u); writer.Write(0u); writer.Write(0u);
        writer.Write(0UL);
        if (version > 2) writer.Write(104UL);
        writer.Write(new byte[64]);
        writer.Write((uint)paths.Length);
        foreach (string path in paths)
        {
            byte[] text = Encoding.UTF8.GetBytes(path);
            int padded = (text.Length + 3) & ~3;
            writer.Write((uint)padded); writer.Write(text); writer.Write(new byte[padded - text.Length]);
            writer.Write(0UL); writer.Write(0UL); writer.Write(new byte[16]); writer.Write(0u);
        }
        return stream.ToArray();
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-C"); start.ArgumentList.Add(root);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git " + arguments[0] + " failed");
    }
}
