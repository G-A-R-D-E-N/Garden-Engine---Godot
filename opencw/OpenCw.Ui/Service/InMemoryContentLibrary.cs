using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Service;

public sealed class InMemoryContentLibrary : IContentLibrary
{
    public InMemoryContentLibrary(IEnumerable<ContentItem> items, Func<string, bool>? enabledOf = null)
    {
        Items = items.ToList();
        EnabledOf = enabledOf;
    }

    public List<ContentItem> Items { get; }

    public Func<string, bool>? EnabledOf { get; set; }

    public int LoadCount { get; private set; }

    public IReadOnlyList<ContentItem> Load()
    {
        LoadCount++;
        return Items.Select(i => EnabledOf is { } of ? i with { Enabled = of(i.FileName) } : i).ToList();
    }
}
