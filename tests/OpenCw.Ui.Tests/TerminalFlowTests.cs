using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Terminals;
using Xunit;

namespace OpenCw.Ui.Tests;

public class TerminalFlowTests
{
    const string Id = "term1";

    static FakeTerminalSource Source()
    {
        var source = new FakeTerminalSource();
        source.Nodes["root"] = FakeTerminalSource.Node("root", "ROBCO", "WELCOME", "Body",
            FakeTerminalSource.Entry("a", "A", TerminalEntryKind.Redraw));
        return source;
    }

    static FakeTerminalSource Locked(FakeHackingSession session)
    {
        var source = Source();
        source.Opening = new TerminalOpening(TerminalOpenStatus.Locked, string.Empty, string.Empty, session);
        return source;
    }

    [Fact]
    public void An_open_terminal_goes_straight_to_the_menu()
    {
        var flow = TerminalFlowModel.TryOpen(Source(), Id, out string reason);

        Assert.NotNull(flow);
        Assert.Equal(string.Empty, reason);
        Assert.Equal(TerminalFlowPhase.Menu, flow!.Phase);
        Assert.NotNull(flow.Menu);
        Assert.Null(flow.Hack);
    }

    [Theory]
    [InlineData(TerminalOpenStatus.Unavailable, "No power.", "No power.")]
    [InlineData(TerminalOpenStatus.LockedOut, "Locked out.", "Locked out.")]
    [InlineData(TerminalOpenStatus.Unavailable, "", TerminalFlowModel.DefaultFailure)]
    [InlineData(TerminalOpenStatus.Locked, "", TerminalFlowModel.DefaultFailure)]
    public void Refused_terminals_return_no_flow_and_the_sources_reason(TerminalOpenStatus status, string given, string expected)
    {
        var source = Source();
        source.Opening = new TerminalOpening(status, "root", given);

        var flow = TerminalFlowModel.TryOpen(source, Id, out string reason);

        Assert.Null(flow);
        Assert.Equal(expected, reason);
    }

    [Fact]
    public void An_open_terminal_whose_root_node_is_missing_is_refused()
    {
        var source = Source();
        source.Opening = new TerminalOpening(TerminalOpenStatus.Open, "gone");

        var flow = TerminalFlowModel.TryOpen(source, Id, out string reason);

        Assert.Null(flow);
        Assert.Equal(TerminalFlowModel.DefaultFailure, reason);
    }

    [Fact]
    public void A_locked_terminal_types_its_login_line_then_hands_over_to_hacking()
    {
        var flow = TerminalFlowModel.TryOpen(Locked(new FakeHackingSession()), Id, out _)!;

        Assert.Equal(TerminalFlowPhase.Login, flow.Phase);
        flow.Tick(0.1);
        Assert.Equal("PASSWO", flow.LoginText);
        Assert.Equal(TerminalFlowPhase.Login, flow.Phase);

        flow.Tick(1);

        Assert.Equal(TerminalFlowPhase.Hacking, flow.Phase);
        Assert.Equal("PASSWORD REQUIRED", flow.LoginText);
    }

    [Fact]
    public void Accept_skips_the_login_typing()
    {
        var flow = TerminalFlowModel.TryOpen(Locked(new FakeHackingSession()), Id, out _)!;

        flow.Handle(MenuInputAction.Accept);

        Assert.Equal(TerminalFlowPhase.Hacking, flow.Phase);
    }

    [Fact]
    public void Cracking_the_password_reopens_the_source_and_enters_the_menu()
    {
        var source = Locked(new FakeHackingSession());
        var flow = TerminalFlowModel.TryOpen(source, Id, out _)!;
        flow.Handle(MenuInputAction.Accept);
        flow.Handle(MenuInputAction.Accept);
        flow.Hack!.Hover(12);

        flow.Handle(MenuInputAction.Accept);
        Assert.Equal(TerminalFlowPhase.Hacking, flow.Phase);
        flow.Tick(5.1);

        Assert.Equal(new[] { HackingOutcome.Accepted }, source.Hacks);
        Assert.Equal(HackingOutcome.Accepted, flow.LastHackOutcome);
        Assert.Equal(TerminalFlowPhase.Menu, flow.Phase);
        Assert.NotNull(flow.Menu);
        Assert.Equal("root", flow.Menu!.NodeId);
    }

