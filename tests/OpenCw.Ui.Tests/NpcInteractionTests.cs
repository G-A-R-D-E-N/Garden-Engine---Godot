using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class NpcInteractionTests
{
    const uint AllowPickpocket = 1u << 23;
    const uint AllowDialogue = 1u << 21;

    static NpcSnapshot Live(bool combat = false, bool sleeping = false, uint? race = AllowPickpocket | AllowDialogue, bool caught = false, bool enabled = true) =>
        new(true, enabled, combat, sleeping, () => true, race, caught);

    static NpcSnapshot Dead(bool items = true, bool combat = false, bool enabled = true) =>
        new(false, enabled, combat, false, () => items, null, false);

    sealed class Actors(ActorSetup? setup) : IActorRecords
    {
        public ActorSetup? Read(string refKey, string baseKey) => setup;
    }

    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u << 23, true)]
    [InlineData(1u << 21, false)]
    [InlineData(uint.MaxValue, true)]
    public void Only_the_race_allow_pickpocket_bit_allows_it(uint flags, bool expected)
    {
        Assert.Equal(expected, NpcInteraction.RaceAllowsPickpocket(flags));
    }

    [Fact]
    public void An_unknown_race_never_allows_pickpocket()
    {
        Assert.False(NpcInteraction.RaceAllowsPickpocket(null));
    }

    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u << 21, true)]
    [InlineData(1u << 23, false)]
    [InlineData(1u << 20, false)]
    [InlineData(1u << 22, false)]
    [InlineData(uint.MaxValue, true)]
    public void Only_the_race_allow_pc_dialogue_bit_allows_talk(uint flags, bool expected)
    {
        Assert.Equal(expected, NpcInteraction.RaceAllowsDialogue(flags));
    }

    [Fact]
    public void An_unreadable_race_still_allows_talk()
    {
        Assert.True(NpcInteraction.RaceAllowsDialogue(null));
    }

    [Fact]
    public void A_race_without_allow_pc_dialogue_refuses_talk_for_a_standing_player()
    {
        var creature = Live(race: AllowPickpocket);
        Assert.False(NpcInteraction.CanEngage(creature));
        Assert.Equal(NpcPromptKind.None, NpcInteraction.Prompt(creature, false));
        Assert.Equal(NpcRoute.Refused, NpcInteraction.Route(creature, false));
    }

    [Fact]
    public void The_dialogue_bit_does_not_change_the_sneaking_pickpocket_route()
    {
        Assert.Equal(NpcRoute.Pickpocket, NpcInteraction.Route(Live(race: AllowPickpocket), true));
        Assert.Equal(NpcRoute.Pickpocket, NpcInteraction.Route(Live(race: AllowPickpocket | AllowDialogue), true));
        Assert.Equal(NpcRoute.Refused, NpcInteraction.Route(Live(race: AllowDialogue), true));
    }

    [Fact]
    public void A_living_enabled_actor_not_in_combat_can_be_engaged()
    {
        Assert.True(NpcInteraction.CanEngage(Live()));
        Assert.Equal(NpcActivationRefusal.None, NpcInteraction.Refusal(Live()));
    }

    [Fact]
    public void An_actor_in_combat_refuses_talk_and_shows_no_prompt()
    {
        var snapshot = Live(combat: true);
        Assert.False(NpcInteraction.CanEngage(snapshot));
        Assert.Equal(NpcActivationRefusal.InCombat, NpcInteraction.Refusal(snapshot));
        Assert.Equal(NpcPromptKind.None, NpcInteraction.Prompt(snapshot, false));
        Assert.Equal(NpcPromptKind.None, NpcInteraction.Prompt(snapshot, true));
        Assert.Equal(NpcRoute.Refused, NpcInteraction.Route(snapshot, true));
    }

    [Fact]
    public void A_sleeping_actor_is_not_refused_because_sleeping_passes_the_gate()
    {
        Assert.True(NpcInteraction.CanEngage(Live(sleeping: true)));
        Assert.Equal(NpcRoute.Talk, NpcInteraction.Route(Live(sleeping: true), false));
    }

    [Fact]
    public void A_dead_or_disabled_actor_cannot_be_engaged()
    {
        Assert.False(NpcInteraction.CanEngage(Dead()));
        Assert.False(NpcInteraction.CanEngage(Live(enabled: false)));
        Assert.Equal(NpcPromptKind.None, NpcInteraction.Prompt(Live(enabled: false), false));
        Assert.Equal(NpcPromptKind.None, NpcInteraction.Prompt(Dead(enabled: false), false));
    }

    [Fact]
    public void A_dead_actor_prompts_search_or_empty_and_routes_to_loot_even_in_combat()
    {
        Assert.Equal(NpcPromptKind.Search, NpcInteraction.Prompt(Dead(items: true), false));
        Assert.Equal(NpcPromptKind.SearchEmpty, NpcInteraction.Prompt(Dead(items: false), false));
        Assert.Equal(NpcPromptKind.Search, NpcInteraction.Prompt(Dead(items: true), true));
        Assert.Equal(NpcRoute.Loot, NpcInteraction.Route(Dead(combat: true), true));
    }

    [Fact]
    public void A_standing_player_talks_to_a_living_actor()
    {
        Assert.Equal(NpcPromptKind.Talk, NpcInteraction.Prompt(Live(race: null), false));
        Assert.Equal(NpcRoute.Talk, NpcInteraction.Route(Live(race: null), false));
    }

    [Fact]
    public void A_sneaking_player_pickpockets_only_when_the_race_allows_and_never_falls_back_to_talk()
    {
        Assert.Equal(NpcPromptKind.Pickpocket, NpcInteraction.Prompt(Live(), true));
        Assert.Equal(NpcRoute.Pickpocket, NpcInteraction.Route(Live(), true));
        Assert.Equal(NpcRoute.Refused, NpcInteraction.Route(Live(race: 0), true));
        Assert.Equal(NpcPromptKind.None, NpcInteraction.Prompt(Live(race: 0), true));
    }

    [Fact]
    public void A_sneaking_player_talks_when_the_race_flags_are_unreadable_and_is_never_offered_pickpocket()
    {
        Assert.Equal(NpcPromptKind.Talk, NpcInteraction.Prompt(Live(race: null), true));
        Assert.Equal(NpcRoute.Talk, NpcInteraction.Route(Live(race: null), true));
    }

    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u << 29, true)]
    [InlineData(1u << 28, false)]
    [InlineData(1u << 30, false)]
    [InlineData(uint.MaxValue, true)]
    public void Only_the_acbs_is_ghost_bit_marks_a_ghost(uint flags, bool expected)
    {
        Assert.Equal(expected, NpcInteraction.IsGhost(flags));
    }

    [Fact]
    public void A_sneaking_player_talks_to_a_ghost_and_is_never_offered_pickpocket()
    {
        var ghost = Live() with { Ghost = true };
        Assert.Equal(NpcPromptKind.Talk, NpcInteraction.Prompt(ghost, true));
        Assert.Equal(NpcRoute.Talk, NpcInteraction.Route(ghost, true));
        Assert.True(NpcInteraction.Target(ghost).IsGhost);
        Assert.Equal(PickpocketOffer.NotOffered, PickpocketRules.Offer(NpcInteraction.Target(ghost), true));
        Assert.Equal(NpcRoute.Pickpocket, NpcInteraction.Route(Live(), true));
        Assert.Equal(PickpocketOffer.Open, PickpocketRules.Offer(NpcInteraction.Target(Live()), true));
    }

    [Fact]
    public void An_alias_ban_or_an_angry_target_in_the_snapshot_refuses_the_pickpocket_offer()
    {
        Assert.Equal(PickpocketOffer.NotOffered, PickpocketRules.Offer(NpcInteraction.Target(Live() with { AliasForbidsPickpocket = true }), true));
        Assert.Equal(PickpocketOffer.AlreadyCaught, PickpocketRules.Offer(NpcInteraction.Target(Live() with { AngryWithPlayer = true }), true));
        Assert.Equal(PickpocketOffer.Open, PickpocketRules.Offer(NpcInteraction.Target(Live()), true));
    }

    [Fact]
    public void A_sneaking_player_talks_to_a_teammate_and_is_never_offered_pickpocket()
    {
        var teammate = Live() with { PlayerTeammate = true };
        Assert.Equal(NpcPromptKind.Talk, NpcInteraction.Prompt(teammate, true));
        Assert.Equal(NpcRoute.Talk, NpcInteraction.Route(teammate, true));
        Assert.True(NpcInteraction.Target(teammate).IsPlayerTeammate);
        Assert.Equal(PickpocketOffer.NotOffered, PickpocketRules.Offer(NpcInteraction.Target(teammate), true));
        Assert.False(NpcInteraction.Target(Live()).IsPlayerTeammate);
        Assert.Equal(NpcRoute.Pickpocket, NpcInteraction.Route(Live(), true));
    }

    [Fact]
    public void A_teammate_still_talks_when_the_player_stands_and_a_dead_one_is_still_searched()
    {
        Assert.Equal(NpcRoute.Talk, NpcInteraction.Route(Live() with { PlayerTeammate = true }, false));
        Assert.Equal(NpcRoute.Loot, NpcInteraction.Route(Dead() with { PlayerTeammate = true }, true));
    }

    [Fact]
    public void A_teammate_in_combat_is_still_refused()
    {
        Assert.Equal(NpcRoute.Refused, NpcInteraction.Route(Live(combat: true) with { PlayerTeammate = true }, true));
    }

    [Fact]
    public void A_ghost_in_combat_is_still_refused()
    {
        Assert.Equal(NpcRoute.Refused, NpcInteraction.Route(Live(combat: true) with { Ghost = true }, true));
    }

    [Fact]
    public void Engaging_never_reads_a_corpse_inventory()
    {
        var corpse = new NpcSnapshot(false, true, false, false, () => throw new InvalidOperationException());
        Assert.False(NpcInteraction.CanEngage(corpse));
    }

    [Fact]
    public void The_pickpocket_target_carries_life_race_and_the_already_caught_mark()
    {
        var open = NpcInteraction.Target(Live());
        Assert.Equal(NpcStates.NormalKnock, open.KnockState);
        Assert.True(open.RaceAllows);
        Assert.False(open.AlreadyCaught);
        Assert.Equal(PickpocketOffer.Open, PickpocketRules.Offer(open, true));
        Assert.Equal(PickpocketOffer.AlreadyCaught, PickpocketRules.Offer(NpcInteraction.Target(Live(caught: true)), true));
        Assert.Equal(PickpocketRules.DeadLifeState, NpcInteraction.Target(Dead()).LifeState);
        Assert.False(PickpocketRules.AllowsPickpocket(NpcInteraction.Target(Dead())));
    }

    [Fact]
    public void An_already_caught_actor_still_routes_to_pickpocket_so_the_refusal_text_shows()
    {
        Assert.Equal(NpcRoute.Pickpocket, NpcInteraction.Route(Live(caught: true), true));
    }

    [Fact]
    public void A_frozen_ledger_entry_decides_whether_a_corpse_has_items()
    {
        var ledger = new ContainerLedger();
        var records = new Actors(new ActorSetup("Vic", false, [new("00aa:a.esm", 2)]));
        Assert.True(ActorLoot.HasItems(records, ledger, "ref", "base"));
        ledger.Freeze("ref", new List<InventoryStack>());
        Assert.False(ActorLoot.HasItems(records, ledger, "ref", "base"));
    }

    [Fact]
    public void The_base_record_decides_when_nothing_is_frozen_and_unreadable_or_partial_counts_as_items()
    {
        var empty = new ActorSetup("Vic", false, []);
        Assert.False(ActorLoot.HasItems(new Actors(empty), new ContainerLedger(), "ref", "base"));
        Assert.False(ActorLoot.HasItems(new Actors(new ActorSetup("Vic", false, [new("00aa:a.esm", 0)])), new ContainerLedger(), "ref", "base"));
        Assert.True(ActorLoot.HasItems(new Actors(null), new ContainerLedger(), "ref", "base"));
        Assert.True(ActorLoot.HasItems(new Actors(empty with { Complete = false }), new ContainerLedger(), "ref", "base"));
    }

    [Fact]
    public void A_downed_essential_actor_refuses_talk_until_the_speak_flag_is_set()
    {
        var down = Live() with { EssentialDown = true };
        Assert.Equal(NpcLifeState.EssentialDown, NpcInteraction.Life(down));
        Assert.Equal(NpcActivationRefusal.EssentialDown, NpcInteraction.Refusal(down));
        Assert.False(NpcInteraction.CanEngage(down));
        Assert.Equal(NpcPromptKind.None, NpcInteraction.Prompt(down, false));
        Assert.Equal(NpcRoute.Refused, NpcInteraction.Route(down, false));

        var talking = down with { CanSpeakToEssentialDown = true };
        Assert.Equal(NpcActivationRefusal.None, NpcInteraction.Refusal(talking));
        Assert.True(NpcInteraction.CanEngage(talking));
        Assert.Equal(NpcRoute.Talk, NpcInteraction.Route(talking, false));
    }

    [Fact]
    public void A_downed_essential_actor_in_combat_is_refused_only_for_being_down()
    {
        var down = Live(combat: true) with { EssentialDown = true };
        Assert.Equal(NpcActivationRefusal.EssentialDown, NpcInteraction.Refusal(down));
        Assert.Equal(NpcActivationRefusal.None, NpcInteraction.Refusal(down with { CanSpeakToEssentialDown = true }));
    }

    [Theory]
    [InlineData(NpcStates.NormalKnock, false)]
    [InlineData(NpcStates.DownKnock, false)]
    [InlineData(3, true)]
    [InlineData(6, true)]
    public void A_knocked_out_actor_in_combat_is_not_refused_but_cannot_be_talked_to(int knock, bool knockedOut)
    {
        var fighting = Live(combat: true) with { KnockState = knock };
        Assert.Equal(knockedOut ? NpcActivationRefusal.None : NpcActivationRefusal.InCombat, NpcInteraction.Refusal(fighting));

        var calm = Live() with { KnockState = knock };
        Assert.Equal(!knockedOut, NpcInteraction.CanEngage(calm));
        Assert.Equal(knockedOut ? NpcRoute.Refused : NpcRoute.Talk, NpcInteraction.Route(calm, false));
        Assert.Equal(NpcPromptKind.Talk, NpcInteraction.Prompt(calm, false));
    }

    [Fact]
    public void A_knocked_out_actor_can_still_be_pickpocketed_and_an_already_caught_one_opens_again()
    {
        var caught = Live(caught: true) with { KnockState = 3 };
        Assert.Equal(NpcRoute.Pickpocket, NpcInteraction.Route(caught, true));
        Assert.Equal(PickpocketOffer.Open, PickpocketRules.Offer(NpcInteraction.Target(caught), true));
        Assert.Equal(PickpocketOffer.AlreadyCaught, PickpocketRules.Offer(NpcInteraction.Target(Live(caught: true)), true));
    }

    [Fact]
    public void A_living_mount_has_the_unmodelled_prompt_and_no_pickpocket_or_talk()
    {
        var mount = Live() with { IsMount = true };
        Assert.True(NpcInteraction.Target(mount).IsMount);
        Assert.Equal(NpcPromptKind.Unmodelled, NpcInteraction.Prompt(mount, false));
        Assert.Equal(NpcRoute.Refused, NpcInteraction.Route(mount, true));
        Assert.Equal(PickpocketOffer.NotOffered, PickpocketRules.Offer(NpcInteraction.Target(mount), true));
    }

    [Fact]
    public void A_dead_mount_is_still_searched_because_the_dead_branch_comes_first()
    {
        var dead = Dead() with { IsMount = true };
        Assert.Equal(NpcRoute.Loot, NpcInteraction.Route(dead, false));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void The_composite_text_needs_a_rider_and_no_mount_flag(bool mount, bool rider, bool expected)
    {
        Assert.Equal(expected, NpcInteraction.IsComposite(Live() with { IsMount = mount, HasRider = rider }));
    }
}
