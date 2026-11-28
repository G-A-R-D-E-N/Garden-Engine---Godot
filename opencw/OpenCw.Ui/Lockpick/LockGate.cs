using OpenCommonwealth.Services.Ui.Trade;
using LockKind = Radicle.Interop.Native.LockKind;

namespace OpenCommonwealth.Services.Ui.Lockpick;

public interface ILockFacts
{
    LockKind Lock(string refKey);
    bool HasKey(string refKey);
}

public enum LockAccess { Open, KeyUnlock, Pick, Refused }

public readonly record struct LockDecision(LockAccess Access, LockLevel Level = LockLevel.Novice);

public static class LockGate
{
    public static LockDecision Decide(ILockFacts facts, string refKey, ContainerLedger ledger)
    {
        var kind = facts.Lock(refKey);
        if (kind == LockKind.None || ledger.IsUnlocked(refKey)) return new(LockAccess.Open);
        if (facts.HasKey(refKey)) return new(LockAccess.KeyUnlock);
        return RecordLockpickRules.Level(kind) is { } level ? new(LockAccess.Pick, level) : new(LockAccess.Refused);
    }
}
