using System;
using OpenCommonwealth;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MovementVanillaTests
{
    private const float UnitsPerMeter = (float)WorldScale.UnitsPerMeter;

    [Fact]
    public void Sprint_falls_back_to_run_when_it_is_not_positive()
    {
        Assert.Equal(2f, new DirectionalSpeeds(1f, 2f, 0f)[MoveTier.Sprint]);
    }

    [Fact]
    public void The_mode_selects_swim_then_sneak_then_sighted_then_power_armor_then_default()
    {
        static MovementSpeeds Units(float value)
        {
            var directional = new DirectionalSpeeds(value, value, value);
            return new MovementSpeeds(directional, directional, directional, directional);
        }

        var set = new MovementSet(Units(1f), Units(2f), Units(3f), Units(4f), Units(5f));
        Assert.Equal(Units(3f), set.Select(true, true, true, true));
        Assert.Equal(Units(2f), set.Select(false, true, true, true));
        Assert.Equal(Units(4f), set.Select(false, false, true, true));
        Assert.Equal(Units(5f), set.Select(false, false, false, true));
        Assert.Equal(Units(1f), set.Select(false, false, false, false));
    }

    [Fact]
    public void Diagonal_input_is_scaled_to_the_speed_ellipse()
    {
        var unit = new DirectionalSpeeds(1f, 1f, 1f);
        var speeds = new MovementSpeeds(unit, unit, unit, unit);
        Assert.Equal(0.2f, speeds.Speed(3f, 4f, MoveTier.Walk));
    }

    [Fact]
    public void Read_returns_scaled_speeds_from_a_fake_reader()
    {
        var reader = new FakeRecordReader();
        var ids = new[]
        {
            "03580D:Fallout4.esm", "03580E:Fallout4.esm", "03580F:Fallout4.esm", "035810:Fallout4.esm", "035811:Fallout4.esm",
        };
        for (var i = 0; i < VanillaMovementSlots.All.Count; i++)
        {
            var record = reader.Add(ids[i], "MOVT", VanillaMovementSlots.All[i].EditorId);
            foreach (var path in MovementTypeSpeeds.All)
                record.Scalars[path] = "120";
        }

        var set = VanillaMovement.Read(reader);
        Assert.Equal(120f / UnitsPerMeter, set.Default.Forward.Walk, 5);
    }

    [Fact]
    public void WithReadLock_enter_and_exit_wrap_the_read()
    {
        var entered = 0;
        var exited = 0;
        var value = MovementLoad.WithReadLock(
            () => entered++,
            () => exited++,
            () => 7);
        Assert.Equal(7, value);
        Assert.Equal(1, entered);
        Assert.Equal(1, exited);
    }

    [Fact]
    public void A_session_over_movement_sets_keeps_the_builtin_on_a_failed_read_and_retries_the_same_session()
    {
        var fail = false;
        var reader = new FakeRecordReader();
        var ids = new[]
        {
            "03580D:Fallout4.esm", "03580E:Fallout4.esm", "03580F:Fallout4.esm", "035810:Fallout4.esm", "035811:Fallout4.esm",
        };
        for (var i = 0; i < VanillaMovementSlots.All.Count; i++)
        {
            var record = reader.Add(ids[i], "MOVT", VanillaMovementSlots.All[i].EditorId);
            foreach (var path in MovementTypeSpeeds.All)
                record.Scalars[path] = "120";
        }

        var log = new List<string>();
        var entered = 0;
        var exited = 0;
        var session = new MovementReadSession<MovementSet>(
            VanillaMovement.Table,
            () => MovementLoad.WithReadLock(
                () => entered++,
                () => exited++,
                () =>
                {
                    if (fail) throw new InvalidOperationException("reader closed");
                    return VanillaMovement.Read(reader);
                }),
            log.Add);
        Assert.Equal(120f / UnitsPerMeter, session.Current(true, 4, false).Default.Forward.Walk, 5);
        fail = true;
        Assert.Equal(VanillaMovement.Table, session.Current(true, 5, false));
        Assert.Equal(4, session.Session);
        Assert.Equal(2, entered);
        Assert.Equal(2, exited);
        Assert.Equal(1, session.RetryIn);
        Assert.Single(log);
        Assert.Contains("reader closed", log[0], StringComparison.Ordinal);
        Assert.Equal(VanillaMovement.Table, session.Current(true, 5, false));
        Assert.Equal(2, entered);
        Assert.Single(log);
        fail = false;
        Assert.Equal(120f / UnitsPerMeter, session.Current(true, 5, false).Default.Forward.Walk, 5);
        Assert.Equal(5, session.Session);
        Assert.Equal(3, entered);
        Assert.Equal(3, exited);
    }
}