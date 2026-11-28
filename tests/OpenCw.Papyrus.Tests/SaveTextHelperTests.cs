using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Papyrus.Save;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class SaveTextHelperTests
{
    [Fact]
    public void Text_returns_only_non_empty_string_values()
    {
        var node = new JsonObject { ["name"] = "quest", ["empty"] = "", ["number"] = JsonValue.Create(3) };

        Assert.Equal("quest", PapyrusStateSave.Text(node, "name"));
        Assert.Null(PapyrusStateSave.Text(node, "empty"));
        Assert.Null(PapyrusStateSave.Text(node, "number"));
        Assert.Null(PapyrusStateSave.Text(node, "missing"));
    }
}
