using System.Diagnostics;
using GardenDevLauncher;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DevSync.Tests;

public abstract class RepoFixture : IDisposable
{
    private const string GitConfig = "[user]\n\tname = Test\n\temail = test@example.com\n" +
        "[commit]\n\tgpgsign = false\n[core]\n\tautocrlf = false\n[init]\n\tdefaultBranch = main\n" +
        "[advice]\n\tdetachedHead = false\n[merge]\n\tconflictStyle = merge\n";

    private readonly string? _previousGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
    private readonly string? _previousNoSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");

    protected string Root { get; }
    protected string Origin { get; }
    protected string Seed { get; }
    protected string Local { get; }
    protected List<string> Log { get; } = new();

    protected RepoFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "devsync-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        var config = Path.Combine(Root, "gitconfig");
        File.WriteAllText(config, GitConfig);
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", config);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");

        Origin = Path.Combine(Root, "origin.git");
        Seed = Path.Combine(Root, "seed");
        Local = Path.Combine(Root, "local");
        Git(Root, "init", "--bare", "--initial-branch=main", Origin);
        Git(Root, "clone", Origin, Seed);
        File.WriteAllText(Path.Combine(Seed, "a.txt"), "one\n");
        Git(Seed, "add", "--all");
        Git(Seed, "commit", "--no-gpg-sign", "-m", "Initial commit");
        Git(Seed, "push", "origin", "main");
        Git(Root, "clone", Origin, Local);
        Git(Local, "switch", "--create", "work");
    }

    protected SyncEngine Engine(string? repo = null, string remote = "origin", string baseBranch = "main") =>
        new(repo ?? Local, Log.Add, remote, baseBranch);

    protected static string Git(string cwd, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(cwd);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)}: {error.Result}");
        return output.Result;
    }

    protected string LocalFile(string name) => Path.Combine(Local, name);

    protected void Write(string name, string content, string? repo = null)
    {
        var path = Path.Combine(repo ?? Local, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    protected void CommitLocal(string message)
    {
        Git(Local, "add", "--all");
        Git(Local, "commit", "--no-gpg-sign", "-m", message);
    }

    protected void PushToMain(string message, string name = "b.txt", string content = "remote\n")
    {
        Git(Seed, "pull", "--rebase", "origin", "main");
        Write(name, content, Seed);
        Git(Seed, "add", "--all");
        Git(Seed, "commit", "--no-gpg-sign", "-m", message);
        Git(Seed, "push", "origin", "main");
    }

    protected string Head() => Git(Local, "rev-parse", "HEAD").Trim();

    protected string GitDir() => Path.GetFullPath(Git(Local, "rev-parse", "--absolute-git-dir").Trim());

    protected void IgnoreLocally(string pattern) =>
        File.AppendAllText(Path.Combine(GitDir(), "info", "exclude"), "\n" + pattern + "\n");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", _previousGlobal);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", _previousNoSystem);
        if (!Directory.Exists(Root)) return;
        foreach (var path in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(Root, recursive: true);
    }
}
