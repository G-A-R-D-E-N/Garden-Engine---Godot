namespace OpenCommonwealth.Services.Ui.Vats;

public static class VatsMarkerLayout
{
    public const double MinimumSpacing = 0.06;
    public const double MaximumMove = 0.04;
    public const int Passes = 2;

    public static VatsPoint[] Relax(IReadOnlyList<VatsPoint> points, double width, double height)
    {
        int count = points.Count;
        var current = new VatsPoint[count];
        for (int i = 0; i < count; i++)
            current[i] = points[i];
        if (count < 2 || !(width > 0) || !(height > 0))
            return current;
        double spacing = MinimumSpacing * height;
        double cap = MaximumMove * height;
        for (int pass = 0; pass < Passes; pass++)
        {
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                    Separate(current, i, j, spacing);
            }
            for (int i = 0; i < count; i++)
                current[i] = Clamp(points[i], current[i], cap, width, height);
        }
        return current;
    }

    private static void Separate(VatsPoint[] current, int i, int j, double spacing)
    {
        double dx = current[j].X - current[i].X;
        double dy = current[j].Y - current[i].Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance >= spacing)
            return;
        double ux = 1;
        double uy = 0;
        if (distance > 1e-9)
        {
            ux = dx / distance;
            uy = dy / distance;
        }
        double push = (spacing - distance) / 2;
        current[i] = new VatsPoint(current[i].X - ux * push, current[i].Y - uy * push);
        current[j] = new VatsPoint(current[j].X + ux * push, current[j].Y + uy * push);
    }

    private static VatsPoint Clamp(VatsPoint origin, VatsPoint moved, double cap, double width, double height)
    {
        double dx = moved.X - origin.X;
        double dy = moved.Y - origin.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length > cap)
        {
            dx = dx / length * cap;
            dy = dy / length * cap;
        }
        return new VatsPoint(Math.Clamp(origin.X + dx, 0, width), Math.Clamp(origin.Y + dy, 0, height));
    }
}
