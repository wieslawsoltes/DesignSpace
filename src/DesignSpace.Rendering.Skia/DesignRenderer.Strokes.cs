using System.Runtime.CompilerServices;
using DesignSpace.Core;
using DesignSpace.Engine;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

public sealed partial class DesignRenderer : IShapeHitTest
{
    private sealed record ShapeGeometry(VectorPath Path,DMatrix Mapping);
    private readonly ConditionalWeakTable<DesignNode,Dictionary<DSize,ShapeGeometry>> _shapeSources=new();
    private readonly record struct BasicShapeKey(string Type,DSize Size,string A,string B,string C,string D);
    private readonly ResourceLruCache<BasicShapeKey,VectorPath> _basicShapes=new(256,4*1024*1024);
    private readonly StrokeGeometryCache _strokeShapes=new();
    private readonly SKPaint _shapePaint=new(){IsAntialias=true,Style=SKPaintStyle.Fill};
    public long CulledShapeDraws { get; private set; }
    public long StrokeBuildCount=>_strokeShapes.Builds;
    public long StrokeCacheHits=>_strokeShapes.Hits;
    public long StrokeCacheBytes=>_strokeShapes.EstimatedBytes;
    private ShapeGeometry Shape(DesignNode node,DSize size)
    {
        var sizes=_shapeSources.GetOrCreateValue(node);
        if(sizes.TryGetValue(size,out var shape))return shape;
        VectorPath path;
        if(node.Type=="Path")path=VectorGeometry.ReadPath(node);
        else
        {
            var key=node.Type switch
            {
                "Rectangle"=>new BasicShapeKey(node.Type,size,node.Get("RadiusX",node.Get("CornerRadius")),node.Get("RadiusY"),"",""),
                "Line"=>new BasicShapeKey(node.Type,size,node.Get("X1"),node.Get("Y1"),node.Get("X2"),node.Get("Y2")),
                "Polygon" or "Polyline"=>new BasicShapeKey(node.Type,size,node.Get("Points"),"","",""),
                _=>new BasicShapeKey(node.Type,size,"","","","")
            };
            if(!_basicShapes.TryGetValue(key,out path!))
            {
                path=VectorGeometry.Local(node,size);
                _basicShapes.Add(key,path,256L+path.SegmentCount*128L+(key.A.Length+key.B.Length+key.C.Length+key.D.Length)*2L);
            }
        }
        shape=new(path,node.Type=="Path" ? VectorGeometry.Mapping(node,size,path) : DMatrix.Identity);
        if(sizes.Count>=8)sizes.Clear();sizes[size]=shape;return shape;
    }
    private void DrawShape(SKCanvas c,LayoutEntry entry)
    {
        var n=entry.Node;var b=entry.Bounds;var shape=Shape(n,new(b.Width,b.Height));
        var hasFill=n.Type!="Line"&&StrokeStyle.HasBrush(n,"Fill");
        var style=StrokeStyle.HasBrush(n,"Stroke") ? StrokeStyle.Read(n) : null;
        if(!hasFill && (style is null || style.Thickness==0))return;
        var brushBounds=SKRect.Create(0,0,(float)b.Width,(float)b.Height);
        c.Save();c.Translate((float)b.X,(float)b.Y);
        try
        {
            // Shape ink can extend beyond its layout box. Use a conservative
            // geometric bound before native outlining or brush allocation.
            var bounds=shape.Mapping.MapBounds(VectorMath.Bounds(shape.Path));
            var extent=style is null ? 1 : 1+style.Thickness*.5*(style.LineJoin==DesignLineJoin.Miter ? style.MiterLimit : 1);
            var ink=new SKRect((float)(bounds.X-extent),(float)(bounds.Y-extent),(float)(bounds.Right+extent),(float)(bounds.Bottom+extent));
            if(float.IsFinite(ink.Left)&&float.IsFinite(ink.Top)&&float.IsFinite(ink.Right)&&float.IsFinite(ink.Bottom)&&c.QuickReject(ink))
            {
                CulledShapeDraws++;return;
            }
            if(hasFill)
            {
                using var brush=Brush(n,"Fill",brushBounds,_resources);
                _shapePaint.Color=brush is null ? Color(n.Get("Fill"),SKColors.Transparent) : SKColors.White;
                _shapePaint.Shader=brush;
                c.DrawPath(_paths.Get(shape.Path,shape.Mapping),_shapePaint);_shapePaint.Shader=null;
            }
            if(style is { Thickness: > 0 })
            {
                using var brush=Brush(n,"Stroke",brushBounds,_resources);
                _shapePaint.Color=brush is null ? Color(n.Get("Stroke"),SKColors.Transparent) : SKColors.White;
                _shapePaint.Shader=brush;
                c.DrawPath(_strokeShapes.Get(shape.Path,shape.Mapping,style),_shapePaint);_shapePaint.Shader=null;
            }
        }
        finally{_shapePaint.Shader=null;c.Restore();}
    }
    /// <summary>Native shape picking uses exactly the filled geometry painted by DrawShape, including dash gaps.</summary>
    public bool? Contains(DesignNode node,DSize size,DPoint point)
    {
        if(!VectorGeometry.IsShape(node))return null;
        try
        {
            var shape=Shape(node,size);
            if(node.Type!="Line"&&StrokeStyle.HasBrush(node,"Fill")&&_paths.Get(shape.Path,shape.Mapping).Contains((float)point.X,(float)point.Y))return true;
            if(!StrokeStyle.HasBrush(node,"Stroke"))return false;
            var style=StrokeStyle.Read(node);
            return style.Thickness>0&&_strokeShapes.Get(shape.Path,shape.Mapping,style).Contains((float)point.X,(float)point.Y);
        }
        catch(Exception e)when(e is InvalidDataException or InvalidOperationException or ArgumentException){return false;}
    }
}
