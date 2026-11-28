using System.Globalization;
using System.Text;
using Radicle.Interop;
using Record = Radicle.Interop.Record;
using Xunit;

namespace OpenCw.Papyrus.Tests;

[Trait("Category", "RealData")]
public sealed class MovementTypeRealDataTests
{
    private static readonly string[] SlotRecords =
    {
        "NPC_Default_MT", "NPC_Sneaking", "NPC_Swimming_MT", "NPC_Sighted_MT", "PowerArmor_Player_Default_MT",
    };

    private static readonly string[] Directions = { "Forward", "Back", "Left", "Right" };

    private static readonly string[] Gaits = { "Walk", "Run", "Sprint" };

    [RealDataFact]
    public void EveryPlayerMovementSlotRecordIsReadableFromTheMaster()
    {
        using var environment = RadicleEnvironment.Load(new[] { Path.Combine(RegistryCorpusRealTests.DataDirectory!, "Fallout4.esm") });
        WriteMeasurements(environment);

        foreach (var editorId in SlotRecords)
        {
            var record = environment.FindEditorId(editorId, "MOVT");
            Assert.True(record is not null, $"{editorId} is not a MOVT record in Fallout4.esm");
            foreach (var direction in Directions)
                foreach (var gait in Gaits)
                    Assert.True(Speed(environment, record!.Value, direction, gait) is { } value && float.IsFinite(value) && value >= 0f, $"{editorId} {direction}{gait} is not a finite non-negative float");
        }
    }

    [RealDataFact]
    public void TheDefaultNpcRecordHoldsTheVanillaForwardSpeeds()
    {
        using var environment = RadicleEnvironment.Load(new[] { Path.Combine(RegistryCorpusRealTests.DataDirectory!, "Fallout4.esm") });
        var record = environment.FindEditorId("NPC_Default_MT", "MOVT") ?? throw new InvalidOperationException("NPC_Default_MT is not a MOVT record in Fallout4.esm");

        Assert.Equal(80.10f, Speed(environment, record, "Forward", "Walk")!.Value, 0.01f);
        Assert.Equal(370.00f, Speed(environment, record, "Forward", "Run")!.Value, 0.01f);
        Assert.Equal(500.00f, Speed(environment, record, "Forward", "Sprint")!.Value, 0.01f);
    }

    private static readonly (string EditorId, float[] Speeds)[] HardCodedTable =
    {
        ("NPC_Default_MT", new[] { 80.1f, 370f, 500f, 71.93f, 260f, 0f, 80.09f, 370f, 0f, 79.75f, 370f, 0f }),
        ("NPC_Sneaking", new[] { 90f, 205f, 388.5f, 90f, 205f, 0f, 90f, 205f, 0f, 90f, 205f, 0f }),
        ("NPC_Swimming_MT", new[] { 80.1f, 370f, 500f, 71.93f, 205.25f, 0f, 80.09f, 370f, 0f, 79.75f, 370f, 0f }),
        ("NPC_Sighted_MT", new[] { 100.76f, 200f, 350f, 100.76f, 200f, 0f, 100.76f, 200f, 0f, 100.76f, 200f, 0f }),
        ("PowerArmor_Player_Default_MT", new[] { 112.48f, 311.66f, 497.06f, 112.48f, 311.66f, 0f, 112.48f, 311.66f, 0f, 112.48f, 311.66f, 0f }),
    };

    [RealDataFact]
    public void EveryHardCodedMovementSpeedMatchesTheMaster()
    {
        using var environment = RadicleEnvironment.Load(new[] { Path.Combine(RegistryCorpusRealTests.DataDirectory!, "Fallout4.esm") });

        foreach (var (editorId, speeds) in HardCodedTable)
        {
            var record = environment.FindEditorId(editorId, "MOVT") ?? throw new InvalidOperationException(editorId + " is not a MOVT record in Fallout4.esm");
            var index = 0;
            foreach (var direction in Directions)
                foreach (var gait in Gaits)
                {
                    var expected = speeds[index++];
                    if (expected > 0f)
                        Assert.Equal(expected, Speed(environment, record, direction, gait) ?? float.NaN, 0.01f);
                }
        }
    }

    private static float? Speed(RadicleEnvironment environment, Record record, string direction, string gait) =>
        float.TryParse(environment.GetField(record, "MOVT\\" + direction + gait), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static void WriteMeasurements(RadicleEnvironment environment)
    {
        var output = Environment.GetEnvironmentVariable("MOVT_MEASUREMENTS_OUT");
        if (string.IsNullOrEmpty(output)) output = Path.Combine(AppContext.BaseDirectory, "movt-measurements.txt");
        var report = new StringBuilder();
        foreach (var record in environment.LiveWinnersOf("MOVT").OrderBy(r => environment.Info(r).EditorId, StringComparer.Ordinal))
        {
            report.Append(environment.Info(record).EditorId);
            foreach (var direction in Directions)
                foreach (var gait in Gaits)
                    report.Append(' ').Append(direction).Append(gait).Append('=').Append(environment.GetField(record, "MOVT\\" + direction + gait) ?? "unreadable");
            report.AppendLine();
        }
        if (Path.GetDirectoryName(output) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
        File.WriteAllText(output, report.ToString());
    }
}
