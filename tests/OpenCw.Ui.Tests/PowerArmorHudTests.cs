using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class PowerArmorHudTests
{
    private static PowerArmorPart[] Full() => Enum.GetValues<PowerArmorPartId>().Select(id => new PowerArmorPart(id, 1)).ToArray();

    private static PowerArmorState Suit(double core = 0.9, bool hasCore = true, double threshold = 0.2, string warning = "",
        int minutes = -1, PowerArmorPart[]? parts = null, bool frame = true, string name = "Sample frame") =>
        new(parts ?? Full(), core, minutes, threshold, hasCore, name, frame, warning);

    private static List<string> Sounds(PowerArmorHudModel model)
    {
        var sounds = new List<string>();
        model.SoundRequested += sounds.Add;
        return sounds;
    }

    [Fact]
    public void Null_hides_the_panel_and_frame_and_a_state_shows_them()
    {
        var model = new PowerArmorHudModel();
        Assert.False(model.Visible);

        model.Apply(Suit());
        Assert.True(model.Visible);
        Assert.True(model.FrameVisible);

        model.Apply(null);
        Assert.False(model.Visible);
        Assert.False(model.FrameVisible);
    }

    [Fact]
    public void The_panel_fades_in_over_300_ms_and_out_over_200_ms()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit());
        Assert.Equal(0, model.Alpha, 6);
        model.Tick(0.15);
        Assert.Equal(0.5, model.Alpha, 6);
        model.Tick(1);
        Assert.Equal(1, model.Alpha, 6);

        model.Apply(null);
        model.Tick(0.1);
        Assert.Equal(0.5, model.Alpha, 6);
        model.Tick(0.1);
        Assert.Equal(0, model.Alpha, 6);
    }

    [Fact]
    public void Low_and_broken_parts_are_flagged_and_a_broken_part_has_no_fill()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit(parts: new[]
        {
            new PowerArmorPart(PowerArmorPartId.Head, 1),
            new PowerArmorPart(PowerArmorPartId.Torso, 0.5),
            new PowerArmorPart(PowerArmorPartId.LeftArm, 0.2),
            new PowerArmorPart(PowerArmorPartId.RightArm, 0, true),
            new PowerArmorPart(PowerArmorPartId.LeftLeg, 1),
            new PowerArmorPart(PowerArmorPartId.RightLeg, 1),
        }));

        Assert.Equal(new[] { false, false, true, true, false, false }, model.Parts.Select(p => p.Warn).ToArray());
        Assert.Equal(new[] { false, false, false, true, false, false }, model.Parts.Select(p => p.Broken).ToArray());
        Assert.Equal(0.2, model.Parts[2].Fill, 6);
        Assert.Equal(0, model.Parts[3].Fill, 6);
        Assert.All(model.Parts, p => Assert.True(p.Present));
    }

    [Fact]
    public void A_broken_part_with_leftover_condition_still_shows_no_fill()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit(parts: new[] { new PowerArmorPart(PowerArmorPartId.Torso, 0.9, true) }));

        Assert.Equal(0, model.Parts[1].Fill, 6);
        Assert.True(model.Parts[1].Warn);
    }

    [Fact]
    public void Duplicate_part_ids_keep_the_first_and_unknown_ids_and_extras_are_ignored()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit(parts: new[]
        {
            new PowerArmorPart(PowerArmorPartId.Head, 0.9),
            new PowerArmorPart(PowerArmorPartId.Head, 0.1),
            new PowerArmorPart(PowerArmorPartId.Torso, 0.8),
            new PowerArmorPart(PowerArmorPartId.LeftArm, 0.7),
            new PowerArmorPart(PowerArmorPartId.RightArm, 0.6),
            new PowerArmorPart(PowerArmorPartId.LeftLeg, 0.5),
            new PowerArmorPart(PowerArmorPartId.RightLeg, 0.4),
            new PowerArmorPart((PowerArmorPartId)9, 1),
        }));

        Assert.Equal(PowerArmorHudModel.PartCount, model.Parts.Count);
        Assert.Equal(0.9, model.Parts[0].Fill, 6);
        Assert.Equal(0.4, model.Parts[5].Fill, 6);
    }

    [Fact]
    public void Missing_parts_draw_dim_with_no_fill_and_a_null_list_is_safe()
    {
        var model = new PowerArmorHudModel();
        model.Apply(new PowerArmorState(null, 0.5));
        Assert.All(model.Parts, p => Assert.False(p.Present));

        model.Apply(Suit(parts: new[] { new PowerArmorPart(PowerArmorPartId.Head, 1) }));
        Assert.True(model.Parts[0].Present);
        Assert.False(model.Parts[1].Present);
        Assert.Equal(0, model.Parts[1].Fill, 6);
    }

    [Fact]
    public void Non_finite_values_become_zero_and_everything_clamps_to_zero_one()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit(core: double.NaN, parts: new[]
        {
            new PowerArmorPart(PowerArmorPartId.Head, double.PositiveInfinity),
            new PowerArmorPart(PowerArmorPartId.Torso, 7),
            new PowerArmorPart(PowerArmorPartId.LeftArm, -3),
        }));

        Assert.Equal(0, model.Parts[0].Fill, 6);
        Assert.Equal(1, model.Parts[1].Fill, 6);
        Assert.Equal(0, model.Parts[2].Fill, 6);
        Assert.Equal(0, model.CoreFill, 6);

        model.Apply(Suit(core: 3));
        Assert.Equal(1, model.CoreFill, 6);
        Assert.Equal(100, model.CorePercent);
    }

    [Fact]
    public void Core_reading_shows_percent_and_minutes_only_when_known()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit(core: 0.86, minutes: 42));
        Assert.Equal(86, model.CorePercent);
        Assert.Equal("42 min", model.CoreMinutesText);

        model.Apply(Suit(core: 0.5, minutes: -1));
        Assert.Equal("", model.CoreMinutesText);
        model.Apply(Suit(core: 0.5, minutes: 0));
        Assert.Equal("0 min", model.CoreMinutesText);
    }

    [Fact]
    public void The_warning_shows_at_the_threshold_and_not_just_above_it()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit(core: 0.2));
        Assert.True(model.WarningActive);

        model.Apply(Suit(core: 0.21));
        Assert.False(model.WarningActive);
        model.Tick(0.25);
        Assert.False(model.WarningVisible);
    }

    [Fact]
    public void Without_a_core_the_bar_is_empty_the_number_is_zero_and_the_warning_shows()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit(core: 0.8, hasCore: false, minutes: 30));

        Assert.True(model.NoCore);
        Assert.Equal(0, model.CoreFill, 6);
        Assert.Equal(0, model.CorePercent);
        Assert.Equal("", model.CoreMinutesText);
        Assert.True(model.WarningActive);
        Assert.True(model.WarningVisible);
    }

    [Fact]
    public void The_exit_token_uses_the_bound_name_or_the_plain_word_Exit()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit(core: 0.1, warning: "Low. Press {action:exit} to leave."));
        Assert.Equal("Low. Press Exit to leave.", model.WarningText);

        model.SetBindingResolver(role => role == "exit" ? "E" : null);
        Assert.Equal("Low. Press E to leave.", model.WarningText);

        model.SetBindingResolver(_ => "");
        Assert.Equal("Low. Press Exit to leave.", model.WarningText);
    }

    [Fact]
    public void Empty_supplied_warning_text_falls_back_to_a_plain_label()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit(core: 0.1));

        Assert.Equal(PowerArmorHudModel.DefaultWarning, model.WarningText);
    }

    [Fact]
    public void The_warning_sound_fires_once_on_the_rise_then_every_20_seconds_and_again_after_a_new_rise()
    {
        var model = new PowerArmorHudModel();
        var sounds = Sounds(model);

        model.Apply(Suit(core: 0.9));
        model.Tick(30);
        Assert.Empty(sounds);

        model.Apply(Suit(core: 0.1));
        Assert.Equal(new[] { "ui.warn" }, sounds);
        model.Apply(Suit(core: 0.15));
        model.Tick(19.5);
        Assert.Single(sounds);
        model.Tick(0.5);
        Assert.Equal(2, sounds.Count);
        model.Tick(20);
        Assert.Equal(3, sounds.Count);

        model.Apply(Suit(core: 0.9));
        model.Tick(25);
        Assert.Equal(3, sounds.Count);
        model.Apply(Suit(core: 0.1));
        Assert.Equal(4, sounds.Count);
    }

    [Fact]
    public void Identical_consecutive_states_restart_no_fade_and_no_sound()
    {
        var model = new PowerArmorHudModel();
        var sounds = Sounds(model);
        model.Apply(Suit(core: 0.1, parts: Full()));
        model.Tick(0.3);
        Assert.Equal(1, model.Alpha, 6);

        model.Apply(Suit(core: 0.1, parts: Full()));
        Assert.Equal(1, model.Alpha, 6);
        Assert.Single(sounds);

        model.Tick(10);
        model.Apply(Suit(core: 0.1, parts: Full()));
        model.Tick(10);
        Assert.Equal(2, sounds.Count);
    }

    [Fact]
    public void Leaving_and_entering_again_requests_the_sound_again()
    {
        var model = new PowerArmorHudModel();
        var sounds = Sounds(model);
        model.Apply(Suit(core: 0.1));
        model.Apply(null);
        Assert.False(model.WarningVisible);
        model.Apply(Suit(core: 0.1));

        Assert.Equal(2, sounds.Count);
    }

    [Fact]
    public void The_warning_pulses_between_full_and_forty_percent_at_one_hertz()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit(core: 0.1));
        model.Tick(0.3);
        model.Tick(0.2);
        Assert.Equal(0.4, model.WarningAlpha, 6);

        model.Tick(0.5);
        Assert.Equal(1, model.WarningAlpha, 6);
    }

    [Fact]
    public void The_effects_list_shifts_while_the_panel_is_visible_and_returns_after_it_hides()
    {
        var model = new PowerArmorHudModel();
        Assert.Equal(0, model.EffectsOffsetX);

        model.Apply(Suit());
        Assert.Equal(50, model.EffectsOffsetX);
        Assert.Equal(15, model.EffectsOffsetY);

        model.Apply(null);
        Assert.Equal(0, model.EffectsOffsetX);
        Assert.Equal(0, model.EffectsOffsetY);
    }

    [Fact]
    public void Turning_the_frame_off_hides_only_the_frame()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit(frame: false));

        Assert.True(model.Visible);
        Assert.False(model.FrameVisible);
        Assert.Equal("Sample frame", model.Name);
    }

    [Fact]
    public void The_name_is_kept_exactly_as_supplied()
    {
        var model = new PowerArmorHudModel();
        model.Apply(Suit(name: " [b]Sample[/b]  frame "));

        Assert.Equal(" [b]Sample[/b]  frame ", model.Name);
    }

    [Fact]
    public void Part_labels_are_the_short_garden_labels()
    {
        Assert.Equal(new[] { "H", "T", "LA", "RA", "LL", "RL" },
            Enum.GetValues<PowerArmorPartId>().Select(PowerArmorHudModel.PartLabel).ToArray());
    }

    [Fact]
    public void A_source_feeds_the_state_and_the_binding()
    {
        var source = new InMemoryPowerArmorSource();
        source.Bind("exit", "F");
        source.Set(Suit(core: 0.05, warning: "Press {action:exit}"));
        var model = new PowerArmorHudModel();

        model.Sync(source);
        Assert.Equal("Press F", model.WarningText);

        source.Set(null);
        model.Sync(source);
        Assert.False(model.Visible);
    }

    [Fact]
    public void The_hud_state_shifts_effects_and_honours_the_power_armor_group_and_hidden_flag()
    {
        var state = new HudState();
        state.PowerArmor.Apply(Suit());
        state.Tick(0.5);

        Assert.True(state.PowerArmorActive);
        Assert.True(state.IsPowerArmorDrawn);
        Assert.Equal(50, state.EffectsOffsetX);
        Assert.Equal(15, state.EffectsOffsetY);

        state.Instruments.SetGroupVisible(HudGroup.PowerArmor, false);
        Assert.False(state.IsPowerArmorDrawn);
        Assert.Equal(0, state.EffectsOffsetX);
        state.Instruments.SetGroupVisible(HudGroup.PowerArmor, true);

        state.Hidden = true;
        Assert.False(state.IsPowerArmorDrawn);
        state.Hidden = false;

        state.PowerArmor.Apply(null);
        Assert.Equal(0, state.EffectsOffsetY);
        state.Tick(0.5);
        Assert.False(state.IsPowerArmorDrawn);
    }
}
