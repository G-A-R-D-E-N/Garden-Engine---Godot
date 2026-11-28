namespace OpenCommonwealth.Services.Interaction;

public enum NpcLifeState { Alive = 0, Dying = 1, Dead = 2, Unconscious = 3, Reanimate = 4, Recycle = 5, Restrained = 6, EssentialDown = 7, Bleedout = 8 }

public enum NpcSitSleep
{
    Normal = 0, WantToSit = 1, WaitingForSitAnim = 2, IsSitting = 3, WantToStand = 4,
    WantToSleep = 5, WaitingForSleepAnim = 6, IsSleeping = 7, WantToWake = 8,
}

public static class NpcStates
{
    public const int NormalKnock = 0;
    public const int DownKnock = 7;
    public const int SneakingStance = 1;

    public static bool KnockedOut(int knockState) => knockState != NormalKnock && knockState != DownKnock;

    // Requires: the engine's extra-data exclusion for a non-mount actor (probably kActorInteraction) is not modelled here.
    public static bool IsSneaking(int stance, bool swimming, bool inPowerArmor) =>
        stance == SneakingStance && (!swimming || inPowerArmor);
}

public enum NpcPromptKind { None, Search, SearchEmpty, Pickpocket, Talk, Unmodelled }

// Requires: Dead is TESObjectREFR::IsDead with its argument true; IsMount is Actor::BOOL_FLAGS::kIsAMount; the unnamed rider handle that selects the composite "%s - %s" text is zero.
public sealed record NpcPromptState(
    bool Dead, bool HasItems, bool IsMount, bool PlayerSneaking, bool AllowsPickpocket, bool Talkable,
    NpcLifeState LifeState, bool CanSpeakToEssentialDown, bool PlayerTeammate = false);

public static class NpcPromptRules
{
    // Requires: UNVERIFIED, the rider handle behind hasRider is an unnamed helper (rider or mount unread); the argument order of the composite text is unnamed.
    public static bool IsComposite(bool isMount, bool hasRider) => !isMount && hasRider;

    public static NpcPromptKind Choose(NpcPromptState s)
    {
        if (s.Dead) return s.HasItems ? NpcPromptKind.Search : NpcPromptKind.SearchEmpty;
        if (s.IsMount) return NpcPromptKind.Unmodelled;
        if (s.PlayerSneaking && !s.PlayerTeammate) return s.AllowsPickpocket ? NpcPromptKind.Pickpocket : NpcPromptKind.None;
        bool downBlocked = s.LifeState == NpcLifeState.EssentialDown && !s.CanSpeakToEssentialDown;
        return s.Talkable && !downBlocked ? NpcPromptKind.Talk : NpcPromptKind.None;
    }
}

public enum NpcActivationRefusal { None, Unconscious, Reanimate, InCombat, EssentialDown, SitSleepTransition }

// Requires: Dead is TESObjectREFR::IsDead with its argument false; the sit/sleep gate applies to activation by the player.
public sealed record NpcActivationState(
    NpcLifeState LifeState, bool Dead, bool InCombat, int KnockState, NpcSitSleep SitSleep, bool CanSpeakToEssentialDown);

public static class NpcActivationGate
{
    public static NpcActivationRefusal Check(NpcActivationState s)
    {
        if (!s.Dead && s.LifeState == NpcLifeState.Unconscious) return NpcActivationRefusal.Unconscious;
        bool essentialDown = s.LifeState == NpcLifeState.EssentialDown;
        if (essentialDown && !s.CanSpeakToEssentialDown) return NpcActivationRefusal.EssentialDown;
        if (s.InCombat && !essentialDown && !NpcStates.KnockedOut(s.KnockState)) return NpcActivationRefusal.InCombat;
        if (s.SitSleep is not (NpcSitSleep.Normal or NpcSitSleep.IsSitting or NpcSitSleep.IsSleeping))
            return NpcActivationRefusal.SitSleepTransition;
        return s.LifeState == NpcLifeState.Reanimate ? NpcActivationRefusal.Reanimate : NpcActivationRefusal.None;
    }
}
