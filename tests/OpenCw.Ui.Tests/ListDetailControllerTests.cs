using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ListDetailControllerTests
{
    sealed class Probe : ListDetailController<string>
    {
        public Probe(PlainListModel<string> rows) : base(rows)
        {
        }

        public override IReadOnlyList<MenuHint> Hints => Array.Empty<MenuHint>();

        public override bool Handle(MenuInputAction action) => false;

        public void SaySomething(string text) => Say(text);

        public void Beep(string name) => Sound(name);

        public void End(bool confirmed) => Finish(confirmed);
    }

    static Probe Make()
    {
        var rows = new PlainListModel<string>(row => row, row => row);
        rows.SetRows(new[] { "a", "b" });
        return new Probe(rows);
    }

    static List<string> Log(Probe probe)
    {
        var log = new List<string>();
        probe.Changed += () => log.Add("changed");
        probe.Finished += confirmed => log.Add("finished:" + confirmed);
        probe.SoundRequested += name => log.Add("sound:" + name);
        return log;
    }

    [Fact]
    public void A_missing_row_model_is_refused()
    {
        var thrown = Assert.Throws<ArgumentNullException>(() => new Probe(null!));

        Assert.Equal("rows", thrown.ParamName);
    }

    [Fact]
    public void A_fresh_controller_has_empty_text_no_detail_and_is_not_finished()
    {
        var probe = Make();

        Assert.Equal("", probe.Title);
        Assert.Equal("", probe.Header);
        Assert.Equal("", probe.Message);
        Assert.Empty(probe.DetailLines);
        Assert.False(probe.IsFinished);
        Assert.False(probe.IsConfirmed);
        Assert.Same(probe.Rows, probe.List);
        Assert.Equal(3.0, ListDetailController<string>.MessageSeconds);
    }

    [Fact]
    public void Changes_to_the_row_model_are_forwarded_as_changed()
    {
        var probe = Make();
        var log = Log(probe);

        probe.Rows.Select(1);

        Assert.Equal(new[] { "changed" }, log);
    }

    [Fact]
    public void A_message_shows_at_once_and_disappears_after_three_seconds_with_one_change_each()
    {
        var probe = Make();
        var log = Log(probe);

        probe.SaySomething("Saved.");
        Assert.Equal("Saved.", probe.Message);
        Assert.Equal(new[] { "changed" }, log);

        probe.Tick(1.5);
        Assert.Equal("Saved.", probe.Message);
        Assert.Equal(new[] { "changed" }, log);

        probe.Tick(1.5);
        Assert.Equal("", probe.Message);
        Assert.Equal(new[] { "changed", "changed" }, log);

        probe.Tick(10.0);
        Assert.Equal(new[] { "changed", "changed" }, log);
    }

    [Fact]
    public void Ticks_with_no_message_or_a_bad_delta_change_nothing()
    {
        var probe = Make();
        var log = Log(probe);

        probe.Tick(5.0);
        Assert.Empty(log);

        probe.SaySomething("Hi");
        probe.Tick(0.0);
        probe.Tick(-3.0);
        probe.Tick(double.NaN);
        probe.Tick(double.PositiveInfinity);
        Assert.Equal("Hi", probe.Message);
        Assert.Equal(new[] { "changed" }, log);

        probe.Tick(3.0);
        Assert.Equal("", probe.Message);
    }

    [Fact]
    public void Saying_a_new_message_restarts_the_three_second_timer()
    {
        var probe = Make();
        probe.SaySomething("first");
        probe.Tick(2.0);

        probe.SaySomething("second");
        probe.Tick(2.0);
        Assert.Equal("second", probe.Message);

        probe.Tick(1.0);
        Assert.Equal("", probe.Message);
    }

    [Fact]
    public void Saying_nothing_clears_the_message_and_arms_no_timer()
    {
        var probe = Make();
        probe.SaySomething("first");
        var log = Log(probe);

        probe.SaySomething("");
        Assert.Equal("", probe.Message);
        Assert.Equal(new[] { "changed" }, log);

        probe.Tick(10.0);
        Assert.Equal(new[] { "changed" }, log);
    }

    [Fact]
    public void A_sound_request_is_passed_on_by_name_and_does_not_raise_changed()
    {
        var probe = Make();
        var log = Log(probe);

        probe.Beep("menu.blocked");

        Assert.Equal(new[] { "sound:menu.blocked" }, log);
    }

    [Fact]
    public void Requests_with_no_listeners_do_not_throw()
    {
        var probe = Make();

        probe.Beep("menu.back");
        probe.SaySomething("x");
        probe.End(true);

        Assert.True(probe.IsFinished);
    }

    [Fact]
    public void Finishing_confirmed_raises_finished_then_changed_once_and_a_second_finish_is_ignored()
    {
        var probe = Make();
        var log = Log(probe);

        probe.End(true);
        probe.End(false);

        Assert.True(probe.IsFinished);
        Assert.True(probe.IsConfirmed);
        Assert.Equal(new[] { "finished:True", "changed" }, log);
    }

    [Fact]
    public void Finishing_unconfirmed_reports_false_and_stays_unconfirmed_after_a_later_confirm()
    {
        var probe = Make();
        var log = Log(probe);

        probe.End(false);
        probe.End(true);

        Assert.True(probe.IsFinished);
        Assert.False(probe.IsConfirmed);
        Assert.Equal(new[] { "finished:False", "changed" }, log);
    }
}
