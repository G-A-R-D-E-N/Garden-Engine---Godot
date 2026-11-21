using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Interaction;

public enum InteractionSound { Pickup, DoorOpen, DoorClose, ContainerOpen, ContainerClose, PutDown, ContainerTakeAll, WeaponAttack2D, WeaponAttack, WeaponAttackFail }

public static class InteractionSoundMap
{
    private static readonly IReadOnlyDictionary<string, string> PickupByBaseType = BuildByBaseType(ItemRecordTypes.PickupSoundPath);

    private static readonly IReadOnlyDictionary<string, string> PutDownByBaseType = BuildByBaseType(ItemRecordTypes.PutDownSoundPath);

    private static Dictionary<string, string> BuildByBaseType(Func<string?, string?> path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var signature in ItemRecordTypes.Core)
            if (path(signature) is { } row) map[ItemRecordTypes.PickupBaseType(signature)!] = row;
        if (path("NOTE") is { } note) map[ItemRecordTypes.HolotapeBaseType] = note;
        return map;
    }

    public static readonly IReadOnlyDictionary<string, string> LockpickCues = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["pin_break"] = "UILockpickingPickBreak",
        ["unlock"] = "UILockpickingUnlock",
        ["cylinder_turn"] = "UILockpickingCylinderTurn",
    };

    public static string? FieldPath(InteractionSound sound, string baseType = "") => sound switch
    {
        InteractionSound.DoorOpen => "DOOR\\OpenSound",
        InteractionSound.DoorClose => "DOOR\\CloseSound",
        InteractionSound.ContainerOpen => "CONT\\OpenSound",
        InteractionSound.ContainerClose => "CONT\\CloseSound",
        InteractionSound.ContainerTakeAll => "CONT\\TakeAllSound",
        InteractionSound.WeaponAttack2D => "WEAP\\SoundAttack2D",
        InteractionSound.WeaponAttack => "WEAP\\SoundAttack",
        InteractionSound.WeaponAttackFail => "WEAP\\SoundAttackFail",
        InteractionSound.Pickup => PickupByBaseType.GetValueOrDefault(baseType),
        InteractionSound.PutDown => PutDownByBaseType.GetValueOrDefault(baseType),
        _ => null,
    };

    public static string? LockpickEditorId(string cue) => LockpickCues.GetValueOrDefault(cue);
}
