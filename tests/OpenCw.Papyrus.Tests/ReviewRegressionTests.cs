using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Papyrus.Host;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ReviewRegressionTests : HostFixture
{
    private const string OnInitEmpty = "Function OnInit()\nEndFunction\n";

    private static EvidenceGate Gate() => TestHosts.Gate(
        (QuestRuntime.StartupOrderingKey, "ENGINE_POLICY"));

    private IScriptHost Host(HostKind kind, params string[] members) =>
        Track(TestHosts.Create(kind, Gate(), TestHosts.Stub("Review", null, members)));

    private IScriptHost FailingHost(HostKind kind) =>
        Track(TestHosts.Create(kind, Gate(), new ScriptSpec("Review", TestHosts.FailingOnInitSource("Review"))));

    private static ScriptInstance Ready(IScriptHost host)
    {
        var instance = host.Attach("Review");
        var initialized = host.Initialize(instance);
        Assert.True(initialized.Completed, TestHosts.Describe(host, initialized));
        return instance;
    }

    private static ScriptOverrideRegistry Overrides(IScriptHost host)
    {
        var registry = new ScriptOverrideRegistry(host.Gate);
        host.Overrides = registry;
        return registry;
    }

    public static IEnumerable<object[]> HostAndCount()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            yield return new object[] { host[0], 0 };
            yield return new object[] { host[0], 2 };
        }
    }

    [Theory]
    [MemberData(nameof(HostAndCount))]
    public void AnyCountOverridesRefuseWrongDeclaredArgumentCounts(HostKind kind, int count)
    {
        var host = Host(kind, "Function Run(Int value)\nEndFunction\n");
        var overrides = Overrides(host);
        var instance = Ready(host);
        var calls = 0;
        overrides.Register("Review", "Run", ScriptOverrideRegistry.AnyParameterCount, _ =>
        {
            calls++;
            return OverrideResult.Handled(PapyrusValue.None);
        });
        var result = host.RunEvent(instance, "Run", Enumerable.Repeat(PapyrusValue.None, count).ToArray());
        Assert.False(result.Completed);
        Assert.Equal(0, calls);
        Assert.Equal(1, ((RadicleScriptHost)host).Stats.OverridesRefused);
        var accepted = host.RunEvent(instance, "Run", PapyrusValue.None);
        Assert.True(accepted.Completed, TestHosts.Describe(host, accepted));
        Assert.Equal(1, calls);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void RegisteredArityCannotOverrideTheDeclaredArgumentCount(HostKind kind)
    {
        var host = Host(kind, "Function Run(Int value)\nEndFunction\n");
        var overrides = Overrides(host);
        var instance = Ready(host);
        var calls = 0;
        overrides.Register("Review", "Run", 0, _ => { calls++; return OverrideResult.Handled(PapyrusValue.None); });
        Assert.False(host.RunEvent(instance, "Run").Completed);
        Assert.Equal(0, calls);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void RepeatedDetachKeepsParkedInstancesAndTheirReferenceBindings(HostKind kind)
    {
        var host = Host(kind, "Int Property Value Auto");
        var session = new CellScriptSession(host);
        var first = session.Bind("Cell", "First", "Review",
            new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase) { ["Value"] = PapyrusValue.FromInt(7) });
        var second = session.Bind("Cell", "Second", "Review");
        Assert.True(host.Initialize(first).Completed);
        session.DetachCell("Cell");
        session.DetachCell("Cell");
        Assert.Equal(2, session.ParkedInstances);
        Assert.Equal(2, session.ReattachCell("Cell").Count);
        Assert.Same(first, session.InstanceFor("First"));
        Assert.Same(second, session.InstanceFor("Second"));
        Assert.Equal(ScriptLifecycle.Ready, first.Lifecycle);
        Assert.Equal(7, first.Properties["Value"].Integer);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void FailedInitializationStaysFailedOnRetry(HostKind kind)
    {
        var host = FailingHost(kind);
        var calls = 0;
        host.RegisterNative(TestHosts.BoomNative("Review"), _ =>
        {
            calls++;
            throw new InvalidOperationException("deliberate failure");
        });
        var instance = host.Attach("Review");
        Assert.False(host.Initialize(instance).Completed);
        var failure = instance.Failure;
        var retry = host.Initialize(instance);
        Assert.False(retry.Completed);
        Assert.Equal(VmStopReason.ModelError, retry.Reason);
        Assert.Equal(EvidenceKeys.OnInitBarrier, retry.Key);
        Assert.Equal(failure, instance.Failure);
        Assert.Equal(ScriptLifecycle.Failed, instance.Lifecycle);
        Assert.Equal(1, calls);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void NamedCallsSelectOnlyTheRequestedFunction(HostKind kind)
    {
        var host = Host(kind, "Int Function First()\n  Return 1\nEndFunction\n", "Int Function Other()\n  Return 2\nEndFunction\n");
        var instance = Ready(host);
        var other = host.RunEvent(instance, "other");
        Assert.True(other.Completed, TestHosts.Describe(host, other));
        Assert.Equal(2, other.Value.Integer);
        Assert.Equal(VmStopReason.ModelError, host.RunEvent(instance, "Missing").Reason);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void InitializationWithoutOnInitDoesNotRunAnArbitraryEvent(HostKind kind)
    {
        var host = Host(kind, "Function Boom() global native", "Function OnActivate()\n  Boom()\nEndFunction\n");
        var calls = 0;
        host.RegisterNative(TestHosts.BoomNative("Review"), _ => { calls++; return PapyrusValue.None; });
        var instance = host.Attach("Review");
        var initialized = host.Initialize(instance);
        Assert.True(initialized.Completed, TestHosts.Describe(host, initialized));
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
        Assert.Equal(0, initialized.Instructions);
        Assert.Equal(0, calls);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ThrowingNativeFailsInitializationClosed(HostKind kind)
    {
        var host = FailingHost(kind);
        TestHosts.RegisterBoom(host, "Review");
        var instance = host.Attach("Review");
        Assert.Equal(VmStopReason.ModelError, host.Initialize(instance).Reason);
        Assert.Equal(ScriptLifecycle.Failed, instance.Lifecycle);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void HostRefusesUndeclaredAndUninitializedEvents(HostKind kind)
    {
        var host = Host(kind, OnInitEmpty);
        var instance = host.Attach("Review");
        var early = host.RunEvent(instance, "OnInit");
        Assert.Equal(VmStopReason.ModelError, early.Reason);
        Assert.Equal(EvidenceKeys.OnInitBarrier, early.Key);
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.ModelError, host.RunEvent(instance, "Missing").Reason);
        Assert.Equal(2, TestHosts.RefusedEvents(host));
        Assert.Equal(0, host.RunStep(100));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AQuestWithAMissingScriptIsBlockedAndTheOtherQuestStarts(HostKind kind)
    {
        var host = Host(kind, OnInitEmpty);
        var runtime = new QuestRuntime(host.Gate);
        var result = runtime.StartNewGame(new[]
        {
            new QuestStartupInput("A", true, new[] { "Review" }, 0, 0),
            new QuestStartupInput("B", true, new[] { "Missing" }, 0, 0),
        }, host);
        Assert.True(result.Applied);
        Assert.Contains("B: missing script Missing", string.Join(" ", result.Reasons), StringComparison.Ordinal);
        Assert.Equal("B", Assert.Single(result.Blocked).Quest);
        Assert.Equal("Review", Assert.Single(host.Instances).ScriptName);
        Assert.Equal(new[] { "A" }, runtime.Started);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ReattachPreservesTwoReferencesWithTheSameScript(HostKind kind)
    {
        var session = new CellScriptSession(Host(kind, OnInitEmpty));
        var first = session.Bind("Cell", "First", "Review");
        var second = session.Bind("Cell", "Second", "Review");
        session.DetachCell("Cell");
        session.ReattachCell("Cell");
        Assert.Same(first, session.InstanceFor("First"));
        Assert.Same(second, session.InstanceFor("Second"));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void OverridesUseTheHostEntryPathOnce(HostKind kind)
    {
        var host = Host(kind, "Function Boom() global native", "Function Run()\n  Boom()\nEndFunction\n");
        var boom = 0;
        host.RegisterNative(TestHosts.BoomNative("Review"), _ => { boom++; return PapyrusValue.None; });
        var overrides = Overrides(host);
        var instance = Ready(host);
        var calls = 0;
        overrides.Register("Review", "Run", 0, _ => { calls++; return OverrideResult.Handled(PapyrusValue.None); });
        var result = host.RunEvent(instance, "Run");
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(1, calls);
        Assert.Equal(0, boom);
        Assert.Equal(0, host.RunStep(1));
    }

    private const string Budgeted =
        "Scriptname Review\n" +
        "Function Count() global native\n" +
        "Function Record(Int value) global native\n" +
        "Function Burn(Int rounds)\n" +
        "  Int n = 0\n" +
        "  While n < rounds\n" +
        "    n += 1\n" +
        "  EndWhile\n" +
        "EndFunction\n" +
        "Function First()\n  Burn(10)\n  Count()\nEndFunction\n" +
        "Function Second()\n  Count()\n  Burn(5)\nEndFunction\n" +
        "Function Run()\n  Int got = Nested(7)\n  Record(got)\nEndFunction\n" +
        "Int Function Nested(Int value)\n  Count()\n  Return value\nEndFunction\n";

    private RadicleScriptHost RadicleHost(params string[] members) =>
        (RadicleScriptHost)Host(HostKind.Radicle, members);

    [Theory]
    [InlineData(16, "First", 0, 2)]
    [InlineData(16, "Second", 0, 1)]
    [InlineData(2, "Run", 1, 2)]
    public void BudgetSlicesDoNotRepeatNativeEffectsOrLoseReturnSlots(int stepBudget, string function, int records, int parked)
    {
        var host = new RadicleScriptHost(Gate(), ConformanceScripts.Compile(new[] { ("Review", Budgeted) }).Values,
            stepBudget: stepBudget);
        Track(host);
        var calls = 0;
        var values = new List<long>();
        host.RegisterNative(new NativeId("Review", "Count"), _ => { calls++; return PapyrusValue.None; });
        host.RegisterNative(new NativeId("Review", "Record"), args => { values.Add(args[0].Integer); return PapyrusValue.None; });
        var instance = Ready(host);

        var result = host.RunEvent(instance, function);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(1, calls);
        Assert.Equal(records, values.Count);
        if (records == 1) Assert.Equal(7, values[0]);
        Assert.Equal(parked, host.Stats.Parked);
        Assert.Equal(0, host.Stats.Dropped);
        Assert.Equal(0, host.RunStep(100));
    }

    [Fact]
    public void DepthExhaustionReturnsItsDedicatedStopReason()
    {
        var host = RadicleHost("Function Recurse()\n  Recurse()\nEndFunction\n");
        var result = host.RunEvent(Ready(host), "Recurse");
        Assert.Equal(VmStopReason.FrameDepthExceeded, result.Reason);
    }

    [Fact]
    public void ParkLimitsBelongToEachEventInstance()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, Gate(),
            new ScriptSpec("Spin", ConformanceScripts.Spin)));
        var first = host.Attach("Spin");
        var second = host.Attach("Spin");
        Assert.True(host.Initialize(first).Completed);
        Assert.True(host.Initialize(second).Completed);
        foreach (var instance in new[] { first, second })
        {
            var result = host.RunEvent(instance, "Run");
            Assert.Equal(VmStopReason.BudgetExhausted, result.Reason);
            Assert.Contains("dropped", result.Message!, StringComparison.Ordinal);
        }
        Assert.Equal(2, host.Stats.Dropped);
        Assert.Equal(14, host.Stats.Parked);
        Assert.Equal(0, host.Stats.Ready);
    }

    [Fact]
    public void AutoPropertyWritesOnAnotherInstanceUseItsVariableWithoutARefusal()
    {
        var host = RadicleHost("Int Property Value Auto",
            "Function Poke(Review other)\n  other.Value = 4\nEndFunction\n",
            "Int Function Peek(Review other)\n  Return other.Value\nEndFunction\n");
        var instance = Ready(host);
        var other = PapyrusValue.FromObject(Ready(host));

        var poked = host.RunEvent(instance, "Poke", other);
        var peeked = host.RunEvent(instance, "Peek", other);

        Assert.True(poked.Completed, TestHosts.Describe(host, poked));
        Assert.True(peeked.Completed, TestHosts.Describe(host, peeked));
        Assert.Equal(4, peeked.Value.Integer);
        Assert.Equal(0, host.Stats.NativeTargetRefusals);
    }
}
