namespace OpenCommonwealth.Services.Ui.Trade;

internal sealed class InventoryRevision(PlayerItems first, PlayerItems second)
{
    private int _own;
    private int _seen = first.Revision + second.Revision;

    private int Shared => unchecked(first.Revision + second.Revision);
    public int Value => unchecked(_own + Shared - _seen);
    public int Mark() => Shared;

    public void Bump(int mark)
    {
        unchecked
        {
            _seen += Shared - mark;
            _own++;
        }
    }
}
