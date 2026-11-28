using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Streaming;

public sealed class DrainRegistry
{
    private readonly Dictionary<string, object> _owners = new();

    public bool Claim(string path)
    {
        lock (_owners) return _owners.Remove(path);
    }

    public bool Claim(string path, object token)
    {
        lock (_owners)
        {
            if (!_owners.TryGetValue(path, out var current) || !ReferenceEquals(current, token)) return false;
            return _owners.Remove(path);
        }
    }

    public Dictionary<string, object> Begin(IEnumerable<string> paths)
    {
        var tokens = new Dictionary<string, object>();
        lock (_owners)
            foreach (var path in paths) _owners[path] = tokens[path] = new object();
        return tokens;
    }

    public bool Owns(string path, object token)
    {
        lock (_owners) return _owners.TryGetValue(path, out var current) && ReferenceEquals(current, token);
    }

    public bool RunIfOwner(string path, object token, Action action)
    {
        lock (_owners)
        {
            if (!_owners.TryGetValue(path, out var current) || !ReferenceEquals(current, token)) return false;
            action();
            return true;
        }
    }
}
