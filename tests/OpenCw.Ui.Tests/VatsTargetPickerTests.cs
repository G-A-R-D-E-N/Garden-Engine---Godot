using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VatsTargetPickerTests
{
    static VatsTarget Target(string id) =>
        new(id, id, 10, 100, true, new List<VatsResistance>(), new[] { new VatsPart("torso", "torso", 70, 100, false, 15, new VatsPoint(900, 500)) });

    static VatsMenuModel Model(params string[] ids)
    {
        var fake = new FakeVatsTargets { InitialTargetId = ids[0] };
        foreach (var id in ids) fake.Targets.Add(Target(id));
        var model = new VatsMenuModel(fake);
        model.Begin();
        return model;
    }

    [Fact]
    public void Unaimed_launch_picks_no_target()
    {
        var picks = new[] { new VatsPick("side", true), new VatsPick("centre", true) };

        Assert.Null(VatsTargetPicker.Pick(null, picks));
    }

    [Fact]
    public void Aimed_living_candidate_wins_even_when_it_is_not_listed_first()
    {
        var picks = new[] { new VatsPick("other", true), new VatsPick("aimed", true) };

        Assert.Equal("aimed", VatsTargetPicker.Pick("aimed", picks));
    }

    [Fact]
    public void Aimed_dead_or_unoffered_target_picks_nothing()
    {
        var picks = new[] { new VatsPick("side", true), new VatsPick("dead", false) };

        Assert.Null(VatsTargetPicker.Pick("dead", picks));
        Assert.Null(VatsTargetPicker.Pick("terminal", picks));
    }

    [Fact]
    public void Empty_candidate_list_picks_nothing()
    {
        Assert.Null(VatsTargetPicker.Pick("x", new List<VatsPick>()));
    }

    [Fact]
    public void Resolve_maps_the_model_focus_and_fails_when_the_node_is_gone()
    {
        var model = Model("A", "B");

        Assert.Equal("node-A", VatsTargetPicker.Resolve(model, id => "node-" + id));
        Assert.Null(VatsTargetPicker.Resolve<string>(model, _ => null));
    }

    [Fact]
    public void Follow_forwards_each_focus_change_and_skips_unresolvable_ids()
    {
        var model = Model("A", "B");
        var log = new List<string>();
        VatsTargetPicker.Follow(model, id => id == "B" ? null : "node-" + id, log.Add);

        model.Arm();
        model.Handle(VatsCommand.CycleNext);
        model.Handle(VatsCommand.CycleNext);

        Assert.Equal(new[] { "node-A" }, log);
    }
}
