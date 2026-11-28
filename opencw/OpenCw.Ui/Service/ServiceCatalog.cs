using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Service;

public enum ServiceKind
{
    ModManager,
    ContentLibrary,
    Profiles,
    Setup,
}

public sealed record ServiceEntry(ServiceKind Kind, string Title, string Description);

public sealed record ServiceBackends(IModLibrary Mods, IContentLibrary Content, IProfileStore Profiles, ISetupSteps Setup);

public static class ServiceCatalog
{
    public const string Heading = "Mods and Setup";

    public static IReadOnlyList<ServiceEntry> Entries(bool setupNeeded) =>
    [
        new ServiceEntry(ServiceKind.ModManager, "Mod Manager",
            "Enable, disable and reorder installed plugins. Changes apply on the next start."),
        new ServiceEntry(ServiceKind.ContentLibrary, "Content Library",
            "Browse installed content with author, description and associated file sizes."),
        new ServiceEntry(ServiceKind.Profiles, "Profiles",
            "Choose, create, rename, delete and restore local profiles."),
        new ServiceEntry(ServiceKind.Setup, "Local Setup",
            setupNeeded
                ? "First run: find your game files and prepare menu data. Not finished yet."
                : "Check or repeat the local setup of menu data."),
    ];
}
