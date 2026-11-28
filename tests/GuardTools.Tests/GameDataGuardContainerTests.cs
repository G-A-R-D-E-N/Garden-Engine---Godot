using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using GameDataGuard;
using Xunit;

namespace GuardTools.Tests;

public sealed class GameDataGuardContainerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "guard-containers-" + Guid.NewGuid().ToString("N"));

    public GameDataGuardContainerTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }

    [Fact]
    public void PackedEntryContentIsInspected()
    {
        string pack = Save("export.pck", Pck(("res://data/a.bin", "BTDX0000"u8.ToArray(), 0), ("res://data/ok.bin", "plain bytes"u8.ToArray(), 0)));
        Assert.Equal("game data header BTDX: export.pck!res://data/a.bin", Assert.Single(Guard.Export(pack)));
    }

    [Fact]
    public void PackedTextTableIsInspected()
    {
        byte[] table = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 25).Select(i => $"$KEY_{i}\tText {i}\n")));
        string pack = Save("export.pck", Pck(("res://strings/en.txt", table, 0)));
        Assert.Equal("strings dump: export.pck!res://strings/en.txt", Assert.Single(Guard.Export(pack)));
    }

    [Theory]
    [InlineData("Game.exe")]
    [InlineData("libgame.dll")]
    [InlineData("libgame.so")]
    [InlineData("libgame.dylib")]
    [InlineData("Game.x86_64")]
    [InlineData("Game.arm64")]
    [InlineData("Game")]
    public void PackEmbeddedInEveryHostFormatIsInspected(string host)
    {
        byte[] pack = Pck((".garden-cache/UI/a.svg", [], 0));
        string path = Save(host, Embed(pack));
        Assert.Equal("local runtime cache path: " + host + "!.garden-cache/UI/a.svg", Assert.Single(Guard.Export(path)));
        Assert.Single(Guard.Export(Directory.GetParent(path)!.FullName));
    }

    [Theory]
    [InlineData(2u, 0u)]
    [InlineData(2u, 2u)]
    [InlineData(3u, 0u)]
    [InlineData(4u, 0u)]
    public void EmbeddedPackPayloadUsesTheFormatFileBase(uint version, uint flags)
    {
        ulong fileBase = version == 2 && (flags & 2) == 0 ? 4UL : 0UL;
        byte[] pack = Pck(version, flags, fileBase, ("res://data/a.bin", "BTDX0000safe"u8.ToArray(), 0));
        string path = Save("Game.exe", Embed(pack.Concat(new byte[4]).ToArray()));
        Assert.Equal("game data header BTDX: Game.exe!res://data/a.bin", Assert.Single(Guard.Export(path)));
    }

    [Fact]
    public void PackInsideZipIsInspected()
    {
        byte[] pack = Pck(("res://meshes/a.nif", [], 0));
        string zip = Save("release.zip", Zip(("game.pck", pack)));
        Assert.Equal("game data file type: release.zip!game.pck!res://meshes/a.nif", Assert.Single(Guard.Export(zip)));
    }

    [Fact]
    public void EmbeddedPackAndNestedZipInsideZipAreInspected()
    {
        byte[] inner = Zip(("deep/blob.dat", "TES4...."u8.ToArray()));
        string zip = Save("release.zip", Zip(("bin/Game.exe", Embed(Pck((".garden-cache/a", [], 0)))), ("more/extra.zip", inner)));
        var found = Guard.Export(zip);
        Assert.Equal(2, found.Count);
        Assert.Contains("local runtime cache path: release.zip!bin/Game.exe!.garden-cache/a", found);
        Assert.Contains("game data header TES4: release.zip!more/extra.zip!deep/blob.dat", found);
    }

    [Fact]
    public void ZipInsidePackIsInspected()
    {
        string pack = Save("export.pck", Pck(("res://bundle.zip", Zip(("a.ogg", [])), 0)));
        Assert.Equal("game data file type: export.pck!res://bundle.zip!a.ogg", Assert.Single(Guard.Export(pack)));
    }

    [Fact]
    public void CleanNestedContainersPass()
    {
        string zip = Save("release.zip", Zip(("game.pck", Pck(("res://scenes/main.tscn", "[gd_scene]"u8.ToArray(), 0))), ("notes.txt", "hello"u8.ToArray())));
        Assert.Empty(Guard.Export(zip));
    }

    [Fact]
    public void NestingBeyondTheLimitIsRefused()
    {
        byte[] data = "ok"u8.ToArray();
        for (int level = 0; level < 8; level++) data = Zip(("inner.zip", data));
        Assert.Throws<InvalidDataException>(() => Guard.Export(Save("deep.zip", data)));
    }

    [Theory]
    [InlineData(8L << 30, true, false)]
    [InlineData((8L << 30) + 1, true, true)]
    [InlineData((8L << 30) + 1, false, false)]
    public void SeekableNestedArchivesEnforceTheSizeLimitAndCloseRejectedStreams(long length, bool nested, bool rejected)
    {
        using var source = new ReportedLengthStream(length);
        if (rejected)
        {
            Assert.Contains("too large", Assert.Throws<InvalidDataException>(() => Guard.Seekable(source, "outer.pck!inner.zip", nested)).Message);
            Assert.True(source.Closed);
        }
        else
        {
            Assert.Same(source, Guard.Seekable(source, "archive.zip", nested));
            Assert.False(source.Closed);
        }
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(4u)]
    public void EncryptedOrDeltaEntryIsRefused(uint flags)
    {
        string pack = Save("export.pck", Pck(("res://a.bin", "BTDX0000"u8.ToArray(), flags)));
        Assert.Contains("cannot be inspected", Assert.Throws<InvalidDataException>(() => Guard.Export(pack)).Message);
    }

    [Fact]
    public void RemovalEntryIsSkipped()
    {
        string pack = Save("export.pck", Pck(("res://gone.bin", [], 2)));
        Assert.Empty(Guard.Export(pack));
    }

    [Fact]
    public void RemovalEntryWithForbiddenPathIsReported()
    {
        string pack = Save("export.pck", Pck(("res://gone.nif", [], 2)));
        Assert.Contains(Guard.Export(pack), problem => problem.Contains("game data file type") && problem.Contains("gone.nif"));
    }

    [Fact]
    public void EntryOutsideThePackIsRefused()
    {
        byte[] bytes = Pck(("res://a.bin", "BTDX0000"u8.ToArray(), 0));
        string pack = Save("export.pck", bytes[..^4]);
        Assert.Throws<InvalidDataException>(() => Guard.Export(pack));
    }

    [Fact]
    public void SparseBundleIsRefused()
    {
        byte[] bytes = Pck(("res://a.bin", [], 0));
        BitConverter.GetBytes(4u).CopyTo(bytes, 20);
        Assert.Contains("Sparse", Assert.Throws<InvalidDataException>(() => Guard.Export(Save("export.pck", bytes))).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepositoryScanReadsTheStagedBlobNotTheWorkingTree(bool deleteWorkingCopy)
    {
        string repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        string file = Path.Combine(repo, "data.bin");
        File.WriteAllBytes(file, "BTDX0000"u8.ToArray());
        Git(repo, "add", "-A");
        if (deleteWorkingCopy) File.Delete(file);
        else File.WriteAllBytes(file, "clean"u8.ToArray());
        Assert.Equal("game data header BTDX: data.bin", Assert.Single(Guard.Repo(repo)));
    }

    [Fact]
    public void RepositoryScanIgnoresUnstagedWorkingChanges()
    {
        string repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        string file = Path.Combine(repo, "data.bin");
        File.WriteAllBytes(file, "clean"u8.ToArray());
        Git(repo, "add", "-A");
        File.WriteAllBytes(file, "BTDX0000"u8.ToArray());
        Assert.Empty(Guard.Repo(repo));
    }

    private sealed class ReportedLengthStream(long length) : MemoryStream
    {
        public bool Closed { get; private set; }
        public override long Length => length;
        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }

    private string Save(string name, byte[] bytes)
    {
        string path = Path.Combine(root, "out", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Embed(byte[] pack) =>
        new byte[] { 77, 90, 0, 0 }.Concat(pack).Concat(BitConverter.GetBytes((ulong)pack.Length)).Concat("GDPC"u8.ToArray()).ToArray();

    private static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach ((string name, byte[] data) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(data);
            }
        return stream.ToArray();
    }

    private static byte[] Pck(params (string Path, byte[] Data, uint Flags)[] entries) => Pck(4, 0, 0, entries);

    private static byte[] Pck(uint version, uint flags, ulong fileBase, params (string Path, byte[] Data, uint Flags)[] entries)
    {
        int header = version == 2 ? 96 : 104;
        var names = entries.Select(entry => Encoding.UTF8.GetBytes(entry.Path)).ToArray();
        int directory = 4 + names.Sum(name => 4 + ((name.Length + 3) & ~3) + 36);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("GDPC"u8.ToArray()); writer.Write(version); writer.Write(4u); writer.Write(4u); writer.Write(0u);
        writer.Write(flags); writer.Write(fileBase);
        if (version > 2) writer.Write((ulong)header);
        writer.Write(new byte[64]);
        writer.Write((uint)entries.Length);
        long offset = header + directory;
        for (int index = 0; index < entries.Length; index++)
        {
            int padded = (names[index].Length + 3) & ~3;
            writer.Write((uint)padded); writer.Write(names[index]); writer.Write(new byte[padded - names[index].Length]);
            writer.Write((ulong)offset); writer.Write((ulong)entries[index].Data.Length); writer.Write(new byte[16]); writer.Write(entries[index].Flags);
            offset += entries[index].Data.Length;
        }
        foreach (var entry in entries) writer.Write(entry.Data);
        return stream.ToArray();
    }

    private static void Git(string repo, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-C"); start.ArgumentList.Add(repo);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
