using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MenuSoundMapTests
{
    static readonly Regex Literal = new("""(?:Sound|Cue)\("([^"]+)"\)|Cue = "([^"]+)"|SoundRequested\?\.Invoke\("([^"]+)"\)|Sound\([^"]*\? "([^"]+)" : "([^"]+)"\)""");

    static string UiSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "opencw", "OpenCw.Ui"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "opencw", "OpenCw.Ui");
    }

    [Fact]
    public void EveryEmittedNameIsMappedOrListedUnmapped()
    {
        var emitted = Directory.EnumerateFiles(UiSource(), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => Literal.Matches(File.ReadAllText(f)).SelectMany(m => m.Groups.Cast<Group>().Skip(1).Where(g => g.Success).Select(g => g.Value)))
            .ToHashSet();
        var known = MenuSoundMap.Mapped.Keys.Concat(MenuSoundMap.Unmapped).ToHashSet();
        Assert.Empty(emitted.Except(known));
        Assert.Empty(MenuSoundMap.Mapped.Keys.Except(emitted));
        Assert.Empty(MenuSoundMap.Unmapped.Except(emitted));
    }

    [Theory]
    [InlineData("perk.first", "UIPerkMenuChoosePerkFirstRank")]
    [InlineData("perk.rank", "UIPerkMenuChoosePerk")]
    [InlineData("menu.close", "UIMenuCancel")]
    public void LevelUpCuesAreMapped(string name, string id) => Assert.Equal(id, MenuSoundMap.EditorId(name));

    [Theory]
    [InlineData("terminal.accept", "UITerminalCharEnter")]
    [InlineData("store_a", "UITerminalHardDriveA")]
    [InlineData("store_b", "UITerminalHardDriveB")]
    public void TerminalPressCuesAreMapped(string name, string id) => Assert.Equal(id, MenuSoundMap.EditorId(name));

    [Theory]
    [InlineData("trade.commit", "ITMBarter")]
    [InlineData("bench.build_fail", "UIMenuCancel")]
    public void InteractionCuesAreMapped(string name, string id) => Assert.Equal(id, MenuSoundMap.EditorId(name));

    [Theory]
    [InlineData("menu.confirm")]
    [InlineData("menu.blocked")]
    [InlineData("focus.move")]
    [InlineData("no.such.cue")]
    public void SharedOrUnknownCuesFallBackToSilence(string name) => Assert.Null(MenuSoundMap.EditorId(name));

    [Theory]
    [InlineData("accept")]
    [InlineData("type_skip")]
    public void SharedOrUnevidencedAcceptCuesStaySilent(string name) => Assert.Null(MenuSoundMap.EditorId(name));

    [Fact]
    public void MappedAndUnmappedDoNotOverlap() =>
        Assert.Empty(MenuSoundMap.Mapped.Keys.Intersect(MenuSoundMap.Unmapped));

    [Theory]
    [InlineData("password_bad", "UITerminalPasswordBad")]
    [InlineData("build.place", "UIWorkshopModeItemPutDownGeneric")]
    [InlineData("Transition", "UIPipBoyScreenBurstStatic")]
    public void KnownPairing(string name, string id) => Assert.Equal(id, MenuSoundMap.EditorId(name));

    [Fact]
    public void UnmappedNameHasNoEditorId() => Assert.Null(MenuSoundMap.EditorId("menu.tab"));
}
