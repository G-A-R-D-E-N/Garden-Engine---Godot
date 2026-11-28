using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace GardenDevLauncher;

public sealed record DirtyFile(string Status, string Path);

public sealed record Snapshot(
    string? Branch,
    int Behind,
    int Ahead,
    IReadOnlyList<DirtyFile> Dirty,
    IReadOnlyList<string> BehindSubjects,
    IReadOnlyList<string> AheadSubjects)
{
    public bool UpToDate => Behind == 0 && Dirty.Count == 0;
}

public sealed record Outcome(Snapshot Snapshot, string Summary);

/// <summary>Raised when the launcher refuses to act, for example on a half
/// finished rebase or a rebase that conflicted.</summary>
public class RefusedException : Exception
{
    public RefusedException(string message) : base(message)
    {
    }
}

public sealed class SyncEngine
{
    private sealed class GitProcessDidNotStopException : RefusedException
    {
        public GitProcessDidNotStopException(string message) : base(message)
        {
        }
    }

    private const string WorkCommitMessage = "Save local work before syncing with main";
    private const int MaxListed = 20;
    private const int GitKillWaitMs = 5_000;
    private const int DefaultGitTimeoutMs = 120_000;

    private static readonly (string Marker, string Message)[] InProgress =
    {
        ("rebase-merge", "a rebase is in progress"),
        ("rebase-apply", "a rebase or am is in progress"),
        ("MERGE_HEAD", "a merge is in progress"),
        ("CHERRY_PICK_HEAD", "a cherry-pick is in progress"),
        ("REVERT_HEAD", "a revert is in progress"),
        ("sequencer", "a multi-commit operation is in progress"),
    };

    private string _repo;
    private readonly string _remote;
    private readonly string _baseBranch;
    private readonly Action<string> _log;
    private readonly int _gitTimeoutMs;

    public SyncEngine(string repo, Action<string> log, string remote = "origin", string baseBranch = "main", int? gitTimeoutMs = null)
    {
        _repo = Path.GetFullPath(repo);
        _remote = remote;
        _baseBranch = baseBranch;
        _log = log;
        _gitTimeoutMs = gitTimeoutMs ?? DefaultGitTimeoutMs;
    }

    public string BaseRef => $"{_remote}/{_baseBranch}";

    public static bool IsRepository(string path) =>
        Directory.Exists(path) && GitProcess(path, DefaultGitTimeoutMs, "rev-parse", "--git-dir").Code == 0;

    public Outcome Check()
    {
        EnsureRepository();
        Fetch();
        var snapshot = ReadSnapshot();
        PrintReport(snapshot);
        return new Outcome(
            snapshot,
            snapshot.UpToDate
                ? $"up to date with {_baseBranch}"
                : $"{snapshot.Behind} behind {_baseBranch}, {snapshot.Dirty.Count} uncommitted file(s)");
    }

    public Outcome Sync()
    {
        EnsureRepository();
        Fetch();
        GuardInProgress();

        var branch = CurrentBranch() ?? throw new RefusedException("HEAD is detached; check out a branch before syncing");
        var options = Git(_repo, "config", "--get-all", $"branch.{branch}.mergeOptions");
        if (options.Code is not (0 or 1)) throw new RefusedException("could not inspect branch merge options");
        if (options.Code == 0 && !string.IsNullOrWhiteSpace(options.Output))
            throw new RefusedException("branch merge options are configured; sync manually to preserve them");
        GuardDirtySubmodules();
        PrintReport(ReadSnapshot());

        var savedCount = SaveWork();
        var snapshot = ReadSnapshot();
        if (snapshot.Behind == 0)
        {
            _log($"already on top of {_baseBranch}");
            return new Outcome(snapshot, savedCount is null ? $"already on top of {_baseBranch}" : $"saved {savedCount} file(s)");
        }

        if (branch == _baseBranch)
        {
            if (snapshot.Ahead > 0)
            {
                throw new RefusedException(
                    $"local {_baseBranch} has {snapshot.Ahead} commit(s) not on {BaseRef}; they are saved, " +
                    $"move them onto a branch and sync that branch: git switch -c my-work");
            }

            GuardIgnored(BaseRef);
            Run("-c", "submodule.recurse=false", "merge", "--ff-only", "--no-squash", "--commit",
                "--no-autostash", "--no-overwrite-ignore", "--verify", BaseRef);
            EnsureMerged();
            _log($"fast-forwarded {_baseBranch} to {BaseRef}");
            return new Outcome(ReadSnapshot(), $"fast-forwarded {_baseBranch}");
        }

        var savedHead = Run("rev-parse", "HEAD").Trim();
        var backup = $"refs/devsync/backups/{Guid.NewGuid():N}";
        Run("update-ref", backup, savedHead);
        var preview = Git(_repo, "merge-tree", "--write-tree", "HEAD", BaseRef);
        if (preview.Code != 0)
            throw new RefusedException($"merge was not started; saved work remains at {backup}. {preview.Output.Trim()} {preview.Error.Trim()}");
        var tree = preview.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(tree))
            throw new RefusedException($"could not inspect the merge; saved work remains at {backup}");
        GuardIgnored(tree);
        (int Code, string Output, string Error) merge;
        try
        {
            merge = Git(_repo, "-c", "submodule.recurse=false", "merge", "--no-edit", "--no-ff", "--no-squash", "--commit",
                "--no-autostash", "--no-overwrite-ignore", "--verify", "-s", "ort", BaseRef);
        }
        catch (GitProcessDidNotStopException ex)
        {
            throw new RefusedException($"merge process did not stop; saved work remains at {backup}. {ex.Message}");
        }
        catch (RefusedException ex)
        {
            merge = (-1, "", ex.Message);
        }
        if (merge.Code != 0)
        {
            var detail = string.Join("\n", new[] { merge.Output.Trim(), merge.Error.Trim() }.Where(x => x.Length > 0));
            if (Path.Exists(Path.Combine(Run("rev-parse", "--absolute-git-dir").Trim(), "MERGE_HEAD")))
            {
                var aborted = Git(_repo, "merge", "--abort");
                var head = Git(_repo, "rev-parse", "HEAD");
                if (aborted.Code != 0 || head.Code != 0 || head.Output.Trim() != savedHead)
                    throw new RefusedException($"merge failed; abort did not restore HEAD. Saved work remains at {backup}. {detail}");
                throw new RefusedException($"merge was aborted; saved work remains at {backup}. {detail}");
            }
            var current = Git(_repo, "rev-parse", "HEAD");
            if (current.Code == 0 && current.Output.Trim() == savedHead)
            {
                var reset = Git(_repo, "reset", "--merge");
                if (reset.Code != 0)
                    throw new RefusedException($"merge failed and the index could not be reset; remove any stale index.lock. Saved work remains at {backup}. {detail}");
            }
            throw new RefusedException($"merge failed; saved work remains at {backup}. {detail}");
        }

