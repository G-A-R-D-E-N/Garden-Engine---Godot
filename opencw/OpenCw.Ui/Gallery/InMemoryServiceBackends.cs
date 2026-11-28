namespace OpenCommonwealth.Services.Ui.Service;

public static class InMemoryServiceBackends
{
    public static ServiceBackends Create()
    {
        var mods = new InMemoryModLibrary(
        [
            new ModEntry("Base.esm", ModKind.Master, true, "Data/Base.esm", [], 12000, Mandatory: true),
            new ModEntry("Addon.esm", ModKind.Master, true, "Data/Addon.esm", ["Base.esm"], 3400),
            new ModEntry("Weapons.esp", ModKind.Plugin, true, "Data/Weapons.esp", ["Base.esm"], 220),
            new ModEntry("Armor.esp", ModKind.Plugin, false, "Data/Armor.esp", ["Base.esm"], 180),
            new ModEntry("Small.esl", ModKind.Light, true, "Data/Small.esl", ["Base.esm"], 30),
            new ModEntry("Gone.esp", ModKind.Plugin, false, "Data/Gone.esp", [], -1, Missing: true),
        ]);
        var content = new InMemoryContentLibrary(
        [
            new ContentItem("Base.esm", ContentType.Installed, true, "", "", [new ContentFile("Data/Base.esm", 90_000_000)]),
            new ContentItem("Addon.esm", ContentType.CreationClub, true, "Sample Studio", "Adds sample content.",
                [new ContentFile("Data/Addon.esm", 4_200_000), new ContentFile("Data/Addon - Main.ba2", 310_000_000)]),
            new ContentItem("Weapons.esp", ContentType.Installed, true, "Sample Author", "Sample weapons.",
                [new ContentFile("Data/Weapons.esp", 52_000)]),
            new ContentItem("Armor.esp", ContentType.Installed, false, "Sample Author", "",
                [new ContentFile("Data/Armor.esp", 41_000)]),
        ], mods.IsEnabled);
        var setup = new InMemorySetupSteps(["Menus", "Fonts", "Sounds", "Icons"]);
        return new ServiceBackends(mods, content, new InMemoryProfileStore(), setup);
    }
}
