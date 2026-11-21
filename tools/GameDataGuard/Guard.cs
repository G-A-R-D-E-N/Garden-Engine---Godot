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
    private const int MaxDepth = 4;
    private const long MaxNested = 8L << 30;
    private static readonly HashSet<string> Scanned = new(StringComparer.OrdinalIgnoreCase) { ".pck", ".exe", ".dll", ".so", ".dylib", ".x86_64", ".arm64", "" };

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
        foreach (string argument in new[] { "-C", root, "ls-files", "-s", "-z" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string listing = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git ls-files failed in " + root);
        var problems = new List<string>();
        using var blobs = new GitBlobs(root);
        foreach (string row in listing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = row.IndexOf('\t');
            string[] meta = row[..tab].Split(' ');
            string mode = meta[0], id = meta[1];
            Entry(problems, row[(tab + 1)..], () => mode is "120000" or "160000" ? null : blobs.Open(id));
        }
        return problems;
    }

    internal static List<string> Export(string path)
    {
        var problems = new List<string>();
        if (File.Exists(path)) { Archive(() => File.OpenRead(path), Path.GetFileName(path), Path.GetExtension(path), problems, 0); return problems; }
        if (!Directory.Exists(path)) throw new FileNotFoundException("Export path not found: " + path);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (string file in Directory.EnumerateFiles(path, "*", options))
        {
            string relative = Path.GetRelativePath(path, file).Replace('\\', '/');
            Entry(problems, relative, () => File.OpenRead(file));
            Archive(() => File.OpenRead(file), relative, Path.GetExtension(file), problems, 0);
        }
        return problems;
    }

    private static void Archive(Func<Stream> open, string name, string extension, List<string> problems, int depth)
    {
        bool zip = extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
        if (!zip && !Scanned.Contains(extension)) return;
        if (depth > MaxDepth) throw new InvalidDataException("Archives nest too deeply to inspect: " + name);
        using var stream = Seekable(open(), name);
        if (!zip) { Pack(stream, extension, name, problems, depth); return; }
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        foreach (var entry in archive.Entries.Where(entry => entry.Name.Length != 0))
        {
            string label = name + "!" + entry.FullName;
            Entry(problems, label, () => entry.Open(), entry.FullName);
            Archive(() => entry.Open(), label, Path.GetExtension(entry.FullName), problems, depth + 1);
        }
    }

    private static Stream Seekable(Stream source, string name)
    {
        if (source.CanSeek) return source;
        using (source)
        {
            var temp = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
            try
            {
                var buffer = new byte[1 << 16];
                for (int read; (read = source.Read(buffer, 0, buffer.Length)) > 0;)
                {
                    temp.Write(buffer, 0, read);
                    if (temp.Length > MaxNested) throw new InvalidDataException("Nested archive is too large to inspect: " + name);
                }
                temp.Position = 0;
                return temp;
            }
            catch { temp.Dispose(); throw; }
        }
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

    private static void Pack(Stream stream, string extension, string name, List<string> problems, int depth)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        long start = 0, end = stream.Length;
        if (!extension.Equals(".pck", StringComparison.OrdinalIgnoreCase))
        {
            if (stream.Length < 12) return;
            stream.Position = stream.Length - 12;
            ulong embedded = reader.ReadUInt64();
            if (reader.ReadUInt32() != PackMagic) return;
            if (embedded > (ulong)(stream.Length - 12)) throw new InvalidDataException("Embedded pack size is invalid: " + name);
            end = stream.Length - 12;
            start = end - (long)embedded;
        }
        stream.Position = start;
        if (end - start < 40 || reader.ReadUInt32() != PackMagic) throw new InvalidDataException("Not a Godot pack: " + name);
        uint version = reader.ReadUInt32();
        if (version is < 2 or > 4) throw new InvalidDataException("Unsupported pack format " + version + ": " + name);
        stream.Position = start + 20;
        uint flags = reader.ReadUInt32();
        if ((flags & 1) != 0) throw new InvalidDataException("Encrypted pack directory cannot be inspected: " + name);
        if ((flags & 4) != 0) throw new InvalidDataException("Sparse pack bundle cannot be inspected: " + name);
        long fileBase = start + checked((long)reader.ReadUInt64());
        long directory = version == 2 ? 96 : checked((long)reader.ReadUInt64());
        if (directory < 40 || start + directory + 4 > end) throw new InvalidDataException("Pack directory is outside the file: " + name);
        stream.Position = start + directory;
        uint count = reader.ReadUInt32();
        for (uint index = 0; index < count; index++)
        {
            uint length = reader.ReadUInt32();
            if (length > 4096 || stream.Position + length + 36 > end) throw new InvalidDataException("Pack directory entry is invalid: " + name);
            string entry = Encoding.UTF8.GetString(reader.ReadBytes((int)length)).TrimEnd('\0');
            ulong offset = reader.ReadUInt64(), size = reader.ReadUInt64();
            stream.Seek(16, SeekOrigin.Current);
            uint entryFlags = reader.ReadUInt32();
            long next = stream.Position;
            if ((entryFlags & 2) != 0) continue;
            if ((entryFlags & 5) != 0) throw new InvalidDataException("Encrypted or delta pack entry cannot be inspected: " + name + "!" + entry);
            if (fileBase > end || offset > (ulong)(end - fileBase) || size > (ulong)(end - fileBase) - offset) throw new InvalidDataException("Pack entry lies outside the pack: " + name + "!" + entry);
            string path = entry.StartsWith("res://", StringComparison.Ordinal) ? entry["res://".Length..] : entry;
            string label = name + "!" + entry;
            Func<Stream> open = () => new Slice(stream, fileBase + (long)offset, (long)size);
            Entry(problems, label, open, path);
            Archive(open, label, Path.GetExtension(path), problems, depth + 1);
            stream.Position = next;
        }
    }
}
