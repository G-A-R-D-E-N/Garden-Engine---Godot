using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Lists;

public interface IFavouriteStore
{
    IReadOnlyCollection<string> Load();

    void Save(IReadOnlyCollection<string> keys);
}

public sealed class InMemoryFavouriteStore : IFavouriteStore
{
    private HashSet<string> _keys;

    public InMemoryFavouriteStore(IEnumerable<string>? keys = null)
    {
        _keys = new HashSet<string>(keys ?? Array.Empty<string>());
    }

    public IReadOnlyCollection<string> Load() => new HashSet<string>(_keys);

    public void Save(IReadOnlyCollection<string> keys)
    {
        _keys = new HashSet<string>(keys);
    }
}
