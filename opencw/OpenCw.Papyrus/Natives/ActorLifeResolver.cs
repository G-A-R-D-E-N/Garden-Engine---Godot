using System;
using OpenCommonwealth.Services.Papyrus.Owners;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class ActorLifeResolver(
    Func<string, bool> isPlayer,
    Func<bool?> playerDead,
    Func<PapyrusOwnerId, bool?> live,
    Func<string, (string BaseKey, bool Dead)?> stored) : IActorLifeSource
{
    // Constraint: the ledger answer is used only when its base form equals the receiver's, an empty ledger base being a legacy record.
    public bool TryIsDead(PapyrusOwnerId owner, out bool dead)
    {
        dead = false;
        bool? answer;
        if (isPlayer(owner.Discriminator)) answer = playerDead();
        else answer = live(owner) ?? StoredAnswer(owner);
        if (answer is not { } value) return false;
        dead = value;
        return true;
    }

    // Invariant: two owners are the same only when kind, base form and discriminator all match; form keys compare case-insensitively.
    public static bool SameOwner(PapyrusOwnerId registered, PapyrusOwnerId requested) =>
        registered.Kind == requested.Kind
        && string.Equals(registered.BaseFormKey, requested.BaseFormKey, StringComparison.OrdinalIgnoreCase)
        && string.Equals(registered.Discriminator, requested.Discriminator, StringComparison.OrdinalIgnoreCase);

    private bool? StoredAnswer(PapyrusOwnerId owner)
    {
        if (stored(owner.Discriminator) is not { } record) return null;
        return record.BaseKey.Length == 0 || string.Equals(record.BaseKey, owner.BaseFormKey, StringComparison.OrdinalIgnoreCase) ? record.Dead : null;
    }
}
