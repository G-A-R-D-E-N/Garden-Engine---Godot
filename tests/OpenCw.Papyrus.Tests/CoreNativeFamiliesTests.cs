using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class CoreNativeFamiliesTests
{
    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.NativeTarget, "ENGINE_POLICY"),
        (NativeSet.TraceKey, "ENGINE_POLICY"),
        (CoreNativeFamilies.MathKey, "ENGINE_POLICY"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides)
    {
        var rows = Rows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            rows[pair.Key] = pair.Status;
        }
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static ScriptHost Host()
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("RefScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        return new ScriptHost(library, Gate());
    }

    [Fact]
    public void AnOwnedFamilyIsBoundWhole()
    {
        var report = CoreNativeFamilies.Bind(Host(), Gate());

        var math = report.Families.Single(family => family.Family == NativeFamily.Math);
        Assert.Equal(FamilyState.Bound, math.State);
        Assert.Equal(5, math.Members);
        Assert.Equal(2, report.Bound);
        Assert.Equal(8, report.Members);
    }

    [Fact]
    public void AMissingSystemFamilyIsRefusedAndCounted()
    {
        var report = CoreNativeFamilies.Bind(Host(), Gate());

        var formList = report.Families.Single(family => family.Family == NativeFamily.FormList);
        Assert.Equal(FamilyState.MissingSystem, formList.State);
        Assert.Equal(0, formList.Members);
        Assert.Contains("gameplay system", formList.Reason, StringComparison.Ordinal);
        Assert.Equal(9, report.Refused);
    }

    [Fact]
    public void NoFamilyIsHalfBound()
    {
        var report = CoreNativeFamilies.Bind(Host(), Gate());

        foreach (var family in report.Families.Where(family => family.State != FamilyState.Bound))
        {
            Assert.Equal(0, family.Members);
            Assert.Equal(0, CoreNativeFamilies.Catalogue[family.Family].Length);
        }
        Assert.Equal(report.Families.Count, report.Bound + report.Refused);
    }

    [Fact]
    public void AnUnprovenFamilyBindsNothing()
    {
        var report = CoreNativeFamilies.Bind(Host(), Gate((CoreNativeFamilies.MathKey, "UNPROVEN")));

        var math = report.Families.Single(family => family.Family == NativeFamily.Math);
        Assert.Equal(FamilyState.Unproven, math.State);
        Assert.Equal(0, math.Members);
        Assert.Equal(1, report.Bound);
    }

    [Fact]
    public void ABoundMemberRunsAndRefusesBadArguments()
    {
        var host = Host();
        CoreNativeFamilies.Bind(host, Gate());
        var math = CoreNativeFamilies.Catalogue[NativeFamily.Math];

        var abs = math.Single(member => member.Native == new NativeId("Math", "Abs"));
        Assert.Equal(7, abs.Body(new[] { PapyrusValue.FromInt(-7) }).Integer);
        Assert.Equal(7.5, abs.Body(new[] { PapyrusValue.FromFloat(-7.5) }).Real);
        Assert.Throws<InvalidOperationException>(() => abs.Body(new[] { PapyrusValue.FromString("x") }));

        var min = math.Single(member => member.Native == new NativeId("Math", "Min"));
        Assert.Equal(2.0, min.Body(new[] { PapyrusValue.FromInt(2), PapyrusValue.FromFloat(9.5) }).Real);
    }

    [Fact]
    public void TheReportIsDeterministicAndCountsEveryFamily()
    {
        var first = CoreNativeFamilies.Bind(Host(), Gate());
        var second = CoreNativeFamilies.Bind(Host(), Gate());

        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(Enum.GetValues<NativeFamily>().Length, first.Families.Count);
        Assert.Equal(64, first.Digest().Length);
    }

    [Fact]
    public void OnlyGardenOwnedFamiliesAreEverBound()
    {
        var report = CoreNativeFamilies.Bind(Host(), Gate());

        foreach (var family in report.Families.Where(family => family.State == FamilyState.Bound))
        {
            Assert.Contains(family.Family, CoreNativeFamilies.GardenOwned);
        }
    }
}
