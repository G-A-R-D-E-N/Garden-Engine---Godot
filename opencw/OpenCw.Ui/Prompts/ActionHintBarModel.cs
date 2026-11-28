using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Prompts;

public sealed record ActionHintSpec(
    string Name,
    string Key,
    string Fallback,
    string Text,
    int Pad = 0,
    bool Visible = true,
    bool Enabled = true);

public sealed record ActionHintItem(string Name, string Binding, string Text, bool Visible, bool Enabled, bool Held)
{
    public string Caption => Binding.Length > 0 ? Binding + ": " + Text : Text;
}

public interface IActionHintSource
{
    IReadOnlyList<ActionHintSpec> Hints { get; }

    bool Controller { get; }

    IReadOnlySet<int> Held { get; }

    void Invoke(string name);
}

public sealed class InMemoryActionHintSource : IActionHintSource
{
    private readonly List<ActionHintSpec> _hints = new();
    private readonly List<string> _invoked = new();

    public IReadOnlyList<ActionHintSpec> Hints => _hints;

    public bool Controller { get; set; }

    public IReadOnlySet<int> Held { get; set; } = new HashSet<int>();

    public IReadOnlyList<string> Invoked => _invoked;

    public void Set(IEnumerable<ActionHintSpec> hints)
    {
        _hints.Clear();
        _hints.AddRange(hints);
    }

    public event Action<string>? Activated;

    public void Invoke(string name)
    {
        _invoked.Add(name);
        Activated?.Invoke(name);
    }
}

public sealed class ActionHintBarModel
{
    private static readonly char[] ForbiddenNameChars = { '.', ':', '@', '/', '"', '%' };

    private readonly IActionHintSource _source;
    private IReadOnlyList<ActionHintItem> _items = Array.Empty<ActionHintItem>();

    public ActionHintBarModel(IActionHintSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public IReadOnlyList<ActionHintItem> Items => _items;

    public int Rejected { get; private set; }

    public int Version { get; private set; }

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(ForbiddenNameChars) < 0 && !name.Any(char.IsControl);

    public void Sync()
    {
        bool controller = _source.Controller;
        var held = new HashSet<int>(_source.Held ?? new HashSet<int>());
        var seen = new HashSet<string>();
        var items = new List<ActionHintItem>();
        int rejected = 0;
        foreach (var spec in _source.Hints ?? Array.Empty<ActionHintSpec>())
        {
            if (spec is null || !IsValidName(spec.Name) || spec.Text is null || !seen.Add(spec.Name))
            {
                rejected++;
                continue;
            }
            string binding = (controller ? spec.Fallback : spec.Key) ?? "";
            items.Add(new ActionHintItem(spec.Name, binding, spec.Text, spec.Visible, spec.Enabled, controller && held.Contains(spec.Pad)));
        }
        Rejected = rejected;
        if (items.SequenceEqual(_items)) return;
        _items = items;
        Version++;
    }

    public bool IsActivatable(string name) => Find(name) is { Visible: true, Enabled: true };

    public bool Activate(string name)
    {
        if (!IsActivatable(name)) return false;
        _source.Invoke(name);
        return true;
    }

    private ActionHintItem? Find(string name) => _items.FirstOrDefault(item => item.Name == name);
}
