using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettlerCommandActivationTests
{
    static ActivationTarget Target(string key) => new(key, "base", "FURN", new HashSet<string>());

    static readonly Dictionary<string, CommandTarget> Objects = new()
    {
        ["crop1"] = new CommandTarget("crop1", true, true, false),
        ["bed1"] = new CommandTarget("bed1", true, false, true),
        ["post1"] = new CommandTarget("post1", false, true, false),
        ["chair1"] = new CommandTarget("chair1", true, false, false),
    };

    static (SettlerCommandActivation Handler, SettlerCommandSession Session, SettlerAssignments Store, List<CommandResult> Reports) Make()
    {
        var store = new SettlerAssignments();
        var session = new SettlerCommandSession(store);
        var reports = new List<CommandResult>();
        var handler = new SettlerCommandActivation(session, target => Objects.GetValueOrDefault(target.RefKey), reports.Add);
        return (handler, session, store, reports);
    }

    [Fact]
    public void ItHandlesNothingUntilASettlerIsInTheCommandState()
    {
        var (handler, session, _, _) = Make();

        Assert.False(handler.Handles(Target("crop1")));
        session.Start("ann", new SettlerFacts(true));
        Assert.True(handler.Handles(Target("crop1")));
    }

    [Fact]
    public void ItLeavesAnObjectThatIsNeitherWorkNorABedToTheOtherHandlers()
    {
        var (handler, session, _, _) = Make();
        session.Start("ann", new SettlerFacts(true));

        Assert.False(handler.Handles(Target("lamp1")));
    }

    [Fact]
    public void ItNeverHandlesForACompanion()
    {
        var (handler, session, _, _) = Make();
        session.Start("piper", new SettlerFacts(true, true, true));

        Assert.False(handler.Handles(Target("crop1")));
    }

    [Fact]
    public void ActivatingAWorkObjectOrABedAssignsAndReports()
    {
        var (handler, session, store, reports) = Make();
        session.Start("ann", new SettlerFacts(true));

        var work = handler.Activate(Target("crop1"));
        session.Start("ann", new SettlerFacts(true));
        var bed = handler.Activate(Target("bed1"));

        Assert.Equal(ActivationStatus.Handled, work.Status);
        Assert.Equal(ActivationStatus.Handled, bed.Status);
        Assert.Equal("crop1", store.WorkObjectOf("ann"));
        Assert.Equal("bed1", store.BedOf("ann"));
        Assert.Equal(new[] { CommandOutcome.Assigned, CommandOutcome.Assigned }, reports.Select(report => report.Outcome).ToArray());
    }

    [Fact]
    public void ARefusedObjectBlocksWithoutANoticeAndKeepsTheState()
    {
        var (handler, session, store, _) = Make();
        session.Start("ann", new SettlerFacts(true));

        var result = handler.Activate(Target("post1"));

        Assert.Equal(ActivationStatus.Blocked, result.Status);
        Assert.False(result.NeedsNotice);
        Assert.Equal("ann", session.Active);
        Assert.True(store.IsEmpty);
    }

    [Fact]
    public void OnlyAnAssignedOrEndedSettlerOutcomeCountsAsASettlerResult()
    {
        var (handler, session, _, _) = Make();
        session.Start("ann", new SettlerFacts(true));
        var ended = handler.Activate(Target("chair1"));
        session.Start("ann", new SettlerFacts(true));
        var assigned = handler.Activate(Target("crop1"));
        session.Start("ann", new SettlerFacts(true));
        var refused = handler.Activate(Target("post1"));

        Assert.True(SettlerCommandActivation.IsSettlerOutcome(ended));
        Assert.True(SettlerCommandActivation.IsSettlerOutcome(assigned));
        Assert.False(SettlerCommandActivation.IsSettlerOutcome(refused));
        Assert.False(SettlerCommandActivation.IsSettlerOutcome(new ActivationResult(ActivationStatus.Handled, "other")));
    }

    [Fact]
    public void TheDispatcherAsksTheSettlerHandlerBeforeEveryOtherHandler()
    {
        var (handler, session, store, _) = Make();
        var other = new DelegateActivationHandler(_ => true, _ => new ActivationResult(ActivationStatus.Handled, "other"));
        var dispatcher = ActivationDispatcher.Build(null, null, new ActivationHandlers(other, other, other, other, other, other, other, other, other, other) { Settler = handler });

        Assert.Equal("other", dispatcher.Dispatch(Target("crop1")).Detail);
        session.Start("ann", new SettlerFacts(true));
        Assert.Equal("Assigned", dispatcher.Dispatch(Target("crop1")).Detail);
        Assert.Equal("crop1", store.WorkObjectOf("ann"));
        Assert.Equal("other", dispatcher.Dispatch(Target("crop1")).Detail);
    }
}
