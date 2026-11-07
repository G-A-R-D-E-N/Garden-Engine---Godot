using System;
using System.IO;

namespace OpenCommonwealth.Services.Papyrus;

/// <summary>
/// Where the machine-local Papyrus script cache lives.
/// </summary>
public static class ScriptCacheLayout
{
    public const string CurrentDirectoryName = "current";
    public const string IncomingDirectoryName = "incoming";
    public const string ObsoleteDirectoryName = "obsolete";
    public const string MarkerFileName = "swap.marker";
    public const string ManifestFileName = "manifest.json";
    public const string QuestStartupFileName = "quests.json";
    public const string ScriptsDirectoryName = "scripts";
    public const string PexExtension = ".pex";
    public const string IrExtension = ".gir";

    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GardenEngine",
        "script-cache");

    public static string CurrentDirectory(string root) => Path.Combine(root, CurrentDirectoryName);

    public static string IncomingDirectory(string root) => Path.Combine(root, IncomingDirectoryName);

    public static string ObsoleteDirectory(string root) => Path.Combine(root, ObsoleteDirectoryName);

    public static string MarkerPath(string root) => Path.Combine(root, MarkerFileName);

    public static string ManifestPath(string root) =>
        Path.Combine(CurrentDirectory(root), ManifestFileName);

    public static string QuestStartupPath(string root) =>
        Path.Combine(CurrentDirectory(root), QuestStartupFileName);

    public static string ScriptsDirectory(string root) => ScriptsDirectoryIn(CurrentDirectory(root));

    /// <summary>The scripts directory inside any cache directory, current or incoming.</summary>
    public static string ScriptsDirectoryIn(string cacheDirectory) =>
        Path.Combine(cacheDirectory, ScriptsDirectoryName);

    /// <summary>The cache-relative path of one script's IR, always forward-slashed.</summary>
    public static string ScriptRelativePath(string name) => ScriptsDirectoryName + "/" + SafeName(name) + IrExtension;

    /// <summary>The absolute path of one script's IR inside a built cache.</summary>
    public static string ScriptPath(string root, string name) => ScriptPathIn(CurrentDirectory(root), name);

    /// <summary>The absolute path of one script's IR inside any cache directory.</summary>
    public static string ScriptPathIn(string cacheDirectory, string name) =>
        Path.Combine(ScriptsDirectoryIn(cacheDirectory), SafeName(name).Replace('/', Path.DirectorySeparatorChar) + IrExtension);

    public static string PexRelativePath(string name) => ScriptsDirectoryName + "/" + SafeName(name) + PexExtension;

    public static string PexPath(string root, string name) => PexPathIn(CurrentDirectory(root), name);

    public static string PexPathIn(string cacheDirectory, string name) =>
        Path.Combine(ScriptsDirectoryIn(cacheDirectory), SafeName(name).Replace('/', Path.DirectorySeparatorChar) + PexExtension);

    internal static string SafeName(string name)
    {
        var normalized = name.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/')
            || normalized.Contains(':') || normalized.IndexOfAny(new[] { '\0', '<', '>', '"', '|', '?', '*' }) >= 0
            || Array.Exists(normalized.Split('/'), part => part.Length == 0 || part is "." or ".."
                || part.EndsWith('.') || part.EndsWith(' ')))
            throw new ArgumentException("a script name must be a safe relative path", nameof(name));
        return normalized;
    }
}
