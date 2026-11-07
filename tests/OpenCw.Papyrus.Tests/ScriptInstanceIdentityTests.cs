using System;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ScriptInstanceIdentityTests
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
    };

    private static EvidenceGate Gate()
    {
        var entries = Rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    [Fact]
    public void AttachedInstanceExposesItsScriptNameAndDefinedFunctions()
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("IdentityScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("OnActivate", new[] { PapyrusInstruction.Return() }, ParameterCount: 1),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, Gate());
        var instance = host.Attach("IdentityScript");

        Assert.Equal("IdentityScript", instance.ScriptName);
        Assert.True(instance.DefinesFunction("OnActivate"));
        Assert.True(instance.DefinesFunction("onactivate"));
        Assert.False(instance.DefinesFunction("OnMissing"));
    }
}
