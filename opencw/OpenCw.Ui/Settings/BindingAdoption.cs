using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Settings;

public static class BindingAdoption
{
    public static int Adopt(KeyBindingModel model, IReadOnlyDictionary<string, IReadOnlyList<string>> live)
    {
        var adopted = 0;
        foreach (var action in model.Actions)
        {
            if (!live.TryGetValue(action.Id, out var codes) || codes.Count == 0) continue;
            var defaults = new[] { action.KeyboardPrimary, action.KeyboardSecondary }.Where(c => c is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (defaults.SetEquals(codes)) continue;
            if (string.Equals(codes[0], model.Get(action.Id, InputDevice.Keyboard, BindingSlot.Primary), StringComparison.Ordinal)) continue;
            if (model.AdoptKeyboard(action.Id, codes[0])) adopted++;
        }
        if (adopted > 0) model.PersistAdopted();
        return adopted;
    }
}
