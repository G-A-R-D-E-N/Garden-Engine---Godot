using Radicle.Interop;

namespace OpenCommonwealth.Tests;

internal static class Fo4Data
{
    internal const string Variable = "FO4_DATA_DIR";

    // Constraint: the same five plugins the Papyrus tests named by archive; adding one changes the registry .pex counts.
    internal static readonly string[] Plugins = { "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCworkshop02.esm", "DLCworkshop03.esm" };

    internal static string? Folder => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } data && Directory.Exists(data) ? data : null;

    internal static string? Master => Folder is { } data && File.Exists(Path.Combine(data, "Fallout4.esm")) ? Path.Combine(data, "Fallout4.esm") : null;

    internal static IReadOnlyList<string> Archives(string data) => AssetResolver.ArchiveLoadOrder(data, Array.Empty<string>(), Plugins);

    internal static AssetResolver? Assets() => Folder is { } data ? AssetResolver.OpenRoots(Array.Empty<string>(), Archives(data)) : null;

    private static readonly object Gate = new();
    private static string? sharedFolder;
    private static AssetResolver? shared;

    internal static byte[]? Read(string path)
    {
        if (Folder is not { } data)
            return null;
        lock (Gate)
        {
            if (!string.Equals(sharedFolder, data, StringComparison.Ordinal))
            {
                shared?.Dispose();
                shared = AssetResolver.OpenRoots(Array.Empty<string>(), Archives(data));
                sharedFolder = data;
            }
            return shared.Read(path);
        }
    }
}
