using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CompanionActivationTests
{
    const string Ann = "00AA:A.esm";
    const string Bob = "00BB:A.esm";

    static ActivationTarget Npc(string key, bool inActors = true) =>
        new(key, "0A01:A.esm", "NPC_", new HashSet<string>(StringComparer.Ordinal) { inActors ? "actors" : "furniture" });

    static (ActivationDispatcher Dispatcher, List<ActivationTarget> Opened) Setup(CompanionRoster roster, NpcRoute route = NpcRoute.Talk, bool withMenu = true)
    {
        var handler = new CompanionActivation(roster, _ => route);
        var opened = new List<ActivationTarget>();
        if (withMenu) handler.Open = opened.Add;
        var dispatcher = new ActivationDispatcher();
        dispatcher.Register(handler);
        return (dispatcher, opened);
    }

    [Fact]
    public void A_companion_talk_opens_the_menu_and_is_handled()
    {
        var roster = new CompanionRoster();
        roster.Recruit(Ann, "0A01:A.esm");
        var (dispatcher, opened) = Setup(roster);

        var result = dispatcher.Dispatch(Npc(Ann));

        Assert.Equal(ActivationStatus.Handled, result.Status);
        Assert.Equal("companion menu", result.Detail);
        Assert.Single(opened);
    }

    [Fact]
    public void Without_a_menu_sink_a_companion_talk_is_not_swallowed()
    {
        var roster = new CompanionRoster();
        roster.Recruit(Ann, "0A01:A.esm");
        var (dispatcher, opened) = Setup(roster, withMenu: false);

        Assert.Equal(ActivationStatus.Unhandled, dispatcher.Dispatch(Npc(Ann)).Status);
        Assert.Empty(opened);
    }

    [Fact]
    public void A_non_companion_is_untouched()
    {
        var roster = new CompanionRoster();
        roster.Recruit(Ann, "0A01:A.esm");
        var (dispatcher, opened) = Setup(roster);

        Assert.Equal(ActivationStatus.Unhandled, dispatcher.Dispatch(Npc(Bob)).Status);
        Assert.Empty(opened);
    }

    [Fact]
    public void A_companion_whose_route_is_not_talk_is_not_handled()
    {
        var roster = new CompanionRoster();
        roster.Recruit(Ann, "0A01:A.esm");
        var (dispatcher, opened) = Setup(roster, NpcRoute.Refused);

        Assert.Equal(ActivationStatus.Unhandled, dispatcher.Dispatch(Npc(Ann)).Status);
        Assert.Empty(opened);
    }

    [Fact]
    public void A_dismissed_companion_is_not_handled()
    {
        var roster = new CompanionRoster();
        roster.Recruit(Ann, "0A01:A.esm");
        roster.Dismiss(Ann);
        var (dispatcher, opened) = Setup(roster);

        Assert.Equal(ActivationStatus.Unhandled, dispatcher.Dispatch(Npc(Ann)).Status);
        Assert.Empty(opened);
    }

    [Fact]
    public void A_companion_outside_the_actors_group_is_not_handled()
    {
        var roster = new CompanionRoster();
        roster.Recruit(Ann, "0A01:A.esm");
        var (dispatcher, opened) = Setup(roster);

        Assert.Equal(ActivationStatus.Unhandled, dispatcher.Dispatch(Npc(Ann, inActors: false)).Status);
        Assert.Empty(opened);
    }

    [Fact]
    public void A_non_companion_is_handled_only_when_the_ask_rule_allows_it()
    {
        var roster = new CompanionRoster();
        var handler = new CompanionActivation(roster, _ => NpcRoute.Talk, target => target.RefKey == Bob) { Open = _ => { } };

        Assert.True(handler.Handles(Npc(Bob)));
        Assert.False(handler.Handles(Npc(Ann)));
        Assert.False(handler.Handles(Npc(Bob, inActors: false)));
    }

    [Fact]
    public void A_recruitable_non_companion_with_a_pickpocket_route_is_not_handled()
    {
        var handler = new CompanionActivation(new CompanionRoster(), _ => NpcRoute.Pickpocket, _ => true) { Open = _ => { } };

        Assert.False(handler.Handles(Npc(Bob)));
    }
}
