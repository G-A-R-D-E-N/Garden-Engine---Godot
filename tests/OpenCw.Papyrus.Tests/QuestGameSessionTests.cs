using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestGameSessionTests : HostFixture
{
    private static readonly ScriptSpec[] Scripts =
    {
        TestHosts.Stub("Form"),
        TestHosts.Stub("QuestScript", null,
            "Int Property Count Auto",
            "Float Property SignedZero Auto",
            "Bool Property Flag Auto",
            "String Property Text Auto",
            "Form Property Link Auto",
            "Function OnInit()\nEndFunction",
            "Function OnProbe()\nEndFunction"),
    };

    private GameSession Session(EvidenceGate gate, params QuestStartupInput[] startup) =>
        new(Track(TestHosts.Create(HostKind.Radicle, gate, Scripts)), gate, startup);

    [Fact]
    public void DirectSessionConstructionRefusesMissingStartupMetadata()
    {
        Assert.Throws<InvalidOperationException>(() => Session(QuestStartupBindingTests.Gate(),
            QuestStartupBindingTests.Input() with { Metadata = null }));
    }

    [Fact]
    public void SessionStartsQuestsOnceOnItsOwnHostBeforeRegisteringTheirOwners()
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = Session(gate, QuestStartupBindingTests.Input());
        session.Host.Overrides = new ScriptOverrideRegistry(gate);
        var initialized = 0;
        session.Host.Overrides.Register("QuestScript", "OnInit", 0, args =>
        {
            Assert.Equal(17, args.Instance.Properties["Count"].Integer);
            initialized++;
            return OverrideResult.Handled(PapyrusValue.None);
        });
        var result = session.StartNewGame();
        Assert.True(result.Applied);
        Assert.Same(result, session.StartNewGame());
        var instance = Assert.Single(session.Quests.InstancesFor(session.QuestStartup[0].Quest));
        Assert.Same(instance, Assert.Single(session.Host.Instances));
        Assert.Equal(1, session.Events.RegisteredOwners);
        session.DetachCell("unrelated-cell");
        Assert.True(session.Host.RunEvent(instance, "OnProbe").Completed);
        Assert.Equal(1, initialized);
        Assert.Equal(1, session.Quests.StartedQuests);
        Assert.Equal(1, session.Quests.AttachedScripts);
    }

    [Fact]
    public void SessionRetainsTheFirstInitializationFailureWithoutRerunningIt()
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = Session(gate, QuestStartupBindingTests.Input());
        session.Host.Overrides = new ScriptOverrideRegistry(gate);
        var attempts = 0;
        session.Host.Overrides.Register("QuestScript", "OnInit", 0, _ =>
        {
            attempts++;
            return OverrideResult.Refused("synthetic initialization failure");
        });
        var result = session.StartNewGame();
        Assert.False(result.Applied);
        Assert.Null(session.StartupResult);
        Assert.False(session.StartNewGame().Applied);
        Assert.Equal(1, attempts);
        Assert.Equal(0, session.Quests.StartedQuests);
        Assert.Equal(1, session.Quests.BlockedQuests);
        Assert.Equal(0, session.Quests.AttachedScripts);
        Assert.Empty(session.Host.Instances);
    }

}
