using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Records;

public sealed record MovementTypeSlot(string EditorId, MovementTypeSpeeds Units);

public readonly record struct MovementUnits(
    MovementTypeSpeeds Default,
    MovementTypeSpeeds Sneak,
    MovementTypeSpeeds Swim,
    MovementTypeSpeeds Sighted,
    MovementTypeSpeeds PowerArmor);

public static class VanillaMovementSlots
{
    public static readonly MovementTypeSlot Default = new("NPC_Default_MT", new MovementTypeSpeeds(
        new MovementRow(80.1f, 370f, 500f),
        new MovementRow(71.93f, 260f, 0f),
        new MovementRow(80.09f, 370f, 0f),
        new MovementRow(79.75f, 370f, 0f)));

    public static readonly MovementTypeSlot Sneak = new("NPC_Sneaking", new MovementTypeSpeeds(
        new MovementRow(90f, 205f, 388.5f),
        new MovementRow(90f, 205f, 0f),
        new MovementRow(90f, 205f, 0f),
        new MovementRow(90f, 205f, 0f)));

    public static readonly MovementTypeSlot Swim = new("NPC_Swimming_MT", new MovementTypeSpeeds(
        new MovementRow(80.1f, 370f, 500f),
        new MovementRow(71.93f, 205.25f, 0f),
        new MovementRow(80.09f, 370f, 0f),
        new MovementRow(79.75f, 370f, 0f)));

    public static readonly MovementTypeSlot Sighted = new("NPC_Sighted_MT", new MovementTypeSpeeds(
        new MovementRow(100.76f, 200f, 350f),
        new MovementRow(100.76f, 200f, 0f),
        new MovementRow(100.76f, 200f, 0f),
        new MovementRow(100.76f, 200f, 0f)));

    public static readonly MovementTypeSlot PowerArmor = new("PowerArmor_Player_Default_MT", new MovementTypeSpeeds(
        new MovementRow(112.48f, 311.66f, 497.06f),
        new MovementRow(112.48f, 311.66f, 0f),
        new MovementRow(112.48f, 311.66f, 0f),
        new MovementRow(112.48f, 311.66f, 0f)));

    public static readonly IReadOnlyList<MovementTypeSlot> All = new[] { Default, Sneak, Swim, Sighted, PowerArmor };

    public static MovementUnits ReadAll(IRecordReader reader, Action<string>? log = null) => new(
        MovementTypeSpeeds.Read(reader, Default.EditorId, Default.Units, log),
        MovementTypeSpeeds.Read(reader, Sneak.EditorId, Sneak.Units, log),
        MovementTypeSpeeds.Read(reader, Swim.EditorId, Swim.Units, log),
        MovementTypeSpeeds.Read(reader, Sighted.EditorId, Sighted.Units, log),
        MovementTypeSpeeds.Read(reader, PowerArmor.EditorId, PowerArmor.Units, log));
}
