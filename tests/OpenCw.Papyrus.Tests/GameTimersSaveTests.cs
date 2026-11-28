using System.Text;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class GameTimersSaveTests : HostFixture
{
    private static readonly PapyrusOwnerId OwnerA = PapyrusOwnerId.ForPlacedReference("00002A:Test.esm", "0000A1:Test.esm");
    private static readonly PapyrusOwnerId OwnerB = PapyrusOwnerId.ForPlacedReference("00002A:Test.esm", "0000B2:Test.esm");

    private IScriptHost NewHost() => Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(),
        TestHosts.Stub("ScriptObject", null),
        new ScriptSpec("Plain", "Scriptname Plain extends ScriptObject\n")));

    private static ScriptTimers Table() => new(ScriptTimers.GameTimeHandlerName);

    private static IReadOnlyDictionary<string, byte[]> Section(string json) => new Dictionary<string, byte[]> { [GameTimersSave.SectionName] = Encoding.UTF8.GetBytes(json) };

    [Fact]
    public void AGameTimerSavedBeforeIsRestoredOntoTheLiveInstanceWithItsRemainingHoursAndFiresOnce()
    {
        var host = NewHost();
        var instance = host.Attach("Plain", null, OwnerA);
        var before = Table();
        Assert.True(before.Start(instance, 5, 7));
        before.Advance(2);
        var sections = GameTimersSave.Sections(before);

        var after = Table();
        var dropped = GameTimersSave.Restore(GameTimersSave.Read(sections)!, host.Instances, after);

        Assert.Equal(0, dropped);
        Assert.Equal(new[] { (instance, 7, 3.0) }, after.Scheduled);
        Assert.Empty(after.Advance(2.9));
        Assert.Equal(new[] { (instance, 7) }, after.Advance(0.2));
        Assert.Empty(after.Advance(10));
    }

    [Fact]
    public void ALoadReplacesTheRunningTimersAndAnEntryWhoseScriptIsNotLiveIsDropped()
    {
        var host = NewHost();
        var live = host.Attach("Plain", null, OwnerA);
        var gone = host.Attach("Plain", null, OwnerB);
        var before = Table();
        before.Start(live, 4, 1);
        before.Start(gone, 6, 2);
        var sections = GameTimersSave.Sections(before);
        host.Discard(gone);
        var running = Table();
        running.Start(live, 1, 9);

        var dropped = GameTimersSave.Restore(GameTimersSave.Read(sections)!, host.Instances, running);

        Assert.Equal(1, dropped);
        Assert.Equal(new[] { (live, 1, 4.0) }, running.Scheduled);
    }

    [Fact]
    public void AnEmptyTableWritesASectionThatClearsTheRunningTimersOnLoad()
    {
        var host = NewHost();
        var running = Table();
        running.Start(host.Attach("Plain", null, OwnerA), 1, 3);

        GameTimersSave.Restore(GameTimersSave.Read(GameTimersSave.Sections(Table()))!, host.Instances, running);

        Assert.Equal(0, running.Count);
    }

    [Fact]
    public void AScriptWithNoOwnerIsNotSaved()
    {
        var host = NewHost();
        var table = Table();
        table.Start(host.Attach("Plain"), 2, 1);

        Assert.Empty(GameTimersSave.Capture(table));
    }

    [Fact]
    public void ASaveWithoutTheSectionReadsAsNothingToRestore()
    {
        Assert.Null(GameTimersSave.Read(new Dictionary<string, byte[]>()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"timers\":[]}")]
    [InlineData("{\"version\":2,\"timers\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"timers\":{}}")]
    [InlineData("{\"version\":1,\"timers\":[1]}")]
    [InlineData("{\"version\":1,\"timers\":[{\"owner\":\"o\",\"script\":\"S\",\"id\":1}]}")]
    [InlineData("{\"version\":1,\"timers\":[{\"owner\":\"o\",\"script\":\"S\",\"id\":1,\"remaining\":-1}]}")]
    [InlineData("{\"version\":1,\"timers\":[{\"owner\":\"\",\"script\":\"S\",\"id\":1,\"remaining\":1}]}")]
    [InlineData("{\"version\":1,\"timers\":[{\"owner\":\"o\",\"script\":\"S\",\"id\":\"x\",\"remaining\":1}]}")]
    [InlineData("{\"version\":1,\"timers\":[{\"owner\":\"o\",\"script\":\"S\",\"id\":1,\"remaining\":1},{\"owner\":\"o\",\"script\":\"S\",\"id\":1,\"remaining\":2}]}")]
    [InlineData("{\"version\":1,\"version\":1,\"timers\":[]}")]
    public void MalformedOrDuplicateDataIsRefusedAsAFormatErrorBeforeAnythingIsApplied(string json)
    {
        Assert.Throws<FormatException>(() => GameTimersSave.Read(Section(json)));
    }

    [Fact]
    public void MoreTimersThanTheTableHoldsIsRefused()
    {
        var entries = string.Join(",", Enumerable.Range(0, ScriptTimers.Capacity + 1).Select(i => $"{{\"owner\":\"o\",\"script\":\"S\",\"id\":{i},\"remaining\":1}}"));

        Assert.Throws<FormatException>(() => GameTimersSave.Read(Section($"{{\"version\":1,\"timers\":[{entries}]}}")));
    }
}
