using GameDataGuard;
using Xunit;

namespace GuardTools.Tests;

public class DoorwayGuardTests
{
    private const string Bypass = "using var a = Ba2Archive.Open(path);";

    private static List<string> Run(Dictionary<string, string> files, params (string Id, string Path, int Count)[] baseline) =>
        Doorway.Check(files.Keys, path => files[path], baseline.ToDictionary(row => (row.Id, row.Path), row => row.Count));

    [Fact]
    public void NewBypassInAnUnlistedFileFails()
    {
        string problem = Assert.Single(Run(new() { ["opencw/scripts/New.cs"] = "class A\n{\n    " + Bypass + "\n}" }));
        Assert.StartsWith("G1 opencw/scripts/New.cs:3: ", problem);
        Assert.Contains("1 hit(s), baseline allows 0", problem);
        Assert.Contains("use Radicle/the wrapper: ", problem);
    }

    [Fact]
    public void BaselineHitPasses()
    {
        Assert.Empty(Run(new() { ["opencw/scripts/Old.cs"] = Bypass }, ("G1", "opencw/scripts/Old.cs", 1)));
    }

    [Fact]
    public void ShrunkCountDemandsALowerBaseline()
    {
        string problem = Assert.Single(Run(new() { ["opencw/scripts/Old.cs"] = Bypass }, ("G1", "opencw/scripts/Old.cs", 3)));
        Assert.Equal("G1 opencw/scripts/Old.cs: 1 hit(s), baseline has 3; lower the baseline to 1", problem);
    }

    [Fact]
    public void StaleBaselineEntriesAreFlagged()
    {
        string clean = Assert.Single(Run(new() { ["opencw/scripts/Old.cs"] = "class A {}" }, ("G1", "opencw/scripts/Old.cs", 3)));
        Assert.Equal("G1 opencw/scripts/Old.cs: no hits; remove stale baseline entry", clean);
        string gone = Assert.Single(Run(new() { ["opencw/scripts/Other.cs"] = "class A {}" }, ("G1", "opencw/scripts/Deleted.cs", 1)));
        Assert.Equal("G1 opencw/scripts/Deleted.cs: no hits; remove stale baseline entry", gone);
        string wrongRule = Assert.Single(Run(new() { ["opencw/scripts/Old.cs"] = "class A {}" }, ("G13", "opencw/scripts/Old.cs", 1)));
        Assert.Contains("remove stale baseline entry", wrongRule);
    }

    [Fact]
    public void ARegexTimeoutBlocksNamingTheRuleAndFile()
    {
        string text = string.Concat(Enumerable.Repeat("regex a. ", 30000));
        var problems = Run(new() { ["tools/a.py"] = text });
        Assert.Contains(problems, problem => problem.StartsWith("G14 tools/a.py:1: regex timed out"));
    }

    [Fact]
    public void GrownCountFails()
    {
        string text = Bypass + "\r\n" + Bypass + "\r\n" + Bypass;
        string problem = Assert.Single(Run(new() { ["opencw/scripts/Old.cs"] = text }, ("G1", "opencw/scripts/Old.cs", 2)));
        Assert.StartsWith("G1 opencw/scripts/Old.cs:1,2,3: ", problem);
        Assert.Contains("3 hit(s), baseline allows 2", problem);
    }

    [Fact]
    public void BaselineEntryDoesNotCoverAnotherFileOrAnotherPattern()
    {
        var files = new Dictionary<string, string> { ["opencw/scripts/Old.cs"] = Bypass, ["opencw/scripts/Other.cs"] = Bypass };
        Assert.StartsWith("G1 opencw/scripts/Other.cs", Assert.Single(Run(files, ("G1", "opencw/scripts/Old.cs", 1))));
        var other = Run(new() { ["opencw/scripts/Old.cs"] = Bypass }, ("G3", "opencw/scripts/Old.cs", 1));
        Assert.Equal(2, other.Count);
        Assert.StartsWith("G1 opencw/scripts/Old.cs:1: ", other[0]);
        Assert.Equal("G3 opencw/scripts/Old.cs: no hits; remove stale baseline entry", other[1]);
    }

    [Fact]
    public void SeveralMatchesOnOneLineAreCounted()
    {
        var files = new Dictionary<string, string> { ["opencw/assets/water/z.json"] = "{\"a\":\"00003C:Fallout4.esm\",\"b\":\"00003D:Fallout4.esm\"}" };
        Assert.Contains("2 hit(s), baseline allows 1", Assert.Single(Run(files, ("G12", "opencw/assets/water/z.json", 1))));
        Assert.Empty(Run(files, ("G12", "opencw/assets/water/z.json", 2)));
    }

