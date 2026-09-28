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
    public LayoutSnapshot(IReadOnlyList<LayoutEntry> entries) { Entries=entries; ById=entries.ToDictionary(e=>e.Node.Id); }
    public LayoutEntry? HitTest(DPoint point,bool includeLocked=false)=>HitStack(point,includeLocked).FirstOrDefault();
    /// <summary>Returns editable owners for generated template visuals, in front-to-back order.</summary>
    public IEnumerable<LayoutEntry> HitStack(DPoint point,bool includeLocked=false)
    {
        var yielded=new HashSet<Guid>();
        for(var i=Entries.Count-1;i>=0;i--)
        {
            var e=Entries[i];
            if(e.Depth==0 || !e.IsEffectivelyVisible || !e.IsHitTestVisible || (!includeLocked && e.IsEffectivelyLocked) || !e.WorldTransform.TryInvert(out var inverse)) continue;
            if(!Contains(e,inverse.Map(point)) || IsClipped(e,point)) continue;
            var owner=e.Node.Get("{https://designspace.dev/designer}TemplateOwner");
            if(Guid.TryParse(owner,out var id) && ById.TryGetValue(id,out var host)) e=host;
            if(yielded.Add(e.Node.Id)) yield return e;
        }
    }
    public bool IsClipped(LayoutEntry e,DPoint point)
    {
        static bool OutsideClip(LayoutEntry entry,DPoint point)
        {
            try
            {
                var clip=VectorGeometry.ReadClip(entry.Node);return clip is not null&&(!entry.WorldTransform.TryInvert(out var inverse)||!VectorMath.Contains(clip,inverse.Map(point)-new DPoint(entry.Bounds.X,entry.Bounds.Y)));
            }
            catch(InvalidDataException){return true;}
        }
        if(OutsideClip(e,point))return true;
        while(e.ParentId is { } parent && ById.TryGetValue(parent,out var ancestor))
        {
            e=ancestor;if(OutsideClip(e,point))return true;
            if(e.Depth==0 || e.Node.Get("ClipToBounds")=="True" || e.Node.Type is "Page" or "UserControl" or "Window")
                if(!e.WorldTransform.TryInvert(out var inverse) || !e.Bounds.Contains(inverse.Map(point))) return true;
        }
        return false;
    }
    private static bool Contains(LayoutEntry e,DPoint p)
    {
        var b=e.Bounds;
        if(e.Node.Type is "Path" or "Polygon" or "Polyline")
        {
            try
            {
                var node=e.Node;var geometry=node.Type=="Path" ? VectorGeometry.ReadPath(node) : VectorGeometry.Local(node,new(b.Width,b.Height));
                var mapping=node.Type=="Path" ? VectorGeometry.Mapping(node,new(b.Width,b.Height),geometry) : DMatrix.Identity;
                var local=p-new DPoint(b.X,b.Y);
                var hasFill=node.Get("Fill").Length>0||node.PropertyElements.Any(raw=>raw.Contains(".Fill",StringComparison.Ordinal));
                if(hasFill&&mapping.TryInvert(out var inv)&&VectorMath.Contains(geometry,inv.Map(local)))return true;
                return node.Get("Stroke").Length>0&&VectorMath.Nearest(geometry,local,mapping,Math.Max(3,node.Number("StrokeThickness",1)/2)) is not null;
            }
            catch(InvalidDataException){return false;}
        }
        if(e.Node.Type=="Line")
        {
            var x=b.X+e.Node.Number("X1");var y=b.Y+e.Node.Number("Y1");var dx=e.Node.Number("X2",b.Width)-e.Node.Number("X1");var dy=e.Node.Number("Y2",b.Height)-e.Node.Number("Y1"); var length=dx*dx+dy*dy;
            var t=length>0 ? Math.Clamp(((p.X-x)*dx+(p.Y-y)*dy)/length,0,1) : 0;
            return Math.Pow(p.X-x-t*dx,2)+Math.Pow(p.Y-y-t*dy,2)<=Math.Pow(Math.Max(3,e.Node.Number("StrokeThickness",1)/2),2);
        }
        if(!b.Contains(p)) return false;
        return e.Node.Type!="Ellipse" || b.Width>0 && b.Height>0 && Math.Pow((p.X-b.Center.X)/(b.Width/2),2)+Math.Pow((p.Y-b.Center.Y)/(b.Height/2),2)<=1;
    }
    public DRect BoundsOf(IEnumerable<Guid> ids)=>DRect.Union(ids.Where(ById.ContainsKey).Select(id=>ById[id].VisualBounds));
}
