using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Vats;

public readonly record struct VatsPick(string Id, bool Alive);

public static class VatsTargetPicker
{
    public static string? Pick(string? preferredId, IEnumerable<VatsPick> candidates)
    {
        if (preferredId is null) return null;
        foreach (var c in candidates)
            if (c.Alive && c.Id == preferredId) return c.Id;
        return null;
    }

    public static T? Resolve<T>(VatsMenuModel model, Func<string, T?> target) where T : class
        => model.Focus is { } focus ? target(focus.Id) : null;

    public static void Follow<T>(VatsMenuModel model, Func<string, T?> target, Action<T> focused) where T : class
        => model.FocusChanged += id => { if (target(id) is { } node) focused(node); };
}
