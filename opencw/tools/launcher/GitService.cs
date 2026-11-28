using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Runtime.InteropServices;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;

namespace OpenCommonwealth.Launcher;

public sealed class GitService
{
    public enum Reach { NotARepo, NoRemote, Offline, Auth, Ok }

    public const string MainBranch = "main";
    public const string Org = "G-A-R-D-E-N";

    public sealed record SourceStatus(
        Reach Reach, string Branch, int Behind, int Ahead, string? LocalSha, string? RemoteSha, string? Error,
        string? LocalOnlyBranch = null);

    readonly string _repoPath;
    readonly string _url;
    readonly string _ns = "refs/garden-launcher/" + Guid.NewGuid().ToString("N");
    readonly string _tempRemote = "_gl_srccheck_" + Guid.NewGuid().ToString("N")[..12];
    int _authAttempt;
    (string user, string secret)? _authCache;

    public GitService(string repoPath, string sourceUrl)
    {
        _repoPath = repoPath;
        _url = sourceUrl;
    }

    public bool IsRepo => Repository.IsValid(DiscoverPath());

    // Destination must be empty or missing; returns null on success, else the error.
    public string? Clone(string destination, Action<string> progress)
    {
        try
        {
            _authAttempt = 0; _authCache = null;
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                return $"{destination} isn't empty - pick an empty folder.";
            int last = -1;
            var fetch = new FetchOptions
            {
                CredentialsProvider = Credentials,
                OnTransferProgress = p =>
                {
                    int pct = p.TotalObjects == 0 ? 0 : (int)(100L * p.ReceivedObjects / p.TotalObjects);
                    if (pct / 10 != last) { last = pct / 10; progress($"Cloning… {pct}%"); }
                    return true;
                },
            };
            Repository.Clone(_url, destination, new CloneOptions(fetch) { BranchName = MainBranch });
            return null;
        }
        catch (LibGit2SharpException ex) when (IsAuth(ex)) { return "GitHub rejected the sign-in (this repo is private): " + ex.Message; }
        catch (Exception ex) { return ex.Message; }
    }

    string DiscoverPath()
    {
        try { return Repository.Discover(_repoPath) ?? _repoPath; }
        catch { return _repoPath; }
    }

    public SourceStatus CheckSource()
    {
        string path = DiscoverPath();
        if (!Repository.IsValid(path))
            return new(Reach.NotARepo, "", 0, 0, null, null, null);

        try
        {
            _authAttempt = 0; _authCache = null;
            using var repo = new Repository(path);
            string branch = repo.Head.FriendlyName;
            string? localSha = repo.Head.Tip?.Sha;
            if (string.IsNullOrEmpty(branch) || branch == "(no branch)") branch = "(detached HEAD)";

            var fetchOpts = new FetchOptions { CredentialsProvider = Credentials, Prune = false };
            var (remoteName, added) = ResolveRemote(repo);
            try
            {
                try { FetchAllHeads(repo, remoteName, fetchOpts); }
                catch (LibGit2SharpException ex) when (IsAuth(ex))
                {
                    return new(Reach.Auth, branch, 0, 0, localSha, null, ex.Message);
                }
                catch (LibGit2SharpException ex) when (IsOffline(ex))
                {
                    return new(Reach.Offline, branch, 0, 0, localSha, null, ex.Message);
                }

                Commit? remoteTip = ResolveFetched(repo, MainBranch);
                if (remoteTip is null)
                    return new(Reach.NoRemote, branch, 0, 0, localSha, null, $"branch '{MainBranch}' not found on GitHub");

                string chosen = MainBranch;
                string? localOnly = branch == MainBranch ? null : branch;

                var local = repo.Head.Tip;
                if (local is null)
                    return new(Reach.Ok, chosen, 0, 0, null, remoteTip.Sha, null, localOnly);

                var div = repo.ObjectDatabase.CalculateHistoryDivergence(local, remoteTip);
                return new(Reach.Ok, chosen, div.BehindBy ?? 0, div.AheadBy ?? 0, local.Sha, remoteTip.Sha, null, localOnly);
            }
            finally
            {
                CleanupFetchedRefs(repo);
                if (added) try { repo.Network.Remotes.Remove(remoteName); } catch { }
            }
        }
        catch (LibGit2SharpException ex) when (IsAuth(ex))
        {
            return new(Reach.Auth, "", 0, 0, null, null, ex.Message);
        }
        catch (LibGit2SharpException ex) when (IsOffline(ex))
        {
            return new(Reach.Offline, "", 0, 0, null, null, ex.Message);
        }
        catch (Exception ex)
        {
            return new(Reach.NoRemote, "", 0, 0, null, null, ex.Message);
        }
    }

