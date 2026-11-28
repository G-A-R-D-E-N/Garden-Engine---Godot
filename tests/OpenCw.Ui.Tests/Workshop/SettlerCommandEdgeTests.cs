using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettlerCommandEdgeTests
{
    static (SettlerCommandController Ctrl, FakeSettlements Provider) Make(string? settler = null)
    {
        var provider = new FakeSettlements();
        return (new SettlerCommandController(provider, settler), provider);
    }

    static (string, LineTone)[] Lines(SettlerCommandController ctrl) => ctrl.DetailLines.Select(line => (line.Text, line.Tone)).ToArray();

    [Theory]
    [InlineData(0, "Ann won't take orders from you.")]
    [InlineData(1, "Ann can't be moved from this settlement.")]
    [InlineData(2, "Ann can't be sent on a caravan route.")]
    public void A_blocked_command_with_no_reason_has_its_own_default_sentence_in_the_card_and_on_accept(int downs, string sentence)
    {
        var (ctrl, provider) = Make("a");
        provider.SettlerList[0] = provider.SettlerList[0] with { CanCommand = false, CanMove = false, CanCaravan = false };
        provider.Raise();
        for (var i = 0; i < downs; i++) ctrl.Handle(MenuInputAction.Down);

        Assert.Equal((sentence, LineTone.Bad), Lines(ctrl).Last());

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(sentence, ctrl.Message);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public void A_settler_with_no_work_object_shows_only_the_job_word()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Down);

        Assert.Equal(new[] { ("Bob", LineTone.Heading), ("Unassigned", LineTone.Normal) }, Lines(ctrl));
    }

    [Fact]
    public void The_hints_follow_the_level()
    {
        var (ctrl, _) = Make();
        var settlers = ctrl.Hints.ToArray();
        ctrl.Handle(MenuInputAction.Accept);
        var commands = ctrl.Hints.ToArray();
        ctrl.Handle(MenuInputAction.Accept);
        var targets = ctrl.Hints.ToArray();

        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Select"), new MenuHint(MenuInputAction.Back, "Close") }, settlers);
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Choose"), new MenuHint(MenuInputAction.Back, "Back") }, commands);
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Assign"), new MenuHint(MenuInputAction.Back, "Cancel") }, targets);
    }

    [Fact]
    public void A_work_object_card_says_whether_it_needs_a_worker_and_who_has_it()
    {
        var (ctrl, provider) = Make("a");
        provider.JobTargets[0] = new JobTarget("t1", "Field", false);
        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(new[] { ("Field", LineTone.Heading), ("Needs a worker: no", LineTone.Normal) }, Lines(ctrl));

        ctrl.Handle(MenuInputAction.Down);

        Assert.Equal(
            new[]
            {
                ("Tower", LineTone.Heading), ("Needs a worker: yes", LineTone.Normal), ("In use by Cy", LineTone.Bad),
                ("Assigning it takes it from Cy.", LineTone.Muted),
            },
            Lines(ctrl));
    }

    [Fact]
    public void A_failed_assignment_with_no_reason_says_it_failed_and_stays_on_the_work_objects()
    {
        var (ctrl, provider) = Make("a");
        provider.Next = new ActionResult(false);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(DestinationPickerController.FailedText, ctrl.Message);
        Assert.Equal(CommandLevel.Targets, ctrl.Level);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void A_provider_change_on_the_work_objects_refreshes_them_and_keeps_the_selection()
    {
        var (ctrl, provider) = Make("a");
        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Down);

        provider.JobTargets.Insert(0, new JobTarget("t0", "Barn", true));
        provider.JobTargets[2] = new JobTarget("t2", "Tower", true);
        provider.Raise();

        Assert.Equal(new[] { "Barn", "Field", "Tower" }, WorkshopTestHelpers.RowTexts(ctrl.List));
        Assert.Equal("t2", ctrl.Rows.Selected!.Key);
    }
}
