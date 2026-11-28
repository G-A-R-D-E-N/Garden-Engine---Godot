using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlayerPerkStatusTests
{
    static IReadOnlyList<OwnedPerk> Owned(params (string Key, int Rank)[] ranks) =>
        PlayerPerkRows.FromRanks(ranks.ToDictionary(pair => pair.Key, pair => pair.Rank));

    static PlayerStatusModel Model(IReadOnlyList<OwnedPerk> perks) =>
        new(new LivePlayerStatus(perks), () => [], true, perks.Count > 0);

    [Fact]
    public void Owned_ranks_become_rows_in_key_order_and_unheld_ranks_are_dropped()
    {
        var perks = Owned(("000b:a.esm", 2), ("000a:a.esm", 1), ("000c:a.esm", 0));

        Assert.Equal(new[] { "000a:a.esm", "000b:a.esm" }, perks.Select(perk => perk.Id));
        Assert.Equal(new[] { 1, 2 }, perks.Select(perk => perk.Rank));
        Assert.All(perks, perk => Assert.Equal(perk.Id, perk.Name));
        Assert.Empty(PlayerPerkRows.FromRanks(new Dictionary<string, int>()));
    }

    [Fact]
    public void Record_names_and_rank_text_replace_the_provisional_key_name()
    {
        var reader = new FakeRecordReader();
        RecordPlayerStatusTests.PerkOf(reader.Add("000002:a.esm", "PERK", "Gunslinger01", "Gunslinger"), "More damage.", null);

        var described = new RecordPlayerStatusSource(new LivePlayerStatus(Owned(("000002:a.esm", 1), ("000009:a.esm", 1))), reader).Perks;

        Assert.Equal("Gunslinger", described.Single(perk => perk.Id == "000002:a.esm").Name);
        Assert.Equal(["More damage."], described.Single(perk => perk.Id == "000002:a.esm").RankDescriptions);
        Assert.Equal("000009:a.esm", described.Single(perk => perk.Id == "000009:a.esm").Name);
        Assert.Equal(new[] { "000002:a.esm" }, PlayerPerkRows.Named(described).Select(perk => perk.Id));
    }

    [Fact]
    public void A_perk_with_a_blank_name_is_dropped_with_the_unresolved_ones()
    {
        var perks = new[] { new OwnedPerk("000001:a.esm", "", 1, []), new OwnedPerk("000002:a.esm", "  ", 1, []), new OwnedPerk("000003:a.esm", "Aquaboy", 2, []) };

        Assert.Equal(new[] { "Aquaboy" }, PlayerPerkRows.Named(perks).Select(perk => perk.Name));
    }

    [Fact]
    public void A_status_and_perks_model_offers_two_sub_tabs_and_position_one_opens_perks()
    {
        var model = Model(Owned(("000a:a.esm", 1)));

        Assert.Equal(new[] { "STATUS", "PERKS" }, model.SubTabLabels);
        Assert.Equal(0, model.SubTabIndex);

        model.SetSubTab(1);

        Assert.Equal(StatusSubTab.Perks, model.SubTab);
        Assert.Equal(1, model.SubTabIndex);
        Assert.Equal(new[] { "000a:a.esm" }, model.View.Visible.Select(row => row.Label));
        Assert.Equal(string.Empty, model.EmptyMessage);

        model.SetSubTab(0);

        Assert.Equal(StatusSubTab.Status, model.SubTab);
        Assert.Equal(0, model.SubTabIndex);
    }

    [Fact]
    public void Cycling_the_list_view_tab_walks_status_then_perks_and_wraps_without_reaching_special()
    {
        var view = new StatusListView(Model(Owned(("000a:a.esm", 1))));

        view.CycleTab(1);
        Assert.Equal(StatusSubTab.Perks, view.Status.SubTab);
        Assert.Equal(1, view.TabIndex);

        view.CycleTab(1);
        Assert.Equal(StatusSubTab.Status, view.Status.SubTab);
        Assert.Equal(0, view.TabIndex);
    }

    [Fact]
    public void A_model_without_special_refuses_the_special_sub_tab_and_a_special_search_hit()
    {
        var model = Model(Owned(("000a:a.esm", 1)));

        model.SetSubTab(StatusSubTab.Special);
        Assert.Equal(StatusSubTab.Status, model.SubTab);

        Assert.False(model.Select("special:Strength"));
        Assert.Equal(StatusSubTab.Status, model.SubTab);
    }

    [Fact]
    public void Selecting_a_perk_hit_opens_the_perks_sub_tab()
    {
        var model = Model(Owned(("000a:a.esm", 1), ("000b:a.esm", 1)));

        Assert.True(model.Select("perk:000b:a.esm"));

        Assert.Equal(StatusSubTab.Perks, model.SubTab);
        Assert.Equal("perk:000b:a.esm", model.CurrentKey);
    }

    [Fact]
    public void Without_perks_the_status_only_model_keeps_a_single_sub_tab()
    {
        var model = Model([]);

        Assert.Equal(new[] { "STATUS" }, model.SubTabLabels);
        model.SetSubTab(1);
        Assert.Equal(StatusSubTab.Status, model.SubTab);
        Assert.Equal(0, model.SubTabIndex);
    }

    [Fact]
    public void The_full_model_keeps_its_three_sub_tabs_in_enum_order()
    {
        var model = new PlayerStatusModel(new LivePlayerStatus());

        Assert.Equal(new[] { "STATUS", "SPECIAL", "PERKS" }, model.SubTabLabels);
        model.SetSubTab(1);
        Assert.Equal(StatusSubTab.Special, model.SubTab);
        Assert.Equal(1, model.SubTabIndex);
        model.SetSubTab(2);
        Assert.Equal(StatusSubTab.Perks, model.SubTab);
        Assert.Equal(2, model.SubTabIndex);
    }
}
