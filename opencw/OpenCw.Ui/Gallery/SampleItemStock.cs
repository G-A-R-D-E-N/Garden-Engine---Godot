using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Gallery;

public sealed class SampleItemStock(int count = 20) : IItemStock
{
    public int CountOf(string itemId) => count;
}
