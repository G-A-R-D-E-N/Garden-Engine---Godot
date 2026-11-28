using Godot;

namespace OpenCommonwealth;

public partial class NpcFacing : Node
{
    public const string NodeName = "Facing";

    private float _left, _rate;

    public bool Turning => _left > 0f;

    public void Cancel() => _left = 0f;

    public static NpcFacing Ensure(Node3D puppet)
    {
        if (puppet.GetNodeOrNull<NpcFacing>(NodeName) is { } existing) return existing;
        var node = new NpcFacing { Name = NodeName };
        puppet.AddChild(node);
        return node;
    }

    public bool Begin(float yawRadians, float seconds)
    {
        if (Turning || !float.IsFinite(yawRadians) || !float.IsFinite(seconds) || seconds <= 0f) return false;
        _left = seconds;
        _rate = yawRadians / seconds;
        return true;
    }

    public override void _Process(double delta)
    {
        if (_left <= 0f || GetParent() is not Node3D puppet) return;
        if (puppet.GetNodeOrNull<NavWalker>(NavService.WalkerName) is { Moving: true })
        {
            _left = 0f;
            return;
        }
        float step = Mathf.Min((float)delta, _left);
        _left -= step;
        puppet.GlobalBasis = new Basis(Vector3.Up, _rate * step) * puppet.GlobalBasis;
    }
}
