namespace DesignSpace.Core;

public readonly record struct DPoint(double X, double Y)
{
    public static DPoint operator +(DPoint a, DPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static DPoint operator -(DPoint a, DPoint b) => new(a.X - b.X, a.Y - b.Y);
}
public readonly record struct DSize(double Width, double Height);
public readonly record struct DRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public DPoint Center => new(X + Width / 2, Y + Height / 2);
    public bool Contains(DPoint p) => p.X >= X && p.Y >= Y && p.X <= Right && p.Y <= Bottom;
    public bool Intersects(DRect b) => b.Right >= X && b.X <= Right && b.Bottom >= Y && b.Y <= Bottom;
    public DRect Translate(double x, double y) => this with { X = X + x, Y = Y + y };
    public static DRect FromPoints(DPoint a, DPoint b) => new(Math.Min(a.X,b.X), Math.Min(a.Y,b.Y), Math.Abs(a.X-b.X), Math.Abs(a.Y-b.Y));
    public static DRect Union(IEnumerable<DRect> boxes)
    {
        var a = boxes.ToArray(); if (a.Length == 0) return default;
        var x = a.Min(b => b.X); var y = a.Min(b => b.Y);
        return new(x, y, a.Max(b => b.Right) - x, a.Max(b => b.Bottom) - y);
    }
    public bool ContainsRotated(DPoint p, double degrees)
    {
        var r = -degrees * Math.PI / 180; var d = p - Center;
        return Contains(Center + new DPoint(d.X * Math.Cos(r) - d.Y * Math.Sin(r), d.X * Math.Sin(r) + d.Y * Math.Cos(r)));
    }
}
public readonly record struct Insets(double Left, double Top, double Right, double Bottom)
{
    public static Insets Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        var p = value.Split(',', StringSplitOptions.TrimEntries).Select(s => Numbers.Parse(s, 0)).ToArray();
        return p.Length switch { 1 => new(p[0],p[0],p[0],p[0]), 2 => new(p[0],p[1],p[0],p[1]), 4 => new(p[0],p[1],p[2],p[3]), _ => default };
    }
}
public static class Numbers
{
    public static double Parse(string? value, double fallback = 0) => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : fallback;
    public static string Format(double n) => n.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    public static double Snap(double n, double grid) => grid > 0 ? Math.Round(n / grid, MidpointRounding.AwayFromZero) * grid : n;
}
