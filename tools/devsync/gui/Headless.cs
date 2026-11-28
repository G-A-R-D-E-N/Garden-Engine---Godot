using System;
using System.Runtime.InteropServices;

namespace GardenDevLauncher;

/// <summary>
/// The same engine without a window: used by a scheduled task, and the way the
/// git behaviour is exercised against a real repository without a display.
/// </summary>
internal static class Headless
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint processId);

    public static int Run(bool checkOnly, string repo, string baseBranch)
    {
        try
        {
            // The app is built as a windowed program, so pick up the console it
            // was started from; when the output is redirected this is a no-op.
            AttachConsole(AttachParentProcess);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        var engine = new SyncEngine(repo, line => Console.WriteLine(line), baseBranch: baseBranch);
        try
        {
            var outcome = checkOnly ? engine.Check() : engine.Sync();
            Console.WriteLine($"garden-dev: {outcome.Summary}");
            return checkOnly && !outcome.Snapshot.UpToDate ? 1 : 0;
        }
        catch (RefusedException error)
        {
            Console.WriteLine($"garden-dev: {error.Message}");
            return 1;
        }
        catch (Exception error)
        {
            Console.WriteLine($"garden-dev: {error.Message}");
            return 1;
        }
    }
}
