using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Records;

public static class ItemRecordTypes
{
    public static readonly string[] Core = { "ALCH", "AMMO", "ARMO", "BOOK", "INGR", "KEYM", "MISC", "NOTE", "WEAP" };

    public static readonly string[] Extra = { "LIGH", "CMPO" };

    public const string HolotapeBaseType = "Holotape";

    // Constraint: a placed NOTE reference carries the RecordVocabulary class name "Holotape" and an inventory NOTE stack carries PickupBaseType("NOTE"); both are item base types.
    public static readonly IReadOnlySet<string> PickupBaseTypes =
        new HashSet<string>(Core.Select(PickupBaseType).OfType<string>(), StringComparer.Ordinal) { HolotapeBaseType };

    public static string Category(string? signature) => signature switch
    {
        "WEAP" => "Weapons",
        "ARMO" => "Apparel",
        "ALCH" => "Aid",
        "AMMO" => "Ammo",
        _ => "Misc",
    };

    // Constraint: results are names in ItemPickup.ItemBaseTypes; null for signatures with no pickup base type.
    public static string? PickupBaseType(string? signature) => signature switch
    {
        "WEAP" => "Weapon",
        "ARMO" => "Armor",
        "ALCH" => "Ingestible",
        "AMMO" => "Ammunition",
        "MISC" => "MiscItem",
        "BOOK" => "Book",
        "KEYM" => "Key",
        "NOTE" => "Note",
        "INGR" => "Ingredient",
        _ => null,
    };

    // Constraint: the pickup sound row is <SIG>\PickUpSound for every item type except WEAP, whose row is WEAP\SoundPickUp; null for a signature with no pickup base type.
    public static string? PickupSoundPath(string? signature) => signature switch
    {
        "WEAP" => "WEAP\\SoundPickUp",
        not null when PickupBaseType(signature) is not null => signature + "\\PickUpSound",
        _ => null,
    };

    // Invariant: the put-down row is <SIG>\PutDownSound except WEAP\SoundPutDown; MISC has no row and returns null.
    public static string? PutDownSoundPath(string? signature) => signature switch
    {
        "WEAP" => "WEAP\\SoundPutDown",
        "MISC" => null,
        not null when PickupBaseType(signature) is not null => signature + "\\PutDownSound",
        _ => null,
    };

    public static string FormType(string signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        if (signature.Length != 4)
            throw new ArgumentException("A record signature is four characters.", nameof(signature));
        var packed = (uint)signature[0] | (uint)signature[1] << 8 | (uint)signature[2] << 16 | (uint)signature[3] << 24;
        return packed.ToString(CultureInfo.InvariantCulture);
    }
}
