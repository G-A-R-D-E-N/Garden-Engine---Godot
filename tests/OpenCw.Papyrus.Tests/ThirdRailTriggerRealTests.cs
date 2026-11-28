using System.Text;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using RadicleEnvironment = Radicle.Interop.RadicleEnvironment;
using OpenCommonwealth.Tests;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ThirdRailTriggerRealTests
{
    [RealDataFact]
    public void TheThirdRailTriggerSetsTheMacCreadyQuestStage()
    {
        var log = new StringBuilder();
        try { Run(log); }
        finally { Console.Error.WriteLine(log.ToString()); }
    }

    private static void Run(StringBuilder log)
    {
        var data = RegistryCorpusRealTests.DataDirectory!;
        using var environment = RadicleEnvironment.Load(new[] { Path.Combine(data, "Fallout4.esm") });
        var inputs = RadicleQuestStartup.Build(environment);
        var archives = Fo4Data.Archives(data);
        using var scripts = OpenedScriptSet.Open(new[] { data }, archives);
        using var temp = new TempCache(); var cacheRoot = temp.Root;
        if (!File.Exists(ScriptCacheLayout.ManifestPath(cacheRoot))) { Directory.CreateDirectory(cacheRoot); ScriptCacheBuilder.Build(scripts, data, cacheRoot, inputs); }
        using var session = GameSession.Load(cacheRoot);
        session.BindQuestNatives(session.QuestStartup.Select(row => row.Quest));
        var start = session.StartNewGame();
        log.AppendLine($"startup applied={start.Applied} started={start.Started}");
        var core = new CoreNativeAdapters(session.Host, session.Gate, Random.Shared.NextDouble, () => null, () => "000014:Fallout4.esm", m => log.AppendLine("core: " + m)).Bind();
        foreach (var n in core.Natives.Where(n => n.State != AdapterState.Bound)) log.AppendLine($"core not bound {n.Family.Native}: {n.Reason}");
        var deleted = new List<string>();
        var enable = new EnableNativeAdapters(session.Host, session.Gate, (_, _) => { }, deleted.Add).Bind();
        foreach (var n in enable.Natives.Where(n => n.State != AdapterState.Bound)) log.AppendLine($"enable not bound {n.Family.Native}: {n.Reason}");
        var form = new FormLookupNativeAdapters(session.Host, session.Gate, _ => null, _ => null).Bind();
        foreach (var n in form.Natives.Where(n => n.State != AdapterState.Bound)) log.AppendLine($"form not bound {n.Family.Native}: {n.Reason}");
        log.AppendLine("quest 027411 state " + session.QuestStages.StateOf("027411:Fallout4.esm") + " stage " + session.QuestStages.StageOf("027411:Fallout4.esm"));

        var area = PapyrusOwnerId.ForPlacedReference("03251C:Fallout4.esm", "03468C:Fallout4.esm");
        var player = PapyrusOwnerId.ForPlacedReference("000007:Fallout4.esm", "000014:Fallout4.esm");
        const string meta = "[{\"name\":\"defaultrefontriggerenter\",\"properties\":[{\"name\":\"PlayerTriggerOnly\",\"type\":\"Bool\",\"value\":\"true\"},{\"name\":\"StageToSet\",\"type\":\"Int\",\"value\":\"5\"},{\"name\":\"myQuest\",\"type\":\"Object\",\"value\":\"027411:Fallout4.esm\"}]}]";
        var attach = session.AttachReference("cell", area, meta);
        log.AppendLine($"attach refusal={attach.Refusal} ready={attach.Ready}");
        var host = (RadicleScriptHost)session.Host;
        var batch = session.Trigger(area, player, true, "Actor");
        log.AppendLine($"trigger delivered={batch.Delivered} refused={batch.Refused} nohandler={batch.NoHandler}");
        foreach (var o in batch.Outcomes) log.AppendLine($"outcome {o.Result} {o.Reason}");
        log.AppendLine("lastNativeFailure=" + host.LastNativeFailure);
        log.AppendLine("summary=" + host.LastFailureSummary);
        for (var i = 0; i < 8; i++) { session.RunQueuedFragments(); host.RunStep(10000); }
        log.AppendLine("quest 027411 state " + session.QuestStages.StateOf("027411:Fallout4.esm") + " stage " + session.QuestStages.StageOf("027411:Fallout4.esm") + " done5=" + session.QuestStages.StageDone("027411:Fallout4.esm", 5));
        Assert.True(session.QuestStages.StageDone("027411:Fallout4.esm", 5), log.ToString());
        Assert.Equal(1, batch.Delivered);
        Assert.Equal(0, batch.Refused);
        Assert.Equal(new[] { "03468C:Fallout4.esm" }, deleted.ToArray());
    }
}
