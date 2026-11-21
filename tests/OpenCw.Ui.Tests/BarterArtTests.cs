using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Swf;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class BarterArtTests
{
    private const string M = "FilterHolder_mc/Menu_mc/";

    private static SwfBindings Build(BarterController ctrl, BarterControllerTests.FakeRules rules) => BarterArt.Build(ctrl, rules);

    [Fact]
    public void An_untouched_menu_binds_the_exact_caps_header_and_transfer_texts()
    {
        var rules = new BarterControllerTests.FakeRules();
        var b = Build(new BarterController(rules), rules);

        Assert.Equal(
            new[]
            {
                (M + "CapsTransferInfo_mc/TransferCaps_tf", "0"),
                (M + "ContainerInventory_mc/ContainerListHeader/textField", "VENDOR VIC"),
                (M + "ContainerInventory_mc/VendorCaps_tf", "100"),
                (M + "PlayerInventory_mc/PlayerCaps_tf", "100"),
                (M + "PlayerInventory_mc/PlayerListHeader/textField", "Your stuff"),
            },
            b.Text.OrderBy(pair => pair.Key, System.StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value)).ToArray());
    }

    [Fact]
    public void A_transfer_of_int_min_value_caps_shows_its_magnitude_instead_of_throwing()
    {
        var rules = new BarterControllerTests.FakeRules { NetOverride = int.MinValue };
        var ctrl = new BarterController(rules);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("2147483648", Build(ctrl, rules).Text[M + "CapsTransferInfo_mc/TransferCaps_tf"]);
    }

    [Fact]
    public void The_lists_show_counts_only_above_one_and_mark_the_selected_row()
    {
        var rules = new BarterControllerTests.FakeRules();
        var b = Build(new BarterController(rules), rules);

        Assert.Equal(
            new[] { new SwfListRow("Pipe (2)", true), new SwfListRow("Letter", false) },
            b.Lists[M + "PlayerInventory_mc/PlayerListHeader"]);
        Assert.Equal(new[] { new SwfListRow("Rifle", false) }, b.Lists[M + "ContainerInventory_mc/ContainerListHeader"]);
    }

    [Fact]
    public void Switching_to_the_vendor_moves_the_selection_and_staged_stacks_keep_their_full_count()
    {
        var rules = new BarterControllerTests.FakeRules();
        var ctrl = new BarterController(rules);
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Right);

        var b = Build(ctrl, rules);

        Assert.Equal(
            new[] { new SwfListRow("Pipe (2)", false), new SwfListRow("Letter", false) },
            b.Lists[M + "PlayerInventory_mc/PlayerListHeader"]);
        Assert.Equal(new[] { new SwfListRow("Rifle", true) }, b.Lists[M + "ContainerInventory_mc/ContainerListHeader"]);
        Assert.Equal("10", b.Text[M + "CapsTransferInfo_mc/TransferCaps_tf"]);
    }

    [Fact]
    public void The_transfer_caps_text_is_the_absolute_value_of_the_quote()
    {
        var rules = new BarterControllerTests.FakeRules();
        var ctrl = new BarterController(rules);
        ctrl.Handle(MenuInputAction.Right);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(-30, ctrl.Quote!.PlayerNet);
        Assert.Equal("30", Build(ctrl, rules).Text[M + "CapsTransferInfo_mc/TransferCaps_tf"]);
    }

    [Fact]
    public void The_hidden_set_for_short_lists_is_exactly_the_template_furniture_and_both_scroll_chevrons()
    {
        var rules = new BarterControllerTests.FakeRules();
        var b = Build(new BarterController(rules), rules);

        Assert.Equal(
            new[]
            {
                "ContainerInventory_mc/ContainerBracketBackground_mc",
                "ContainerInventory_mc/ContainerSwitchButton_tf",
                "ContainerInventory_mc/LeftHitBox_tf",
                "ContainerInventory_mc/RightHitBox_tf",
                "ContainerList_mc/ScrollDown",
                "ContainerList_mc/ScrollUp",
                "ContainerList_mc/border",
                "PlayerInventory_mc/LeftHitBox_tf",
                "PlayerInventory_mc/PlayerBracketBackground_mc",
                "PlayerInventory_mc/PlayerList_mc/ScrollDown",
                "PlayerInventory_mc/PlayerList_mc/ScrollUp",
                "PlayerInventory_mc/PlayerList_mc/border",
                "PlayerInventory_mc/PlayerSwitchButton_tf",
                "PlayerInventory_mc/RightHitBox_tf",
                "QuantityMenu_mc",
            },
            b.Hide.Select(path => path[M.Length..]).OrderBy(path => path, System.StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { M + "ContainerInventory_mc/ContainerListHeader", M + "PlayerInventory_mc/PlayerListHeader" },
            b.Headers.OrderBy(path => path, System.StringComparer.Ordinal).ToArray());
    }
}
