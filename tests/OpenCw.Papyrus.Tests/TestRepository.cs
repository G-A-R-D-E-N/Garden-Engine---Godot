namespace OpenCw.Papyrus.Tests;

internal static class TestRepository
{
    internal static string? FindRoot()
    {
        var configured = Environment.GetEnvironmentVariable("OPENCW_REPO_ROOT");
        if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
        {
            return configured;
        }
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "opencw", "project.godot"))
                && Path.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return null;
    }
}
