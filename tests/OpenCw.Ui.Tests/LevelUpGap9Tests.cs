using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class LevelUpGap9Tests
{
    static readonly PerkDef[] Perks =
    {
        new("a", "Alpha", "Group A", new[] { new PerkRank("alpha one") }),
        new("b", "Bravo", "Group B", new[] { new PerkRank("bravo one", new[] { "own:a" }) }),
        new("c", "Charlie", "Group C", new[] { new PerkRank("charlie one", new[] { "own:a" }) }),
    };

    static LevelUpController Make() =>
        new(new LevelUpModel(Perks, new Dictionary<string, int>(), 2, new OwnedPerkRequirementChecker()));

    static string[] Ids(LevelUpController ctrl) => ctrl.Perks.Rows.Select(e => e.Def.Id).ToArray();

    [Fact]
    public void Toggling_the_locked_perks_flips_the_message_the_hint_and_the_rows_each_time()
    {
        var ctrl = Make();
        Assert.Equal(string.Empty, ctrl.Message);
        Assert.Equal(new[] { "a" }, Ids(ctrl));

        ctrl.ToggleLocked();
        Assert.Equal("Showing locked perks.", ctrl.Message);
        Assert.Equal(new[] { "a", "b", "c" }, Ids(ctrl));
        Assert.Equal("Hide locked", ctrl.Hints.Single(h => h.Action == MenuInputAction.Compare).Label);

        ctrl.ToggleLocked();
        Assert.Equal("Hiding locked perks.", ctrl.Message);
        Assert.Equal(new[] { "a" }, Ids(ctrl));
        Assert.Equal("Show locked", ctrl.Hints.Single(h => h.Action == MenuInputAction.Compare).Label);

        ctrl.ToggleLocked();
        Assert.Equal("Showing locked perks.", ctrl.Message);
    }

    [Fact]
    public void Toggling_the_locked_perks_raises_changed()
    {
        var ctrl = Make();
        var raised = 0;
        ctrl.Changed += () => raised++;

        ctrl.ToggleLocked();

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Toggling_works_through_the_compare_action_and_is_ignored_once_finished()
    {
        var ctrl = Make();

        Assert.True(ctrl.Handle(MenuInputAction.Compare));
        Assert.Equal("Showing locked perks.", ctrl.Message);

        ctrl.Cancel();
        Assert.False(ctrl.Handle(MenuInputAction.Compare));
        Assert.Equal("Showing locked perks.", ctrl.Message);
    }

    [Fact]
    public void The_category_tabs_are_built_from_the_perks_currently_listed()
    {
        var ctrl = Make();
        Assert.Equal(new[] { "All", "Group A", "Favourites" }, ctrl.Perks.Tabs.ToArray());

        ctrl.ToggleLocked();
        Assert.Equal(new[] { "All", "Group A", "Group B", "Group C", "Favourites" }, ctrl.Perks.Tabs.ToArray());

        ctrl.ToggleLocked();
        Assert.Equal(new[] { "All", "Group A", "Favourites" }, ctrl.Perks.Tabs.ToArray());
    }

    [Fact]
    public void Picking_a_perk_adds_the_categories_it_unlocks()
    {
        var ctrl = Make();

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "a", "b", "c" }, Ids(ctrl));
        Assert.Equal(new[] { "All", "Group A", "Group B", "Group C", "Favourites" }, ctrl.Perks.Tabs.ToArray());
        Assert.Equal("Picked Alpha.", ctrl.Message);
    }
}