    [Theory]
    [InlineData("G1", "opencw/scripts/A.cs", Bypass)]
    [InlineData("G2", "opencw/scripts/A.cs", @"var all = Directory.GetFiles(data, ""*Textures*.ba2"");")]
    [InlineData("G3", "opencw/scripts/A.cs", @"var p = Path.Combine(data, ""Fallout4 - Meshes.ba2"");")]
    [InlineData("G3", "opencw/scripts/A.cs", @"var p = ""DLCRobot - Main.ba2"";")]
    [InlineData("G4", "opencw/scripts/A.cs", "using var env = RadicleEnvironment.Load(order);")]
    [InlineData("G5", "opencw/Services/A.cs", @"var magic = ""BTDX"";")]
    [InlineData("G5", "opencw/Services/A.cs", @"var magic = ""TES4"";")]
    [InlineData("G5", "opencw/Services/A.cs", "uint magic = 0xFA57C0DE;")]
    [InlineData("G6", "opencw/Services/A.cs", @"NativeLibrary.Load(""radicle.dll"");")]
    [InlineData("G6", "opencw/Services/A.cs", @"[DllImport(""radicle_abi"")] static extern int Abi();")]
    [InlineData("G7", "opencw/Services/A.cs", @"var bytes = File.ReadAllBytes(Path.Combine(data, ""Door.nif""));")]
    [InlineData("G8", "tools/a.py", "subprocess.run(['texconv', 'x.dds'])")]
    [InlineData("G8", "opencw/Services/A.cs", @"var jar = ""hkxpack-cli.jar"";")]
    [InlineData("G9", "tools/a.ps1", "$data = 'D:/SteamLibrary/steamapps/common/Fallout 4/Data'")]
    [InlineData("G10", "tools/a.py", "data = os.environ['GE_FO4_DATA']")]
    [InlineData("G10", "opencw/Services/A.cs", @"var d = Environment.GetEnvironmentVariable(""OC_FO4_DATA"");")]
    [InlineData("G10", "tools/a.ps1", "$env:GE_FO4_DATA = $Fo4Data")]
    [InlineData("G10", "opencw/tools/A.cs", @"var d = OS.GetEnvironment(""FALLOUT4_DATA"");")]
    [InlineData("G11", "tests/X/X.csproj", @"<PackageReference Include=""Dapper"" Version=""2.1.35"" />")]
    [InlineData("G12", "opencw/data/lighting/x.tsv", "cells/a\tlamp_0FBE86_Fallout4_esm\tdiffuser")]
    [InlineData("G12", "opencw/assets/water/x.json", @"{""worlds"":{""00003C:Fallout4.esm"":{}}}")]
    [InlineData("G13", "opencw/Services/A.cs", "var v = BinaryPrimitives.ReadInt32LittleEndian(bytes);\n// parses meshes/a.nif")]
    [InlineData("G13", "opencw/Services/A.cs", "using var r = new BinaryReader(stream);\n// reads a.ba2")]
    [InlineData("G14", "opencw/Services/A.cs", @"static readonly Regex Model = new(@""\.nif$"");")]
    [InlineData("G14", "opencw/Services/A.cs", @"static readonly Regex Plugin = new(@""\.(esm|esp)$"");")]
    [InlineData("G14", "tools/a.py", "rx = re.compile(r'\\.hkx$')")]
    public void EveryPatternFiresOnAnUnlistedFile(string id, string path, string text)
    {
        var problems = Run(new() { [path] = text });
        Assert.Contains(problems, problem => problem.StartsWith(id + " " + path + ":1: "));
    }

