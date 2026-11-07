using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ScriptInstanceHostNeutralTests
{
    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY"),
    };

    private static EvidenceGate Gate()
    {
        var entries = Rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

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

    [Fact]
    public void HostNeutralScriptIsOwnedByTheHost()
    {
        var instance = HostNeutral("HostScript", _ => false, _ => null);

        Assert.Throws<InvalidOperationException>(() => { _ = instance.Script; });
    }

    [Fact]
    public void CSharpInstanceDeclaredParameterCountMatchesTheFunction()
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("HostedScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("OnActivate", new[] { PapyrusInstruction.Return() }, ParameterCount: 2),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, Gate());
        var instance = host.Attach("HostedScript");

        Assert.Equal(0UL, instance.HostId);
        Assert.Equal(2, instance.DeclaredParameterCount("OnActivate"));
        Assert.Equal(2, instance.DeclaredParameterCount("onactivate"));
        Assert.Null(instance.DeclaredParameterCount("OnMissing"));
        Assert.NotNull(instance.Script);
    }

    [Fact]
    public void EntriesListRegistrations()
    {
        var overrides = new ScriptOverrideRegistry(Gate());
        overrides.Register("QuestScript", "Event", 2, _ => OverrideResult.Declined());
        overrides.Register("QuestScript", "OnInit", 0, _ => OverrideResult.Declined());

        var entries = overrides.Entries.OrderBy(entry => entry.Function).ToArray();

        Assert.Equal(new[] { ("QuestScript", "Event", 2), ("QuestScript", "OnInit", 0) }, entries);
    }

    [Fact]
    public void RegisteredFiresOncePerSuccessfulRegister()
    {
        var overrides = new ScriptOverrideRegistry(Gate());
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
        var overrides = new ScriptOverrideRegistry(Gate());
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
