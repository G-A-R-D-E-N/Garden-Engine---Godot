using System.Diagnostics;
using GameDataGuard;
using Xunit;

namespace GuardTools.Tests;

public class GameDataGuardTests
{
    [Fact]
    public void SelfTestPasses() => Checks.Run();

    [Theory]
    [InlineData("Data/Fallout4 - Interface.ba2")]
    [InlineData("opencw/ui/Menu.swf")]
    [InlineData("opencw/meshes/Door.NIF")]
    [InlineData("opencw/textures/Wall.dds")]
    public void TrackedGameFileIsReported(string path)
    {
        Assert.Equal("game data file type: " + path, Assert.Single(Scan((path, "placeholder"))));
    }

    [Fact]
    public void TrackedGameHeaderIsReported()
    {
        Assert.Equal("game data header BTDX: data.bin", Assert.Single(Scan(("data.bin", "BTDX0000"))));
    }

    [Theory]
    [InlineData("opencw/.garden-cache/layout.json")]
    [InlineData("opencw/ui-layout-cache/layouts.json")]
    public void TrackedRuntimeCacheIsReported(string path)
    {
        Assert.Equal("local runtime cache path: " + path, Assert.Single(Scan((path, "{}"))));
    }

    [Fact]
    public void TrackedLayoutTableIsReported()
    {
        Assert.Equal("layout table: opencw/layout.json", Assert.Single(Scan(("opencw/layout.json", Rows(25, i => $"  \"x\": {i}, \"y\": {i},\n")))));
    }

    [Fact]
    public void TrackedStringsDumpIsReported()
    {
        Assert.Equal("strings dump: opencw/strings.txt", Assert.Single(Scan(("opencw/strings.txt", Rows(25, i => $"$KEY_{i}\tText {i}\n")))));
    }

    [Fact]
    public void SmallTablesAndCodeAreNotReported()
    {
        Assert.Empty(Scan(
            ("opencw/small.json", Rows(3, i => $"  \"x\": {i}, \"y\": {i},\n")),
            ("opencw/scripts/UI/Layout.cs", Rows(25, i => $"    new({i}, {i}),\n")),
            ("src/Main.cs", "class Main {}")));
    }

    [Theory]
    [InlineData("opencw/.garden-cache/a.svg", "local runtime cache path")]
    [InlineData("opencw\\ui-layout-cache\\a.json", "local runtime cache path")]
    [InlineData("a/.GARDEN-CACHE", "local runtime cache path")]
    [InlineData("Interface/Translate_en.txt", "installed interface translation table")]
    [InlineData("data/TRANSLATE_fr.TXT", "installed interface translation table")]
    [InlineData("x/Interface.SWF", "game data file type")]
    [InlineData("sounds/a.WAV", "game data file type")]
    [InlineData("video/movie.webm", "game data file type")]
    [InlineData("a/b.strings", "game data file type")]
    public void PathProblemNamesTheRule(string path, string reason)
    {
        Assert.Equal(reason, Guard.PathProblem(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("///")]
    [InlineData("docs/Translate.txt")]
    [InlineData("opencw/scripts/UI/LocalArt.cs")]
    [InlineData("opencw/assets/UI/Fonts/RobotoCondensed-Bold.ttf")]
    [InlineData("notes/readme")]
    public void PathProblemAllowsOrdinaryFiles(string path)
    {
        Assert.Null(Guard.PathProblem(path));
    }

    [Theory]
    [InlineData("BTDX....", "game data header BTDX")]
    [InlineData("TES4....", "game data header TES4")]
    [InlineData("FWS\u000f..", "game data header FWS")]
    [InlineData("GFX.....", "game data header GFX")]
    [InlineData("DDS |...", "game data header DDS")]
    [InlineData("Gamebryo File Format, Version 20.2.0.7", "game data header Gamebryo File Format")]
    [InlineData("NetImmerse File Format", "game data header NetImmerse File Format")]
    [InlineData("OggS", "local sound data header")]
    [InlineData("RIFF0000WAVE", "local sound data header")]
    [InlineData("RIFF0000XWMA", "local sound data header")]
    public void HeadProblemNamesTheRule(string head, string reason)
    {
        Assert.Equal(reason, Guard.HeadProblem(System.Text.Encoding.Latin1.GetBytes(head)));
    }

    [Theory]
    [InlineData("RIFF0000WEBP")]
    [InlineData("RIFF0000WAV")]
    [InlineData("RIFF")]
    [InlineData("TES")]
    [InlineData("using System;")]
    [InlineData("")]
    public void HeadProblemAllowsOrdinaryAndShortHeads(string head)
    {
        Assert.Null(Guard.HeadProblem(System.Text.Encoding.Latin1.GetBytes(head)));
    }

    [Fact]
    public void TableProblemNeedsTwentyRowsOfOneKind()
    {
        Assert.Null(Guard.TableProblem(Rows(19, i => $"$KEY_{i}\tText\n")));
        Assert.Equal("strings dump", Guard.TableProblem(Rows(20, i => $"$KEY_{i}\tText\n")));
        Assert.Equal("strings dump", Guard.TableProblem(Rows(20, i => $"{i:X8}\tText\n")));
        Assert.Equal("strings dump", Guard.TableProblem(Rows(20, i => $"\"$KEY_{i}\":\"Text\",\n")));
        Assert.Null(Guard.TableProblem(Rows(19, i => $"  \"x\": {i}\n")));
        Assert.Equal("layout table", Guard.TableProblem(Rows(20, i => $"  \"x\": {i}\n")));
        Assert.Equal("layout table", Guard.TableProblem(Rows(20, i => $"scaley = {i}.5\n")));
    }

    [Fact]
    public void TableProblemRowKindsAreCountedSeparately()
    {
        string mixed = Rows(10, i => $"$KEY_{i}\tText\n") + Rows(10, i => $"  \"x\": {i}\n");
        Assert.Null(Guard.TableProblem(mixed));
    }

    [Fact]
    public void TableProblemIgnoresRowsThatAreNotTables()
    {
        Assert.Null(Guard.TableProblem(Rows(25, i => $"width: {i}px\n")));
        Assert.Null(Guard.TableProblem(Rows(25, i => $"$KEY_{i}\t\n")));
        Assert.Null(Guard.TableProblem(Rows(25, i => $"widthy: {i}\n")));
        Assert.Null(Guard.TableProblem(""));
    }

    private static string Rows(int count, Func<int, string> row) => string.Concat(Enumerable.Range(0, count).Select(row));

    private static List<string> Scan(params (string Name, string Text)[] files)
    {
        string root = Path.Combine(Path.GetTempPath(), "guard-tools-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Git(root, "init", "-q");
            foreach ((string path, string text) in files)
            {
                string file = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, text);
            }
            Git(root, "add", "-A", "-f");
            return Guard.Repo(root);
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    private static void Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-C"); start.ArgumentList.Add(root);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
