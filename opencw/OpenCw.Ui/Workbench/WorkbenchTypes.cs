using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Dialogs;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workshop;

namespace OpenCommonwealth.Services.Ui.Workbench;

public enum WorkbenchOptionState
{
    Available,
    Installed,
    LackingMaterials,
    PerkRequired,
    Unavailable,
}

public sealed record WorkbenchTarget(string Id, string Name)
{
    public string Description { get; init; } = string.Empty;
}

public sealed record WorkbenchSlot(string Id, string Name)
{
    public string Current { get; init; } = string.Empty;
}

public sealed record WorkbenchOption(string Id, string Name)
{
    public string Description { get; init; } = string.Empty;
}

public interface IWorkbenchRecipes
{
    string Title { get; }

    string ApplyVerb { get; }

    string Stamp { get; }

    IReadOnlyList<WorkbenchTarget> Targets { get; }

    IReadOnlyList<WorkbenchSlot> SlotsOf(string targetId);

    IReadOnlyList<WorkbenchOption> OptionsOf(string targetId, string slotId);

    WorkbenchOptionState StateOf(string targetId, string slotId, string optionId);

    IReadOnlyList<WorkshopMaterial> Requirements(string targetId, string slotId, string optionId);

    int AvailableCount(string materialId);

    ActionResult Craft(string targetId, string slotId, string optionId);
}

public interface IWorkbenchRename
{
    int MaxNameLength { get; }

    bool CanRename(string targetId);

    string CurrentName(string targetId);

    string? Validate(string name);

    ActionResult Rename(string targetId, string name);
}

public sealed record RenameRequest(string Title, string Prompt, TextPromptModel Model, Action<string?> Complete);
