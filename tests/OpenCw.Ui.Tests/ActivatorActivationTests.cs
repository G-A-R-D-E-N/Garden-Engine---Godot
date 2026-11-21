using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ActivatorActivationTests
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static ActivationTarget Target(string baseType, string key = "000800:A.esp") => new(key, "000801:A.esp", baseType, None);

    private static ActivatorOpener Opener(string name, bool opens, List<string> calls) => new(name, _ => { calls.Add(name); return opens; });

    [Theory]
    [InlineData("Activator", true)]
    [InlineData("TalkingActivator", false)]
    [InlineData("Container", false)]
    [InlineData("Furniture", false)]
    [InlineData("", false)]
    public void Only_plain_activators_are_handled(string baseType, bool handled) =>
        Assert.Equal(handled, new ActivatorActivation([]).Handles(Target(baseType)));

    [Fact]
    public void The_first_opener_that_opens_wins_and_later_ones_do_not_run()
    {
        var calls = new List<string>();
        var handler = new ActivatorActivation([Opener("a", false, calls), Opener("b", true, calls), Opener("c", true, calls)]);

        var result = handler.Activate(Target("Activator"));

        Assert.Equal(new ActivationResult(ActivationStatus.Handled, "b"), result);
        Assert.Equal(new[] { "a", "b" }, calls);
    }

    [Fact]
    public void An_activator_no_opener_takes_is_handled_so_it_never_falls_through_to_dialogue()
    {
        var calls = new List<string>();
        var dispatcher = new ActivationDispatcher();
        dispatcher.Register(new ActivatorActivation([Opener("bench", false, calls)]));
        var fallbackRan = false;
        dispatcher.Register(new DelegateActivationHandler(_ => true, _ => { fallbackRan = true; return new(ActivationStatus.Handled); }));

        var result = dispatcher.Dispatch(Target("Activator"));

        Assert.Equal(new ActivationResult(ActivationStatus.Handled, "no default action"), result);
        Assert.Equal(new[] { "bench" }, calls);
        Assert.False(fallbackRan);
    }

    [Fact]
    public void A_talking_activator_skips_the_activator_handler_and_reaches_the_next_handler()
    {
        var calls = new List<string>();
        var dispatcher = new ActivationDispatcher();
        dispatcher.Register(new ActivatorActivation([Opener("bench", true, calls)]));
        dispatcher.Register(new DelegateActivationHandler(_ => true, _ => new(ActivationStatus.Handled, "dialogue")));

        Assert.Equal("dialogue", dispatcher.Dispatch(Target("TalkingActivator")).Detail);
        Assert.Empty(calls);
    }

    [Fact]
    public void A_script_block_stops_the_openers_but_the_script_event_is_still_delivered()
    {
        var calls = new List<string>();
        var dispatcher = new ActivationDispatcher(_ => true, key => key == "000800:A.esp");
        dispatcher.Register(new ActivatorActivation([Opener("bench", true, calls)]));

        var blocked = dispatcher.Dispatch(Target("Activator"));
        var open = dispatcher.Dispatch(Target("Activator", "000802:A.esp"));

        Assert.Equal(ActivationStatus.Blocked, blocked.Status);
        Assert.Equal("bench", open.Detail);
        Assert.Equal(new[] { "bench" }, calls);
        Assert.Equal(2, dispatcher.ScriptEventsDelivered);
    }
}
