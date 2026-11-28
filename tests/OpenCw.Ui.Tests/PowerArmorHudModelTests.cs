using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PowerArmorHudModelTests
{
    static PowerArmorState State(double charge = 0.5, bool hasCore = true, string warning = "") =>
        new(null, charge, HasCore: hasCore, WarningText: warning);

    [Fact]
    public void A_model_that_never_received_a_state_reads_as_empty()
    {
        var model = new PowerArmorHudModel();

        Assert.False(model.Visible);
        Assert.False(model.HasCore);
        Assert.False(model.NoCore);
        Assert.Equal(0.0, model.CoreFill);
        Assert.Equal(0, model.CorePercent);
        Assert.Equal("", model.CoreMinutesText);
        Assert.Equal("", model.Name);
        Assert.Equal("", model.WarningText);
        Assert.False(model.WarningVisible);
        Assert.False(model.FrameVisible);
        Assert.Equal(PowerArmorHudModel.PartCount, model.Parts.Count);
        Assert.All(model.Parts, part => Assert.False(part.Present));
    }

    [Fact]
    public void The_core_flags_follow_whether_the_suit_has_a_core()
    {
        var model = new PowerArmorHudModel();

        model.Apply(State(hasCore: true));
        Assert.True(model.HasCore);
        Assert.False(model.NoCore);

        model.Apply(State(hasCore: false));
        Assert.False(model.HasCore);
        Assert.True(model.NoCore);
    }

    [Theory]
    [InlineData(0.125, 13)]
    [InlineData(0.124, 12)]
    [InlineData(1.5, 100)]
    [InlineData(-0.2, 0)]
    public void The_core_percent_rounds_halves_away_from_zero_after_clamping(double charge, int expected)
    {
        var model = new PowerArmorHudModel();

        model.Apply(State(charge));

        Assert.Equal(expected, model.CorePercent);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(-1)]
    public void A_part_id_outside_the_six_has_no_label(int id)
    {
        Assert.Equal("", PowerArmorHudModel.PartLabel((PowerArmorPartId)id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void The_exit_token_reads_exit_when_the_binding_is_missing_or_blank(string? bound)
    {
        var model = new PowerArmorHudModel();
        model.SetBindingResolver(_ => bound);

        model.Apply(State(warning: "Press {action:exit}"));

        Assert.Equal("Press Exit", model.WarningText);
    }

    [Fact]
    public void Every_exit_token_in_the_warning_is_replaced_by_the_bound_name()
    {
        var model = new PowerArmorHudModel();
        model.SetBindingResolver(role => role == PowerArmorHudModel.ExitRole ? "E" : null);

        model.Apply(State(warning: "{action:exit} or {action:exit}"));

        Assert.Equal("E or E", model.WarningText);
    }

    [Fact]
    public void Without_a_resolver_the_exit_token_reads_exit()
    {
        var model = new PowerArmorHudModel();
        model.SetBindingResolver(null);

        model.Apply(State(warning: "{action:exit}!"));

        Assert.Equal("Exit!", model.WarningText);
    }

    [Fact]
    public void Syncing_a_source_again_takes_back_the_binding_a_manual_resolver_replaced()
    {
        var source = new InMemoryPowerArmorSource();
        source.Bind(PowerArmorHudModel.ExitRole, "Q");
        source.Set(State(warning: "{action:exit}"));
        var model = new PowerArmorHudModel();

        model.Sync(source);
        Assert.Equal("Q", model.WarningText);

        model.SetBindingResolver(_ => "Z");
        Assert.Equal("Z", model.WarningText);

        model.Sync(source);
        Assert.Equal("Q", model.WarningText);
    }

    [Fact]
    public void Sync_rejects_a_missing_source()
    {
        Assert.Throws<ArgumentNullException>(() => new PowerArmorHudModel().Sync(null!));
    }

    [Fact]
    public void Clearing_the_state_while_the_warning_shows_hides_it_at_once_and_it_returns_with_one_sound()
    {
        var sounds = new List<string>();
        var model = new PowerArmorHudModel();
        model.SoundRequested += sounds.Add;

        model.Apply(State(0.1));
        Assert.True(model.WarningActive);
        Assert.True(model.WarningVisible);
        Assert.Equal(new[] { PowerArmorHudModel.WarnCue }, sounds);

        model.Apply(null);
        Assert.False(model.Visible);
        Assert.False(model.WarningActive);
        Assert.False(model.WarningVisible);
        Assert.Equal(0.0, model.WarningAlpha);

        model.Apply(State(0.1));
        Assert.Equal(new[] { PowerArmorHudModel.WarnCue, PowerArmorHudModel.WarnCue }, sounds);
    }

    [Fact]
    public void A_charge_above_the_threshold_raises_no_warning_and_no_sound()
    {
        var sounds = new List<string>();
        var model = new PowerArmorHudModel();
        model.SoundRequested += sounds.Add;

        model.Apply(State(0.5));
        model.Tick(60);

        Assert.False(model.WarningActive);
        Assert.False(model.WarningVisible);
        Assert.Empty(sounds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_tick_that_is_not_a_positive_finite_step_changes_nothing(double dt)
    {
        var model = new PowerArmorHudModel();
        model.Apply(State(0.5));

        model.Tick(dt);

        Assert.Equal(0.0, model.Alpha);
    }
}
