using Avalonia;

namespace OpenCommonwealth.Launcher;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "/selftest", StringComparison.OrdinalIgnoreCase))
        {
            SelfTest(args);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    static void SelfTest(string[] args)
    {
        string root = args.Length > 1 ? args[1] : AppContext.BaseDirectory;
        string url = args.Length > 2 ? args[2] : "https://github.com/G-A-R-D-E-N/Garden-Engine.git";
        var lines = new List<string>();
        try
        {
            var git = new GitService(root, url);
            lines.Add($"OS={Environment.OSVersion.Platform} IsWindows={OperatingSystem.IsWindows()}");
            lines.Add($"IsRepo={git.IsRepo}");
            lines.Add($"HasStoredCredential={GitService.HasStoredCredential()}");
            var s = git.CheckSource();
            lines.Add($"Reach={s.Reach} Branch={s.Branch} Behind={s.Behind} Ahead={s.Ahead}");
            lines.Add($"Local={s.LocalSha} Remote={s.RemoteSha}");
            if (s.Error is not null) lines.Add($"Error={s.Error}");
            var release = EngineReleases.Latest(EngineReleases.Project, out string? releaseError);
            lines.Add(release is null
                ? $"EngineRelease=none ({releaseError})"
                : $"EngineRelease={release.Tag} assets=[{string.Join(", ", release.Assets.Keys)}]");
        }
        catch (Exception ex) { lines.Add("EXCEPTION: " + ex); }

        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "selftest.txt"), lines);
    }
}
