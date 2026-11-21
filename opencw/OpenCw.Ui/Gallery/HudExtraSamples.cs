using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Prompts;

namespace OpenCommonwealth.Services.Ui.Gallery;

public readonly record struct SampleStep<T>(double At, T Value);

public readonly record struct SampleHintMode(bool Controller, IReadOnlySet<int> Held);

public static class HudExtraSamples
{
    public const string ExitBinding = "E";
    public const string ExitWarning = "Fusion core low. Press {action:exit} to exit the frame.";

    public static IReadOnlyList<SampleStep<PowerArmorState?>> PowerArmorSteps { get; } = new SampleStep<PowerArmorState?>[]
    {
        new(0, Suit(0.9, 90, 1, 1, 1, 1, 1, 1)),
        new(2.5, Suit(0.6, 60, 0.6, 0.45, 1, 0.8, 1, 1)),
        new(5, Suit(0.15, 12, 0.6, 0.45, 0.2, 0, 1, 1, brokenRightArm: true)),
        new(8, Suit(0, 0, 0.6, 0.45, 0.2, 0, 1, 1, brokenRightArm: true, hasCore: false)),
        new(11, null),
    };

    public static IReadOnlyList<SampleStep<ScopeState?>> ScopeSteps { get; } = new SampleStep<ScopeState?>[]
    {
        new(0, new ScopeState(ScopeStyle.Crosshair, 4)),
        new(3, new ScopeState(ScopeStyle.Duplex, 4, Recon: Marks(), Label: "Sample rifle")),
        new(6, new ScopeState(ScopeStyle.Dot, 8, ReticleScale: 1.5)),
        new(9, new ScopeState(ScopeStyle.Ladder, 6, 4, 8, Recon: Marks(), Label: "Sample rifle with an adjustable scope")),
        new(12, null),
    };

    public static IReadOnlyList<ActionHintSpec> ActionHintSpecs { get; } = new[]
    {
        new ActionHintSpec("accept", "Enter", "A", "Choose", 0),
        new ActionHintSpec("back", "Esc", "B", "Back", 1),
        new ActionHintSpec("filter", "F", "Y", "Filter", 3),
        new ActionHintSpec("locked", "L", "X", "Locked option", 2, Enabled: false),
    };

    public static IReadOnlyList<SampleStep<SampleHintMode>> ActionHintSteps { get; } = new SampleStep<SampleHintMode>[]
    {
        new(0, new SampleHintMode(false, new HashSet<int>())),
        new(3, new SampleHintMode(true, new HashSet<int>())),
        new(5, new SampleHintMode(true, new HashSet<int> { 0 })),
        new(8, new SampleHintMode(false, new HashSet<int>())),
        new(11, new SampleHintMode(false, new HashSet<int>())),
    };

    public static IReadOnlyList<SampleStep<ContextPromptDetails?>> ContextPromptSteps { get; } = new SampleStep<ContextPromptDetails?>[]
    {
        new(0, new ContextPromptDetails("Sample door", "Open", "E")),
        new(3, new ContextPromptDetails("Sample terminal with a long name that wraps over more than one line in a narrow area", "Use", "E")),
        new(6, new ContextPromptDetails("Sample crate", "Search", "")),
        new(9, null),
    };

    private static PowerArmorState Suit(double core, int minutes, double head, double torso, double leftArm, double rightArm,
        double leftLeg, double rightLeg, bool brokenRightArm = false, bool hasCore = true) =>
        new(new[]
        {
            new PowerArmorPart(PowerArmorPartId.Head, head),
            new PowerArmorPart(PowerArmorPartId.Torso, torso),
            new PowerArmorPart(PowerArmorPartId.LeftArm, leftArm),
            new PowerArmorPart(PowerArmorPartId.RightArm, rightArm, brokenRightArm),
            new PowerArmorPart(PowerArmorPartId.LeftLeg, leftLeg),
            new PowerArmorPart(PowerArmorPartId.RightLeg, rightLeg),
        }, core, minutes, 0.2, hasCore, "Sample frame", true, ExitWarning);

    private static ReconMark[] Marks() => new[]
    {
        new ReconMark("a", 0.5, 0.45, 42, true),
        new ReconMark("b", 0.3, 0.6, 18),
        new ReconMark("c", 0.7, 0.35, 120, true),
    };
}
