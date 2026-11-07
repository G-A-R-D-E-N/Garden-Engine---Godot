using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class CellScriptSessionTests
{
    private static readonly (string Key, string Status)[] PolicyRows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.NativeTarget, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
    };

    private static EvidenceGate Gate()
    {
        var entries = PolicyRows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static CellScriptSession Session(out ScriptHost host)
    {
        var gate = Gate();
        host = new ScriptHost(Library(), gate);
        return new CellScriptSession(host);
    }

    private static ScriptLibrary Library()
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("RefScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("OnActivate", new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        return library;
    }

    [Fact]
    public void PlayedAndStreamedCellsShareOneHost()
    {
        var session = Session(out var host);
        var played = session.Bind("PlayedCell", "000800:Played.esp", "RefScript");
        var streamed = session.Bind("StreamedCell", "000900:Streamed.esp", "RefScript");

        Assert.Same(host, session.Host);
        Assert.Same(played, session.InstanceFor("000800:Played.esp"));
        Assert.Same(streamed, session.InstanceFor("000900:Streamed.esp"));
        Assert.Equal(2, session.AttachedCells);
        Assert.Equal(2, session.BoundInstances);
        Assert.Equal(2, host.Instances.Count);
    }

    [Fact]
    public void DetachParksAndReattachReusesTheInstance()
    {
        var session = Session(out var host);
        var instance = session.Bind("StreamedCell", "000900:Streamed.esp", "RefScript");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);

        var parked = session.DetachCell("StreamedCell");

        Assert.Same(instance, Assert.Single(parked));
        Assert.Null(session.InstanceFor("000900:Streamed.esp"));
        Assert.Equal(1, session.ParkedInstances);
        Assert.Equal(0, session.BoundInstances);

        var restored = session.ReattachCell("StreamedCell");

        Assert.Same(instance, Assert.Single(restored));
        Assert.Same(instance, session.InstanceFor("000900:Streamed.esp"));
        Assert.Equal(1, session.ReusedInstances);
        Assert.Equal(0, session.ParkedInstances);
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
    }

    [Fact]
    public void ActivationReachesTheScript()
    {
        var session = Session(out var host);
        var instance = session.Bind("PlayedCell", "000800:Played.esp", "RefScript");
        Assert.True(host.Initialize(instance).Completed);

        var activation = session.Activate("000800:Played.esp", "OnActivate");

        Assert.True(activation.Reached);
        Assert.NotNull(activation.Result);
        Assert.True(activation.Result!.Completed);
        Assert.Equal(1, session.Activations);
        Assert.Equal(0, session.RefusedActivations);
    }

    [Fact]
    public void ActivationForAParkedReferenceIsRefused()
    {
        var session = Session(out var host);
        var instance = session.Bind("StreamedCell", "000900:Streamed.esp", "RefScript");
        Assert.True(host.Initialize(instance).Completed);
        session.DetachCell("StreamedCell");

        var activation = session.Activate("000900:Streamed.esp", "OnActivate");

        Assert.False(activation.Reached);
        Assert.Contains("parked", activation.Reason!, StringComparison.Ordinal);
        Assert.Equal(1, session.RefusedActivations);
        Assert.Equal(0, session.Activations);
    }

    [Fact]
    public void ActivationForAnUnknownReferenceIsRefused()
    {
        var session = Session(out _);

        var activation = session.Activate("000A00:Unknown.esp", "OnActivate");

        Assert.False(activation.Reached);
        Assert.Contains("not bound", activation.Reason!, StringComparison.Ordinal);
        Assert.Equal(1, session.RefusedActivations);
    }

    [Fact]
    public void TickOnlyDelegatesToTheHost()
    {
        var session = Session(out var host);
        var instance = session.Bind("PlayedCell", "000800:Played.esp", "RefScript");
        Assert.True(host.Initialize(instance).Completed);
        host.Enqueue(instance, "OnActivate");

        var spent = session.Tick(16);

        Assert.True(spent > 0);
        Assert.True(spent <= 16);
        Assert.Equal(0, host.ReadyCount);
    }

    [Fact]
    public void CellSessionRunsItsEventThroughTheInterfaceTypedHost()
    {
        IScriptHost host = new ScriptHost(Library(), Gate());
        var session = new CellScriptSession(host);
        var instance = session.Bind("PlayedCell", "000800:Played.esp", "RefScript");
        Assert.True(host.Initialize(instance).Completed);

        var activation = session.Activate("000800:Played.esp", "OnActivate");

        Assert.True(activation.Reached);
        Assert.True(activation.Result!.Completed);
        Assert.Same(host, session.Host);
    }
}
