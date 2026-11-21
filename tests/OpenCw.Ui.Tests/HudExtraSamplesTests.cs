using System.Linq;
using OpenCommonwealth.Services.Ui.Gallery;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Prompts;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudExtraSamplesTests
{
    [Fact]
    public void Every_scenario_runs_forward_in_time_and_ends_cleanly()
    {
        Assert.True(HudExtraSamples.PowerArmorSteps.Zip(HudExtraSamples.PowerArmorSteps.Skip(1), (a, b) => a.At < b.At).All(ok => ok));
        Assert.True(HudExtraSamples.ScopeSteps.Zip(HudExtraSamples.ScopeSteps.Skip(1), (a, b) => a.At < b.At).All(ok => ok));
        Assert.True(HudExtraSamples.ActionHintSteps.Zip(HudExtraSamples.ActionHintSteps.Skip(1), (a, b) => a.At < b.At).All(ok => ok));
        Assert.True(HudExtraSamples.ContextPromptSteps.Zip(HudExtraSamples.ContextPromptSteps.Skip(1), (a, b) => a.At < b.At).All(ok => ok));
        Assert.Null(HudExtraSamples.PowerArmorSteps[^1].Value);
        Assert.Null(HudExtraSamples.ScopeSteps[^1].Value);
        Assert.Null(HudExtraSamples.ContextPromptSteps[^1].Value);
    }

    [Fact]
    public void The_power_armor_script_reaches_a_warning_a_broken_part_and_no_core()
    {
        var model = new PowerArmorHudModel();
        var steps = HudExtraSamples.PowerArmorSteps.Where(s => s.Value is not null).ToArray();

        model.Apply(steps[0].Value);
        Assert.False(model.WarningActive);
        model.Apply(steps[2].Value);
        Assert.True(model.WarningActive);
        Assert.Contains(model.Parts, p => p.Broken);
        model.Apply(steps[^1].Value);
        Assert.True(model.NoCore);
    }

    [Fact]
    public void The_scope_script_covers_every_style_and_a_zoom_range()
    {
        var states = HudExtraSamples.ScopeSteps.Select(s => s.Value).Where(v => v is not null).ToArray();

        Assert.Equal(4, states.Select(s => s!.Style).Distinct().Count());
        Assert.Contains(states, s => s!.ZoomMax > s.ZoomMin);
        Assert.Contains(states, s => s!.Recon is { Count: > 0 });
    }

    [Fact]
    public void The_action_hint_script_has_unique_valid_names_and_a_disabled_option()
    {
        var source = new InMemoryActionHintSource();
        source.Set(HudExtraSamples.ActionHintSpecs);
        var model = new ActionHintBarModel(source);
        model.Sync();

        Assert.Equal(HudExtraSamples.ActionHintSpecs.Count, model.Items.Count);
        Assert.Equal(0, model.Rejected);
        Assert.Contains(model.Items, i => !i.Enabled);
        Assert.Contains(HudExtraSamples.ActionHintSteps, s => s.Value.Controller);
        Assert.Contains(HudExtraSamples.ActionHintSteps, s => s.Value.Held.Count > 0);
    }

    [Fact]
    public void The_context_prompt_script_has_a_long_target_and_a_missing_binding()
    {
        var details = HudExtraSamples.ContextPromptSteps.Select(s => s.Value).Where(v => v is not null).Select(v => v!.Value).ToArray();

        Assert.Contains(details, d => d.Target.Length > 60);
        Assert.Contains(details, d => d.Binding.Length == 0);
        Assert.All(details, d => Assert.False(string.IsNullOrWhiteSpace(d.Action)));
    }
}
