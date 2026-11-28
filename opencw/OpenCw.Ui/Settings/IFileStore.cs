using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Settings;

public interface IFileStore
{
    string? ReadAllText(string name);

    bool WriteAllText(string name, string text);
}

public sealed class InMemoryFileStore : IFileStore
{
    public Dictionary<string, string> Files { get; } = new();

    public string? ReadAllText(string name) => Files.TryGetValue(name, out var text) ? text : null;

    public bool WriteAllText(string name, string text)
    {
        Files[name] = text;
        return true;
    }
}

public enum SettingsLoadResult
{
    NoFile,
    Loaded,
    Corrupt,
}
