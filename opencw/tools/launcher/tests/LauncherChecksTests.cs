using System.Diagnostics;
using Xunit;

namespace OpenCommonwealth.Launcher.Tests;

public class LauncherChecksTests
{
    [Fact]
    public void SdkRequiresKnownMatchingVersions()
    {
        Assert.True(LauncherChecks.SdkMatches("4.4", "4.4"));
        Assert.False(LauncherChecks.SdkMatches("4.3", "4.4"));
        Assert.False(LauncherChecks.SdkMatches(null, null));
    }

    [Fact]
    public void FalloutInstallRequiresBothFiles()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(Path.Combine(t.Path, "Data"));
        File.WriteAllText(Path.Combine(t.Path, "Fallout4.exe"), "");
        Assert.False(Fallout4Locator.IsValidInstall(t.Path));
        File.WriteAllText(Path.Combine(t.Path, "Data", "Fallout4.esm"), "");
        Assert.True(Fallout4Locator.IsValidInstall(t.Path));
        File.Delete(Path.Combine(t.Path, "Fallout4.exe"));
        Assert.False(Fallout4Locator.IsValidInstall(t.Path));
    }

    [Fact]
    public void PrebuiltRejectsMissingOrInvalidBinary()
    {
        using var t = new TempDir();
        string root = t.Path, radicle = Path.Combine(root, "native", "radicle"), bin = Path.Combine(radicle, "bin");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(Path.Combine(root, "OpenCw.Papyrus"));
        File.WriteAllText(Path.Combine(root, "OpenCw.Papyrus", "RadiclePin.cs"), "const int AbiVersion = 42;");
        File.WriteAllText(Path.Combine(bin, "Radicle.props"), "props");
        var lines = new List<string>();
        Assert.False(LauncherChecks.ValidRadicle(root, radicle, lines.Add));
        Assert.Contains("is missing", Assert.Single(lines));
        string library = Path.Combine(bin, OperatingSystem.IsWindows() ? "radicle.dll" : "libradicle.so");
        File.WriteAllText(library, "not a native library");
        lines.Clear();
        Assert.False(LauncherChecks.ValidRadicle(root, radicle, lines.Add));
        Assert.Contains("cannot be loaded", Assert.Single(lines));
    }

    [Fact]
    public void PinAndAbiComparison()
    {
        Assert.Equal(700633u, LauncherChecks.PinnedAbi("public const int AbiVersion = 700633;"));
        Assert.Equal(3u, LauncherChecks.PinnedAbi("const uint AbiVersion=3;"));
        Assert.Null(LauncherChecks.PinnedAbi("no pin here"));
        Assert.True(LauncherChecks.AbiMatches(5, 5));
        Assert.False(LauncherChecks.AbiMatches(5, 6));
        Assert.False(LauncherChecks.AbiMatches(null, null));
        Assert.False(LauncherChecks.AbiMatches(5, null));
    }

    [Fact]
    public void UnparsablePinIsRejectedWithReason()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(Path.Combine(t.Path, "OpenCw.Papyrus"));
        File.WriteAllText(Path.Combine(t.Path, "OpenCw.Papyrus", "RadiclePin.cs"), "nothing");
        var lines = new List<string>();
        Assert.False(LauncherChecks.ValidRadicle(t.Path, Path.Combine(t.Path, "native", "radicle"), lines.Add));
        Assert.Contains("AbiVersion", Assert.Single(lines));
    }

    [Fact]
    public void ImmediateNonzeroExitFails()
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c exit 7")
            : new ProcessStartInfo("/bin/sh", "-c \"exit 7\"");
        info.UseShellExecute = false;
        Assert.Contains("code 7", Assert.Throws<InvalidOperationException>(() => LauncherChecks.StartEditor(info)).Message);
    }

    [Fact]
    public void PrebuiltAcceptsMatchingNativeAbi()
    {
        using var t = new TempDir();
        RequireStubRadicleTree(t, 701, 701, out string root, out string radicle);
        Assert.True(LauncherChecks.ValidRadicle(root, radicle));
    }

    [Fact]
    public void PrebuiltRejectsNativeAbiMismatch()
    {
        using var t = new TempDir();
        RequireStubRadicleTree(t, 701, 702, out string root, out string radicle);
        var lines = new List<string>();
        Assert.False(LauncherChecks.ValidRadicle(root, radicle, lines.Add));
        Assert.Contains("702", Assert.Single(lines));
        Assert.Contains("701", Assert.Single(lines));
    }

    static void RequireStubRadicleTree(TempDir t, uint pinAbi, uint libAbi, out string root, out string radicle)
    {
        root = t.Path;
        radicle = Path.Combine(root, "native", "radicle");
        string bin = Path.Combine(radicle, "bin");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(Path.Combine(root, "OpenCw.Papyrus"));
        File.WriteAllText(Path.Combine(root, "OpenCw.Papyrus", "RadiclePin.cs"), $"public const int AbiVersion = {pinAbi};");
        File.WriteAllText(Path.Combine(bin, "Radicle.props"), "props");
        string library = Path.Combine(bin, OperatingSystem.IsWindows() ? "radicle.dll" : "libradicle.so");
        Assert.True(RadicleStubCompiler.TryCompile(library, libAbi),
            "This test requires a working cl, clang, or gcc to compile a Radicle ABI stub.");
    }
}
