using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterLevelUpMenuTests
{
    static readonly GenericCharacterData Data = new();

    static (LevelUpController Ctrl, LevelUpModel Model) Make(int picks = 2, bool requireAll = false)
    {
        var model = new LevelUpModel(Data.Perks, new Dictionary<string, int>(), picks, Data.PerkRequirements, requireAll);
        return (new LevelUpController(model), model);
    }

    static string[] Ids(LevelUpController ctrl) => ctrl.Perks.Rows.Select(e => e.Def.Id).ToArray();

    static string Hint(LevelUpController ctrl, MenuInputAction action) =>
        ctrl.Hints.Single(h => h.Action == action).Label;

    [Fact]
    public void The_list_shows_available_perks_and_hides_locked_ones_until_asked()
    {
        var (ctrl, _) = Make();

        Assert.Equal(new[] { "p1", "p3" }, Ids(ctrl));

        Assert.True(ctrl.Handle(MenuInputAction.Compare));
        Assert.Equal(new[] { "p1", "p2", "p3" }, Ids(ctrl));
        Assert.Equal("Locked", ctrl.Perks.RowDetail(1));
        Assert.Equal("Hide locked", Hint(ctrl, MenuInputAction.Compare));

        Assert.True(ctrl.Handle(MenuInputAction.Compare));
        Assert.Equal(new[] { "p1", "p3" }, Ids(ctrl));
    }

    [Fact]
    public void Accept_picks_then_unpicks_and_a_pick_unlocks_dependent_perks_in_the_list()
    {
        var (ctrl, model) = Make();
        Assert.Equal("Picks left: 2", ctrl.Header);
        Assert.Equal("Pick", Hint(ctrl, MenuInputAction.Accept));

        Assert.True(ctrl.Handle(MenuInputAction.Accept));
        Assert.Equal("Picked Perk 1.", ctrl.Message);
        Assert.Equal("Picks left: 1", ctrl.Header);
        Assert.Equal(new[] { "p1", "p2", "p3" }, Ids(ctrl));
        Assert.Equal("Picked", ctrl.Perks.RowDetail(0));
        Assert.Equal("Unpick", Hint(ctrl, MenuInputAction.Accept));
        Assert.Single(model.Picks);

        Assert.True(ctrl.Handle(MenuInputAction.Accept));
        Assert.Equal("Removed Perk 1.", ctrl.Message);
        Assert.Equal(new[] { "p1", "p3" }, Ids(ctrl));
        Assert.Empty(model.Picks);
    }

    [Fact]
    public void Picking_with_no_picks_left_or_unmet_requirements_explains_why()
    {
        var (ctrl, model) = Make(picks: 1);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal("No picks left.", ctrl.Message);
        Assert.Single(model.Picks);

        var (locked, _) = Make();
        locked.Handle(MenuInputAction.Compare);
        locked.Handle(MenuInputAction.Down);
        locked.Handle(MenuInputAction.Accept);
        Assert.Equal("Requirements not met.", locked.Message);
    }

    [Fact]
    public void The_detail_lines_compare_current_and_next_rank_text_and_name_unmet_requirements()
    {
        var (ctrl, _) = Make();

        var lines = ctrl.DetailLines;
        Assert.Equal("Perk 1", lines[0].Text);
        Assert.Contains(lines, l => l.Text == "Status: Available");
        Assert.Contains(lines, l => l.Text == "Not owned yet.");
        Assert.Contains(lines, l => l.Text == "Next rank 1: Rank one of perk 1.");

        ctrl.Handle(MenuInputAction.Compare);
        ctrl.Handle(MenuInputAction.Down);
        Assert.Contains(ctrl.DetailLines, l => l.Tone == LineTone.Bad && l.Text == "Requires: own:p1");
    }

    [Fact]
    public void Owned_ranks_show_current_and_next_text_together()
    {
        var model = new LevelUpModel(Data.Perks, new Dictionary<string, int> { ["p1"] = 1 }, 1, Data.PerkRequirements);
        var ctrl = new LevelUpController(model);

        Assert.Contains(ctrl.DetailLines, l => l.Text == "Current rank 1: Rank one of perk 1.");
        Assert.Contains(ctrl.DetailLines, l => l.Text == "Next rank 2: Rank two of perk 1.");
        Assert.Equal("Rank 2/2", ctrl.Perks.RowDetail(0));
    }

    [Fact]
    public void Confirm_waits_for_every_pick_when_required_then_finishes_with_the_picks()
    {
        var (ctrl, _) = Make(requireAll: true);
        var finished = new List<bool>();
        ctrl.Finished += finished.Add;

        Assert.True(ctrl.Handle(MenuInputAction.Sort));
        Assert.Equal("Use all 2 remaining picks first.", ctrl.Message);
        Assert.False(ctrl.IsFinished);

        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal("Picked Perk 2.", ctrl.Message);

        Assert.True(ctrl.Handle(MenuInputAction.Sort));
        Assert.True(ctrl.IsFinished);
        Assert.Equal(new[] { true }, finished.ToArray());
        Assert.Equal(new[] { "p1", "p2" }, ctrl.ConfirmedPicks.Select(p => p.PerkId).ToArray());
        Assert.False(ctrl.Handle(MenuInputAction.Accept));
    }

    [Fact]
    public void Back_clears_the_search_first_and_then_cancels_the_level_up()
    {
        var (ctrl, model) = Make();
        var finished = new List<bool>();
        ctrl.Finished += finished.Add;
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Perks.SetSearch("zzz");

        Assert.True(ctrl.Handle(MenuInputAction.Back));
        Assert.Equal(string.Empty, ctrl.Perks.SearchText);
        Assert.False(ctrl.IsFinished);

        Assert.True(ctrl.Handle(MenuInputAction.Back));
        Assert.True(ctrl.IsFinished);
        Assert.True(model.IsCancelled);
        Assert.Empty(model.Picks);
        Assert.Equal(new[] { false }, finished.ToArray());
        Assert.Equal("Cancel", Hint(Make().Ctrl, MenuInputAction.Back));
    }

    [Fact]
    public void While_searching_confirm_and_toggle_actions_belong_to_the_search_box()
    {
        var (ctrl, _) = Make();

        Assert.True(ctrl.Handle(MenuInputAction.Search));
        Assert.True(ctrl.Perks.IsSearching);
        Assert.True(ctrl.Handle(MenuInputAction.Sort));
        Assert.False(ctrl.IsFinished);
        Assert.DoesNotContain(ctrl.Hints, h => h.Label == "Confirm");

        ctrl.Perks.SetSearch("3");
        Assert.Equal(new[] { "p3" }, Ids(ctrl));
    }

    [Fact]
    public void The_hint_bar_offers_confirm_and_the_locked_toggle()
    {
        var (ctrl, _) = Make();

        Assert.Equal("Confirm", Hint(ctrl, MenuInputAction.Sort));
        Assert.Equal("Show locked", Hint(ctrl, MenuInputAction.Compare));
        Assert.Equal("Cancel", Hint(ctrl, MenuInputAction.Back));
    }

    [Fact]
    public void The_controller_raises_changed_when_the_list_or_the_messages_change()
    {
        var (ctrl, _) = Make();
        var count = 0;
        ctrl.Changed += () => count++;

        ctrl.Handle(MenuInputAction.Down);
        var afterMove = count;
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(1, afterMove);
        Assert.Equal(3, count);
    }
}
