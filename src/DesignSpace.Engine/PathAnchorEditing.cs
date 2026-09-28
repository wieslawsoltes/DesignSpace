using System.Collections.Immutable;
using DesignSpace.Core;
namespace DesignSpace.Engine;

/// <summary>Batch anchor operations. Work is linear in path size, not one path copy per selected point.</summary>
public static class PathAnchorEditing
{
    public static IEnumerable<PathHandle> Anchors(VectorPath path)
    {
        for(var f=0;f<path.Figures.Length;f++)
        {
            var figure=path.Figures[f]; yield return new(f,-1,PathHandleKind.Anchor);
            for(var i=0;i<figure.Segments.Length;i++)
                if(!IsSeam(figure,i)) yield return new(f,i,PathHandleKind.Anchor);
        }
    }
    private static bool IsSeam(VectorFigure f,int i)=>f.Closed && i==f.Segments.Length-1 && f.Segments[i].End==f.Start;
    private static PathHandle Canonical(VectorPath path,PathHandle handle)
    {
        if(handle.Kind!=PathHandleKind.Anchor || handle.Figure<0 || handle.Figure>=path.Figures.Length)
            throw new ArgumentException("Select valid path anchors, not tangent handles.",nameof(handle));
        var f=path.Figures[handle.Figure];
        if(handle.Segment < -1 || handle.Segment>=f.Segments.Length) throw new ArgumentOutOfRangeException(nameof(handle));
        return handle.Segment>=0 && IsSeam(f,handle.Segment) ? handle with { Segment=-1 } : handle;
    }
    public static ImmutableHashSet<PathHandle> Selection(VectorPath path,IEnumerable<PathHandle> handles)
    {
        VectorPathCodec.Validate(path); ArgumentNullException.ThrowIfNull(handles);
        return handles.Select(h=>Canonical(path,h)).ToImmutableHashSet();
    }
    public static ImmutableHashSet<PathHandle> SelectInRectangle(VectorPath path,DRect rectangle,DMatrix toSelectionSpace)
    {
        VectorPathCodec.Validate(path); CheckMatrix(toSelectionSpace);
        if(!double.IsFinite(rectangle.X) || !double.IsFinite(rectangle.Y) || !double.IsFinite(rectangle.Right) || !double.IsFinite(rectangle.Bottom) || rectangle.Width<0 || rectangle.Height<0)
            throw new ArgumentOutOfRangeException(nameof(rectangle));
        return Anchors(path).Where(h=>rectangle.Contains(toSelectionSpace.Map(PathEditing.Position(path,h)))).ToImmutableHashSet();
    }
    public static VectorPath Translate(VectorPath path,IEnumerable<PathHandle> handles,DPoint delta)
    {
        if(!double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) throw new ArgumentOutOfRangeException(nameof(delta));
        var selected=Selection(path,handles);
        if(selected.IsEmpty || delta==default) return path;
        return MoveTo(path,selected.ToDictionary(h=>h,h=>PathEditing.Position(path,h)+delta));
    }
    /// <summary>Moves each anchor once. A quadratic shared by two moved endpoints follows their average displacement.</summary>
    public static VectorPath MoveTo(VectorPath path,IReadOnlyDictionary<PathHandle,DPoint> positions)
    {
        VectorPathCodec.Validate(path); ArgumentNullException.ThrowIfNull(positions);
        var changes=new Dictionary<PathHandle,DPoint>();
        foreach(var pair in positions)
        {
            var handle=Canonical(path,pair.Key); var p=pair.Value;
            if(!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X)>VectorPathCodec.MaxCoordinate || Math.Abs(p.Y)>VectorPathCodec.MaxCoordinate)
                throw new ArgumentOutOfRangeException(nameof(positions),"Anchor positions exceed the finite coordinate budget.");
            if(changes.TryGetValue(handle,out var old) && old!=p) throw new ArgumentException("The closing seam has conflicting anchor positions.",nameof(positions));
            changes[handle]=p;
        }
        if(changes.Count==0) return path;
        var affected=changes.Keys.Select(h=>h.Figure).ToHashSet();
        ImmutableArray<VectorFigure>.Builder? figures=null;
        foreach(var fi in affected)
        {
            var f=path.Figures[fi]; var startHandle=new PathHandle(fi,-1,PathHandleKind.Anchor);
            var start=changes.GetValueOrDefault(startHandle,f.Start);
            ImmutableArray<VectorSegment>.Builder? segments=null;
            for(var i=0;i<f.Segments.Length;i++)
            {
                var s=f.Segments[i]; var a=i==0 ? f.Start : f.Segments[i-1].End;
                var ah=new PathHandle(fi,i-1,PathHandleKind.Anchor);
                var bh=IsSeam(f,i) ? startHandle : new PathHandle(fi,i,PathHandleKind.Anchor);
                var moveA=changes.TryGetValue(ah,out var ap); var moveB=changes.TryGetValue(bh,out var bp);
                var da=moveA ? ap-a : default; var db=moveB ? bp-s.End : default;
                if(da==default && db==default) continue;
                var next=s with { End=s.End+db };
                if(s.Kind==VectorSegmentKind.Cubic) next=next with { Control1=s.Control1+da,Control2=s.Control2+db };
                if(s.Kind==VectorSegmentKind.Quadratic)
                {
                    var d=moveA && moveB ? new DPoint((da.X+db.X)/2,(da.Y+db.Y)/2) : da+db;
                    next=next with { Control1=s.Control1+d };
                }
                // Arcs retain their radii/rotation; only selected endpoints move.
                if(next==s) continue;
                segments ??= f.Segments.ToBuilder(); segments[i]=next;
            }
            if(segments is null && start==f.Start) continue;
            figures ??= path.Figures.ToBuilder(); figures[fi]=f with { Start=start,Segments=segments?.ToImmutable() ?? f.Segments };
        }
        if(figures is null) return path;
        var result=path with { Figures=figures.ToImmutable() }; VectorPathCodec.Validate(result); return result;
    }
    /// <summary>Deletes a selection in one pass; surviving adjacent edges are retained and skipped anchors are bridged.</summary>
    public static VectorPath Remove(VectorPath path,IEnumerable<PathHandle> handles)
    {
        var selection=Selection(path,handles); if(selection.IsEmpty) return path;
        var byFigure=selection.GroupBy(h=>h.Figure).ToDictionary(g=>g.Key,g=>g.Select(h=>h.Segment+1).ToHashSet());
        var result=ImmutableArray.CreateBuilder<VectorFigure>();
        for(var fi=0;fi<path.Figures.Length;fi++)
        {
            var f=path.Figures[fi]; if(!byFigure.TryGetValue(fi,out var deleted)) { result.Add(f); continue; }
            var seam=f.Segments.Length>0 && IsSeam(f,f.Segments.Length-1);
            var count=f.Segments.Length+1-(seam ? 1 : 0);
            var kept=Enumerable.Range(0,count).Where(i=>!deleted.Contains(i)).ToArray();
            if(kept.Length<2) continue;
            DPoint Point(int i)=>i==0 ? f.Start : f.Segments[i-1].End;
            VectorSegment Edge(int i)=>i<f.Segments.Length ? f.Segments[i] : VectorSegment.Line(f.Start);
            var segments=ImmutableArray.CreateBuilder<VectorSegment>();
            for(var k=1;k<kept.Length+(f.Closed ? 1 : 0);k++)
            {
                var from=kept[k-1]; var to=kept[k%kept.Length]; var first=Edge(from);
                var adjacent=(from+1)%count==to;
                var last=Edge((to+count-1)%count);
                var next=adjacent ? first : first.Kind==VectorSegmentKind.Cubic && last.Kind==VectorSegmentKind.Cubic
                    ? VectorSegment.Cubic(first.Control1,last.Control2,Point(to)) : VectorSegment.Line(Point(to));
                // Keep an implicit closing edge implicit when possible.
                if(k==kept.Length && next.Kind==VectorSegmentKind.Line) continue;
                segments.Add(next);
            }
            result.Add(new(Point(kept[0]),segments.ToImmutable(),f.Closed));
        }
        var nextPath=path with { Figures=result.ToImmutable() }; VectorPathCodec.Validate(nextPath); return nextPath;
    }
    public static VectorPath Align(VectorPath path,IEnumerable<PathHandle> handles,string alignment,DMatrix space)
    {
        if(alignment is not ("Left" or "Center" or "Right" or "Top" or "Middle" or "Bottom")) throw new ArgumentException("Unknown point alignment.",nameof(alignment));
        var selected=Selection(path,handles); CheckMatrix(space);
        if(!space.TryInvert(out var inverse)) throw new InvalidOperationException("The editing coordinate system is not invertible.");
        if(selected.Count<2) return path;
        var points=selected.ToDictionary(h=>h,h=>space.Map(PathEditing.Position(path,h)));
        var horizontal=alignment is "Left" or "Center" or "Right";
        var min=points.Values.Min(p=>horizontal ? p.X : p.Y); var max=points.Values.Max(p=>horizontal ? p.X : p.Y);
        var target=alignment is "Left" or "Top" ? min : alignment is "Right" or "Bottom" ? max : min+(max-min)/2;
        return MoveTo(path,points.ToDictionary(p=>p.Key,p=>inverse.Map(horizontal ? p.Value with { X=target } : p.Value with { Y=target })));
    }
    public static VectorPath Distribute(VectorPath path,IEnumerable<PathHandle> handles,bool horizontal,DMatrix space)
    {
        var selected=Selection(path,handles); CheckMatrix(space);
        if(!space.TryInvert(out var inverse)) throw new InvalidOperationException("The editing coordinate system is not invertible.");
        if(selected.Count<3) return path;
        var points=selected.OrderBy(h=>h.Figure).ThenBy(h=>h.Segment).Select(h=>(Handle:h,Point:space.Map(PathEditing.Position(path,h)))).OrderBy(p=>horizontal ? p.Point.X : p.Point.Y).ToArray();
        var first=horizontal ? points[0].Point.X : points[0].Point.Y; var last=horizontal ? points[^1].Point.X : points[^1].Point.Y;
        var changes=new Dictionary<PathHandle,DPoint>();
        for(var i=0;i<points.Length;i++)
        {
            var coordinate=first+(last-first)*i/(points.Length-1);
            changes.Add(points[i].Handle,inverse.Map(horizontal ? points[i].Point with { X=coordinate } : points[i].Point with { Y=coordinate }));
        }
        return MoveTo(path,changes);
    }
    private static void CheckMatrix(DMatrix m)
    {
        if(!double.IsFinite(m.M11)||!double.IsFinite(m.M12)||!double.IsFinite(m.M21)||!double.IsFinite(m.M22)||!double.IsFinite(m.DX)||!double.IsFinite(m.DY)) throw new ArgumentException("The editing coordinate system must be finite.");
    }
}
