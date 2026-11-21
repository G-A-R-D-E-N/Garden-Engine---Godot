using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Workshop;

namespace OpenCommonwealth.Services.Ui.Records;

public interface IItemStock
{
    int CountOf(string itemId);

    bool CanSpend(IReadOnlyList<WorkshopMaterial> needs) => needs.All(need => need.Needed <= 0 || CountOf(need.Id) >= need.Needed);
}
