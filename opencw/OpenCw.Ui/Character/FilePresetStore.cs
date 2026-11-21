using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Character;

public interface IPresetFiles
{
    IReadOnlyList<string> List();

    string? Read(string key);

    // Requires: a failed Write leaves any existing file for the key intact.
    bool Write(string key, string text);

    // Requires: returns false without touching the file when the key already exists; the default is not atomic.
    bool WriteNew(string key, string text) => Read(key) is null && Write(key, text);

    bool Remove(string key);
}

public sealed class FilePresetStore : IAppearancePresetStore
{
    public const int MaxNameLength = 48;

    private readonly IPresetFiles _files;

    public FilePresetStore(IPresetFiles files)
    {
        _files = files;
    }

    public string? LastError { get; private set; }

    public static string? Key(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length > MaxNameLength) trimmed = trimmed.Substring(0, MaxNameLength).TrimEnd();
        var key = new string(trimmed.Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' ? c : '_').ToArray());
        return key.Any(char.IsLetterOrDigit) ? key : null;
    }

    public IReadOnlyList<string> Names()
    {
        try
        {
            LastError = null;
            return _files.List().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Array.Empty<string>();
        }
    }

    public string? Load(string name) => Guard<string>(name, key => _files.Read(key));

    public bool Save(string name, string json) => Guard<bool>(name, key => Checked(name, key) && _files.Write(key, json));

    public bool Create(string name, string json) => Guard<bool>(name, key =>
    {
        if (!Checked(name, key)) return false;
        if (_files.WriteNew(key, json)) return true;
        LastError ??= "A preset with that name already exists.";
        return false;
    });

    private bool Checked(string name, string key)
    {
        if (!string.Equals(name.Trim(), key, StringComparison.Ordinal))
        {
            LastError = "The preset name contains unsupported characters or is too long.";
            return false;
        }
        if (_files.List().Any(existing =>
                !string.Equals(existing, key, StringComparison.Ordinal) &&
                string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)))
        {
            LastError = "A preset with that name already exists with different capitalization.";
            return false;
        }
        return true;
    }

    public bool Delete(string name) => Guard<bool>(name, key => _files.Remove(key));

    private T? Guard<T>(string name, Func<string, T?> action)
    {
        var key = Key(name);
        if (key is null)
        {
            LastError = "The preset name is not usable.";
            return default;
        }
        try
        {
            LastError = null;
            return action(key);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return default;
        }
    }
}
