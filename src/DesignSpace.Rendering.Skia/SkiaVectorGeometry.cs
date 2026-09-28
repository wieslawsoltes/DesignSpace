using DesignSpace.Core;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

/// <summary>Reusable native vector geometry operations; caller owns returned Skia paths.</summary>
public static class SkiaVectorGeometry
{
    public static SKMatrix Matrix(DMatrix m)=>new(){ScaleX=(float)m.M11,SkewY=(float)m.M12,SkewX=(float)m.M21,ScaleY=(float)m.M22,TransX=(float)m.DX,TransY=(float)m.DY,Persp2=1};
    public static SKPath Create(VectorPath geometry)
    {
        VectorPathCodec.Validate(geometry);var path=new SKPath{FillType=geometry.NonZero ? SKPathFillType.Winding : SKPathFillType.EvenOdd};
        static SKPoint P(DPoint p)=>new((float)p.X,(float)p.Y);
        try
        {
            foreach(var f in geometry.Figures)
            {
                path.MoveTo(P(f.Start));
                foreach(var s in f.Segments)
                    switch(s.Kind)
                    {
                        case VectorSegmentKind.Line:path.LineTo(P(s.End));break;
                        case VectorSegmentKind.Quadratic:path.QuadTo(P(s.Control1),P(s.End));break;
                        case VectorSegmentKind.Cubic:path.CubicTo(P(s.Control1),P(s.Control2),P(s.End));break;
                        case VectorSegmentKind.Arc:path.ArcTo(new SKPoint((float)s.Radius.Width,(float)s.Radius.Height),(float)s.Angle,s.LargeArc ? SKPathArcSize.Large : SKPathArcSize.Small,s.Clockwise ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,P(s.End));break;
                    }
                if(f.Closed)path.Close();
            }
            return path;
        }
        catch{path.Dispose();throw;}
    }
    public static VectorPath Combine(IReadOnlyList<VectorPath> shapes,SKPathOp operation)
    {
        if(shapes.Count<2||shapes.Count>128)throw new ArgumentException("Combine needs between 2 and 128 paths.");
        SKPath current=Create(shapes[0]);
        try
        {
            foreach(var shape in shapes.Skip(1))
            {
                using var next=Create(shape);var result=current.Op(next,operation) ?? throw new InvalidOperationException("Skia could not combine these geometries.");
                current.Dispose();current=result;
            }
            return VectorPathCodec.Parse((current.FillType==SKPathFillType.Winding ? "F1 " : "F0 ")+current.ToSvgPathData());
        }
        finally{current.Dispose();}
    }
}

internal sealed class SkiaVectorCache : IDisposable
{
    private readonly ResourceLruCache<(VectorPath,DMatrix),SKPath> _paths=new(256,8*1024*1024,p=>p.Dispose());
    public long Builds { get; private set; }
    public long Hits { get; private set; }
    public SKPath Get(VectorPath geometry,DMatrix matrix)
    {
        if(_paths.TryGetValue((geometry,matrix),out var path)){Hits++;return path;}
        path=SkiaVectorGeometry.Create(geometry);try{path.Transform(SkiaVectorGeometry.Matrix(matrix));_paths.Add((geometry,matrix),path,256L+geometry.SegmentCount*128L);Builds++;return path;}catch{path.Dispose();throw;}
    }
    public void Dispose()=>_paths.Clear();
}
