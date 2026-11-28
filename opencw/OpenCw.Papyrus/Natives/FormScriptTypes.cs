using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public static class FormScriptTypes
{
    private static readonly Dictionary<string, string> BySignature = new(StringComparer.Ordinal)
    {
        ["GLOB"] = "GlobalVariable", ["KYWD"] = "Keyword", ["ACTV"] = "ActorValue", ["FLST"] = "FormList", ["QUST"] = "Quest",
        ["FACT"] = "Faction", ["SPEL"] = "Spell", ["MESG"] = "Message", ["PERK"] = "Perk", ["MISC"] = "MiscObject",
        ["WEAP"] = "Weapon", ["ARMO"] = "Armor", ["AMMO"] = "Ammo", ["ALCH"] = "Potion", ["BOOK"] = "Book", ["KEYM"] = "Key",
        ["COBJ"] = "ConstructibleObject", ["LCTN"] = "Location", ["LCRT"] = "LocationRefType", ["CELL"] = "Cell",
        ["WRLD"] = "WorldSpace", ["NPC_"] = "ActorBase", ["ACHR"] = "Actor", ["REFR"] = "ObjectReference", ["STAT"] = "Static",
        ["ACTI"] = "Activator", ["DOOR"] = "Door", ["CONT"] = "Container", ["FURN"] = "Furniture", ["IDLE"] = "Idle",
        ["PACK"] = "Package", ["RACE"] = "Race", ["SNDR"] = "SoundDescriptor",
    };

    // Requires: a signature not listed answers null and keeps the declared type; the table itself has no cited source and is UNVERIFIED.
    public static string? For(string signature) => BySignature.TryGetValue(signature, out var type) ? type : null;
}