    public string? Pull(out int pulled, out string? note, out bool needsAttention)
    {
        pulled = 0; note = null; needsAttention = false;
        string path = DiscoverPath();
        var (remoteName, added) = (null as string, false);
        try
        {
            _authAttempt = 0; _authCache = null;
            using var repo = new Repository(path);
            string branch = repo.Head.FriendlyName;
            if (repo.Info.IsHeadDetached)
                return "HEAD is detached, so there's no branch to fast-forward. Check out a branch first.";
            if (branch != MainBranch)
                return $"you're on '{branch}'. This only fast-forwards {MainBranch}, so switch to it first.";
            var before = repo.Head.Tip;
            var fetchOpts = new FetchOptions { CredentialsProvider = Credentials };
            (remoteName, added) = ResolveRemote(repo);

            FetchAllHeads(repo, remoteName, fetchOpts);
            var remoteTip = ResolveFetched(repo, MainBranch);
            if (remoteTip is null) return $"branch '{MainBranch}' doesn't exist on GitHub, so there's nothing to pull.";
            if (before is not null)
            {
                var div = repo.ObjectDatabase.CalculateHistoryDivergence(before, remoteTip);
                if ((div.BehindBy ?? 0) == 0) return null;
                if ((div.AheadBy ?? 0) > 0)
                    return "your branch has diverged from GitHub; a plain fast-forward isn't possible. Merge/rebase manually.";
            }

            Stash? stash = null;
            if (repo.RetrieveStatus(new StatusOptions { IncludeUntracked = true, IncludeIgnored = false }).IsDirty)
            {
                int stashesBefore = repo.Stashes.Count();
                try
                {
                    stash = repo.Stashes.Add(BuildSignature(repo), $"garden-launcher auto-stash {DateTime.Now:yyyy-MM-dd HH:mm:ss}", StashModifiers.IncludeUntracked);
                }
                catch (Exception ex)
                {
                    if (repo.Stashes.Count() > stashesBefore)
                        return "couldn't clear your local changes (" + ex.Message + "). " + RestoreStash(repo, repo.Stashes.First()).Message;
                    return "couldn't stash your local changes (" + ex.Message + "), so nothing was pulled.";
                }
            }

            MergeResult merge;
            try
            {
                merge = repo.Merge(remoteTip, BuildSignature(repo),
                    new MergeOptions { FastForwardStrategy = FastForwardStrategy.FastForwardOnly });
            }
            catch (Exception ex)
            {
                return ex.Message + (stash is null ? "" : " " + RestoreStash(repo, stash).Message);
            }
            if (merge.Status == MergeStatus.Conflicts)
                return "pull hit conflicts - resolve them with git manually." + (stash is null ? "" : " " + RestoreStash(repo, stash).Message);
            var after = repo.Head.Tip;
            if (before is not null && after is not null && before.Sha != after.Sha)
            {
                try { pulled = repo.ObjectDatabase.CalculateHistoryDivergence(before, after).BehindBy ?? 0; }
                catch { pulled = 0; }
            }
            if (stash is not null)
            {
                var restored = RestoreStash(repo, stash);
                note = restored.Message; needsAttention = !restored.Applied;
            }
            return null;
        }
        catch (LibGit2SharpException ex) when (ex.Message.Contains("fast-forward", StringComparison.OrdinalIgnoreCase)
                                            || ex.Message.Contains("non-fast", StringComparison.OrdinalIgnoreCase))
        {
            return "your branch has diverged from GitHub; a plain fast-forward isn't possible. Merge/rebase manually.";
        }
        catch (Exception ex) { return ex.Message; }
        finally
        {
            try
            {
                using var r = new Repository(DiscoverPath());
                CleanupFetchedRefs(r);
                if (added && remoteName is not null) r.Network.Remotes.Remove(remoteName);
            }
            catch { }
        }
    }

