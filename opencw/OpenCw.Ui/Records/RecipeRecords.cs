using System.Collections.Generic;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

// Constraint: Row holds the raw CTDA ids; Global, Param1, Param2 and Reference are the master-resolved keys, null where the slot is an index (alias or package data) or not a form.
public sealed record RecordCondition(RadicleCondition Row, FormKey? Global, FormKey? Param1, FormKey? Param2, FormKey? Reference)
{
    public static RecordCondition From(RecipeCondition condition) =>
        new(condition.Row, condition.Global, condition.Param1, condition.Param2, condition.Reference);
}

public sealed record RecipeRecord(string? Bench, string Created, IReadOnlyList<string> Categories, IReadOnlyList<ComponentPart> Components,
    int CreatedCount, IReadOnlyList<RecordCondition>? Conditions, string Description);
