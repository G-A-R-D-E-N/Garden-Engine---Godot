using System;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

[Collection("MenuAccessibility")]
public class MenuAccessibilityTests : IDisposable
{
    public MenuAccessibilityTests()
    {
        MenuAccessibility.TextScale = 1;
        MenuAccessibility.HighContrast = false;
    }

    public void Dispose()
    {
        MenuAccessibility.TextScale = 1;
        MenuAccessibility.HighContrast = false;
    }

    [Fact]
    public void At_the_default_scale_every_menu_size_is_unchanged()
    {
        Assert.Equal(26, MenuAccessibility.Size(26));
        Assert.Equal(14, MenuAccessibility.Size(14, 18));
    }

    [Fact]
    public void A_larger_text_scale_grows_flowing_text_and_caps_text_in_fixed_boxes()
    {
        MenuAccessibility.TextScale = 1.6;

        Assert.Equal(29, MenuAccessibility.Size(18));
        Assert.Equal(40, MenuAccessibility.Size(26, 40));
        Assert.Equal(18, MenuAccessibility.Size(14, 18));
    }

    [Fact]
    public void A_smaller_scale_shrinks_capped_text_too()
    {
        MenuAccessibility.TextScale = 0.8;

        Assert.Equal(21, MenuAccessibility.Size(26, 40));
    }

    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(0.0, MenuAccessibility.MinScale)]
    [InlineData(99.0, MenuAccessibility.MaxScale)]
    public void An_unusable_scale_is_kept_inside_its_range(double given, double expected)
    {
        MenuAccessibility.TextScale = given;

        Assert.Equal(expected, MenuAccessibility.TextScale);
    }

    [Fact]
    public void Changing_a_setting_raises_one_notification_and_repeating_it_raises_none()
    {
        var raised = 0;
        void Count() => raised++;
        MenuAccessibility.Changed += Count;
        try
        {
            MenuAccessibility.TextScale = 1.3;
            MenuAccessibility.TextScale = 1.3;
            MenuAccessibility.HighContrast = true;
            MenuAccessibility.HighContrast = true;
        }
        finally { MenuAccessibility.Changed -= Count; }

        Assert.Equal(2, raised);
    }

    [Theory]
    [InlineData(MenuTone.Text, 7.0)]
    [InlineData(MenuTone.Muted, 7.0)]
    [InlineData(MenuTone.Accent, 4.5)]
    [InlineData(MenuTone.Good, 4.5)]
    [InlineData(MenuTone.Bad, 4.5)]
    public void High_contrast_text_tones_clear_the_contrast_floor_on_the_menu_panel(MenuTone tone, double floor)
    {
        var panel = MenuAccessibility.HighContrastRgba(MenuTone.PanelFill);

        Assert.True(HudPalette.Contrast(MenuAccessibility.HighContrastRgba(tone), panel) >= floor);
    }

    [Fact]
    public void High_contrast_selected_rows_keep_white_text_readable_and_the_border_is_visible()
    {
        var selection = MenuAccessibility.HighContrastRgba(MenuTone.Selection);

        Assert.True(HudPalette.Contrast(0xFFFFFFFF, selection) >= 4.5);
        Assert.True(HudPalette.Contrast(MenuAccessibility.HighContrastRgba(MenuTone.PanelBorder), MenuAccessibility.HighContrastRgba(MenuTone.PanelFill)) >= 7);
    }

    [Fact]
    public void Every_high_contrast_dialogue_choice_colour_is_readable_and_the_bands_differ()
    {
        var panel = MenuAccessibility.HighContrastRgba(MenuTone.PanelFill);
        var seen = new System.Collections.Generic.HashSet<uint>();
        for (var band = 0; band <= 4; band++)
        {
            var colour = MenuAccessibility.ChoiceBand(band);
            Assert.True(HudPalette.Contrast(colour, panel) >= 4.5);
            Assert.True(seen.Add(colour));
        }
    }
}
