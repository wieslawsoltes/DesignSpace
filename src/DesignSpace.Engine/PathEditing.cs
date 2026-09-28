using System.Collections.Immutable;
using DesignSpace.Core;
namespace DesignSpace.Engine;

public enum PathHandleKind { Anchor, Control1, Control2 }
public readonly record struct PathHandle(int Figure,int Segment,PathHandleKind Kind);
/// <summary>Immutable path topology edits with exact line/Bezier subdivision and arc-preserving insertion.</summary>
public static class PathEditing
{
    public static IEnumerable<(PathHandle Handle,DPoint Point,DPoint Anchor)> Handles(VectorPath path)
    {
        for(var f=0;f<path.Figures.Length;f++)
        {
            var figure=path.Figures[f];yield return(new(f,-1,PathHandleKind.Anchor),figure.Start,figure.Start);var start=figure.Start;
            for(var i=0;i<figure.Segments.Length;i++)
            {
                var s=figure.Segments[i];
                if(!(figure.Closed&&i==figure.Segments.Length-1&&s.End==figure.Start))yield return(new(f,i,PathHandleKind.Anchor),s.End,s.End);
                if(s.Kind is VectorSegmentKind.Cubic or VectorSegmentKind.Quadratic)yield return(new(f,i,PathHandleKind.Control1),s.Control1,start);
                if(s.Kind==VectorSegmentKind.Cubic)yield return(new(f,i,PathHandleKind.Control2),s.Control2,s.End);
                start=s.End;
            }
        }
    }
    public static DPoint Position(VectorPath path,PathHandle handle)
    {
        var f=path.Figures[handle.Figure];if(handle.Segment<0)return f.Start;var s=f.Segments[handle.Segment];
        return handle.Kind==PathHandleKind.Control1 ? s.Control1 : handle.Kind==PathHandleKind.Control2 ? s.Control2 : s.End;
    }
    private static VectorPath Replace(VectorPath path,int figure,VectorFigure next)
    {
        var result=path with { Figures=path.Figures.SetItem(figure,next) };VectorPathCodec.Validate(result);return result;
    }
    public static VectorPath Move(VectorPath path,PathHandle handle,DPoint position,bool mirror=false)
    {
        var figure=path.Figures[handle.Figure];var points=figure.Segments.ToBuilder();var old=Position(path,handle);if(old==position)return path;
        var delta=position-old;var index=handle.Segment;var start=figure.Start;
        if(handle.Kind==PathHandleKind.Anchor)
        {
            if(index<0)
            {
                start=position;
                if(points.Count>0&&points[0].Kind is VectorSegmentKind.Cubic or VectorSegmentKind.Quadratic)points[0]=points[0] with { Control1=points[0].Control1+delta };
                if(figure.Closed&&points.Count>0&&points[^1].End==old)points[^1]=points[^1] with { End=position,Control2=points[^1].Control2+delta,Control1=points[^1].Kind==VectorSegmentKind.Quadratic ? points[^1].Control1+delta : points[^1].Control1 };
            }
            else
            {
                var s=points[index];points[index]=s with { End=position,Control2=s.Control2+delta,Control1=s.Kind==VectorSegmentKind.Quadratic ? s.Control1+delta : s.Control1 };
                if(index+1<points.Count&&points[index+1].Kind is VectorSegmentKind.Cubic or VectorSegmentKind.Quadratic)points[index+1]=points[index+1] with { Control1=points[index+1].Control1+delta };
            }
        }
        else
        {
            var s=points[index];points[index]=handle.Kind==PathHandleKind.Control1 ? s with { Control1=position } : s with { Control2=position };
            if(mirror&&s.Kind==VectorSegmentKind.Cubic)
            {
                if(handle.Kind==PathHandleKind.Control1)
                {
                    var previous=index-1;if(previous<0&&figure.Closed&&points[^1].End==figure.Start)previous=points.Count-1;
                    var anchor=index==0 ? figure.Start : points[index-1].End;
                    if(previous>=0&&points[previous].Kind==VectorSegmentKind.Cubic)points[previous]=points[previous] with { Control2=VectorMath.Lerp(position,anchor,2) };
                }
                else
                {
                    var next=index+1;if(next==points.Count&&figure.Closed&&s.End==figure.Start)next=0;
                    if(next<points.Count&&points[next].Kind==VectorSegmentKind.Cubic)points[next]=points[next] with { Control1=VectorMath.Lerp(position,s.End,2) };
                }
            }
        }
        return Replace(path,handle.Figure,figure with { Start=start,Segments=points.ToImmutable() });
    }
    public static VectorPath Insert(VectorPath path,int figureIndex,int segmentIndex,double time)
    {
        if(path.SegmentCount>=VectorPathCodec.MaxSegments)throw new InvalidOperationException("Path segment limit reached.");
        var f=path.Figures[figureIndex];var points=f.Segments;
        if(segmentIndex==points.Length&&f.Closed)
        {
            var start=points.IsEmpty ? f.Start : points[^1].End;var split=VectorMath.Split(start,VectorSegment.Line(f.Start),time);
            return Replace(path,figureIndex,f with { Segments=points.Add(split.First) });
        }
        var begin=segmentIndex==0 ? f.Start : points[segmentIndex-1].End;var pair=VectorMath.Split(begin,points[segmentIndex],time);
        return Replace(path,figureIndex,f with { Segments=points.SetItem(segmentIndex,pair.Second).Insert(segmentIndex,pair.First) });
    }
    public static VectorPath RemoveAnchor(VectorPath path,PathHandle handle)
    {
        if(handle.Kind!=PathHandleKind.Anchor)throw new InvalidOperationException("Select an anchor point, not a tangent handle.");
        var f=path.Figures[handle.Figure];var segments=f.Segments;
        if(segments.Length<=1)return path with { Figures=path.Figures.RemoveAt(handle.Figure) };
        if(handle.Segment<0)
        {
            var start=segments[0].End;var old=f.Start;segments=segments.RemoveAt(0);
            if(f.Closed&&segments[^1].End==old)segments=segments.SetItem(segments.Length-1,VectorSegment.Line(start));
            return Replace(path,handle.Figure,f with { Start=start,Segments=segments });
        }
        var at=handle.Segment;var deleted=segments[at];segments=segments.RemoveAt(at);
        if(at<segments.Length)
        {
            // Preserve the incoming and outgoing cubic tangents when deleting an intermediate anchor.
            var next=segments[at];segments=segments.SetItem(at,deleted.Kind==VectorSegmentKind.Cubic&&next.Kind==VectorSegmentKind.Cubic ? next with { Control1=deleted.Control1 } : VectorSegment.Line(next.End));
        }
        return Replace(path,handle.Figure,f with { Segments=segments });
    }
    /// <summary>Deletes an edge without inventing a replacement; an open contour splits into two figures.</summary>
    public static VectorPath RemoveSegment(VectorPath path,int figureIndex,int segmentIndex)
    {
        var f=path.Figures[figureIndex];var segments=f.Segments;
        if(segmentIndex<0||segmentIndex>segments.Length||segmentIndex==segments.Length&&!f.Closed)throw new ArgumentOutOfRangeException(nameof(segmentIndex));
        if(segmentIndex==segments.Length)return Replace(path,figureIndex,f with { Closed=false });
        if(f.Closed)
        {
            var result=segments.Skip(segmentIndex+1).ToList();
            var last=segments.IsEmpty ? f.Start : segments[^1].End;
            if(last!=f.Start)result.Add(VectorSegment.Line(f.Start));
            result.AddRange(segments.Take(segmentIndex));
            return Replace(path,figureIndex,new(segments[segmentIndex].End,result.ToImmutableArray(),false));
        }
        var replacement=new List<VectorFigure>();
        if(segmentIndex>0)replacement.Add(new(f.Start,segments.Take(segmentIndex).ToImmutableArray()));
        if(segmentIndex+1<segments.Length)replacement.Add(new(segments[segmentIndex].End,segments.Skip(segmentIndex+1).ToImmutableArray()));
        return path with { Figures=path.Figures.RemoveAt(figureIndex).InsertRange(figureIndex,replacement) };
    }
    public static VectorPath SetClosed(VectorPath path,int figure,bool closed)=>Replace(path,figure,path.Figures[figure] with { Closed=closed });
    public static VectorPath SetSegmentKind(VectorPath path,int figure,int segment,bool curve)
    {
        var f=path.Figures[figure];if(segment<0||segment>=f.Segments.Length)throw new InvalidOperationException("Select a segment endpoint.");
        var start=segment==0 ? f.Start : f.Segments[segment-1].End;var end=f.Segments[segment].End;
        return Replace(path,figure,f with { Segments=f.Segments.SetItem(segment,curve ? VectorSegment.Cubic(VectorMath.Lerp(start,end,1d/3),VectorMath.Lerp(start,end,2d/3),end) : VectorSegment.Line(end)) });
    }
    /// <summary>Bounded iterative Ramer-Douglas-Peucker simplification in document units.</summary>
    public static VectorPath Freehand(IReadOnlyList<DPoint> points,double tolerance)
    {
        if(points.Count<2)return VectorPath.Empty;
        if(points.Count>VectorPathCodec.MaxSegments||!double.IsFinite(tolerance)||tolerance<=0)throw new ArgumentOutOfRangeException(nameof(tolerance));
        var keep=new SortedSet<int>{0,points.Count-1};var stack=new Stack<(int,int)>();stack.Push((0,points.Count-1));
        while(stack.TryPop(out var range))
        {
            var (first,last)=range;var a=points[first];var b=points[last];var d=b-a;var denom=d.X*d.X+d.Y*d.Y;var distance=tolerance;var best=-1;
            for(var i=first+1;i<last;i++)
            {
                var p=points[i]-a;var t=denom==0 ? 0 : Math.Clamp((p.X*d.X+p.Y*d.Y)/denom,0,1);var deviation=VectorMath.Length(points[i]-VectorMath.Lerp(a,b,t));
                if(deviation>distance){distance=deviation;best=i;}
            }
            if(best>=0){keep.Add(best);stack.Push((first,best));stack.Push((best,last));}
        }
        var path=new VectorPath([new(points[0],keep.Skip(1).Select(i=>VectorSegment.Line(points[i])).ToImmutableArray())]);VectorPathCodec.Validate(path);return path;
    }
}