    [Fact]
    public void Being_locked_out_reports_to_the_source_and_closes_the_flow()
    {
        var source = Locked(new FakeHackingSession(1));
        var flow = TerminalFlowModel.TryOpen(source, Id, out _)!;
        int closed = 0;
        flow.Closed += () => closed++;
        flow.Handle(MenuInputAction.Accept);
        flow.Handle(MenuInputAction.Accept);

        flow.Handle(MenuInputAction.Accept);
        flow.Tick(3.1);
        flow.Tick(3.1);

        Assert.Equal(new[] { HackingOutcome.LockedOut }, source.Hacks);
        Assert.Equal(TerminalFlowPhase.Closed, flow.Phase);
        Assert.Null(flow.Menu);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Backing_out_of_hacking_reports_cancelled_and_closes()
    {
        var source = Locked(new FakeHackingSession());
        var flow = TerminalFlowModel.TryOpen(source, Id, out _)!;
        flow.Handle(MenuInputAction.Accept);

        flow.Handle(MenuInputAction.Back);

        Assert.Equal(new[] { HackingOutcome.Cancelled }, source.Hacks);
        Assert.Equal(TerminalFlowPhase.Closed, flow.Phase);
    }

    [Fact]
    public void Backing_out_of_the_login_line_closes_without_telling_the_source_about_a_hack()
    {
        var source = Locked(new FakeHackingSession());
        var flow = TerminalFlowModel.TryOpen(source, Id, out _)!;

        flow.Handle(MenuInputAction.Back);

        Assert.Equal(TerminalFlowPhase.Closed, flow.Phase);
        Assert.Empty(source.Hacks);
    }

    [Fact]
    public void The_source_going_invalid_closes_the_flow_in_every_phase()
    {
        var login = Locked(new FakeHackingSession());
        var loginFlow = TerminalFlowModel.TryOpen(login, Id, out _)!;
        login.Valid = false;
        loginFlow.Tick(0.1);
        Assert.Equal(TerminalFlowPhase.Closed, loginFlow.Phase);

        var menuSource = Source();
        var menuFlow = TerminalFlowModel.TryOpen(menuSource, Id, out _)!;
        menuSource.Valid = false;
        menuFlow.Tick(0.1);
        Assert.Equal(TerminalFlowPhase.Closed, menuFlow.Phase);

        var hack = Locked(new FakeHackingSession());
        var hackFlow = TerminalFlowModel.TryOpen(hack, Id, out _)!;
        hackFlow.Handle(MenuInputAction.Accept);
        hack.Valid = false;
        hackFlow.Tick(0.1);
        Assert.Equal(TerminalFlowPhase.Closed, hackFlow.Phase);
    }

    [Fact]
    public void The_source_going_invalid_during_the_accepted_hold_still_closes_the_flow()
    {
        var source = Locked(new FakeHackingSession());
        var flow = TerminalFlowModel.TryOpen(source, Id, out _)!;
        int closed = 0;
        flow.Closed += () => closed++;
        flow.Handle(MenuInputAction.Accept);
        flow.Handle(MenuInputAction.Accept);
        flow.Hack!.Hover(12);
        flow.Handle(MenuInputAction.Accept);
        Assert.Equal(HackingPhase.Accepted, flow.Hack.Phase);

        source.Valid = false;
        for (int i = 0; i < 5; i++)
            flow.Tick(0.1);

        Assert.Equal(TerminalFlowPhase.Closed, flow.Phase);
        Assert.Equal(HackingPhase.Closed, flow.Hack.Phase);
        Assert.Equal(new[] { HackingOutcome.Cancelled }, source.Hacks);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Closing_the_flow_while_hacking_reports_cancelled_to_the_source()
    {
        var source = Locked(new FakeHackingSession());
        var flow = TerminalFlowModel.TryOpen(source, Id, out _)!;
        int closed = 0;
        flow.Closed += () => closed++;
        flow.Handle(MenuInputAction.Accept);

        flow.Close();

        Assert.Equal(new[] { HackingOutcome.Cancelled }, source.Hacks);
        Assert.Equal(HackingPhase.Closed, flow.Hack!.Phase);
        Assert.Equal(TerminalFlowPhase.Closed, flow.Phase);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Leaving_the_top_menu_closes_the_flow_exactly_once()
    {
        var flow = TerminalFlowModel.TryOpen(Source(), Id, out _)!;
        int closed = 0;
        flow.Closed += () => closed++;
        flow.Handle(MenuInputAction.Accept);

        flow.Handle(MenuInputAction.Back);
        flow.Handle(MenuInputAction.Back);
        flow.Close();

        Assert.Equal(TerminalFlowPhase.Closed, flow.Phase);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Sounds_and_changes_from_the_inner_models_surface_on_the_flow()
    {
        var flow = TerminalFlowModel.TryOpen(Source(), Id, out _)!;
        var sounds = new List<string>();
        int changes = 0;
        flow.SoundRequested += sounds.Add;
        flow.Changed += () => changes++;

        flow.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "type_skip", "type_loop_stop" }, sounds);
        Assert.Equal(1, changes);
    }
}