    static (bool Applied, string Message) RestoreStash(Repository repo, Stash stash)
    {
        try
        {
            int index = repo.Stashes.Select((x, i) => (x, i)).Where(t => t.x.WorkTree.Sha == stash.WorkTree.Sha).Select(t => t.i).DefaultIfEmpty(-1).First();
            if (index < 0) return (false, "Your local changes are in the git stash (garden-launcher auto-stash). Run `git stash list` and `git stash pop` to re-apply them.");
            var status = repo.Stashes.Pop(index, new StashApplyOptions());
            if (status == StashApplyStatus.Applied) return (true, "Your local changes were stashed and re-applied on top of the latest main (staging was not preserved).");
            return (false, $"Your local changes are safe in the git stash (result: {status}). Make sure the working tree is clean, then run `git stash pop` to re-apply them.");
        }
        catch (Exception ex) { return (false, "Your local changes are safe in the git stash but couldn't be re-applied: " + ex.Message + " Run `git stash pop` to retry."); }
    }

    void CleanupFetchedRefs(Repository repo)
    {
        try
        {
            foreach (var r in repo.Refs.Where(x => x.CanonicalName.StartsWith(_ns + "/", StringComparison.Ordinal)).ToList())
            {
                try { repo.Refs.Remove(r); } catch { }
            }
        }
        catch { }
    }

    void FetchAllHeads(Repository repo, string remoteName, FetchOptions opts)
    {
        Commands.Fetch(repo, remoteName, new[] { $"+refs/heads/*:{_ns}/*" }, opts, null);
    }

    (string name, bool added) ResolveRemote(Repository repo)
    {
        string want = Normalize(_url);
        foreach (var r in repo.Network.Remotes)
        {
            string u = (r.Url ?? "").TrimStart();
            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
            if (Normalize(u) == want) return (r.Name, false);
        }
        try { repo.Network.Remotes.Remove(_tempRemote); } catch { }
        repo.Network.Remotes.Add(_tempRemote, _url);
        return (_tempRemote, true);
    }

    static string Normalize(string url)
    {
        string s = (url ?? "").Trim();
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        int at = s.IndexOf('@');
        if (at >= 0) s = s[(at + 1)..];
        s = s.TrimEnd('/');
        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return s.ToLowerInvariant();
    }

    Commit? ResolveFetched(Repository repo, string branch)
    {
        var r = repo.Refs[$"{_ns}/{branch}"];
        if (r is null) return null;
        if (r.ResolveToDirectReference()?.Target is Commit c) return c;
        string id = r.ResolveToDirectReference()?.TargetIdentifier ?? r.TargetIdentifier ?? "";
        return string.IsNullOrEmpty(id) ? null : repo.Lookup<Commit>(id);
    }

    static Signature BuildSignature(Repository repo)
    {
        try
        {
            string name = repo.Config.GetValueOrDefault("user.name", "OpenCommonwealth Dev");
            string email = repo.Config.GetValueOrDefault("user.email", "dev@opencommonwealth.local");
            return new Signature(name, email, DateTimeOffset.Now);
        }
        catch { return new Signature("OpenCommonwealth Dev", "dev@opencommonwealth.local", DateTimeOffset.Now); }
    }

    static bool IsAuth(LibGit2SharpException ex)
    {
        string m = ex.Message.ToLowerInvariant();
        return m.Contains("authentication") || m.Contains("401") || m.Contains("403") ||
               m.Contains("credential") || m.Contains("too many redirects") || m.Contains("denied") ||
               ex is LibGit2Sharp.NotFoundException == false && m.Contains("username");
    }

    static bool IsOffline(LibGit2SharpException ex)
    {
        string m = ex.Message.ToLowerInvariant();
        return m.Contains("resolve host") || m.Contains("could not resolve") || m.Contains("timed out") ||
               m.Contains("failed to connect") || m.Contains("network") || m.Contains("connection");
    }

    internal Credentials Credentials(string url, string usernameFromUrl, SupportedCredentialTypes types)
    {
        if (!types.HasFlag(SupportedCredentialTypes.UsernamePassword)) return new DefaultCredentials();

        _authAttempt++;
        if (_authAttempt > MaxAuthAttempts)
            throw new LibGit2SharpException("authentication failed for github.com: the stored token was rejected.");

        if (Verified(_verified) is { } v)
            return new UsernamePasswordCredentials { Username = v.user, Password = v.secret };

        if (_authAttempt == 1 && OperatingSystem.IsWindows())
        {
            var cached = WinCred.Read("git:https://github.com") ?? WinCred.Read("git:https://github.com/");
            if (cached is not null)
                return new UsernamePasswordCredentials { Username = cached.Value.user, Password = cached.Value.secret };
        }

        _authCache ??= CredentialFill();
        if (_authCache is not null)
            return new UsernamePasswordCredentials { Username = _authCache.Value.user, Password = _authCache.Value.secret };
        return new DefaultCredentials();
    }

    const int MaxAuthAttempts = 3;

    static string? _verified;

