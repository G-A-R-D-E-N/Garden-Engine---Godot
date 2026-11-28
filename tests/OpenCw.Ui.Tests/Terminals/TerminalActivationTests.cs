using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Terminals;
using Xunit;

namespace OpenCw.Ui.Tests;

public class TerminalActivationTests
{
    static readonly IReadOnlySet<string> Terminals = new HashSet<string> { "terminals" };
    static readonly IReadOnlySet<string> NoGroups = new HashSet<string>();

    static ActivationTarget Target(IReadOnlySet<string>? groups = null, string key = "000800:A.esp") =>
        new(key, "000801:A.esp", "Terminal", groups ?? Terminals);

    static FakeTerminalSource Source()
    {
        var source = new FakeTerminalSource();
        source.Nodes["root"] = FakeTerminalSource.Node("root", "H", "W", "B");
        return source;
    }

    [Fact]
    public void Only_the_terminals_group_is_handled()
    {
        var handler = new TerminalActivation(Source(), _ => true, _ => { });
        Assert.True(handler.Handles(Target()));
        Assert.False(handler.Handles(Target(NoGroups)));
        Assert.False(handler.Handles(Target(key: string.Empty)));
    }

    [Fact]
    public void A_good_terminal_is_shown_and_handled()
    {
        TerminalFlowModel? shown = null;
        var handler = new TerminalActivation(Source(), f => { shown = f; return true; }, _ => { });
        Assert.Equal(ActivationStatus.Handled, handler.Activate(Target()).Status);
        Assert.NotNull(shown);
    }

    [Fact]
    public void An_unopenable_terminal_is_blocked_and_the_reason_is_shown()
    {
        var refused = new List<string>();
        var source = Source();
        source.Opening = new TerminalOpening(TerminalOpenStatus.Unavailable, Reason: "Locked up.");
        var result = new TerminalActivation(source, _ => true, refused.Add).Activate(Target());
        Assert.Equal(ActivationStatus.Blocked, result.Status);
        Assert.Equal(new[] { "Locked up." }, refused);
    }

    [Fact]
    public void A_refusal_the_terminal_already_reported_needs_no_generic_notice_but_a_silent_stack_refusal_does()
    {
        var source = Source();
        source.Opening = new TerminalOpening(TerminalOpenStatus.Unavailable, Reason: "Locked up.");
        Assert.False(new TerminalActivation(source, _ => true, _ => { }).Activate(Target()).NeedsNotice);
        Assert.True(new TerminalActivation(Source(), _ => false, _ => { }).Activate(Target()).NeedsNotice);
        Assert.False(new ActivationResult(ActivationStatus.Handled).NeedsNotice);
    }

    [Fact]
    public void A_terminal_already_open_is_refused_until_its_flow_closes()
    {
        var refused = new List<string>();
        TerminalFlowModel? first = null;
        var handler = new TerminalActivation(Source(), f => { first ??= f; return true; }, refused.Add);
        handler.Activate(Target());
        Assert.Equal(ActivationStatus.Blocked, handler.Activate(Target()).Status);
        Assert.Equal(new[] { TerminalActivation.InUse }, refused);
        Assert.Equal(ActivationStatus.Handled, handler.Activate(Target(key: "000802:A.esp")).Status);
        first!.Close();
        Assert.Equal(ActivationStatus.Handled, handler.Activate(Target()).Status);
    }

    [Fact]
    public void A_menu_stack_that_refuses_the_flow_closes_it_and_frees_the_terminal()
    {
        bool accept = false;
        var handler = new TerminalActivation(Source(), _ => accept, _ => { });
        Assert.Equal(ActivationStatus.Blocked, handler.Activate(Target()).Status);
        accept = true;
        Assert.Equal(ActivationStatus.Handled, handler.Activate(Target()).Status);
    }

    [Fact]
    public void A_menu_push_that_throws_frees_the_terminal_and_rethrows()
    {
        bool throwNow = true;
        var handler = new TerminalActivation(Source(), _ => throwNow ? throw new InvalidOperationException() : true, _ => { });
        Assert.Throws<InvalidOperationException>(() => handler.Activate(Target()));
        throwNow = false;
        Assert.Equal(ActivationStatus.Handled, handler.Activate(Target()).Status);
    }

    [Fact]
    public void The_dispatcher_routes_a_terminal_target_to_the_handler()
    {
        var dispatcher = new ActivationDispatcher();
        int shown = 0;
        dispatcher.Register(new TerminalActivation(Source(), _ => { shown++; return true; }, _ => { }));
        Assert.Equal(ActivationStatus.Handled, dispatcher.Dispatch(Target()).Status);
        Assert.Equal(1, shown);
    }
}
