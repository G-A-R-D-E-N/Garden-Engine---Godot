using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Versioning;

namespace OpenCommonwealth.Launcher;

internal static class LauncherProcess
{
    [UnsupportedOSPlatform("windows")]
    public static void MakeExecutable(string path)
    {
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static string? VsCmake()
    {
        string? vswhere = FirstExisting(Environment.GetEnvironmentVariable("ProgramFiles(x86)") + @"\Microsoft Visual Studio\Installer\vswhere.exe");
        if (vswhere is null) return null;
        string? found = Capture(vswhere, "-latest -find Common7\\IDE\\CommonExtensions\\Microsoft\\CMake\\CMake\\bin\\cmake.exe")?.Split('\n').FirstOrDefault()?.Trim();
        return !string.IsNullOrEmpty(found) && File.Exists(found) ? found : null;
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
    }

    public static string? FindOnPath(string exe)
    {
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;
        string name = OperatingSystem.IsWindows() && !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe + ".exe" : exe;
        foreach (string dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
            }
        }
        return null;
    }

    public static string? FirstExisting(params string?[] paths)
    {
        foreach (string? p in paths) if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
        return null;
    }

    public static bool Extract(string zip, string dest)
    {
        try { ZipFile.ExtractToDirectory(zip, dest, overwriteFiles: true); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return false; }
    }

    public static void Save(string file, string content)
    {
        try { File.WriteAllText(file, content + Environment.NewLine); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static string? ReadFirstLine(string file)
    {
        try { return File.Exists(file) ? File.ReadAllLines(file).FirstOrDefault()?.Trim() : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static int Run(string file, string args, string workingDirectory, Action<string> log)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1";
            using var p = Process.Start(psi);
            if (p is null) return -1;
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) log("    " + e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) log("    " + e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception ex) { log("[X] Couldn't run " + file + ": " + ex.Message); return -1; }
    }

    public static string? Capture(string file, string args, string? cwd = null, int timeoutSeconds = 30)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                WorkingDirectory = cwd ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                try { p.Kill(true); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                return null;
            }
            Task.WaitAll(new Task[] { stdout, stderr }, TimeSpan.FromSeconds(5));
            return p.ExitCode == 0 ? stdout.Result : null;
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or AggregateException)
        {
            return null;
        }
    }
}
