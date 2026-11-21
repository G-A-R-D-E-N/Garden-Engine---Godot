using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Hud;

// Requires: main thread; the first Observe only records the current stages, so loading a save never shows a banner.
public sealed class ObjectiveWatch
{
    private readonly Dictionary<string, int> _stages = new(StringComparer.Ordinal);
    private bool _seeded;

    public void Reset()
    {
        _stages.Clear();
        _seeded = false;
    }

    public string? Observe(IEnumerable<(string Id, int Stage, string? Text)> running)
    {
        string? shown = null;
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, stage, text) in running)
        {
            present.Add(id);
            bool changed = !_stages.TryGetValue(id, out var held) || held != stage;
            _stages[id] = stage;
            if (_seeded && changed && !string.IsNullOrWhiteSpace(text)) shown = text;
        }
        foreach (var gone in new List<string>(_stages.Keys))
            if (!present.Contains(gone)) _stages.Remove(gone);
        _seeded = true;
        return shown;
    }
}
