using System.Linq;
using OpenCommonwealth.Services.Ui.Flow;
using OpenCommonwealth.Services.Ui.Lockpick;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ModalCursorTests
{
    sealed class FakeMenu : IMenu
    {
        public string Id => "fake";

        public bool CapturesMouse { get; init; }

        public bool HandleAction(MenuInputAction action) => true;
    }

    sealed class PlainMenu : IMenu
    {
        public string Id => "plain";

        public bool HandleAction(MenuInputAction action) => true;
    }

    [Fact]
    public void A_menu_does_not_capture_the_mouse_unless_it_says_so()
    {
        Assert.False(((IMenu)new PlainMenu()).CapturesMouse);
    }

    [Fact]
    public void The_cursor_is_captured_only_while_a_session_is_active_and_the_top_menu_asks_for_it()
    {
        var session = new ModalSession();
        var lockpick = new FakeMenu { CapturesMouse = true };
        Assert.False(session.CursorCaptured(lockpick));

        session.Update(true, true);

        Assert.True(session.CursorCaptured(lockpick));
        Assert.False(session.CursorCaptured(new FakeMenu()));
        Assert.False(session.CursorCaptured(null));

        session.Update(false, false);
        Assert.False(session.CursorCaptured(lockpick));
    }

    [Fact]
    public void The_prior_capture_is_the_real_one_because_the_menu_no_longer_changes_the_mouse_before_the_session_samples_it()
    {
        var session = new ModalSession();

        Assert.Equal(ModalChange.Entered, session.Update(true, false));

        Assert.Equal(ModalChange.Exited, session.Update(false, true));
    }

    [Fact]
    public void Releasing_inputs_stops_held_turns_and_a_stuck_cancel_from_finishing_the_lock()
    {
        var session = new FakeLockpickSession();
        var model = new LockpickMenuModel(session);
        model.SetTurn(LockpickTurnKey.W, true);
        model.PressCancel(LockpickCancelKey.Escape);
        model.Tick(0.1);
        Assert.Equal(1.0, session.Steps.Last().Turn);

        model.ReleaseInputs();
        model.Tick(0.1);
        model.ReleaseCancel(LockpickCancelKey.Escape);

        Assert.Equal(0.0, session.Steps.Last().Turn);
        Assert.Equal(LockpickMenuPhase.Active, model.Phase);
    }

    [Fact]
    public void Releasing_inputs_zeroes_a_held_stick()
    {
        var session = new FakeLockpickSession();
        var model = new LockpickMenuModel(session);
        model.SetStick(1);
        model.Tick(0.1);
        var moved = session.Moved;
        Assert.NotEqual(0, moved);

        model.ReleaseInputs();
        model.Tick(0.1);

        Assert.Equal(moved, session.Moved);
    }
}
