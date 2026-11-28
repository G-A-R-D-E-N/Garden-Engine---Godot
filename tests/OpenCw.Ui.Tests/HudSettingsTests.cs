using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudSettingsTests
{
    [Fact]
    public void ScaleAndSafeMarginAreClamped()
    {
        var settings = new HudSettings { Scale = 9, SafeMargin = -4 };
        Assert.Equal(HudSettings.MaxScale, settings.Scale, 6);
        Assert.Equal(0.0, settings.SafeMargin, 6);

        settings.Scale = 0.01;
        settings.SafeMargin = 5000;
        Assert.Equal(HudSettings.MinScale, settings.Scale, 6);
        Assert.Equal(HudSettings.MaxSafeMargin, settings.SafeMargin, 6);
    }

    [Fact]
    public void NonFiniteValuesAreIgnored()
    {
        var settings = new HudSettings { Scale = 1.25 };
        int version = settings.Version;
        settings.Scale = double.NaN;
        settings.SafeMargin = double.PositiveInfinity;
        settings.SetOffset(HudElement.Ammo, double.NaN, 1);
        Assert.Equal(1.25, settings.Scale, 6);
        Assert.Equal(0.0, settings.SafeMargin, 6);
        Assert.Equal(default(HudElementPlacement), settings.PlacementOf(HudElement.Ammo));
        Assert.Equal(version, settings.Version);
    }

    [Fact]
    public void VersionOnlyChangesWhenSomethingChanges()
    {
        var settings = new HudSettings();
        int start = settings.Version;
        settings.SetEnabled(HudElement.Ammo, true);
        settings.Scale = 1;
        settings.Palette = HudPaletteKind.Standard;
        settings.ResetPlacement(HudElement.Health);
        Assert.Equal(start, settings.Version);

        settings.SetEnabled(HudElement.Ammo, false);
        Assert.NotEqual(start, settings.Version);
        Assert.False(settings.IsEnabled(HudElement.Ammo));

        int afterToggle = settings.Version;
        settings.SetEnabled(HudElement.Ammo, false);
        Assert.Equal(afterToggle, settings.Version);
    }

    [Fact]
    public void PlacementCanBeSetAndReset()
    {
        var settings = new HudSettings();
        settings.SetOffset(HudElement.Health, 10, -20);
        settings.SetAnchor(HudElement.Health, HudAnchor.TopRight);
        Assert.Equal(new HudElementPlacement(HudAnchor.TopRight, 10, -20), settings.PlacementOf(HudElement.Health));

        settings.SetOffset(HudElement.Health, 99999, 0);
        Assert.Equal(HudSettings.MaxOffset, settings.PlacementOf(HudElement.Health).OffsetX, 6);

        settings.ResetPlacement(HudElement.Health);
        Assert.Equal(default(HudElementPlacement), settings.PlacementOf(HudElement.Health));
    }

    [Fact]
    public void RestoringTheStoredPlacementDoesNotBumpTheVersion()
    {
        var settings = new HudSettings();
        int start = settings.Version;
        settings.SetAnchor(HudElement.Health, null);
        settings.SetOffset(HudElement.Health, 0, 0);
        Assert.Equal(start, settings.Version);

        settings.SetOffset(HudElement.Health, 4, 4);
        int changed = settings.Version;
        Assert.NotEqual(start, changed);
        settings.SetOffset(HudElement.Health, 4, 4);
        settings.SetAnchor(HudElement.Health, null);
        Assert.Equal(changed, settings.Version);
    }

    [Fact]
    public void ResetAllRestoresDefaults()
    {
        var settings = new HudSettings { Scale = 1.5, SafeMargin = 40, Palette = HudPaletteKind.ColourBlindSafe };
        settings.SetEnabled(HudElement.Compass, false);
        settings.SetOffset(HudElement.Ammo, 5, 5);
        settings.ResetAll();
        Assert.Equal(1.0, settings.Scale, 6);
        Assert.Equal(0.0, settings.SafeMargin, 6);
        Assert.Equal(HudPaletteKind.Standard, settings.Palette);
        Assert.True(settings.IsEnabled(HudElement.Compass));
        Assert.Equal(default(HudElementPlacement), settings.PlacementOf(HudElement.Ammo));
    }

    [Theory]
    [InlineData(HudAnchor.TopLeft, HudAlign.Start, HudAlign.Start)]
    [InlineData(HudAnchor.TopCenter, HudAlign.Middle, HudAlign.Start)]
    [InlineData(HudAnchor.MiddleRight, HudAlign.End, HudAlign.Middle)]
    [InlineData(HudAnchor.Center, HudAlign.Middle, HudAlign.Middle)]
    [InlineData(HudAnchor.BottomLeft, HudAlign.Start, HudAlign.End)]
    [InlineData(HudAnchor.BottomRight, HudAlign.End, HudAlign.End)]
    public void AnchorsSplitIntoAxes(HudAnchor anchor, HudAlign horizontal, HudAlign vertical)
    {
        Assert.Equal(horizontal, HudAnchors.Horizontal(anchor));
        Assert.Equal(vertical, HudAnchors.Vertical(anchor));
    }

    [Theory]
    [InlineData(1.0, 0x5FD7A6FFu)]
    [InlineData(0.9, 0x5FD7A6FFu)]
    [InlineData(0.5, 0xF2C14EFFu)]
    [InlineData(0.26, 0xF2C14EFFu)]
    [InlineData(0.25, 0xE5533DFFu)]
    [InlineData(0.0, 0xE5533DFFu)]
    public void FractionPicksTheStandardHealthColour(double fraction, uint expected)
    {
        Assert.Equal(expected, HudPalette.Standard.ForFraction(fraction));
    }

    [Fact]
    public void NaNFractionIsTreatedAsCritical()
    {
        Assert.Equal(HudPalette.Standard.Critical, HudPalette.Standard.ForFraction(double.NaN));
    }

    [Fact]
    public void PaletteKindSelectsThePalette()
    {
        Assert.Same(HudPalette.Standard, HudPalette.For(HudPaletteKind.Standard));
        Assert.Same(HudPalette.ColourBlindSafe, HudPalette.For(HudPaletteKind.ColourBlindSafe));
    }

    [Fact]
    public void ColourBlindPaletteSeparatesGoodWarningAndCriticalByLuminance()
    {
        var palette = HudPalette.ColourBlindSafe;
        Assert.True(HudPalette.Contrast(palette.Good, palette.Warning) >= 1.5);
        Assert.True(HudPalette.Contrast(palette.Good, palette.Critical) >= 1.5);
        Assert.True(HudPalette.Contrast(palette.Warning, palette.Critical) >= 1.5);
        Assert.NotEqual(palette.Friendly, palette.Hostile);
    }

    [Fact]
    public void ColourBlindPaletteAvoidsRedGreenPairs()
    {
        var palette = HudPalette.ColourBlindSafe;
        Assert.Equal(0x56B4E9FFu, palette.Good);
        Assert.Equal(0xF0E442FFu, palette.Warning);
        Assert.Equal(0xD55E00FFu, palette.Critical);
    }

    [Fact]
    public void ContrastIsSymmetricAndBounded()
    {
        Assert.Equal(1.0, HudPalette.Contrast(0xFFFFFFFFu, 0xFFFFFFFFu), 6);
        Assert.Equal(HudPalette.Contrast(0x000000FFu, 0xFFFFFFFFu), HudPalette.Contrast(0xFFFFFFFFu, 0x000000FFu), 6);
        Assert.Equal(21.0, HudPalette.Contrast(0x000000FFu, 0xFFFFFFFFu), 6);
    }
}
