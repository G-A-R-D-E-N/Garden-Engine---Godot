using System;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Quest;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class RadicleStartupStagesTests
{
    private static QuestStartupInput Quest(string name) => new(name, true, Array.Empty<string>(), 0, 0);

    [Fact]
    public void NoInputsIdentifiesNothingWithAnEmptyReport()
    {
        var result = RadicleStartupStages.Identify(Array.Empty<QuestStartupInput>());

        Assert.Empty(result.Stages);
        Assert.Equal((0, 0, string.Empty), (result.Unresolved, result.Refused, result.Report));
    }

    [Fact]
    public void QuestsThatCarryNoStageListAreLeftOutWithoutAskingTheNativeLibrary()
    {
        var result = RadicleStartupStages.Identify(new[] { Quest("A"), Quest("B") });

        Assert.Empty(result.Stages);
        Assert.Equal((0, 0, string.Empty), (result.Unresolved, result.Refused, result.Report));
    }
}