    [Theory]
    [InlineData("opencw/Services/Ba2/Ba2ExtractorService.cs", Bypass)]
    [InlineData("tests/GuardTools.Tests/Own.cs", Bypass)]
    [InlineData("tools/GameDataGuard/Checks.cs", @"var p = ""Data/Fallout4 - Interface.ba2""; var m = ""BTDX"";")]
    [InlineData("opencw/Services/Core/GameLoader.cs", "return RadicleEnvironment.Load(order);")]
    [InlineData("opencw/Services/Mo2ProfileLoader.cs", "return RadicleEnvironment.Load(order);")]
    [InlineData("opencw/tools/probes/P.cs", "using var env = RadicleEnvironment.Load(order);")]
    [InlineData("tests/OpenCw.Parity.Tests/T.cs", "using var env = RadicleEnvironment.Load(order);")]
    [InlineData("opencw/Services/Records/RadicleNativePath.cs", @"var name = ""radicle.dll"";")]
    [InlineData("tools/a.ps1", "$env:FO4_DATA_DIR")]
    [InlineData("opencw/Services/A.cs", @"var d = Environment.GetEnvironmentVariable(""FO4_DATA_DIR"");")]
    [InlineData("tests/X/X.csproj", @"<PackageReference Include=""xunit"" Version=""2.9.3"" />")]
    [InlineData("tests/X/X.csproj", @"<PackageReference Include=""Newtonsoft.Json"" Version=""13.0.3"" />")]
    [InlineData("tests/X/X.csproj", @"<ProjectReference Include=""..\Other\Other.csproj"" />")]
    [InlineData("opencw/Services/A.cs", "var v = BinaryPrimitives.ReadInt32LittleEndian(bytes);")]
    [InlineData("opencw/Services/Terrain/GrassMapStore.cs", "// grass.dds\nvar v = BinaryPrimitives.ReadInt32LittleEndian(bytes);")]
    [InlineData("tests/Hlod.Tests/PageTests.cs", "// a.nif\nvar v = BitConverter.ToInt32(page, 4);")]
    [InlineData("opencw/scripts/A.cs", """var p = "LIGH\\Radius";""")]
    [InlineData("opencw/data/lighting/x.tsv", "cells/a\tlamp\tdiffuser")]
    [InlineData("opencw/Services/A.cs", @"static readonly Regex Uniform = new(@""^uniform\s+(\w+)"");")]
    [InlineData("README.md", Bypass)]
    public void AllowListedAndCleanFilesPass(string path, string text)
    {
        Assert.Empty(Run(new() { [path] = text }));
    }

    [Fact]
    public void BaselineLinesAreParsed()
    {
        var baseline = Doorway.Baseline(["# note", "", "G1 opencw/my file.cs 3", "  G12 opencw/data/a.tsv 2084  "]);
        Assert.Equal(3, baseline[("G1", "opencw/my file.cs")]);
        Assert.Equal(2084, baseline[("G12", "opencw/data/a.tsv")]);
        Assert.Equal(2, baseline.Count);
    }

    [Theory]
    [InlineData("G1 opencw/a.cs")]
    [InlineData("G1 opencw/a.cs 0")]
    [InlineData("G1 opencw/a.cs many")]
    [InlineData("G1")]
    public void MalformedBaselineLineIsRejected(string line)
    {
        Assert.Throws<InvalidDataException>(() => Doorway.Baseline([line]));
    }

