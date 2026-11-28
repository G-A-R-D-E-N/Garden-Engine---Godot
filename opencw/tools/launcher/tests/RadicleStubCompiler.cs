using System.ComponentModel;
using System.Diagnostics;

namespace OpenCommonwealth.Launcher.Tests;

internal static class RadicleStubCompiler
{
    internal static bool TryCompile(string outputLibrary, uint abi)
    {
        string work = Path.GetDirectoryName(outputLibrary)!;
        string sourceName = "radicle_abi_stub.c";
        File.WriteAllText(Path.Combine(work, sourceName), Source(abi));
        string libName = Path.GetFileName(outputLibrary);
        foreach (var (file, args) in Commands(work, libName, sourceName))
        {
            if (Run(file, args, work) == 0 && File.Exists(outputLibrary))
                return true;
        }
        return false;
    }

    static string Source(uint abi) => OperatingSystem.IsWindows()
        ? $"__declspec(dllexport) unsigned int radicle_abi_version(void) {{ return {abi}; }}\n"
        : $"unsigned int radicle_abi_version(void) __attribute__((visibility(\"default\")));\n"
          + $"unsigned int radicle_abi_version(void) {{ return {abi}; }}\n";

    static IEnumerable<(string File, string Args)> Commands(string work, string libName, string sourceName)
    {
        if (OperatingSystem.IsWindows())
        {
            string? vs = VsInstall();
            if (vs is not null)
            {
                string vcvars = Path.Combine(vs, "VC", "Auxiliary", "Build", "vcvars64.bat");
                if (File.Exists(vcvars))
                    yield return ("cmd.exe", $"/c \"\"{vcvars}\" >nul && cl /nologo /LD /Fe:{libName} {sourceName}\"");
            }
            foreach (string exe in new[] { "clang", "gcc" })
                yield return (exe, $"-shared -o {libName} {sourceName}");
        }
        else
        {
            foreach (string exe in new[] { "gcc", "clang" })
                yield return (exe, $"-shared -fPIC -o {libName} {sourceName}");
        }
    }

    static string? VsInstall()
    {
        string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere)) return null;
        using var p = Process.Start(new ProcessStartInfo(vswhere,
            "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath")
        { RedirectStandardOutput = true, UseShellExecute = false })!;
        string path = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return p.ExitCode == 0 && Directory.Exists(path) ? path : null;
    }

    static int Run(string file, string args, string work)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = work,
            });
            if (p is null) return 1;
            p.WaitForExit(60_000);
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException) { return 1; }
    }
}