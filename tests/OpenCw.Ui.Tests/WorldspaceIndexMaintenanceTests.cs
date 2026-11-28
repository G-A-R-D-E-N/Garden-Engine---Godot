using Newtonsoft.Json.Linq;
using OpenCommonwealth.Services;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WorldspaceIndexMaintenanceTests
{
    [Fact]
    public void Unreadable_index_is_preserved_and_rebuilt_without_losing_other_worldspaces()
    {
        var rootAbs = TempDir();
        try
        {
            var goodneighbor = new FormKey("Fallout4.esm", 0x0ABCDE).ToString();
            WriteManifest(rootAbs, "DiamondCity", new FormKey("Fallout4.esm", 0x123456).ToString(), "DiamondCity", "Diamond City");
            WriteManifest(rootAbs, "Goodneighbor", goodneighbor, "Goodneighbor", "Goodneighbor");
            var indexPath = Path.Combine(rootAbs, WorldspaceIndexMaintenance.IndexFile);
            const string corrupt = "{\"worldspaces\": [{\"dir\": \"DiamondCity\"},";
            File.WriteAllText(indexPath, corrupt);

            WorldspaceIndexMaintenance.Update(rootAbs, "Vault111", new FormKey("Fallout4.esm", 0x0F00D5).ToString(), "Vault111", "Vault 111");

            var entries = (JArray)JObject.Parse(File.ReadAllText(indexPath))["worldspaces"]!;
            var dirs = entries.Select(e => (string)e["dir"]!).OrderBy(d => d, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "DiamondCity", "Goodneighbor", "Vault111" }, dirs);
            var rebuilt = (JObject)entries.Single(e => (string?)e["dir"] == "Goodneighbor");
            Assert.Equal(goodneighbor, (string?)rebuilt["formKey"]);

            var backupAbs = Directory.EnumerateFiles(rootAbs, "index.json.corrupt-*").Single();
            Assert.Equal(corrupt, File.ReadAllText(backupAbs));
        }
        finally
        {
            Directory.Delete(rootAbs, recursive: true);
        }
    }

    static void WriteManifest(string rootAbs, string dirName, string formKey, string editorId, string name)
    {
        var dirAbs = Path.Combine(rootAbs, dirName);
        Directory.CreateDirectory(dirAbs);
        File.WriteAllText(Path.Combine(dirAbs, WorldspaceIndexMaintenance.ManifestFile), new JObject
        {
            ["formKey"] = formKey,
            ["editorId"] = editorId,
            ["name"] = name,
            ["cells"] = new JArray(),
        }.ToString());
    }

    static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "opencw-worldspace-index-" + Guid.NewGuid().ToString("N"));
}