using System.Diagnostics;
using GameDataGuard;
using Xunit;

namespace GuardTools.Tests;

public sealed class GameDataGuardExportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "guard-export-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> links = new();

    public GameDataGuardExportTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        foreach (string link in links)
        {
            try
            {
                if (OperatingSystem.IsWindows() && Directory.Exists(link)) Directory.Delete(link);
                else File.Delete(link);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }

    [Fact]
    public void ADirectlySuppliedGameDataFileIsReported()
    {
        string file = Save("Fallout4 - Main.ba2", "placeholder");
        Assert.Equal("game data file type: Fallout4 - Main.ba2", Assert.Single(Guard.Export(file)));
    }

    [Fact]
    public void ADirectlySuppliedFileWithAGameDataHeaderIsReported()
    {
        string file = Save("Game.exe", "BTDX0000");
        Assert.Equal("game data header BTDX: Game.exe", Assert.Single(Guard.Export(file)));
    }

    [Fact]
    public void ADirectlySuppliedTextTableIsReported()
    {
        string file = Save("strings.txt", string.Concat(Enumerable.Range(0, 25).Select(i => $"$KEY_{i}\tText {i}\n")));
        Assert.Equal("strings dump: strings.txt", Assert.Single(Guard.Export(file)));
    }

    [Fact]
    public void ADirectlySuppliedCleanFilePasses()
    {
        Assert.Empty(Guard.Export(Save("Plain.exe", "MZ plain executable bytes")));
    }

    [Fact]
    public void ADirectFileIsJudgedByItsNameNotByTheFolderItSitsIn()
    {
        Assert.Empty(Guard.Export(Save(".garden-cache/notes.txt", "plain")));
    }

    [Fact]
    public void TheDirectoryFormAndTheFileFormAgree()
    {
        Save("release/Fallout4 - Main.ba2", "placeholder");
        Assert.Equal(Guard.Export(Path.Combine(root, "release", "Fallout4 - Main.ba2")), Guard.Export(Path.Combine(root, "release")));
    }

    [Fact]
    public void ADirectoryWithoutLinksStillPasses()
    {
        Save("release/readme.txt", "plain");
        Save("release/bin/Game.exe", "MZ plain executable bytes");
        Assert.Empty(Guard.Export(Path.Combine(root, "release")));
    }

    [LinkFact]
    public void ALinkedFileInAnExportDirectoryIsReported()
    {
        string target = Save("outside/real.txt", "plain");
        Directory.CreateDirectory(Path.Combine(root, "release"));
        Link(Path.Combine(root, "release", "alias.txt"), target, directory: false);
        Assert.Equal("link not inspected: alias.txt", Assert.Single(Guard.Export(Path.Combine(root, "release"))));
    }

    [LinkFact]
    public void ALinkNamedAsGameDataReportsTheNameAndTheLink()
    {
        string target = Save("outside/real.txt", "plain");
        Directory.CreateDirectory(Path.Combine(root, "release"));
        Link(Path.Combine(root, "release", "evil.ba2"), target, directory: false);
        var found = Guard.Export(Path.Combine(root, "release"));
        Assert.Equal(new[] { "game data file type: evil.ba2", "link not inspected: evil.ba2" }, found.OrderBy(row => row, StringComparer.Ordinal));
    }

    [LinkFact]
    public void ALinkedDirectoryIsReportedAndNotFollowed()
    {
        Save("outside/data/x.ba2", "placeholder");
        Directory.CreateDirectory(Path.Combine(root, "release"));
        Link(Path.Combine(root, "release", "linked"), Path.Combine(root, "outside", "data"), directory: true);
        Assert.Equal("link not inspected: linked", Assert.Single(Guard.Export(Path.Combine(root, "release"))));
    }

    [LinkFact(nonWindowsOnly: true)]
    public void ATrackedLinkIsReported()
    {
        string repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        File.WriteAllText(Path.Combine(repo, "real.txt"), "plain");
        Link(Path.Combine(repo, "alias.txt"), "real.txt", directory: false);
        Git(repo, "add", "-A");
        Assert.Equal("link not inspected: alias.txt", Assert.Single(Guard.Repo(repo)));
    }

    private string Save(string name, string text)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private void Link(string link, string target, bool directory)
    {
        if (directory) Directory.CreateSymbolicLink(link, target);
        else File.CreateSymbolicLink(link, target);
        links.Add(link);
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

    private sealed class LinkFactAttribute : FactAttribute
    {
        private static readonly string? CannotLink = Probe();

        public LinkFactAttribute(bool nonWindowsOnly = false) =>
            Skip = nonWindowsOnly && OperatingSystem.IsWindows() ? "needs a non-Windows host" : CannotLink;

        private static string? Probe()
        {
            string dir = Directory.CreateTempSubdirectory("guard-link-probe-").FullName;
            try
            {
                File.CreateSymbolicLink(Path.Combine(dir, "probe"), "target");
                return null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return "this host cannot create symbolic links";
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
