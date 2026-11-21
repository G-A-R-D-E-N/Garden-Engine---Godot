using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ScriptInstanceHostNeutralTests : HostFixture
{
    private static Dictionary<string, PapyrusValue> Properties() => new(StringComparer.OrdinalIgnoreCase);

    private static ScriptInstance HostNeutral(string name, Func<string, bool> defines, Func<string, int?> count) =>
        new(name, defines, count, 42UL, Properties(), null);

    [Fact]
    public void HostNeutralInstanceExposesItsNameFunctionsAndHostId()
    {
        var instance = HostNeutral("HostScript", name => name == "OnActivate", name => name == "OnActivate" ? 1 : null);

        Assert.Equal("HostScript", instance.ScriptName);
        Assert.True(instance.DefinesFunction("OnActivate"));
        Assert.False(instance.DefinesFunction("OnMissing"));
        Assert.Equal(1, instance.DeclaredParameterCount("OnActivate"));
        Assert.Null(instance.DeclaredParameterCount("OnMissing"));
        Assert.Equal(42UL, instance.HostId);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void HostedInstanceDeclaredParameterCountMatchesTheFunction(HostKind kind)
    {
        var host = Track(TestHosts.Create(kind, TestHosts.Gate(), new ScriptSpec("HostedScript",
            "Scriptname HostedScript\n" +
            "Function OnActivate(Int first, Int second)\n" +
            "EndFunction\n")));
        var instance = host.Attach("HostedScript");

        Assert.True(instance.DefinesFunction("OnActivate"));
        Assert.Null(instance.DeclaredParameterCount("OnMissing"));
    }

    [Fact]
    public void EntriesListRegistrations()
    {
        var overrides = new ScriptOverrideRegistry(TestHosts.Gate());
        overrides.Register("QuestScript", "Event", 2, _ => OverrideResult.Declined());
        overrides.Register("QuestScript", "OnInit", 0, _ => OverrideResult.Declined());

        var entries = overrides.Entries.OrderBy(entry => entry.Function).ToArray();

        Assert.Equal(new[] { ("QuestScript", "Event", 2), ("QuestScript", "OnInit", 0) }, entries);
    }

    [Fact]
    public void RegisteredFiresOncePerSuccessfulRegister()
    {
        var overrides = new ScriptOverrideRegistry(TestHosts.Gate());
        var fired = new List<(string Script, string Function, int ParamCount)>();
        overrides.Registered += (script, function, parameterCount) => fired.Add((script, function, parameterCount));

        overrides.Register("QuestScript", "Event", 2, _ => OverrideResult.Declined());
        overrides.Register("QuestScript", "OnInit", 0, _ => OverrideResult.Declined());
        Assert.Throws<ArgumentException>(() => overrides.Register("QuestScript", " ", 0, _ => OverrideResult.Declined()));

        Assert.Equal(new[] { ("QuestScript", "Event", 2), ("QuestScript", "OnInit", 0) }, fired);
    }

    [Fact]
    public void TryInvokeOnHostNeutralInstanceSkipsTheCountCheckAndRefusesUndefinedFunctions()
    {
        var overrides = new ScriptOverrideRegistry(TestHosts.Gate());
        var instance = HostNeutral("HostScript", name => name == "Event", _ => null);
        var ran = false;
        overrides.Register("HostScript", "Event", ScriptOverrideRegistry.AnyParameterCount, _ =>
        {
            ran = true;
            return OverrideResult.Handled(PapyrusValue.FromInt(3));
        });
        overrides.Register("HostScript", "Missing", ScriptOverrideRegistry.AnyParameterCount,
            _ => OverrideResult.Handled(PapyrusValue.None));

        var handled = overrides.TryInvoke(instance, "Event", new[] { PapyrusValue.FromInt(1), PapyrusValue.FromInt(2) });
        var refused = overrides.TryInvoke(instance, "Missing", Array.Empty<PapyrusValue>());

        Assert.Equal(OverrideOutcome.Handled, handled.Outcome);
        Assert.True(ran);
        Assert.Equal(3, handled.Value.Integer);
        Assert.Equal(OverrideOutcome.Refused, refused.Outcome);
        Assert.Contains("does not define Missing", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(1, overrides.RefusedCount);
    }
}
