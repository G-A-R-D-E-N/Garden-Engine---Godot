using System.Linq;
using OpenCommonwealth.Services.Ui.Layout;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class MenuSwfMappingTests
{
    [Fact]
    public void AMenuReadsItsArchiveEntryAndRowAliases()
    {
        var refs = MenuSwfMapping.Parse("""
            { "menus": [ { "menu": "list_menu", "archive": "C:/data/ui.ba2", "entry": "Interface/List.swf",
                           "rows": { "title": "Root/Header", "hint": "Root/Hint" } } ] }
            """);
        var menu = Assert.Single(refs);
        Assert.Equal("list_menu", menu.Menu);
        Assert.Equal("C:/data/ui.ba2", menu.Archive);
        Assert.Equal("Interface/List.swf", menu.Entry);
        Assert.Equal("Root/Header", menu.Rows["title"]);
        Assert.Equal("Root/Hint", menu.Rows["hint"]);
    }

    [Fact]
    public void ANullOrEmptyArchiveMeansALooseSwfFile()
    {
        var refs = MenuSwfMapping.Parse("""
            { "menus": [ { "menu": "a", "entry": "a.swf" },
                         { "menu": "b", "archive": null, "entry": "b.swf" },
                         { "menu": "c", "archive": "", "entry": "c.swf" } ] }
            """);
        Assert.All(refs, menu => Assert.Null(menu.Archive));
        Assert.Equal(new[] { "a", "b", "c" }, refs.Select(menu => menu.Menu));
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        var refs = MenuSwfMapping.Parse("""
            {
              // the list menu
              "menus": [
                { "menu": "a", "entry": "a.swf", /* loose */ "rows": { "t": "x", }, },
              ],
            }
            """);
        var menu = Assert.Single(refs);
        Assert.Equal("a", menu.Menu);
        Assert.Equal("x", menu.Rows["t"]);
    }

    [Fact]
    public void AnEmptyMenuListIsValid()
    {
        Assert.Empty(MenuSwfMapping.Parse("""{ "menus": [] }"""));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "menus": {} }""")]
    [InlineData("""{ "menus": [ 3 ] }""")]
    [InlineData("""{ "menus": [ { "entry": "a.swf" } ] }""")]
    [InlineData("""{ "menus": [ { "menu": "a" } ] }""")]
    [InlineData("""{ "menus": [ { "menu": " ", "entry": "a.swf" } ] }""")]
    [InlineData("""{ "menus": [ { "menu": "a", "entry": "a.swf", "archive": 3 } ] }""")]
    [InlineData("""{ "menus": [ { "menu": "a", "entry": "a.swf", "rows": [] } ] }""")]
    [InlineData("""{ "menus": [ { "menu": "a", "entry": "a.swf", "rows": { "t": 3 } } ] }""")]
    [InlineData("""{ "menus": [ { "menu": "a", "entry": "a.swf", "rows": { "t": "" } } ] }""")]
    public void AMalformedMappingIsRefused(string json)
    {
        Assert.Throws<FormatException>(() => MenuSwfMapping.Parse(json));
    }

    [Fact]
    public void ListingAMenuTwiceIsRefused()
    {
        var error = Assert.Throws<FormatException>(() => MenuSwfMapping.Parse("""
            { "menus": [ { "menu": "a", "entry": "1.swf" }, { "menu": "a", "entry": "2.swf" } ] }
            """));
        Assert.Contains("\"a\"", error.Message);
    }
}
