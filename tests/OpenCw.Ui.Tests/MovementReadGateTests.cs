using System;
using System.Globalization;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MovementReadGateTests
{
    [Fact]
    public void No_load_order_holds_the_table_without_touching_the_session()
    {
        Assert.Equal(MovementReadStep.Hold, MovementReadGate.Decide(false, MovementReadGate.Unread, 7, false));
        Assert.Equal(MovementReadStep.Hold, MovementReadGate.Decide(false, 6, 7, true));
    }

    [Fact]
    public void The_same_session_is_never_read_again()
    {
        Assert.Equal(MovementReadStep.Hold, MovementReadGate.Decide(true, 7, 7, false));
        Assert.Equal(MovementReadStep.Hold, MovementReadGate.Decide(true, 7, 7, true));
    }

    [Fact]
    public void A_new_session_with_the_lifetime_lock_free_reads()
    {
        Assert.Equal(MovementReadStep.Read, MovementReadGate.Decide(true, 6, 7, false));
        Assert.Equal(MovementReadStep.Read, MovementReadGate.Decide(true, MovementReadGate.Unread, 0, false));
    }

    [Fact]
    public void A_new_session_under_a_held_lock_defers_instead_of_entering_it()
    {
        Assert.Equal(MovementReadStep.Defer, MovementReadGate.Decide(true, 6, 7, true));
        Assert.Equal(MovementReadStep.Defer, MovementReadGate.Decide(true, MovementReadGate.Unread, 7, true));
    }

    [Fact]
    public void A_deferred_session_is_retried_once_the_lock_is_free()
    {
        Assert.Equal(MovementReadStep.Defer, MovementReadGate.Decide(true, 6, 7, true));
        Assert.Equal(MovementReadStep.Read, MovementReadGate.Decide(true, MovementReadGate.Unread, 7, false));
    }

    [Fact]
    public void Resolve_holds_the_cached_set_without_touching_the_session()
    {
        var session = 4;
        var resolved = MovementReadGate.Resolve(ref session, 9, true, 4, false, 1, () => 2);
        Assert.Equal(9, resolved);
        Assert.Equal(4, session);
    }

    [Fact]
    public void Resolve_on_a_new_session_reads_once_and_stamps_the_session()
    {
        var session = 3;
        var calls = 0;
        var resolved = MovementReadGate.Resolve(ref session, 9, true, 8, false, 1, () => { calls++; return 6; });
        Assert.Equal(6, resolved);
        Assert.Equal(8, session);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Resolve_under_a_held_lock_resets_to_built_in_instead_of_keeping_stale_speeds()
    {
        var session = 5;
        var calls = 0;
        var resolved = MovementReadGate.Resolve(ref session, 99, true, 6, true, 1, () => { calls++; return 2; });
        Assert.Equal(1, resolved);
        Assert.Equal(MovementReadGate.Unread, session);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Resolve_after_a_deferred_session_reads_on_the_next_unlocked_call()
    {
        var session = 5;
        MovementReadGate.Resolve(ref session, 50, true, 6, true, 1, () => 2);
        var calls = 0;
        var resolved = MovementReadGate.Resolve(ref session, 1, true, 6, false, 1, () => { calls++; return 7; });
        Assert.Equal(7, resolved);
        Assert.Equal(6, session);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Resolve_that_throws_leaves_the_session_unstamped_so_the_next_call_retries()
    {
        var session = 3;
        Assert.Throws<InvalidOperationException>(() =>
        {
            MovementReadGate.Resolve(ref session, 9, true, 8, false, 1, () => throw new InvalidOperationException("reader closed"));
        });
        Assert.Equal(3, session);
        Assert.Equal(6, MovementReadGate.Resolve(ref session, 9, true, 8, false, 1, () => 6));
        Assert.Equal(8, session);
    }

    static readonly MovementUnits Builtin = new(
        VanillaMovementSlots.Default.Units,
        VanillaMovementSlots.Sneak.Units,
        VanillaMovementSlots.Swim.Units,
        VanillaMovementSlots.Sighted.Units,
        VanillaMovementSlots.PowerArmor.Units);

    static FakeRecordReader Records(float walk)
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
                record.Scalars[path] = walk.ToString(CultureInfo.InvariantCulture);
        }
        return reader;
    }

    [Fact]
    public void A_read_result_is_stored_and_reused_across_ticks_of_the_same_session()
    {
        var reads = 0;
        var reader = Records(120f);
        var session = new MovementReadSession<MovementUnits>(
            Builtin, () => { reads++; return VanillaMovementSlots.ReadAll(reader); });
        var first = session.Current(true, 4, false);
        Assert.Equal(120f, first.Default.Forward.Walk);
        Assert.Equal(120f, session.Set.Default.Forward.Walk);
        Assert.Equal(4, session.Session);
        Assert.Equal(1, reads);
        Assert.Equal(first, session.Current(true, 4, false));
        Assert.Equal(first, session.Current(true, 4, true));
        Assert.Equal(1, reads);
    }

    [Fact]
    public void A_reload_reads_the_new_session_and_replaces_the_stored_units()
    {
        var reads = 0;
        var reader = Records(120f);
        var session = new MovementReadSession<MovementUnits>(
            Builtin, () => { reads++; return VanillaMovementSlots.ReadAll(reader); });
        Assert.Equal(120f, session.Current(true, 4, false).Default.Forward.Walk);
        reader = Records(130f);
        Assert.Equal(130f, session.Current(true, 5, false).Default.Forward.Walk);
        Assert.Equal(130f, session.Set.PowerArmor.Right.Walk);
        Assert.Equal(5, session.Session);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void A_new_session_under_a_held_lock_keeps_the_builtin_until_the_lock_frees()
    {
        var reads = 0;
        var reader = Records(120f);
        var session = new MovementReadSession<MovementUnits>(
            Builtin, () => { reads++; return VanillaMovementSlots.ReadAll(reader); });
        Assert.Equal(Builtin, session.Current(true, 4, true));
        Assert.Equal(0, reads);
        Assert.Equal(MovementReadGate.Unread, session.Session);
        Assert.Equal(120f, session.Current(true, 4, false).Default.Forward.Walk);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void A_locked_new_session_does_not_keep_the_previous_sessions_speeds()
    {
        var reader = Records(120f);
        var session = new MovementReadSession<MovementUnits>(
            Builtin, () => VanillaMovementSlots.ReadAll(reader));
        Assert.Equal(120f, session.Current(true, 4, false).Default.Forward.Walk);
        Assert.Equal(Builtin, session.Current(true, 5, true));
        Assert.Equal(Builtin, session.Set);
    }

    [Fact]
    public void A_failed_read_keeps_the_builtin_logs_once_and_spaces_its_retries()
    {
        var reads = 0;
        var fail = false;
        var reader = Records(120f);
        var log = new List<string>();
        var session = new MovementReadSession<MovementUnits>(
            Builtin,
            () =>
            {
                reads++;
                if (fail) throw new InvalidOperationException("reader closed");
                return VanillaMovementSlots.ReadAll(reader);
            },
            log.Add);
        Assert.Equal(120f, session.Current(true, 4, false).Default.Forward.Walk);
        fail = true;
        Assert.Equal(Builtin, session.Current(true, 5, false));
        Assert.Equal(Builtin, session.Set);
        Assert.Equal(4, session.Session);
        Assert.Equal(1, session.RetryIn);
        Assert.Single(log);
        Assert.Contains("reader closed", log[0], StringComparison.Ordinal);
        Assert.Equal(Builtin, session.Current(true, 5, false));
        Assert.Equal(0, session.RetryIn);
        Assert.Equal(2, reads);
        Assert.Equal(Builtin, session.Current(true, 5, false));
        Assert.Equal(2, session.RetryIn);
        Assert.Single(log);
        fail = false;
        Assert.Equal(Builtin, session.Current(true, 5, false));
        Assert.Equal(Builtin, session.Current(true, 5, false));
        Assert.Equal(120f, session.Current(true, 5, false).Default.Forward.Walk);
        Assert.Equal(5, session.Session);
        Assert.Equal(0, session.RetryIn);
        Assert.Equal(4, reads);
    }

    [Fact]
    public void A_new_session_retries_immediately_after_a_failed_read()
    {
        var fail = false;
        var reader = Records(120f);
        var log = new List<string>();
        var session = new MovementReadSession<MovementUnits>(
            Builtin,
            () =>
            {
                if (fail) throw new InvalidOperationException("reader closed");
                return VanillaMovementSlots.ReadAll(reader);
            },
            log.Add);
        Assert.Equal(120f, session.Current(true, 4, false).Default.Forward.Walk);
        fail = true;
        Assert.Equal(Builtin, session.Current(true, 5, false));
        Assert.Equal(1, session.RetryIn);
        fail = false;
        Assert.Equal(120f, session.Current(true, 6, false).Default.Forward.Walk);
        Assert.Equal(6, session.Session);
        Assert.Equal(0, session.RetryIn);
        Assert.Single(log);
    }
}
