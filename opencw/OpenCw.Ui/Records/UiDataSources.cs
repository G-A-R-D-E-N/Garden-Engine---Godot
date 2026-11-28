using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lockpick;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using OpenCommonwealth.Services.Ui.Workshop;

namespace OpenCommonwealth.Services.Ui.Records;

public sealed class UiDataSources
{
    public static readonly IReadOnlyList<string> WorkbenchKeywordEditorIds =
    [
        "WorkshopWorkbenchTypeSettlement", "WorkshopWorkbenchTypeFurniture", "WorkshopWorkbenchTypeExterior",
        "WorkshopWorkbenchTypePower", "WorkshopWorkbenchTypeDecorations", "WorkshopWorkbenchTypeCrafting",
        "WorkshopWorkbenchTypeInteriorOnly", "WorkshopWorkbenchTypeWire",
    ];

    private readonly TradeHeadings _headings;

    public UiDataSources(IRecordReader records)
    {
        ArgumentNullException.ThrowIfNull(records);
        Records = records;
        Items = new RecordItems(records);
        _headings = new TradeHeadings(records);
    }

    public IRecordReader Records { get; }

    public IItemRecords Items { get; }

    public IReadOnlyCollection<string> WorkbenchKeywords() =>
        WorkbenchKeywordEditorIds.Select(name => Records.FindEditorId(name, "KYWD")).OfType<string>().ToArray();

    public IWorkshopCatalog Workshop(IItemStock stock, bool freeBuild = false, IRecipeFacts? facts = null) =>
        new RecordWorkshopCatalog(Records, stock, WorkbenchKeywords(), freeBuild, facts);

    public IContainerRules Container(IContainerInventory inventory, Func<string?>? playerListHeader = null) =>
        new RecordContainerRules(inventory, Items, playerListHeader ?? _headings.Source(TradeHeading.ContainerPlayer));

    public IBarterRules Barter(IBarterLedger ledger, Func<string?>? playerListHeader = null, Func<string?>? vendorListHeader = null) =>
        new RecordBarterRules(ledger, Items, playerListHeader ?? _headings.Source(TradeHeading.BarterPlayer),
            vendorListHeader ?? _headings.Source(TradeHeading.BarterVendor));

    public ILockpickRules Lockpicking(ILockpickSessions sessions, LockpickOptions? options = null) =>
        new RecordLockpickRules(Records, sessions, options);
}
