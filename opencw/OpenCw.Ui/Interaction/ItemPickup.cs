using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Interaction;

public sealed record ItemTaken(string RefKey, string BaseKey, int Count, bool Stolen, string BaseType = "");

public sealed class ItemPickup(PlayerItems store, Func<ActivationTarget, bool> ownerAllows, Action<ActivationTarget> removeReference, ContainerLedger? taken = null,
    DroppedItemStates? dropped = null, CrimeLedger? crimes = null) : IActivationHandler
{
    public static readonly IReadOnlySet<string> ItemBaseTypes = OpenCommonwealth.Services.Ui.Records.ItemRecordTypes.PickupBaseTypes;

    public event Action<ItemTaken>? Taken;

    public static Func<ActivationTarget, bool> SameOwner(string? playerKey) =>
        target => string.Equals(target.Owner, playerKey, StringComparison.OrdinalIgnoreCase);

    public bool Handles(ActivationTarget target) => ItemBaseTypes.Contains(target.BaseType) && target.BaseKey.Length > 0;

    public ActivationResult Activate(ActivationTarget target)
    {
        var count = Math.Max(1, target.Count);
        var stolen = target.Owner is { Length: > 0 }
            && !target.OwnerNoCrime
            && !ownerAllows(target);
        // Invariant: a dropped item never takes default mods, so detaching its mods, dropping and picking it up again cannot return them.
        var isDrop = target.RefKey.StartsWith(DroppedItemStates.TokenPrefix, StringComparison.Ordinal);
        store.Add(target.BaseKey, (dropped ?? DroppedItemStates.Current).Take(target.RefKey) ?? (isDrop ? ItemState.Plain : store.NewState(target.BaseKey)), count);
        if (target.RefKey.Length > 0 && !target.RefKey.StartsWith(DroppedItemStates.TokenPrefix, StringComparison.Ordinal)) taken?.MarkTaken(target.RefKey);
        if (stolen && !string.IsNullOrWhiteSpace(target.Owner) && !string.IsNullOrWhiteSpace(target.BaseKey)) crimes?.Report(CrimeKind.Theft, target.Owner!, target.BaseKey, count, target.RefKey, witnessed: false);
        removeReference(target);
        Taken?.Invoke(new ItemTaken(target.RefKey, target.BaseKey, count, stolen, target.BaseType));
        return new(ActivationStatus.Handled, stolen ? $"stole {count}" : $"took {count}");
    }
}