    [Fact]
    public void DuplicateBaselineLineIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Doorway.Baseline(["G1 opencw/a.cs 1", "G1 opencw/a.cs 2"]));
    }

    private static Dictionary<(string Id, string Path), int> Map(params (string Id, string Path, int Count)[] rows) =>
        rows.ToDictionary(row => (row.Id, row.Path), row => row.Count);

    [Fact]
    public void ANewHeadEntryFailsAgainstTheTrustedBase()
    {
        string problem = Assert.Single(Doorway.Compare(Map(("G1", "a/Old.cs", 1)), Map(("G1", "a/Old.cs", 1), ("G1", "a/New.cs", 1))));
        Assert.StartsWith("G1 a/New.cs: not in the trusted base baseline", problem);
        string otherRule = Assert.Single(Doorway.Compare(Map(("G1", "a/Old.cs", 1)), Map(("G3", "a/Old.cs", 1))));
        Assert.StartsWith("G3 a/Old.cs: not in the trusted base baseline", otherRule);
    }

    [Fact]
    public void ARaisedHeadCountFailsAgainstTheTrustedBase()
    {
        string problem = Assert.Single(Doorway.Compare(Map(("G1", "a/Old.cs", 2)), Map(("G1", "a/Old.cs", 3))));
        Assert.StartsWith("G1 a/Old.cs: baseline raised to 3 from 2", problem);
    }

    [Fact]
    public void LoweredRemovedAndEqualHeadEntriesPassAgainstTheTrustedBase()
    {
        var baseline = Map(("G1", "a/Old.cs", 3), ("G3", "a/Other.cs", 2));
        Assert.Empty(Doorway.Compare(baseline, Map(("G1", "a/Old.cs", 1), ("G3", "a/Other.cs", 2))));
        Assert.Empty(Doorway.Compare(baseline, Map(("G3", "a/Other.cs", 2))));
        Assert.Empty(Doorway.Compare(baseline, Map()));
        Assert.Empty(Doorway.Compare(baseline, baseline));
    }

    [Fact]
    public void ARenamedEntryIsComparedWithTheOldPath()
    {
        var baseline = Map(("G1", "a/Old.cs", 2));
        var renames = new Dictionary<string, string> { ["b/New.cs"] = "a/Old.cs" };
        Assert.Empty(Doorway.Compare(baseline, Map(("G1", "b/New.cs", 2)), renames));
        Assert.Empty(Doorway.Compare(baseline, Map(("G1", "b/New.cs", 1)), renames));
        string raised = Assert.Single(Doorway.Compare(baseline, Map(("G1", "b/New.cs", 3)), renames));
        Assert.StartsWith("G1 b/New.cs: baseline raised to 3 from 2 at a/Old.cs", raised);
        string otherRule = Assert.Single(Doorway.Compare(baseline, Map(("G3", "b/New.cs", 1)), renames));
        Assert.StartsWith("G3 b/New.cs: not in the trusted base baseline", otherRule);
    }

    [Fact]
    public void AMovedEntryWithoutARenameFails()
    {
        var baseline = Map(("G1", "a/Old.cs", 2));
        Assert.Single(Doorway.Compare(baseline, Map(("G1", "b/New.cs", 2))));
        Assert.Single(Doorway.Compare(baseline, Map(("G1", "b/New.cs", 2)), new Dictionary<string, string>()));
        Assert.Single(Doorway.Compare(baseline, Map(("G1", "b/New.cs", 2)), new Dictionary<string, string> { ["c/Other.cs"] = "a/Old.cs" }));
    }

    [Fact]
    public void BaselineTextWithCrlfAndNoTrailingNewlineIsParsed()
    {
        var baseline = Doorway.Baseline("# note\r\nG1 a/Old.cs 2\r\nG3 a/My File.cs 1".Split('\n'));
        Assert.Equal(2, baseline[("G1", "a/Old.cs")]);
        Assert.Equal(1, baseline[("G3", "a/My File.cs")]);
        Assert.Equal(2, baseline.Count);
    }

    [Fact]
    public void RenameLinesAreParsedAsNewToOld()
    {
        var renames = Doorway.Renames("a/Old.cs\tb/New.cs\r\n\r\na/My File.cs\tb/My File.cs".Split('\n'));
        Assert.Equal("a/Old.cs", renames["b/New.cs"]);
        Assert.Equal("a/My File.cs", renames["b/My File.cs"]);
        Assert.Equal(2, renames.Count);
        Assert.Empty(Doorway.Renames([]));
    }

    [Theory]
    [InlineData("a/Old.cs")]
    [InlineData("a/Old.cs\t")]
    [InlineData("\tb/New.cs")]
    [InlineData("a/Old.cs\tb/New.cs\tc/Extra.cs")]
    public void MalformedRenameLineIsRejected(string line)
    {
        Assert.Throws<InvalidDataException>(() => Doorway.Renames([line]));
    }

    [Fact]
    public void DuplicateRenameTargetIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Doorway.Renames(["a/One.cs\tb/New.cs", "a/Two.cs\tb/New.cs"]));
    }

    [Fact]
    public void MissingBaseFileBlocksTheRun()
    {
        string root = Path.Combine(Path.GetTempPath(), "doorway-no-base-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "tools"));
        try
        {
            File.WriteAllText(Path.Combine(root, Doorway.BaselinePath), "G1 a/Old.cs 1\n");
            Assert.Throws<FileNotFoundException>(() => Doorway.Repo(root, Path.Combine(root, "missing-base.txt")));
            File.WriteAllText(Path.Combine(root, "base.txt"), "G1 a/Old.cs 1\nG1 a/Old.cs 2\n");
            Assert.Throws<InvalidDataException>(() => Doorway.Repo(root, Path.Combine(root, "base.txt")));
            Assert.Throws<FileNotFoundException>(() => Doorway.Repo(root, null, Path.Combine(root, "missing-renames.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MissingBaselineFileBlocksTheRun()
    {
        string root = Path.Combine(Path.GetTempPath(), "doorway-no-baseline-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<FileNotFoundException>(() => Doorway.Repo(root));
    }

    [Fact]
    public void EveryRuleHasAnIdReasonAndWrapper()
    {
        Assert.Equal(14, Doorway.Rules.Length);
        Assert.Equal(Doorway.Rules.Length, Doorway.Rules.Select(rule => rule.Id).Distinct().Count());
        Assert.All(Doorway.Rules, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Why) || string.IsNullOrWhiteSpace(rule.Use) || string.IsNullOrWhiteSpace(rule.Pattern)));
    }
}
