using System;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Interaction;

// Requires: angry-with-player and alias bans reach the pickpocket rules through the snapshot, but no live reader sets them (no engine source), so they stay false until one does; teammate is set by the SetPlayerTeammate native or by a companion roster member (CompanionRoster.IsTeammate); essential-down, knock, mount and rider come from ActorState, which only a caller or the essential damage path sets (no trigger for knock and no riding system exist).
public sealed record NpcSnapshot(
    bool Alive, bool Enabled, bool InCombat, bool Sleeping, Func<bool> HasItems,
    uint? RaceFlags = null, bool AlreadyPickpocketed = false, bool Ghost = false, bool PlayerTeammate = false,
    bool EssentialDown = false, bool CanSpeakToEssentialDown = false, int KnockState = NpcStates.NormalKnock,
    bool IsMount = false, bool HasRider = false, bool AliasForbidsPickpocket = false, bool AngryWithPlayer = false);

public enum NpcRoute { Refused, Loot, Pickpocket, Talk }

public static class NpcInteraction
{
    public const int RaceAllowPickpocketBit = 23;

    public const int RaceAllowDialogueBit = 21;

    public const int GhostBit = 29;

    public static bool IsGhost(uint configurationFlags) => (configurationFlags & 1u << GhostBit) != 0;

    public static bool RaceAllowsPickpocket(uint? raceFlags) => raceFlags is { } flags && (flags & 1u << RaceAllowPickpocketBit) != 0;

    // Invariant: unreadable race flags (null) read as talkable, so an unknown race never loses Talk; only a readable word with the bit clear refuses.
    public static bool RaceAllowsDialogue(uint? raceFlags) => raceFlags is not { } flags || (flags & 1u << RaceAllowDialogueBit) != 0;

    public static NpcLifeState Life(NpcSnapshot s) => !s.Alive ? NpcLifeState.Dead : s.EssentialDown ? NpcLifeState.EssentialDown : NpcLifeState.Alive;

    public static bool IsComposite(NpcSnapshot s) => NpcPromptRules.IsComposite(s.IsMount, s.HasRider);

    public static NpcActivationRefusal Refusal(NpcSnapshot s) =>
        NpcActivationGate.Check(new NpcActivationState(
            Life(s), !s.Alive, s.InCombat, s.KnockState, s.Sleeping ? NpcSitSleep.IsSleeping : NpcSitSleep.Normal, s.CanSpeakToEssentialDown));

    // Invariant: the engine's activation returns before dialogue for a knocked-out actor, after the pickpocket path, so only Talk and CanEngage refuse it.
    public static bool CanEngage(NpcSnapshot s) =>
        s.Alive && s.Enabled && Refusal(s) == NpcActivationRefusal.None && !NpcStates.KnockedOut(s.KnockState) && RaceAllowsDialogue(s.RaceFlags);

    public static PickpocketTarget Target(NpcSnapshot s) =>
        new((int)Life(s), s.KnockState, RaceAllowsPickpocket(s.RaceFlags), s.AliasForbidsPickpocket, s.AngryWithPlayer, s.AlreadyPickpocketed, s.IsMount, s.Ghost, s.PlayerTeammate);

    public static NpcPromptKind Prompt(NpcSnapshot s, bool playerSneaking)
    {
        if (!s.Enabled || (s.Alive && Refusal(s) != NpcActivationRefusal.None)) return NpcPromptKind.None;
        return NpcPromptRules.Choose(new NpcPromptState(
            !s.Alive, !s.Alive && s.HasItems(), s.IsMount, playerSneaking && !s.Ghost && s.RaceFlags is not null, PickpocketRules.AllowsPickpocket(Target(s)), RaceAllowsDialogue(s.RaceFlags), Life(s), s.CanSpeakToEssentialDown, s.PlayerTeammate));
    }

    public static NpcRoute Route(NpcSnapshot s, bool playerSneaking) => Prompt(s, playerSneaking) switch
    {
        NpcPromptKind.Search or NpcPromptKind.SearchEmpty => NpcRoute.Loot,
        NpcPromptKind.Pickpocket => NpcRoute.Pickpocket,
        NpcPromptKind.Talk => NpcStates.KnockedOut(s.KnockState) ? NpcRoute.Refused : NpcRoute.Talk,
        _ => NpcRoute.Refused,
    };
}
