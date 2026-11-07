using System.Text;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestGameSessionTests
{
    private static ScriptLibrary Library()
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("QuestScript", new[]
            { new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
                new PapyrusFunction("OnProbe", new[] { PapyrusInstruction.Return() }) },
            Array.Empty<string>()));
        return library;
    }

    [Fact]
    public void DirectSessionConstructionRefusesMissingStartupMetadata()
    {
        Assert.Throws<InvalidOperationException>(() => new GameSession(Library(), QuestStartupBindingTests.Gate(),
            new[] { QuestStartupBindingTests.Input() with { Metadata = null } }));
    }

    [Fact]
    public void SessionStartsQuestsOnceOnItsOwnHostBeforeRegisteringTheirOwners()
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = new GameSession(Library(), gate, new[] { QuestStartupBindingTests.Input() });
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
        var session = new GameSession(Library(), gate, new[] { QuestStartupBindingTests.Input() });
        session.Host.Overrides = new ScriptOverrideRegistry(gate);
        var attempts = 0;
        session.Host.Overrides.Register("QuestScript", "OnInit", 0, _ =>
        {
            attempts++;
            return OverrideResult.Refused("synthetic initialization failure");
        });
        var result = session.StartNewGame();
        Assert.False(result.Applied);
        Assert.Same(result, session.StartNewGame());
        Assert.Equal(1, attempts);
        Assert.Equal(0, session.Quests.StartedQuests);
        Assert.Equal(1, session.Quests.BlockedQuests);
        Assert.Equal(1, session.Quests.AttachedScripts);
        Assert.Equal(ScriptLifecycle.Failed, Assert.Single(session.Host.Instances).Lifecycle);
    }

    [Fact]
    public void ConvertedStartupIsLoadedButCanonicalUnprovenOrderingNeverExecutesIt()
    {
        using var cache = new TempCache();
        var script = Encoding.UTF8.GetBytes(PapyrusIr.ToJson(new[]
        {
            new PapyrusScript("QuestScript", Array.Empty<PapyrusFunction>(), Array.Empty<string>()),
        }));
        ScriptCacheBuilder.Build(new[]
        {
            new ScriptSourceCandidate("QuestScript", ScriptSourceKind.Loose, "project-authored", 0,
                script.Length, () => script),
        }, "project-authored", new ScriptCacheConverter(1, 1, bytes => Encoding.UTF8.GetString(bytes)), cache.Root,
            questStartup: new[] { QuestStartupBindingTests.Input() });
        var session = GameSession.Load(cache.Root);
        Assert.Empty(session.Host.Instances);
        var result = session.StartNewGame();
        Assert.False(result.Applied);
        Assert.NotEmpty(result.Reasons);
        Assert.Empty(session.Host.Instances);
        Assert.Equal(0, session.Events.RegisteredOwners);
        Assert.Same(result, session.StartNewGame());
        Assert.Equal(1, session.Quests.BlockedQuests);
        Assert.Equal(1, session.Quests.Approximations);
    }
}
