using Xunit;

namespace OpenCommonwealth.Launcher.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gl-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Path, true);
        }
        catch { }
    }
}

public class EngineInstallerTests
{
    [Fact]
    public void OccupiedFolderGetsNestedDestinationAndKeepsUserFiles()
    {
        using var t = new TempDir();
        File.WriteAllText(System.IO.Path.Combine(t.Path, "mine.txt"), "keep");
        string? dest = EngineInstaller.ResolveDestination(t.Path, out string? err);
        Assert.Null(err);
        Assert.Equal(System.IO.Path.Combine(t.Path, "GardenGodot"), dest);
        Assert.Equal("keep", File.ReadAllText(System.IO.Path.Combine(t.Path, "mine.txt")));
    }

    [Fact]
    public void UnownedNestedFolderIsRefused()
    {
        using var t = new TempDir();
        string nested = System.IO.Path.Combine(t.Path, "GardenGodot");
        Directory.CreateDirectory(nested);
        File.WriteAllText(System.IO.Path.Combine(nested, "user.dat"), "keep");
        File.WriteAllText(System.IO.Path.Combine(t.Path, "other.txt"), "keep");
        Assert.Null(EngineInstaller.ResolveDestination(t.Path, out string? err));
        Assert.NotNull(err);
        Assert.True(File.Exists(System.IO.Path.Combine(nested, "user.dat")));
    }

    [Fact]
    public void SwapNeverTouchesPreexistingStagingOrPreviousFolders()
    {
        using var t = new TempDir();
        string dest = System.IO.Path.Combine(t.Path, "engine");
        Directory.CreateDirectory(dest);
        File.WriteAllText(System.IO.Path.Combine(dest, EngineInstaller.ReleaseMarker), "old");
        File.WriteAllText(System.IO.Path.Combine(dest, "old.bin"), "old");
        foreach (string kind in new[] { ".staging", ".previous" })
        {
            Directory.CreateDirectory(dest + kind);
            File.WriteAllText(System.IO.Path.Combine(dest + kind, "user.txt"), "keep");
        }

        string staging = EngineInstaller.UniqueSibling(dest, "staging");
        Directory.CreateDirectory(staging);
        File.WriteAllText(System.IO.Path.Combine(staging, "new.bin"), "new");
        File.WriteAllText(System.IO.Path.Combine(staging, EngineInstaller.ReleaseMarker), "new");

        Assert.Null(EngineInstaller.Swap(staging, dest));
        Assert.True(File.Exists(System.IO.Path.Combine(dest, "new.bin")));
        Assert.False(File.Exists(System.IO.Path.Combine(dest, "old.bin")));
        Assert.Equal("keep", File.ReadAllText(System.IO.Path.Combine(dest + ".staging", "user.txt")));
        Assert.Equal("keep", File.ReadAllText(System.IO.Path.Combine(dest + ".previous", "user.txt")));
        Assert.Empty(Directory.GetDirectories(t.Path, "engine.*-*"));
    }

    [Fact]
    public void SwapRefusesAnUnownedDestination()
    {
        using var t = new TempDir();
        string dest = System.IO.Path.Combine(t.Path, "engine");
        Directory.CreateDirectory(dest);
        File.WriteAllText(System.IO.Path.Combine(dest, "user.txt"), "keep");
        string staging = EngineInstaller.UniqueSibling(dest, "staging");
        Directory.CreateDirectory(staging);

        Assert.NotNull(EngineInstaller.Swap(staging, dest));
        Assert.Equal("keep", File.ReadAllText(System.IO.Path.Combine(dest, "user.txt")));
        Assert.True(Directory.Exists(staging));
    }

    [Fact]
    public void UniqueSiblingNeverReusesAnExistingPath()
    {
        using var t = new TempDir();
        string dest = System.IO.Path.Combine(t.Path, "engine");
        string a = EngineInstaller.UniqueSibling(dest, "staging");
        Directory.CreateDirectory(a);
        Assert.NotEqual(a, EngineInstaller.UniqueSibling(dest, "staging"));
    }
}

public class EngineSetupTests
{
    [Fact]
    public void CancelledChoiceOpensNoDialogAndDownloadsNothing()
    {
        var r = EngineSetup.FromChoice(0, "def", () => throw new InvalidOperationException(), () => throw new InvalidOperationException());
        Assert.Equal(EngineStep.Cancelled, r.Step);
    }

    [Fact]
    public void CancellingTheFolderPickerCancelsSetup()
    {
        Assert.Equal(EngineStep.Cancelled, EngineSetup.FromChoice(2, "def", () => null, () => null).Step);
    }

    [Fact]
    public void CancellingTheExecutablePickerCancelsSetup()
    {
        Assert.Equal(EngineStep.Cancelled, EngineSetup.FromChoice(3, "def", () => null, () => null).Step);
    }

    [Fact]
    public void ChosenPathsAreUsed()
    {
        var exe = EngineSetup.FromChoice(3, "def", () => "g.exe", () => null);
        Assert.Equal(EngineStep.UseExisting, exe.Step);
        Assert.Equal("g.exe", exe.Path);
        var dir = EngineSetup.FromChoice(2, "def", () => null, () => "dir");
        Assert.Equal(EngineStep.Download, dir.Step);
        Assert.Equal("dir", dir.Path);
        var def = EngineSetup.FromChoice(1, "def", () => null, () => null);
        Assert.Equal(EngineStep.Download, def.Step);
        Assert.Equal("def", def.Path);
    }
}