        EnsureMerged();
        var after = ReadSnapshot();
        _log($"merged {BaseRef} into {branch}; {after.Ahead} ahead, {after.Behind} behind");
        return new Outcome(after, $"merged {BaseRef} into {branch}");
    }

    private void EnsureRepository()
    {
        if (!Directory.Exists(_repo))
        {
            throw new RefusedException($"{_repo} does not exist");
        }

        var root = Git(_repo, "rev-parse", "--show-toplevel");
        if (root.Code != 0 || root.Output.Contains('\uFFFD') || string.IsNullOrEmpty(root.Output.TrimEnd('\r', '\n')))
        {
            throw new RefusedException($"{_repo} is not a git working tree");
        }
        _repo = Path.GetFullPath(root.Output.TrimEnd('\r', '\n'));
    }

    private void Fetch()
    {
        _log($"fetching {_remote} {_baseBranch}…");
        Run("fetch", "--prune", _remote, $"+refs/heads/{_baseBranch}:refs/remotes/{BaseRef}");
        if (Git(_repo, "rev-parse", "--verify", "--quiet", BaseRef).Code != 0)
        {
            throw new RefusedException($"{BaseRef} does not exist after fetching {_remote} {_baseBranch}");
        }
    }

    private void GuardInProgress()
    {
        var gitDirectory = Run("rev-parse", "--absolute-git-dir").Trim();
        foreach (var (marker, message) in InProgress)
        {
            if (Path.Exists(Path.Combine(gitDirectory, marker)))
            {
                throw new RefusedException($"{message}; finish or abort it first");
            }
        }
    }

    private int? SaveWork()
    {
        var dirty = ReadDirty();
        if (dirty.Count == 0)
        {
            _log("saved: nothing to save");
            return null;
        }

        if (HasStagedChanges())
            Run("commit", "-m", "Save staged work before syncing with main");
        Run("add", "--all");
        if (HasStagedChanges())
            Run("commit", "-m", WorkCommitMessage);
        if (ReadDirty().Count != 0)
            throw new RefusedException("working tree changed during save; sync stopped with remaining work intact");
        var revision = Git(_repo, "rev-parse", "--short", "HEAD").Output.Trim();
        _log($"saved {dirty.Count} file(s) as {revision} \"{WorkCommitMessage}\"");
        return dirty.Count;
    }

    private bool HasStagedChanges()
    {
        var result = Git(_repo, "diff", "--cached", "--quiet");
        if (result.Code is not (0 or 1)) throw new RefusedException("could not inspect staged changes");
        return result.Code == 1;
    }

    private void EnsureMerged()
    {
        var gitDirectory = Run("rev-parse", "--absolute-git-dir").Trim();
        if (Path.Exists(Path.Combine(gitDirectory, "MERGE_HEAD")) || Git(_repo, "merge-base", "--is-ancestor", BaseRef, "HEAD").Code != 0)
            throw new RefusedException("merge did not finish; check git status before continuing");
    }

    private void GuardIgnored(string tree)
    {
        var ignored = Run("ls-files", "--others", "--ignored", "--exclude-standard", "--directory", "-z");
        if (ignored.Length == 0) return;
        var tracked = Run("ls-tree", "-r", "--name-only", "-z", tree);
        if (ignored.Contains('\uFFFD') || tracked.Contains('\uFFFD'))
            throw new RefusedException("could not safely compare ignored file names; sync stopped");
        var files = new HashSet<string>(tracked.Split('\0', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
            for (int slash = file.IndexOf('/'); slash >= 0; slash = file.IndexOf('/', slash + 1))
                parents.Add(file[..slash]);
        foreach (var name in ignored.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var path = name.TrimEnd('/');
            bool collision = files.Contains(path) || parents.Contains(path);
            for (int slash = path.IndexOf('/'); !collision && slash >= 0; slash = path.IndexOf('/', slash + 1))
                collision = files.Contains(path[..slash]);
            if (collision) throw new RefusedException($"ignored local path may be overwritten: {name}; move it before syncing");
        }
    }

    private void GuardDirtySubmodules()
    {
        var status = Run("status", "--porcelain=v2", "--ignore-submodules=none");
        foreach (var line in status.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ', 4);
            if (fields.Length > 2 && fields[2].Length == 4 && fields[2][0] == 'S' && fields[2][2..] != "..")
                throw new RefusedException("a submodule contains uncommitted work; save it before syncing");
        }
    }

    private Snapshot ReadSnapshot()
    {
        var counts = Run("rev-list", "--left-right", "--count", $"{BaseRef}...HEAD")
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var behind = counts.Length > 0 && int.TryParse(counts[0], out var parsedBehind) ? parsedBehind : 0;
        var ahead = counts.Length > 1 && int.TryParse(counts[1], out var parsedAhead) ? parsedAhead : 0;
        return new Snapshot(
            CurrentBranch(),
            behind,
            ahead,
            ReadDirty(),
            Subjects($"HEAD..{BaseRef}"),
            Subjects($"{BaseRef}..HEAD"));
    }

    private IReadOnlyList<DirtyFile> ReadDirty()
    {
        var files = new List<DirtyFile>();
        var listed = Run("status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none").Replace("\r", string.Empty);
        foreach (var line in listed.Split('\n'))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var status = line[..2].Trim();
            files.Add(new DirtyFile(status.Length == 0 ? "??" : status, line[3..]));
        }

        return files;
    }

    private IReadOnlyList<string> Subjects(string range) =>
        Run("log", "--format=%h %s", $"--max-count={MaxListed}", range)
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private string? CurrentBranch()
    {
        var result = Git(_repo, "symbolic-ref", "--quiet", "--short", "HEAD");
        return result.Code == 0 ? result.Output.Trim() : null;
    }

    private void PrintReport(Snapshot snapshot)
    {
        var head = snapshot.Branch is null ? "detached HEAD" : $"branch {snapshot.Branch}";
        _log($"{head} ({snapshot.Behind} behind {_baseBranch}, {snapshot.Ahead} ahead)");
        if (snapshot.Dirty.Count == 0)
        {
            _log("uncommitted work: none");
        }
        else
        {
            _log($"uncommitted work: {snapshot.Dirty.Count} file(s)");
            foreach (var file in snapshot.Dirty.Take(MaxListed))
            {
                _log($"  {file.Status,-2} {file.Path}");
            }

            if (snapshot.Dirty.Count > MaxListed)
            {
                _log($"  … {snapshot.Dirty.Count - MaxListed} more");
            }
        }

        if (snapshot.BehindSubjects.Count > 0)
        {
            _log($"new on {_baseBranch}:");
            foreach (var subject in snapshot.BehindSubjects)
            {
                _log($"  {subject}");
            }
        }

        if (snapshot.AheadSubjects.Count > 0)
        {
            _log("only on this branch:");
            foreach (var subject in snapshot.AheadSubjects)
            {
                _log($"  {subject}");
            }
        }
    }

    private string Run(params string[] args)
    {
        var result = Git(_repo, args);
        if (result.Code != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            throw new RefusedException($"git {string.Join(' ', args)} failed: {detail.Trim()}");
        }

        return result.Output;
    }

    private (int Code, string Output, string Error) Git(string repo, params string[] args) =>
        GitProcess(repo, _gitTimeoutMs, args);

    private static (int Code, string Output, string Error) GitProcess(string repo, int timeoutMs, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(repo) ? repo : Environment.CurrentDirectory,
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(repo);
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start)
                ?? throw new RefusedException("git could not be started");
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }

                if (!process.WaitForExit(GitKillWaitMs))
                    throw new GitProcessDidNotStopException($"git {string.Join(' ', args)} did not stop within {GitKillWaitMs / 1000} seconds after being killed");
                throw new RefusedException($"git {string.Join(' ', args)} did not finish in {timeoutMs / 1000} seconds and was stopped");
            }

            process.WaitForExit();
            return (process.ExitCode, output.Result, error.Result);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new RefusedException("git is not installed or not on PATH");
        }
    }
}
