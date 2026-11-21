using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class LinkedRefActivationTests
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static ActivationTarget Lever(string key = "000800:A.esp") => new(key, "000801:A.esp", "Activator", None);

    private static ActivatorOpener Opener(bool scripted, string[] links, List<string> activated, Func<string, bool>? accepts = null) =>
        LinkedRefActivation.Opener(_ => scripted, _ => links, (_, link) => { activated.Add(link); return accepts?.Invoke(link) ?? true; });

    [Fact]
    public void An_unscripted_lever_activates_each_linked_reference_and_reports_the_activation_opened()
    {
        var activated = new List<string>();

        Assert.True(Opener(false, ["000A01:A.esp", "000A02:A.esp"], activated).Open(Lever()));
        Assert.Equal(["000A01:A.esp", "000A02:A.esp"], activated);
    }

    [Fact]
    public void A_scripted_lever_is_left_to_its_own_script_so_the_link_is_not_activated_twice()
    {
        var activated = new List<string>();

        Assert.False(Opener(true, ["000A01:A.esp"], activated).Open(Lever()));
        Assert.Empty(activated);
    }

    [Fact]
    public void A_scripted_lever_does_not_read_its_links_at_all()
    {
        var activated = new List<string>();
        var linksRead = 0;
        var opener = LinkedRefActivation.Opener(
            _ => true,
            _ => { linksRead++; return ["000A01:A.esp"]; },
            (_, link) => { activated.Add(link); return true; });

        Assert.False(opener.Open(Lever()));
        Assert.Equal(0, linksRead);
        Assert.Empty(activated);
    }

    [Fact]
    public void A_lever_with_no_links_does_not_open_so_the_activator_keeps_no_default_action()
    {
        var activated = new List<string>();
        var result = new ActivatorActivation([Opener(false, [], activated)]).Activate(Lever());

        Assert.Equal(new ActivationResult(ActivationStatus.Handled, "no default action"), result);
        Assert.Empty(activated);
    }

    [Fact]
    public void A_self_link_and_a_repeated_link_are_activated_at_most_once()
    {
        var activated = new List<string>();

        Assert.True(Opener(false, ["000800:A.esp", "000A01:A.esp", "000a01:a.esp"], activated).Open(Lever()));
        Assert.Equal(["000A01:A.esp"], activated);
    }

    [Fact]
    public void A_self_link_is_skipped_whatever_its_case()
    {
        var activated = new List<string>();

        Assert.True(Opener(false, ["000800:a.ESP", "000A01:A.esp"], activated).Open(Lever()));
        Assert.Equal(["000A01:A.esp"], activated);
    }

    [Fact]
    public void Only_a_link_target_that_accepts_the_activation_counts_as_opened()
    {
        var activated = new List<string>();
        var opener = Opener(false, ["000A01:A.esp", "000A02:A.esp"], activated, link => link == "000A02:A.esp");

        Assert.True(opener.Open(Lever()));
        Assert.Equal(["000A01:A.esp", "000A02:A.esp"], activated);
        Assert.False(Opener(false, ["000A01:A.esp"], new List<string>(), _ => false).Open(Lever()));
    }

    [Fact]
    public void Every_link_is_still_activated_when_they_all_refuse()
    {
        var activated = new List<string>();
        var opener = Opener(false, ["000A01:A.esp", "000A02:A.esp"], activated, _ => false);

        Assert.False(opener.Open(Lever()));
        Assert.Equal(["000A01:A.esp", "000A02:A.esp"], activated);
    }

    [Fact]
    public void Forwarding_does_not_stop_at_the_first_link_that_accepts()
    {
        var activated = new List<string>();
        var opener = Opener(false, ["000A01:A.esp", "000A02:A.esp"], activated, link => link == "000A01:A.esp");

        Assert.True(opener.Open(Lever()));
        Assert.Equal(["000A01:A.esp", "000A02:A.esp"], activated);
    }

    [Fact]
    public void The_link_read_and_the_forward_both_carry_the_target_being_activated()
    {
        var lever = Lever();
        var activated = new List<string>();
        ActivationTarget? read = null;
        ActivationTarget? forwarded = null;
        var opener = LinkedRefActivation.Opener(
            _ => false,
            target => { read = target; return ["000A01:A.esp"]; },
            (target, link) => { forwarded = target; activated.Add(link); return true; });

        Assert.True(opener.Open(lever));
        Assert.Same(lever, read);
        Assert.Same(lever, forwarded);
    }

    [Fact]
    public void A_lever_with_a_link_is_handled_by_the_activator_handler_under_the_linked_ref_name()
    {
        var activated = new List<string>();
        var dispatcher = new ActivationDispatcher();
        dispatcher.Register(new ActivatorActivation([Opener(false, ["000A01:A.esp"], activated)]));

        var result = dispatcher.Dispatch(Lever());

        Assert.Equal(new ActivationResult(ActivationStatus.Handled, "linked ref"), result);
        Assert.Equal(["000A01:A.esp"], activated);
    }

    [Fact]
    public void An_earlier_opener_that_opens_keeps_the_act_so_the_link_is_not_forwarded()
    {
        var activated = new List<string>();
        var handler = new ActivatorActivation([
            new ActivatorOpener("workbench", _ => true),
            Opener(false, ["000A01:A.esp"], activated)]);

        var result = handler.Activate(Lever());

        Assert.Equal(new ActivationResult(ActivationStatus.Handled, "workbench"), result);
        Assert.Empty(activated);
    }
}
