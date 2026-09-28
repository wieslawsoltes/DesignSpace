using DesignSpace.Core;
namespace DesignSpace.Engine;

public sealed record LayoutEntry(DesignNode Node,DRect Bounds,double Opacity,int Depth,Guid? ParentId)
{
    public DMatrix LocalTransform { get; init; }=DMatrix.Identity;
    public DMatrix WorldTransform { get; init; }=DMatrix.Identity;
    public bool IsEffectivelyLocked { get; init; }
    public bool IsEffectivelyVisible { get; init; }=true;
    public bool IsHitTestVisible { get; init; }=true;
    public DRect VisualBounds=>WorldTransform.MapBounds(Bounds);
}
public sealed class LayoutSnapshot
{
    public IReadOnlyList<LayoutEntry> Entries { get; }
    public IReadOnlyDictionary<Guid,LayoutEntry> ById { get; }
    public LayoutSnapshot(IReadOnlyList<LayoutEntry> entries)
    {
        Entries=entries; ById=entries.ToDictionary(e=>e.Node.Id);
    }
    public LayoutEntry? HitTest(DPoint point,bool includeLocked=false)=>HitStack(point,includeLocked).FirstOrDefault();
    public IEnumerable<LayoutEntry> HitStack(DPoint point,bool includeLocked=false)
    {
        for(var i=Entries.Count-1;i>=0;i--)
        {
            var e=Entries[i];
            if(e.Depth==0 || !e.IsEffectivelyVisible || !e.IsHitTestVisible || (!includeLocked && e.IsEffectivelyLocked) || !e.WorldTransform.TryInvert(out var inverse)) continue;
            var p=inverse.Map(point); if(!Contains(e,p) || IsClipped(e,point)) continue;
            yield return e;
        }
    }
    public bool IsClipped(LayoutEntry e,DPoint point)
    {
        while(e.ParentId is { } parent && ById.TryGetValue(parent,out var ancestor))
        {
            e=ancestor;
            if(e.Depth==0 || e.Node.Get("ClipToBounds")=="True" || e.Node.Type is "Page" or "UserControl" or "Window")
                if(!e.WorldTransform.TryInvert(out var inverse) || !e.Bounds.Contains(inverse.Map(point))) return true;
        }
        return false;
    }
    private static bool Contains(LayoutEntry e,DPoint p)
    {
        var b=e.Bounds;
        if(e.Node.Type=="Line")
        {
            var dx=b.Width; var dy=b.Height; var length=dx*dx+dy*dy;
            var t=length>0 ? Math.Clamp(((p.X-b.X)*dx+(p.Y-b.Y)*dy)/length,0,1) : 0;
            return Math.Pow(p.X-b.X-t*dx,2)+Math.Pow(p.Y-b.Y-t*dy,2)<=Math.Pow(Math.Max(3,e.Node.Number("StrokeThickness",1)/2),2);
        }
        if(!b.Contains(p)) return false;
        if(e.Node.Type=="Ellipse") return b.Width>0 && b.Height>0 && Math.Pow((p.X-b.Center.X)/(b.Width/2),2)+Math.Pow((p.Y-b.Center.Y)/(b.Height/2),2)<=1;
        return true;
    }
    public DRect BoundsOf(IEnumerable<Guid> ids)=>DRect.Union(ids.Where(ById.ContainsKey).Select(id=>ById[id].VisualBounds));
}
