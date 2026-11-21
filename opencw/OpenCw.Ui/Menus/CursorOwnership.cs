using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Menus;

// Invariant: the mode before the first owner is kept until the last owner releases; a repeated Acquire or Release by the same owner changes nothing.
public sealed class CursorOwnership<TMode>
{
    private readonly HashSet<object> _owners = new(ReferenceEqualityComparer.Instance);
    private TMode _prior = default!;

    public int Count => _owners.Count;

    public void Acquire(object owner, TMode current)
    {
        if (_owners.Count == 0)
            _prior = current;
        _owners.Add(owner);
    }

    public bool Release(object owner, out TMode prior)
    {
        prior = _prior;
        return _owners.Remove(owner) && _owners.Count == 0;
    }
}
