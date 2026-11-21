using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Flow;

// Requires: main thread only; the tree is paused while any owner holds a claim, so one owner releasing cannot unpause another's pause.
public sealed class PauseClaims
{
    private readonly HashSet<object> _owners = new(ReferenceEqualityComparer.Instance);

    public bool Paused => _owners.Count > 0;

    public bool Set(object owner, bool claimed) => claimed ? _owners.Add(owner) : _owners.Remove(owner);
}
