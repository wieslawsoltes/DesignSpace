using DesignSpace.Core;
namespace DesignSpace.Engine;

public enum SnapGuideKind { Alignment, Padding, Margin }
[Flags]
public enum SnapEdges { None=0, Left=1, Top=2, Right=4, Bottom=8 }
public readonly record struct SnapGuide(DPoint Start,DPoint End,SnapGuideKind Kind,double Distance);
public readonly record struct SnaplineResult(DRect Bounds,SnapGuide? XGuide,SnapGuide? YGuide)
{
    public bool SnappedX=>XGuide is not null;
    public bool SnappedY=>YGuide is not null;
}

/// <summary>Immutable per-gesture edge/center/spacing index. Queries allocate no objects.
/// Coordinates are caller-owned design units; pass independent axis tolerances for scaled hosts.</summary>
public sealed class SnaplineIndex
{
    private readonly record struct Anchor(double Position,DRect Source,int Point,SnapGuideKind Kind,int Side);
    private readonly Anchor[] _x,_y;
    private readonly record struct Match(Anchor Anchor,double Delta);
    public int TargetCount { get; }
    public SnaplineIndex(IEnumerable<DRect> targets,DRect? container=null,double margin=8,double padding=8)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if(!double.IsFinite(margin)||margin<0||margin>10000||!double.IsFinite(padding)||padding<0||padding>10000)
            throw new ArgumentOutOfRangeException(nameof(margin));
        var x=new List<Anchor>();var y=new List<Anchor>();var count=0;
        foreach(var box in targets)
        {
            Validate(box);if(++count>DocumentValidator.MaxNodes)throw new ArgumentException("Snap target budget exceeded.",nameof(targets));
            Add(x,box,true,margin);Add(y,box,false,margin);
        }
        TargetCount=count;
        if(container is { } host)
        {
            Validate(host);AddContainer(x,host,true,padding);AddContainer(y,host,false,padding);
        }
        _x=Sort(x);_y=Sort(y);
    }
    private static Anchor[] Sort(List<Anchor> items)
    {
        var sorted=items.OrderBy(a=>a.Position).ThenBy(a=>a.Kind).ThenBy(a=>a.Point)
            .ThenBy(a=>a.Source.X).ThenBy(a=>a.Source.Y).ThenBy(a=>a.Source.Width).ThenBy(a=>a.Source.Height).ThenBy(a=>a.Side);
        var result=new List<Anchor>(items.Count);
        foreach(var anchor in sorted)
        {
            // Thousands of aligned objects share one coordinate bucket and one guide extent.
            if(result.Count>0&&anchor.Kind==SnapGuideKind.Alignment&&result[^1] is var last&&last.Kind==anchor.Kind&&last.Position==anchor.Position)
            {
                var x=Math.Min(last.Source.X,anchor.Source.X);var y=Math.Min(last.Source.Y,anchor.Source.Y);
                result[^1]=last with{Source=new(x,y,Math.Max(last.Source.Right,anchor.Source.Right)-x,Math.Max(last.Source.Bottom,anchor.Source.Bottom)-y)};
            }
            else result.Add(anchor);
        }
        return result.ToArray();
    }
    private static void Add(List<Anchor> items,DRect b,bool x,double margin)
    {
        for(var point=0;point<3;point++)items.Add(new(Coordinate(b,x,point),b,-1,SnapGuideKind.Alignment,0));
        if(margin>0)
        {
            items.Add(new(Coordinate(b,x,0)-margin,b,2,SnapGuideKind.Margin,-1));
            items.Add(new(Coordinate(b,x,2)+margin,b,0,SnapGuideKind.Margin,1));
        }
    }
    private static void AddContainer(List<Anchor> items,DRect b,bool x,double padding)
    {
        Add(items,b,x,0);
        if(padding>0&&padding*2<=(x?b.Width:b.Height))
        {
            items.Add(new(Coordinate(b,x,0)+padding,b,0,SnapGuideKind.Padding,1));
            items.Add(new(Coordinate(b,x,2)-padding,b,2,SnapGuideKind.Padding,-1));
        }
    }
    private static double Coordinate(DRect b,bool x,int point)=>x?b.X+b.Width*point/2:b.Y+b.Height*point/2;
    private static void Validate(DRect b)
    {
        if(!double.IsFinite(b.X)||!double.IsFinite(b.Y)||!double.IsFinite(b.Right)||!double.IsFinite(b.Bottom)||b.Width<0||b.Height<0)
            throw new ArgumentException("Snap bounds must be finite and nonnegative.");
    }
    private static void ValidateTolerance(double x,double y)
    {
        if(!double.IsFinite(x)||!double.IsFinite(y)||x<=0||y<=0)throw new ArgumentOutOfRangeException(nameof(x));
    }
    private static int LowerBound(Anchor[] values,double position)
    {
        var low=0;var high=values.Length;
        while(low<high){var mid=low+(high-low)/2;if(values[mid].Position<position)low=mid+1;else high=mid;}
        return low;
    }
    private static Match? Find(Anchor[] values,DRect box,bool x,int points,double tolerance,bool resize)
    {
        Match? best=null;
        for(var point=0;point<3;point++)
        {
            if((points&(1<<point))==0)continue;
            var coordinate=Coordinate(box,x,point);
            for(var i=LowerBound(values,coordinate-tolerance);i<values.Length&&values[i].Position<=coordinate+tolerance;i++)
            {
                var a=values[i];if(a.Point>=0&&a.Point!=point)continue;
                if(a.Kind==SnapGuideKind.Margin&&
                    (x?Math.Min(box.Bottom,a.Source.Bottom)<=Math.Max(box.Y,a.Source.Y):Math.Min(box.Right,a.Source.Right)<=Math.Max(box.X,a.Source.X)))continue;
                var delta=a.Position-coordinate;
                // Resizing must never cross the fixed opposite edge or collapse to zero.
                if(resize&&(point==0?Coordinate(box,x,2)-a.Position:a.Position-Coordinate(box,x,0))<1)continue;
                if(best is { } old)
                {
                    var distance=Math.Abs(delta).CompareTo(Math.Abs(old.Delta));
                    if(distance>0||distance==0&&((int)a.Kind>(int)old.Anchor.Kind||a.Kind==old.Anchor.Kind&&a.Position>=old.Anchor.Position))continue;
                }
                best=new(a,delta);
            }
        }
        return best;
    }
    public SnaplineResult Move(DRect proposed,double toleranceX,double toleranceY)
    {
        Validate(proposed);ValidateTolerance(toleranceX,toleranceY);
        var x=Find(_x,proposed,true,7,toleranceX,false);var y=Find(_y,proposed,false,7,toleranceY,false);
        var result=proposed.Translate(x?.Delta??0,y?.Delta??0);
        return new(result,Guide(x,result,true),Guide(y,result,false));
    }
    public SnaplineResult Resize(DRect proposed,SnapEdges edges,double toleranceX,double toleranceY)
    {
        Validate(proposed);ValidateTolerance(toleranceX,toleranceY);
        if((edges&~(SnapEdges.Left|SnapEdges.Top|SnapEdges.Right|SnapEdges.Bottom))!=0||
           edges.HasFlag(SnapEdges.Left)&&edges.HasFlag(SnapEdges.Right)||edges.HasFlag(SnapEdges.Top)&&edges.HasFlag(SnapEdges.Bottom))
            throw new ArgumentException("Choose at most one moving edge per axis.",nameof(edges));
        var x=Find(_x,proposed,true,edges.HasFlag(SnapEdges.Left)?1:edges.HasFlag(SnapEdges.Right)?4:0,toleranceX,true);
        var y=Find(_y,proposed,false,edges.HasFlag(SnapEdges.Top)?1:edges.HasFlag(SnapEdges.Bottom)?4:0,toleranceY,true);
        var left=proposed.X+(edges.HasFlag(SnapEdges.Left)?x?.Delta??0:0);var right=proposed.Right+(edges.HasFlag(SnapEdges.Right)?x?.Delta??0:0);
        var top=proposed.Y+(edges.HasFlag(SnapEdges.Top)?y?.Delta??0:0);var bottom=proposed.Bottom+(edges.HasFlag(SnapEdges.Bottom)?y?.Delta??0:0);
        var result=new DRect(left,top,right-left,bottom-top);
        return new(result,Guide(x,result,true),Guide(y,result,false));
    }
    private static SnapGuide? Guide(Match? match,DRect box,bool x)
    {
        if(match is not { } m)return null;var a=m.Anchor;
        if(a.Kind==SnapGuideKind.Alignment)
            return x?new(new(a.Position,Math.Min(box.Y,a.Source.Y)),new(a.Position,Math.Max(box.Bottom,a.Source.Bottom)),a.Kind,0):
                new(new(Math.Min(box.X,a.Source.X),a.Position),new(Math.Max(box.Right,a.Source.Right),a.Position),a.Kind,0);
        var near=Coordinate(box,x,a.Point);var far=Coordinate(a.Source,x,a.Kind==SnapGuideKind.Margin?(a.Side>0?2:0):(a.Side>0?0:2));
        var across=x?(Math.Max(box.Y,a.Source.Y)+Math.Min(box.Bottom,a.Source.Bottom))/2:(Math.Max(box.X,a.Source.X)+Math.Min(box.Right,a.Source.Right))/2;
        return x?new(new(far,across),new(near,across),a.Kind,Math.Abs(near-far)):
            new(new(across,far),new(across,near),a.Kind,Math.Abs(near-far));
    }
}
