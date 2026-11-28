using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace GameDataGuard;

internal static class Guard
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".swf", ".gfx", ".ba2", ".bsa", ".esm", ".esp", ".esl", ".nif", ".dds", ".bgsm", ".bgem", ".hkx", ".fuz", ".xwm", ".lip",
        ".strings", ".dlstrings", ".ilstrings", ".wav", ".ogg", ".mp3", ".bk2", ".bik", ".ogv", ".avi", ".mp4", ".webm",
    };
    private static readonly HashSet<string> CacheDirectories = new(StringComparer.OrdinalIgnoreCase) { ".garden-cache", "ui-layout-cache" };
    private static readonly HashSet<string> TextData = new(StringComparer.OrdinalIgnoreCase) { ".json", ".csv", ".tsv", ".txt", ".xml" };
    private static readonly string[] Magic = ["BTDX", "TES4", "FWS", "CWS", "ZWS", "GFX", "CFX", "DDS ", "Gamebryo File Format", "NetImmerse File Format"];
    private static readonly Regex StringRow = new(@"^\s*(\$[A-Za-z0-9_]+\t|""\$[A-Za-z0-9_]+""\s*:|[0-9A-Fa-f]{8}\t)\S");
    private static readonly Regex LayoutRow = new(@"(?i)""?\b(x|y|width|height|depth|tx|ty|scalex|scaley|rotation|alpha|charid|char_id|characterid)""?\s*[:=]\s*""?-?\d+(\.\d+)?(?![\w.])");
    private const int TableRows = 20;
    private const uint PackMagic = 0x43504447;
    private static readonly HashSet<string> Scanned = new(StringComparer.OrdinalIgnoreCase) { ".pck", ".exe", ".x86_64", "" };

    internal static string? PathProblem(string path)
    {
        string[] parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        if (parts.Any(part => CacheDirectories.Contains(part))) return "local runtime cache path";
        string filename = parts[^1], extension = Path.GetExtension(filename).ToLowerInvariant();
        if (extension == ".txt" && filename.StartsWith("Translate_", StringComparison.OrdinalIgnoreCase)) return "installed interface translation table";
        return Extensions.Contains(Path.GetExtension(parts[^1])) ? "game data file type" : null;
    }

    internal static string? TableProblem(string text)
    {
        string[] lines = text.Split('\n');
        if (lines.Count(line => StringRow.IsMatch(line)) >= TableRows) return "strings dump";
        if (lines.Count(line => LayoutRow.IsMatch(line)) >= TableRows) return "layout table";
        return null;
    }

    internal static string? HeadProblem(ReadOnlySpan<byte> head)
    {
        foreach (string magic in Magic)
            if (head.StartsWith(Encoding.ASCII.GetBytes(magic))) return "game data header " + magic.Trim();
        if (head.StartsWith("OggS"u8) || head.Length >= 12 && head.StartsWith("RIFF"u8) && (head.Slice(8, 4).SequenceEqual("WAVE"u8) || head.Slice(8, 4).SequenceEqual("XWMA"u8))) return "local sound data header";
        return null;
    }

    internal static List<string> Repo(string root)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (string argument in new[] { "-C", root, "ls-files", "-z" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string listing = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git ls-files failed in " + root);
        var problems = new List<string>();
        foreach (string name in listing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            string full = Path.Combine(root, name);
            Entry(problems, name, () => File.Exists(full) && !File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint) ? File.OpenRead(full) : null);
        }
        return problems;
    }

    internal static List<string> Export(string path)
    {
        var problems = new List<string>();
        if (File.Exists(path)) { Archive(path, Path.GetFileName(path), problems); return problems; }
        if (!Directory.Exists(path)) throw new FileNotFoundException("Export path not found: " + path);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (string file in Directory.EnumerateFiles(path, "*", options))
        {
            string relative = Path.GetRelativePath(path, file).Replace('\\', '/');
            Entry(problems, relative, () => File.OpenRead(file));
            Archive(file, relative, problems);
        }
        return problems;
    }

    private static void Archive(string file, string name, List<string> problems)
    {
        string extension = Path.GetExtension(file);
        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(file);
            foreach (var entry in zip.Entries.Where(entry => entry.Name.Length != 0))
                Entry(problems, name + "!" + entry.FullName, () => entry.Open(), entry.FullName);
        }
        else if (Scanned.Contains(extension)) Pack(file, name, problems);
    }

    private static void Entry(List<string> problems, string name, Func<Stream?> open, string? inner = null)
    {
        string entry = inner ?? name;
        if (PathProblem(entry) is { } path) { problems.Add(path + ": " + name); return; }
        using var stream = open();
        if (stream == null) return;
        byte[] head = new byte[32];
        int read = stream.Read(head, 0, head.Length);
        if (HeadProblem(head.AsSpan(0, read)) is { } magic) { problems.Add(magic + ": " + name); return; }
        if (!TextData.Contains(Path.GetExtension(entry))) return;
        using var reader = new StreamReader(stream, Encoding.Latin1);
        if (TableProblem(Encoding.Latin1.GetString(head, 0, read) + reader.ReadToEnd()) is { } table) problems.Add(table + ": " + name);
    }

    private static void Pack(string file, string name, List<string> problems)
    {
        using var stream = File.OpenRead(file);
        using var reader = new BinaryReader(stream);
        long start = 0;
        if (!file.EndsWith(".pck", StringComparison.OrdinalIgnoreCase))
        {
            if (stream.Length < 12) return;
            stream.Position = stream.Length - 12;
            ulong embedded = reader.ReadUInt64();
            if (reader.ReadUInt32() != PackMagic) return;
            if (embedded > (ulong)(stream.Length - 12)) throw new InvalidDataException("Embedded pack size is invalid: " + name);
            start = stream.Length - 12 - (long)embedded;
        }
        stream.Position = start;
        if (stream.Length - start < 40 || reader.ReadUInt32() != PackMagic) throw new InvalidDataException("Not a Godot pack: " + name);
        uint version = reader.ReadUInt32();
        if (version is < 2 or > 4) throw new InvalidDataException("Unsupported pack format " + version + ": " + name);
        stream.Position = start + 20;
        if ((reader.ReadUInt32() & 1) != 0) throw new InvalidDataException("Encrypted pack directory cannot be inspected: " + name);
        reader.ReadUInt64();
        long directory = version == 2 ? 96 : checked((long)reader.ReadUInt64());
        if (directory < 40 || start + directory + 4 > stream.Length) throw new InvalidDataException("Pack directory is outside the file: " + name);
        stream.Position = start + directory;
        uint count = reader.ReadUInt32();
        for (uint index = 0; index < count; index++)
        {
            uint length = reader.ReadUInt32();
            if (length > 4096 || stream.Position + length + 36 > stream.Length) throw new InvalidDataException("Pack directory entry is invalid: " + name);
            string entry = Encoding.UTF8.GetString(reader.ReadBytes((int)length)).TrimEnd('\0');
            stream.Seek(36, SeekOrigin.Current);
            if (PathProblem(entry.StartsWith("res://", StringComparison.Ordinal) ? entry["res://".Length..] : entry) is { } problem)
                problems.Add(problem + ": " + name + "!" + entry);
        }
    }
}
