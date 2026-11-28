using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class NpcInteractionRulesTests
{
    static NpcPromptState Prompt(
        bool dead = false, bool items = true, bool mount = false, bool sneaking = false, bool allows = true,
        bool talkable = true, NpcLifeState life = NpcLifeState.Alive, bool canSpeakDown = false, bool teammate = false) =>
        new(dead, items, mount, sneaking, allows, talkable, life, canSpeakDown, teammate);

    static NpcActivationState Gate(
        NpcLifeState life = NpcLifeState.Alive, bool dead = false, bool combat = false, int knock = 0,
        NpcSitSleep sit = NpcSitSleep.Normal, bool canSpeakDown = false) =>
        new(life, dead, combat, knock, sit, canSpeakDown);

    [Theory]
    [InlineData(1, false, false, true)]
    [InlineData(1, true, false, false)]
    [InlineData(1, true, true, true)]
    [InlineData(0, false, false, false)]
    [InlineData(2, false, false, false)]
    public void Sneaking_is_the_sneak_stance_unless_swimming_outside_power_armor(int stance, bool swimming, bool armor, bool expected)
    {
        Assert.Equal(expected, NpcStates.IsSneaking(stance, swimming, armor));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(3, true)]
    [InlineData(8, true)]
    public void Knocked_out_is_a_knock_state_that_is_neither_normal_nor_down(int knock, bool expected)
    {
        Assert.Equal(expected, NpcStates.KnockedOut(knock));
    }

    [Fact]
    public void A_dead_NPC_prompts_search_and_empty_only_without_items_never_steal()
    {
        Assert.Equal(NpcPromptKind.Search, NpcPromptRules.Choose(Prompt(dead: true, items: true)));
        Assert.Equal(NpcPromptKind.SearchEmpty, NpcPromptRules.Choose(Prompt(dead: true, items: false)));
        Assert.Equal(NpcPromptKind.Search, NpcPromptRules.Choose(Prompt(dead: true, items: true, sneaking: true)));
    }

    [Fact]
    public void A_sneaking_player_gets_pickpocket_or_nothing_never_talk()
    {
        Assert.Equal(NpcPromptKind.Pickpocket, NpcPromptRules.Choose(Prompt(sneaking: true, allows: true)));
        Assert.Equal(NpcPromptKind.None, NpcPromptRules.Choose(Prompt(sneaking: true, allows: false)));
    }

    [Fact]
    public void A_standing_player_gets_talk_only_for_a_talkable_NPC()
    {
        Assert.Equal(NpcPromptKind.Talk, NpcPromptRules.Choose(Prompt()));
        Assert.Equal(NpcPromptKind.None, NpcPromptRules.Choose(Prompt(talkable: false)));
    }

    [Theory]
    [InlineData(NpcLifeState.EssentialDown, false, NpcPromptKind.None)]
    [InlineData(NpcLifeState.EssentialDown, true, NpcPromptKind.Talk)]
    [InlineData(NpcLifeState.Bleedout, false, NpcPromptKind.Talk)]
    [InlineData(NpcLifeState.Unconscious, false, NpcPromptKind.Talk)]
    public void Talk_is_withheld_only_in_essential_down_without_the_speak_flag(NpcLifeState life, bool canSpeak, NpcPromptKind expected)
    {
        Assert.Equal(expected, NpcPromptRules.Choose(Prompt(life: life, canSpeakDown: canSpeak)));
    }

    [Fact]
    public void A_living_mount_is_not_modelled_but_a_dead_one_is_searched()
    {
        Assert.Equal(NpcPromptKind.Unmodelled, NpcPromptRules.Choose(Prompt(mount: true)));
        Assert.Equal(NpcPromptKind.Search, NpcPromptRules.Choose(Prompt(mount: true, dead: true)));
        Assert.Equal(NpcPromptKind.SearchEmpty, NpcPromptRules.Choose(Prompt(mount: true, dead: true, items: false)));
    }

    [Fact]
    public void A_player_teammate_gets_talk_even_while_sneaking()
    {
        Assert.Equal(NpcPromptKind.Talk, NpcPromptRules.Choose(Prompt(sneaking: true, teammate: true)));
        Assert.Equal(NpcPromptKind.Talk, NpcPromptRules.Choose(Prompt(sneaking: true, teammate: true, allows: false)));
        Assert.Equal(NpcPromptKind.None, NpcPromptRules.Choose(Prompt(sneaking: true, teammate: true, talkable: false)));
    }

    [Fact]
    public void An_ordinary_NPC_proceeds()
    {
        Assert.Equal(NpcActivationRefusal.None, NpcActivationGate.Check(Gate()));
    }

    [Theory]
    [InlineData(NpcLifeState.Unconscious, false, NpcActivationRefusal.Unconscious)]
    [InlineData(NpcLifeState.Unconscious, true, NpcActivationRefusal.None)]
    [InlineData(NpcLifeState.Reanimate, false, NpcActivationRefusal.Reanimate)]
    [InlineData(NpcLifeState.Bleedout, false, NpcActivationRefusal.None)]
    public void Life_state_refusals(NpcLifeState life, bool dead, NpcActivationRefusal expected)
    {
        Assert.Equal(expected, NpcActivationGate.Check(Gate(life: life, dead: dead)));
    }

    [Theory]
    [InlineData(0, NpcActivationRefusal.InCombat)]
    [InlineData(7, NpcActivationRefusal.InCombat)]
    [InlineData(3, NpcActivationRefusal.None)]
    public void An_NPC_in_combat_refuses_unless_knocked_out(int knock, NpcActivationRefusal expected)
    {
        Assert.Equal(expected, NpcActivationGate.Check(Gate(combat: true, knock: knock)));
    }

    [Fact]
    public void Essential_down_in_combat_is_not_refused_for_combat()
    {
        Assert.Equal(NpcActivationRefusal.None, NpcActivationGate.Check(Gate(NpcLifeState.EssentialDown, combat: true, canSpeakDown: true)));
        Assert.Equal(NpcActivationRefusal.EssentialDown, NpcActivationGate.Check(Gate(NpcLifeState.EssentialDown, combat: true)));
    }

    [Theory]
    [InlineData(NpcSitSleep.Normal, NpcActivationRefusal.None)]
    [InlineData(NpcSitSleep.IsSitting, NpcActivationRefusal.None)]
    [InlineData(NpcSitSleep.IsSleeping, NpcActivationRefusal.None)]
    [InlineData(NpcSitSleep.WantToSit, NpcActivationRefusal.SitSleepTransition)]
    [InlineData(NpcSitSleep.WaitingForSitAnim, NpcActivationRefusal.SitSleepTransition)]
    [InlineData(NpcSitSleep.WantToStand, NpcActivationRefusal.SitSleepTransition)]
    [InlineData(NpcSitSleep.WantToSleep, NpcActivationRefusal.SitSleepTransition)]
    [InlineData(NpcSitSleep.WaitingForSleepAnim, NpcActivationRefusal.SitSleepTransition)]
    [InlineData(NpcSitSleep.WantToWake, NpcActivationRefusal.SitSleepTransition)]
    public void Only_transitional_sit_and_sleep_states_refuse(NpcSitSleep state, NpcActivationRefusal expected)
    {
        Assert.Equal(expected, NpcActivationGate.Check(Gate(sit: state)));
    }
}
