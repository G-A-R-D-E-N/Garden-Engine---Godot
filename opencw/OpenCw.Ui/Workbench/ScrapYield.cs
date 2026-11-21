using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Workshop;

namespace OpenCommonwealth.Services.Ui.Workbench;

// Constraint: fails closed; null means unknown (not a MISC item or unreadable), an empty list means it yields nothing.
public static class ScrapYield
{
    private const string MiscSignature = "MISC";
    public const string ObjectPath = "MISC\\ComponentObject";
    public const string CountPath = "MISC\\ComponentCount";

    public static IReadOnlyList<WorkshopMaterial>? Of(IRecordReader reader, string itemId)
    {
        if (reader.Signature(itemId) != MiscSignature || RecordRows.Components(reader, itemId, ObjectPath, CountPath) is not { } parts) return null;
        return parts.Where(part => part.Count > 0).GroupBy(part => part.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WorkshopMaterial(group.Key, reader.Name(group.Key) ?? reader.EditorId(group.Key) ?? group.Key,
                (int)Math.Min(group.Sum(part => (long)part.Count), int.MaxValue))).ToArray();
    }
}
