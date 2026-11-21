using System;

namespace OpenCommonwealth.Services.Combat;

public sealed class HeldWeaponTracker
{
    private string? _shown;
    private string _variant = string.Empty;

    // Invariant: the shown key is the equipped key only while the weapon is drawn, so equip, draw, sheathe, unequip and restore all reduce to one comparison; a changed variant (the installed mods) counts as a change.
    public string? Shown => _shown;

    public bool Update(string? equippedKey, bool drawn, string variant = "")
    {
        var wanted = drawn && !string.IsNullOrEmpty(equippedKey) ? equippedKey : null;
        var wantedVariant = wanted is null ? string.Empty : variant;
        if (string.Equals(wanted, _shown, StringComparison.OrdinalIgnoreCase) && string.Equals(wantedVariant, _variant, StringComparison.OrdinalIgnoreCase)) return false;
        _shown = wanted;
        _variant = wantedVariant;
        return true;
    }

    public void Reset()
    {
        _shown = null;
        _variant = string.Empty;
    }
}
