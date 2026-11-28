using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Launcher;

internal static class LauncherChecks
{
    internal static bool SdkMatches(string? have, string? want) =>
        !string.IsNullOrEmpty(have) && !string.IsNullOrEmpty(want) && have == want;

    internal static void StartEditor(ProcessStartInfo info)
    {
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The editor process did not start.");
        using var timeout = new CancellationTokenSource(3000);
        try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) when (!process.HasExited) { return; }
        if (process.HasExited && process.ExitCode != 0)
            throw new InvalidOperationException($"The editor exited immediately with code {process.ExitCode}.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersion();

    internal static uint? PinnedAbi(string pin) =>
        Regex.Match(pin, @"AbiVersion\s*=\s*(\d+)") is { Success: true } m && uint.TryParse(m.Groups[1].Value, out uint v) ? v : null;

    internal static bool AbiMatches(uint? got, uint? expected) => got.HasValue && expected.HasValue && got == expected;

    internal static bool ValidRadicle(string root, string radicle, Action<string>? log = null)
    {
        bool Reject(string why) { log?.Invoke("[X] Prebuilt Radicle rejected: " + why); return false; }
        try
        {
            uint? expected = PinnedAbi(File.ReadAllText(Path.Combine(root, "OpenCw.Papyrus", "RadiclePin.cs")));
            string bin = Path.Combine(radicle, "bin");
            string library = Path.Combine(bin, OperatingSystem.IsWindows() ? "radicle.dll" : "libradicle.so");
            if (expected is null) return Reject("RadiclePin.cs has no readable AbiVersion.");
            if (!File.Exists(Path.Combine(bin, "Radicle.props"))) return Reject("Radicle.props is missing.");
            if (!File.Exists(library)) return Reject(library + " is missing.");
            if (!NativeLibrary.TryLoad(library, out IntPtr handle)) return Reject(library + " cannot be loaded.");
            try
            {
                if (!NativeLibrary.TryGetExport(handle, "radicle_abi_version", out IntPtr export)) return Reject("the library has no radicle_abi_version export.");
                uint got = Marshal.GetDelegateForFunctionPointer<AbiVersion>(export)();
                return AbiMatches(got, expected) || Reject($"library ABI {got} does not match the pinned {expected}.");
            }
            finally { NativeLibrary.Free(handle); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or BadImageFormatException or DllNotFoundException or EntryPointNotFoundException) { return Reject(ex.Message); }
    }
}