    internal static (string user, string secret)? Verified(string? token) =>
        string.IsNullOrEmpty(token) ? null : ("x-access-token", token);

    static (string user, string secret)? CredentialFill() => CredentialFrom(GitFill, GhToken);

    internal static (string user, string secret)? CredentialFrom(Func<(string user, string secret)?> git, Func<string?> gh)
    {
        var fromGit = git();
        if (fromGit is not null) return fromGit;
        string? token = gh();
        return token is null ? null : ("x-access-token", token);
    }

    static (string user, string secret)? GitFill()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("core.askpass=");
            psi.ArgumentList.Add("credential"); psi.ArgumentList.Add("fill");
            NoninteractiveEnv(psi);
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            p.StandardInput.NewLine = "\n";
            p.StandardInput.WriteLine("protocol=https");
            p.StandardInput.WriteLine("host=github.com");
            p.StandardInput.WriteLine();
            p.StandardInput.Close();
            if (!p.WaitForExit(TimeSpan.FromSeconds(8))) { try { p.Kill(true); } catch { } return null; }
            if (!Task.WaitAll(new Task[] { stdout, stderr }, TimeSpan.FromSeconds(3))) return null;
            if (p.ExitCode != 0) return null;
            string outp = stdout.Result;
            string? user = null, secret = null;
            foreach (string line in outp.Split('\n'))
            {
                string t = line.Trim();
                if (t.StartsWith("username=")) user = t[9..];
                else if (t.StartsWith("password=")) secret = t[9..];
            }
            if (!string.IsNullOrEmpty(secret)) return (string.IsNullOrEmpty(user) ? "x-access-token" : user, secret);
        }
        catch { }
        return null;
    }

    static string? GhToken()
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = "gh", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string a in new[] { "auth", "token", "--hostname", "github.com" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(TimeSpan.FromSeconds(8))) { try { p.Kill(true); } catch { } return null; }
            if (p.ExitCode != 0 || !stdout.Wait(TimeSpan.FromSeconds(3))) return null;
            string t = stdout.Result.Trim();
            return t.Length == 0 ? null : t;
        }
        catch { return null; }
    }

    public enum OrgState { Member, Blocked, Unverified }

    public static void ResetTokenCache() { _token = null; _verified = null; }

    public static (OrgState State, string Message) VerifyOrg() => VerifyOrg(Token, GhToken, null);

    internal static (OrgState State, string Message) VerifyOrg(Func<string?> stored, Func<string?> gh, HttpMessageHandler? handler)
    {
        string? token = stored();
        if (token is null) return (OrgState.Blocked, "Not signed in to GitHub.");
        try
        {
            using var http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GardenDevLauncher/1");
            http.DefaultRequestHeaders.Authorization = new("Bearer", token);
            var me = http.GetAsync("https://api.github.com/user").GetAwaiter().GetResult();
            if (me.StatusCode == HttpStatusCode.Unauthorized && gh() is { } fresh && fresh != token)
            {
                _token = fresh;
                _verified = fresh;
                http.DefaultRequestHeaders.Authorization = new("Bearer", fresh);
                me.Dispose();
                me = http.GetAsync("https://api.github.com/user").GetAwaiter().GetResult();
            }
            using (me)
            {
                if (me.StatusCode == HttpStatusCode.Unauthorized) return (OrgState.Blocked, "GitHub rejected the stored sign-in. Run `gh auth login`, or paste a new token.");
                if (!me.IsSuccessStatusCode) return (OrgState.Unverified, $"GitHub answered {(int)me.StatusCode}; couldn't verify the org right now.");
                using var json = JsonDocument.Parse(me.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                string login = json.RootElement.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
                if (login.Length == 0) return (OrgState.Unverified, "GitHub didn't return a user name.");
                using var member = http.GetAsync($"https://api.github.com/orgs/{Org}/members/{login}").GetAwaiter().GetResult();
                if (member.StatusCode == HttpStatusCode.NoContent) return (OrgState.Member, $"{login} is a member of {Org}.");
                if (member.StatusCode == HttpStatusCode.Forbidden && member.Headers.Contains("X-GitHub-SSO"))
                    return (OrgState.Blocked, $"{login}: authorise this token for the {Org} org (SSO) at github.com/settings/tokens, or run `gh auth refresh`.");
                if (member.StatusCode is HttpStatusCode.Found or HttpStatusCode.NotFound)
                    return (OrgState.Blocked, $"{login} is not a member of {Org}. Ask the owner for an invite.");
                return (OrgState.Unverified, $"GitHub answered {(int)member.StatusCode}; couldn't verify the org right now.");
            }
        }
        catch (Exception ex) { return (OrgState.Unverified, "Couldn't reach GitHub: " + ex.Message); }
    }

    static void NoninteractiveEnv(ProcessStartInfo psi)
    {
        psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
        psi.EnvironmentVariables["GCM_INTERACTIVE"] = "never";
        psi.EnvironmentVariables["GIT_ASKPASS"] = "";
        psi.EnvironmentVariables["SSH_ASKPASS"] = "";
        psi.EnvironmentVariables["SSH_ASKPASS_REQUIRE"] = "never";
    }

    static string? _token;

    public static string? Token()
    {
        if (_token is not null) return _token;
        if (OperatingSystem.IsWindows())
            _token = (WinCred.Read("git:https://github.com") ?? WinCred.Read("git:https://github.com/"))?.secret;
        return _token ??= CredentialFill()?.secret;
    }

    public static bool HasStoredCredential()
    {
        if (OperatingSystem.IsWindows() &&
            (WinCred.Read("git:https://github.com") is not null || WinCred.Read("git:https://github.com/") is not null))
            return true;
        return CredentialFill() is not null;
    }

    public static string? SaveToken(string username, string token)
    {
        _token = null;
        _verified = null;
        string user = string.IsNullOrWhiteSpace(username) ? "x-access-token" : username;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                WinCred.Write("git:https://github.com", user, token);
                return null;
            }
            if (!CredentialApprove(user, token)) return "git credential approve failed.";
            var back = CredentialFill();
            if (back is null || back.Value.secret != token)
                return "no git credential helper is configured, so the token wasn't stored. Set one up "
                     + "(e.g. `git config --global credential.helper store`, or install libsecret / "
                     + "Git Credential Manager) and sign in again.";
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    static bool CredentialApprove(string user, string secret)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("core.askpass=");
            psi.ArgumentList.Add("credential"); psi.ArgumentList.Add("approve");
            NoninteractiveEnv(psi);
            using var p = Process.Start(psi);
            if (p is null) return false;
            var stderr = p.StandardError.ReadToEndAsync();
            p.StandardInput.NewLine = "\n";
            p.StandardInput.WriteLine("protocol=https");
            p.StandardInput.WriteLine("host=github.com");
            p.StandardInput.WriteLine($"username={user}");
            p.StandardInput.WriteLine($"password={secret}");
            p.StandardInput.WriteLine();
            p.StandardInput.Close();
            if (!p.WaitForExit(TimeSpan.FromSeconds(8))) { try { p.Kill(true); } catch { } return false; }
            Task.WaitAll(new Task[] { stderr }, TimeSpan.FromSeconds(3));
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class WinCred
{
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
    static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
    static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    static extern void CredFree(IntPtr cred);

    [StructLayout(LayoutKind.Sequential)]
    struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    public static (string user, string secret)? Read(string target)
    {
        if (!CredRead(target, 1 /* GENERIC */, 0, out IntPtr h) || h == IntPtr.Zero) return null;
        try
        {
            var c = Marshal.PtrToStructure<CREDENTIAL>(h);
            string user = c.UserName != IntPtr.Zero ? Marshal.PtrToStringUni(c.UserName) ?? "" : "";
            string secret = "";
            if (c.CredentialBlob != IntPtr.Zero && c.CredentialBlobSize > 0)
                secret = Marshal.PtrToStringUni(c.CredentialBlob, (int)(c.CredentialBlobSize / 2));
            if (string.IsNullOrEmpty(user)) user = "x-access-token";
            return string.IsNullOrEmpty(secret) ? null : (user, secret);
        }
        finally { CredFree(h); }
    }

    public static void Write(string target, string user, string secret)
    {
        byte[] blob = System.Text.Encoding.Unicode.GetBytes(secret);
        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        IntPtr targetPtr = Marshal.StringToCoTaskMemUni(target);
        IntPtr userPtr = Marshal.StringToCoTaskMemUni(user);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var c = new CREDENTIAL
            {
                Type = 1 /* GENERIC */,
                TargetName = targetPtr,
                CredentialBlob = blobPtr,
                CredentialBlobSize = (uint)blob.Length,
                Persist = 2 /* LOCAL_MACHINE */,
                UserName = userPtr,
            };
            if (!CredWrite(ref c, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
            Marshal.FreeCoTaskMem(targetPtr);
            Marshal.FreeCoTaskMem(userPtr);
        }
    }
}
