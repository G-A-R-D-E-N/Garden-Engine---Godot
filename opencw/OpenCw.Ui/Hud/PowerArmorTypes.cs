using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Hud;

public enum PowerArmorPartId
{
    Head,
    Torso,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
}

public readonly record struct PowerArmorPart(PowerArmorPartId Id, double Condition, bool IsBroken = false);

public sealed record PowerArmorState(
    IReadOnlyList<PowerArmorPart>? Parts,
    double CoreCharge,
    int CoreMinutesRemaining = -1,
    double CoreLowThreshold = 0.2,
    bool HasCore = true,
    string Name = "",
    bool FrameVisible = true,
    string WarningText = "");

public interface IPowerArmorSource
{
    PowerArmorState? Current { get; }

    string? ResolveBinding(string role);
}

public sealed class InMemoryPowerArmorSource : IPowerArmorSource
{
    private readonly Dictionary<string, string> _bindings = new();

    public PowerArmorState? Current { get; private set; }

    public void Set(PowerArmorState? state) => Current = state;

    public void Bind(string role, string display) => _bindings[role] = display;

    public string? ResolveBinding(string role) => _bindings.TryGetValue(role, out var display) ? display : null;
}
