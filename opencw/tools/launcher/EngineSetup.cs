namespace OpenCommonwealth.Launcher;

internal enum EngineStep { Cancelled, UseExisting, Download }

internal static class EngineSetup
{
    internal static (EngineStep Step, string? Path) FromChoice(int picked, string defaultDir, Func<string?> pickExe, Func<string?> pickDir)
    {
        switch (picked)
        {
            case 0:
                return (EngineStep.Cancelled, null);
            case 3:
                return pickExe() is { } exe ? (EngineStep.UseExisting, exe) : (EngineStep.Cancelled, null);
            case 2:
                return pickDir() is { } dir ? (EngineStep.Download, dir) : (EngineStep.Cancelled, null);
            default:
                return (EngineStep.Download, defaultDir);
        }
    }
}
