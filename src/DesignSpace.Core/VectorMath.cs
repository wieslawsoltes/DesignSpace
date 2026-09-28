using System.Collections.Immutable;
using System.Runtime.CompilerServices;
namespace DesignSpace.Core;

/// <summary>Elliptical arc center parameterization using endpoint radii correction.</summary>
public readonly record struct VectorArc(DPoint Center,DSize Radius,double Rotation,double Start,double Sweep)
{
    public DPoint Point(double t)
    {
        var angle=Start+Sweep*t;var x=Radius.Width*Math.Cos(angle);var y=Radius.Height*Math.Sin(angle);
        return Center+new DPoint(Math.Cos(Rotation)*x-Math.Sin(Rotation)*y,Math.Sin(Rotation)*x+Math.Cos(Rotation)*y);
    }
    public DPoint Tangent(double t)
    {
        var angle=Start+Sweep*t;var x=-Radius.Width*Math.Sin(angle);var y=Radius.Height*Math.Cos(angle);
        return new(Math.Cos(Rotation)*x-Math.Sin(Rotation)*y,Math.Sin(Rotation)*x+Math.Cos(Rotation)*y);
    }
    public static bool TryCreate(DPoint start,VectorSegment segment,out VectorArc arc)
    {
        arc=default;var rx=segment.Radius.Width;var ry=segment.Radius.Height;
        if(rx<=0||ry<=0||start==segment.End)return false;
        var phi=segment.Angle%360*Math.PI/180;var cos=Math.Cos(phi);var sin=Math.Sin(phi);var delta=VectorMath.Multiply(start-segment.End,.5);
        var x=cos*delta.X+sin*delta.Y;var y=-sin*delta.X+cos*delta.Y;
        var scale=x*x/(rx*rx)+y*y/(ry*ry);if(scale>1){scale=Math.Sqrt(scale);rx*=scale;ry*=scale;}
        var denom=rx*rx*y*y+ry*ry*x*x;
        if(denom<=0||!double.IsFinite(denom))return false;
        var factor=(segment.LargeArc==segment.Clockwise ? -1 : 1)*Math.Sqrt(Math.Max(0,(rx*rx*ry*ry-denom)/denom));
        var cx=factor*rx*y/ry;var cy=-factor*ry*x/rx;
        var center=VectorMath.Lerp(start,segment.End,.5)+new DPoint(cos*cx-sin*cy,sin*cx+cos*cy);
        var u=new DPoint((x-cx)/rx,(y-cy)/ry);var v=new DPoint((-x-cx)/rx,(-y-cy)/ry);
        var angle=Math.Atan2(u.Y,u.X);var sweep=Math.Atan2(u.X*v.Y-u.Y*v.X,u.X*v.X+u.Y*v.Y);
        if(segment.Clockwise&&sweep<0)sweep+=2*Math.PI;else if(!segment.Clockwise&&sweep>0)sweep-=2*Math.PI;
        arc=new(center,new(rx,ry),phi,angle,sweep);return true;
    }
}
public readonly record struct PathEdge(int Figure,int Segment,double From,double To,DPoint Start,DPoint End);
public readonly record struct PathLocation(int Figure,int Segment,double Time,double Distance);
/// <summary>Shared exact bounds and adaptive flattening, cached by immutable geometry identity.</summary>
public static class VectorMath
{
    private sealed class Metrics(VectorPath path)
    {
        public DRect Bounds { get; }=ComputeBounds(path);
        private readonly Lazy<ImmutableArray<PathEdge>> _edges=new(()=>Flatten(path,.2));
        public ImmutableArray<PathEdge> Edges=>_edges.Value;
    }
    private static readonly ConditionalWeakTable<VectorPath,Metrics> Cache=new();
    public static DPoint Multiply(DPoint point,double scale)=>new(point.X*scale,point.Y*scale);
    public static DPoint Lerp(DPoint a,DPoint b,double t)=>a+Multiply(b-a,t);
    public static double Length(DPoint point)=>Math.Sqrt(point.X*point.X+point.Y*point.Y);
    public static DRect Bounds(VectorPath path)=>Cache.GetValue(path,p=>new(p)).Bounds;
    public static DPoint Evaluate(DPoint start,VectorSegment s,double t)=>s.Kind switch
    {
        VectorSegmentKind.Line=>Lerp(start,s.End,t),
        VectorSegmentKind.Quadratic=>Lerp(Lerp(start,s.Control1,t),Lerp(s.Control1,s.End,t),t),
        VectorSegmentKind.Cubic=>Lerp(Lerp(Lerp(start,s.Control1,t),Lerp(s.Control1,s.Control2,t),t),Lerp(Lerp(s.Control1,s.Control2,t),Lerp(s.Control2,s.End,t),t),t),
        _=>VectorArc.TryCreate(start,s,out var arc) ? arc.Point(t) : Lerp(start,s.End,t)
    };
    public static (VectorSegment First,VectorSegment Second) Split(DPoint start,VectorSegment s,double t)
    {
        if(!double.IsFinite(t)||t<=0||t>=1)throw new ArgumentOutOfRangeException(nameof(t));
        var mid=Evaluate(start,s,t);
        if(s.Kind==VectorSegmentKind.Quadratic){var a=Lerp(start,s.Control1,t);var b=Lerp(s.Control1,s.End,t);return(s with { End=mid,Control1=a },s with { Control1=b });}
        if(s.Kind==VectorSegmentKind.Cubic)
        {
            var a=Lerp(start,s.Control1,t);var b=Lerp(s.Control1,s.Control2,t);var c=Lerp(s.Control2,s.End,t);var d=Lerp(a,b,t);var e=Lerp(b,c,t);
            return(VectorSegment.Cubic(a,d,mid),VectorSegment.Cubic(e,c,s.End));
        }
        if(s.Kind==VectorSegmentKind.Arc&&VectorArc.TryCreate(start,s,out var arc))return(s with { End=mid,Radius=arc.Radius,LargeArc=Math.Abs(arc.Sweep*t)>Math.PI },s with { Radius=arc.Radius,LargeArc=Math.Abs(arc.Sweep*(1-t))>Math.PI });
        return(VectorSegment.Line(mid),VectorSegment.Line(s.End));
    }
    private static DRect ComputeBounds(VectorPath path)
    {
        var points=new List<DPoint>();
        foreach(var f in path.Figures)
        {
            points.Add(f.Start);var start=f.Start;
            foreach(var s in f.Segments)
            {
                points.Add(s.End);
                if(s.Kind is VectorSegmentKind.Cubic or VectorSegmentKind.Quadratic)
                {
                    IEnumerable<double> Roots(double p,double a,double b,double e)
                    {
                        if(s.Kind==VectorSegmentKind.Quadratic){var den=p-2*a+e;if(Math.Abs(den)>1e-14)yield return(p-a)/den;yield break;}
                        var aa=-p+3*a-3*b+e;var bb=2*(p-2*a+b);var cc=a-p;
                        if(Math.Abs(aa)<1e-14){if(Math.Abs(bb)>1e-14)yield return-cc/bb;yield break;}
                        var d=bb*bb-4*aa*cc;if(d<0)yield break;yield return(-bb+Math.Sqrt(d))/(2*aa);yield return(-bb-Math.Sqrt(d))/(2*aa);
                    }
                    foreach(var t in Roots(start.X,s.Control1.X,s.Control2.X,s.End.X).Concat(Roots(start.Y,s.Control1.Y,s.Control2.Y,s.End.Y)))if(t>0&&t<1)points.Add(Evaluate(start,s,t));
                }
                if(s.Kind==VectorSegmentKind.Arc&&VectorArc.TryCreate(start,s,out var arc))
                {
                    var x=Math.Atan2(-arc.Radius.Height*Math.Sin(arc.Rotation),arc.Radius.Width*Math.Cos(arc.Rotation));
                    var y=Math.Atan2(arc.Radius.Height*Math.Cos(arc.Rotation),arc.Radius.Width*Math.Sin(arc.Rotation));
                    foreach(var a in new[]{x,x+Math.PI,y,y+Math.PI})
                    {
                        var delta=(a-arc.Start)%(2*Math.PI);if(arc.Sweep>=0&&delta<0)delta+=2*Math.PI;if(arc.Sweep<0&&delta>0)delta-=2*Math.PI;
                        var t=delta/arc.Sweep;if(t>=0&&t<=1)points.Add(arc.Point(t));
                    }
                }
                start=s.End;
            }
        }
        if(points.Count==0)return default;
        var left=points.Min(p=>p.X);var top=points.Min(p=>p.Y);return new(left,top,points.Max(p=>p.X)-left,points.Max(p=>p.Y)-top);
    }
    public static ImmutableArray<PathEdge> Flatten(VectorPath path,double tolerance=.2)
    {
        if(!double.IsFinite(tolerance)||tolerance<=0)throw new ArgumentOutOfRangeException(nameof(tolerance));
        var edges=ImmutableArray.CreateBuilder<PathEdge>();
        void Edge(int f,int i,DPoint start,VectorSegment s,double a,double b,DPoint p,DPoint q,int depth)
        {
            if(edges.Count>=131072)throw new InvalidDataException("Path tessellation exceeds the safety budget.");
            var m=(a+b)/2;var mid=Evaluate(start,s,m);
            var deviation=Math.Max(DistanceToLine(mid,p,q).Distance,Math.Max(DistanceToLine(Evaluate(start,s,(a+m)/2),p,q).Distance,DistanceToLine(Evaluate(start,s,(m+b)/2),p,q).Distance));
            if(s.Kind==VectorSegmentKind.Line||deviation<=tolerance||depth>=12){edges.Add(new(f,i,a,b,p,q));return;}
            Edge(f,i,start,s,a,m,p,mid,depth+1);Edge(f,i,start,s,m,b,mid,q,depth+1);
        }
        for(var f=0;f<path.Figures.Length;f++)
        {
            var figure=path.Figures[f];var start=figure.Start;
            for(var i=0;i<figure.Segments.Length;i++){var segment=figure.Segments[i];Edge(f,i,start,segment,0,1,start,segment.End,0);start=segment.End;}
            if(figure.Closed&&start!=figure.Start)edges.Add(new(f,figure.Segments.Length,0,1,start,figure.Start));
        }
        return edges.ToImmutable();
    }
    private static (double Time,double Distance) DistanceToLine(DPoint p,DPoint a,DPoint b)
    {
        var d=b-a;var length=d.X*d.X+d.Y*d.Y;var t=length==0 ? 0 : Math.Clamp(((p.X-a.X)*d.X+(p.Y-a.Y)*d.Y)/length,0,1);
        return(t,Length(p-Lerp(a,b,t)));
    }
    public static PathLocation? Nearest(VectorPath path,DPoint point,DMatrix? transform=null,double maxDistance=double.PositiveInfinity)
    {
        var matrix=transform ?? DMatrix.Identity;PathLocation? nearest=null;var distance=maxDistance;
        foreach(var edge in Cache.GetValue(path,p=>new(p)).Edges)
        {
            var hit=DistanceToLine(point,matrix.Map(edge.Start),matrix.Map(edge.End));
            if(hit.Distance<=distance){distance=hit.Distance;nearest=new(edge.Figure,edge.Segment,edge.From+(edge.To-edge.From)*hit.Time,hit.Distance);}
        }
        return nearest;
    }
    public static bool Contains(VectorPath path,DPoint point)
    {
        var winding=0;
        void Cross(DPoint a,DPoint b)
        {
            if((a.Y<=point.Y&&b.Y>point.Y)||(a.Y>point.Y&&b.Y<=point.Y))
                if(a.X+(point.Y-a.Y)*(b.X-a.X)/(b.Y-a.Y)>point.X)winding+=b.Y>a.Y ? 1 : -1;
        }
        foreach(var edge in Cache.GetValue(path,p=>new(p)).Edges)Cross(edge.Start,edge.End);
        foreach(var figure in path.Figures)if(!figure.Closed&&!figure.Segments.IsEmpty)Cross(figure.Segments[^1].End,figure.Start);
        return path.NonZero ? winding!=0 : (winding&1)!=0;
    }
    /// <summary>Transforms points exactly; nondegenerate arcs are approximated by cubic segments (at most 45° each).</summary>
    public static VectorPath Transform(VectorPath path,DMatrix transform)
    {
        if(transform==DMatrix.Identity)return path;
        // Translation and positive uniform scale keep endpoint arcs exact.
        if(transform.M12==0&&transform.M21==0&&transform.M11>0&&transform.M11==transform.M22)
        {
            var exact=path with { Figures=path.Figures.Select(f=>f with { Start=transform.Map(f.Start),Segments=f.Segments.Select(s=>s with { End=transform.Map(s.End),Control1=transform.Map(s.Control1),Control2=transform.Map(s.Control2),Radius=new(s.Radius.Width*transform.M11,s.Radius.Height*transform.M22) }).ToImmutableArray() }).ToImmutableArray() };
            VectorPathCodec.Validate(exact);return exact;
        }
        var figures=ImmutableArray.CreateBuilder<VectorFigure>();
        foreach(var f in path.Figures)
        {
            var segments=ImmutableArray.CreateBuilder<VectorSegment>();var start=f.Start;
            foreach(var s in f.Segments)
            {
                if(s.Kind==VectorSegmentKind.Arc&&VectorArc.TryCreate(start,s,out var arc))
                {
                    var count=Math.Max(1,(int)Math.Ceiling(Math.Abs(arc.Sweep)/(Math.PI/4)));
                    for(var i=0;i<count;i++)
                    {
                        var t0=(double)i/count;var t1=(double)(i+1)/count;var k=4d/3*Math.Tan(arc.Sweep/count/4);
                        segments.Add(VectorSegment.Cubic(transform.Map(arc.Point(t0)+Multiply(arc.Tangent(t0),k)),transform.Map(arc.Point(t1)-Multiply(arc.Tangent(t1),k)),transform.Map(i==count-1 ? s.End : arc.Point(t1))));
                    }
                }
                else segments.Add(s.Kind==VectorSegmentKind.Arc ? VectorSegment.Line(transform.Map(s.End)) : s with { End=transform.Map(s.End),Control1=transform.Map(s.Control1),Control2=transform.Map(s.Control2) });
                start=s.End;
            }
            figures.Add(new(transform.Map(f.Start),segments.ToImmutable(),f.Closed));
        }
        var result=new VectorPath(figures.ToImmutable(),path.NonZero);VectorPathCodec.Validate(result);return result;
    }
}
