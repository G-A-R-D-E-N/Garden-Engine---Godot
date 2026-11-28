using System.Text;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using RadicleEnvironment = Radicle.Interop.RadicleEnvironment;
using OpenCommonwealth.Tests;
using Xunit;

namespace OpenCw.Papyrus.Tests;

[Trait("Category", "RealData")]
public sealed class Mq101ProofRealTests
{
    private const int PumpRounds = 64;

    [RealDataFact]
    public void Mq101StartsOnTheRadicleHostWithItsRealScripts()
    {
        var report = new StringBuilder();
        var output = Environment.GetEnvironmentVariable("MQ101_PROOF_OUT");
        if (string.IsNullOrEmpty(output)) output = Path.Combine(AppContext.BaseDirectory, "mq101-proof.txt");
        try
        {
            RunProof(report);
        }
        catch (Exception failure)
        {
            report.AppendLine($"failed before the report was complete: {failure.GetType().Name}: {failure.Message}");
            throw;
        }
        finally
        {
            File.WriteAllText(output, report.ToString());
        }
    }

    private static void RunProof(StringBuilder report)
    {
        var data = RegistryCorpusRealTests.DataDirectory!;
        using var environment = RadicleEnvironment.Load(new[] { Path.Combine(data, "Fallout4.esm") });
        var record = environment.FindEditorId("MQ101", "QUST") ?? throw new InvalidOperationException("MQ101 is not in Fallout4.esm");
        var key = environment.Info(record).FormKey.ToString();
        var input = RadicleQuestStartup.Build(environment, new HashSet<string> { key }).SingleOrDefault(row => row.Quest == key)
            ?? throw new InvalidOperationException($"MQ101 ({key}) has no quest startup row");
        input = input with { StartGameEnabled = true };

        var archives = Fo4Data.Archives(data);
        using var scripts = OpenedScriptSet.Open(new[] { data }, archives);
        using var cache = new TempCache();
        ScriptCacheBuilder.Build(scripts, data, cache.Root, new[] { input });
        using var session = GameSession.Load(cache.Root);
        session.BindQuestNatives(session.QuestStartup.Select(row => row.Quest));
        var result = session.StartNewGame();
        for (var pump = 0; pump < PumpRounds; pump++)
        {
            session.RunQueuedFragments();
            session.Host.RunStep(PapyrusHostDefaults.StepBudget);
        }
        var instances = session.Quests.InstancesFor(key).ToList();

        report.AppendLine($"MQ101 proof, Fallout4.esm, quest {key}");
        report.AppendLine($"start game enabled: {input.StartGameEnabled}; fragments: {input.Fragments}; aliases: {input.Aliases}; stages: {input.Stages?.Count ?? 0}");
        report.AppendLine($"scripts on the row ({input.Scripts.Count}): {string.Join(", ", input.Scripts)}");
        report.AppendLine($"unsupported: {string.Join("; ", input.Unsupported ?? Array.Empty<string>())}");
        report.AppendLine($"startup: applied {result.Applied}, started {result.Started}, attached scripts {result.AttachedScripts}");
        foreach (var reason in result.Reasons) report.AppendLine($"reason: {reason}");
        report.AppendLine($"quest state: {session.QuestStages.StateOf(key)}; stage: {session.QuestStages.StageOf(key)}");
        foreach (var instance in instances)
            report.AppendLine($"instance: {instance.ScriptName}, {instance.Lifecycle}, failure {instance.Failure ?? "none"}, {instance.Properties.Count} properties");
        report.AppendLine($"failed initializations: {session.FailedInitializations}; fragments run: {session.FragmentsRun}; fragments refused: {session.FragmentsRefused}");

        Assert.True(result.Applied, string.Join("; ", result.Reasons));
        Assert.NotEmpty(instances);
        Assert.All(instances, instance => Assert.True(instance.Failure is null, $"{instance.ScriptName}: {instance.Failure}"));
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(key));
    }
}
