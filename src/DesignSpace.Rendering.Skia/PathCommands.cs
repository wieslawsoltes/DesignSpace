using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
using DesignSpace.Engine;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

/// <summary>Transactional vector authoring commands shared by any host. Never rasterizes editable shapes.</summary>
public static class PathCommands
{
    public static void Convert(DesignSession session,LayoutSnapshot layout)
    {
        var nodes=session.TopLevelSelection().Select(session.Index.Find).OfType<DesignNode>().ToArray();
        if(nodes.Length==0)throw new InvalidOperationException("Select shapes to convert.");
        var changed=new Dictionary<Guid,DesignNode>();
        foreach(var n in nodes)
        {
            if(session.Index.IsLocked(n.Id))throw new InvalidOperationException("Unlock the selection and its ancestors first.");
            if(n.Type=="Path")continue;
            if(n.Get("Style").Length>0||n.PropertyElements.Any(raw=>XElement.Parse(raw).Name.LocalName.EndsWith(".Style",StringComparison.Ordinal)))throw new InvalidOperationException("Resolve explicit styles to local values before changing a shape's type.");
            if(!n.Children.IsEmpty||!layout.ById.TryGetValue(n.Id,out var entry))throw new InvalidOperationException("Select visible shapes without children.");
            var path=VectorGeometry.Local(n,new(entry.Bounds.Width,entry.Bounds.Height));
            changed[n.Id]=VectorGeometry.WithPath(n,path);
        }
        if(changed.Count==0)return;
        session.Execute("Convert to path",d=>d with { Root=DesignIndex.For(d.Root).Transform(changed.Keys.ToHashSet(),n=>changed[n.Id]) });
    }
    /// <summary>Creates one normalized Canvas child, retaining the input's document-space anchor coordinates.</summary>
    public static DesignNode CreateNode(string name,VectorPath path,DesignNode? appearance=null)
    {
        VectorPathCodec.Validate(path);var bounds=VectorMath.Bounds(path);var stroke=appearance?.Number("StrokeThickness",1) ?? 2;
        var x=bounds.X-stroke/2;var y=bounds.Y-stroke/2;
        var local=VectorMath.Transform(path,DMatrix.Translate(-x,-y));
        var n=appearance ?? new DesignNode{Type="Path"}.Set("Stroke","#FF0078D4").Set("StrokeThickness","2").Set("Fill",path.Figures.Any(f=>f.Closed) ? "#FF8BC8FF" : "Transparent");
        n=VectorGeometry.WithPath(n,local).Set(DesignNode.NameKey,name).Set("Canvas.Left",x).Set("Canvas.Top",y).Set("Width",Math.Max(1,bounds.Width+stroke)).Set("Height",Math.Max(1,bounds.Height+stroke));
        return n;
    }
    private static (DesignNode Parent,DesignNode[] Nodes,VectorPath[] Paths) Operands(DesignSession session,LayoutSnapshot layout,int minimum)
    {
        var ids=session.TopLevelSelection();if(ids.Count<minimum)throw new InvalidOperationException($"Select at least {minimum} shapes.");
        var parent=session.Index.ParentOf(ids.First());
        if(parent?.Type!="Canvas"||ids.Any(id=>session.Index.ParentOf(id)?.Id!=parent.Id))throw new InvalidOperationException("Select shapes in the same Canvas.");
        var ordered=parent.Children.Where(n=>ids.Contains(n.Id)).OrderBy(n=>n.Number("Canvas.ZIndex",n.Number("Panel.ZIndex"))).ToArray();
        if(!layout.ById.TryGetValue(parent.Id,out var host)||!host.WorldTransform.TryInvert(out var inverse))throw new InvalidOperationException("The Canvas has no invertible layout.");
        var pad=Insets.Parse(parent.Get("Padding"));var border=Insets.Parse(parent.Get("BorderThickness"));
        var origin=DMatrix.Translate(-host.Bounds.X-pad.Left-border.Left,-host.Bounds.Y-pad.Top-border.Top)*inverse;
        var paths=new List<VectorPath>();
        foreach(var n in ordered)
        {
            if(!VectorGeometry.IsShape(n)||!n.Children.IsEmpty||session.Index.IsLocked(n.Id)||!layout.ById.TryGetValue(n.Id,out var entry))throw new InvalidOperationException("Select unlocked, visible vector shapes without children.");
            if(session.Document.Storyboards.Any(b=>b.Tracks.Any(t=>t.TargetId==n.Id))||session.Document.States.Any(s=>s.Setters.Any(t=>t.TargetId==n.Id)))throw new InvalidOperationException("Remove animation/state references before destructive path combination.");
            if(n.Get("Style").Length>0||n.Get("Template").Length>0||n.PropertyElements.Any(p=>XElement.Parse(p).Name.LocalName.EndsWith(".Style",StringComparison.Ordinal)))throw new InvalidOperationException("Resolve explicit styles to local shape values before combining paths.");
            if(n.Get("Clip").Length>0||n.Get("Effect").Length>0||n.Number("Opacity",1)!=1||n.PropertyElements.Any(p=>XElement.Parse(p).Name.LocalName.EndsWith(".Clip",StringComparison.Ordinal)||XElement.Parse(p).Name.LocalName.EndsWith(".Effect",StringComparison.Ordinal)))throw new InvalidOperationException("Remove per-shape clips, effects and opacity before combining.");
            var local=VectorGeometry.Local(n,new(entry.Bounds.Width,entry.Bounds.Height));
            paths.Add(VectorMath.Transform(local,origin*entry.WorldTransform*DMatrix.Translate(entry.Bounds.X,entry.Bounds.Y)));
        }
        return(parent,ordered,paths.ToArray());
    }
    public static void Combine(DesignSession session,LayoutSnapshot layout,string operation)
    {
        var (parent,nodes,paths)=Operands(session,layout,2);VectorPath[] result;
        switch(operation)
        {
            case "Compound":result=[new(paths.SelectMany(p=>p.Figures).ToImmutableArray(),false)];break;
            case "Divide":
                if(paths.Length!=2)throw new InvalidOperationException("Divide currently takes exactly two shapes.");
                result=[SkiaVectorGeometry.Combine(paths,SKPathOp.Intersect),SkiaVectorGeometry.Combine(paths,SKPathOp.Difference),SkiaVectorGeometry.Combine([paths[1],paths[0]],SKPathOp.Difference)];break;
            default:
                var op=operation switch { "Unite"=>SKPathOp.Union,"Intersect"=>SKPathOp.Intersect,"Subtract"=>SKPathOp.Difference,"Exclude"=>SKPathOp.Xor,_=>throw new ArgumentException("Unknown path operation.",nameof(operation)) };
                result=[SkiaVectorGeometry.Combine(paths,op)];break;
        }
        Replace(session,parent,nodes,result,"Path "+operation);
    }
    public static void BreakApart(DesignSession session,LayoutSnapshot layout)
    {
        var (parent,nodes,paths)=Operands(session,layout,1);if(nodes.Length!=1||paths[0].Figures.Length<2)throw new InvalidOperationException("Select one compound path with multiple figures.");
        Replace(session,parent,nodes,paths[0].Figures.Select(f=>new VectorPath([f],paths[0].NonZero)).ToArray(),"Break apart path");
    }
    public static void ApplyClip(DesignSession session,LayoutSnapshot layout)
    {
        var ids=session.TopLevelSelection();if(ids.Count!=2)throw new InvalidOperationException("Select a target and a topmost clipping shape in one Canvas.");
        var parent=session.Index.ParentOf(ids.First());if(parent?.Type!="Canvas"||ids.Any(id=>session.Index.ParentOf(id)?.Id!=parent.Id||session.Index.IsLocked(id)))throw new InvalidOperationException("Clipping requires unlocked Canvas siblings.");
        var nodes=parent.Children.Where(n=>ids.Contains(n.Id)).OrderBy(n=>n.Number("Canvas.ZIndex",n.Number("Panel.ZIndex"))).ToArray();var target=nodes[0];var mask=nodes[1];
        if(!VectorGeometry.IsShape(mask)||!mask.Children.IsEmpty)throw new InvalidOperationException("The topmost object must be a vector shape.");
        if(mask.Get("Clip").Length>0||mask.Get("Effect").Length>0||mask.Get("Style").Length>0||mask.PropertyElements.Any(raw=>XElement.Parse(raw).Name.LocalName.EndsWith(".Clip",StringComparison.Ordinal)||XElement.Parse(raw).Name.LocalName.EndsWith(".Effect",StringComparison.Ordinal)||XElement.Parse(raw).Name.LocalName.EndsWith(".Style",StringComparison.Ordinal)))throw new InvalidOperationException("Resolve the clipping shape's style, clip and effects before making a clipping path.");
        if(session.Document.Storyboards.Any(b=>b.Tracks.Any(t=>t.TargetId==mask.Id))||session.Document.States.Any(s=>s.Setters.Any(t=>t.TargetId==mask.Id)))throw new InvalidOperationException("Remove animation/state references from the clipping shape first.");
        if(!layout.ById.TryGetValue(target.Id,out var destination)||!layout.ById.TryGetValue(mask.Id,out var source)||!destination.WorldTransform.TryInvert(out var inverse))throw new InvalidOperationException("The clipping transform is not invertible.");
        var geometry=VectorGeometry.Local(mask,new(source.Bounds.Width,source.Bounds.Height));
        var mapping=DMatrix.Translate(-destination.Bounds.X,-destination.Bounds.Y)*inverse*source.WorldTransform*DMatrix.Translate(source.Bounds.X,source.Bounds.Y);
        var data=VectorPathCodec.Write(VectorMath.Transform(geometry,mapping));
        session.Execute("Make clipping path",d=>d with { Root=d.Root.Remove(new HashSet<Guid>{mask.Id}).Update(target.Id,n=>n.Set("Clip",data) with { PropertyElements=n.PropertyElements.Where(raw=>!XElement.Parse(raw).Name.LocalName.EndsWith(".Clip",StringComparison.Ordinal)).ToImmutableArray() }) },[target.Id]);
    }
    public static void ReleaseClip(DesignSession session)
    {
        var ids=session.TopLevelSelection();
        session.Execute("Release clipping path",d=>d with { Root=DesignIndex.For(d.Root).Transform(ids,n=>n with { Properties=n.Properties.Remove("Clip"),PropertyElements=n.PropertyElements.Where(raw=>!XElement.Parse(raw).Name.LocalName.EndsWith(".Clip",StringComparison.Ordinal)).ToImmutableArray() },respectLocks:true) });
    }
    private static void Replace(DesignSession session,DesignNode parent,DesignNode[] nodes,VectorPath[] paths,string label)
    {
        var ids=nodes.Select(n=>n.Id).ToHashSet();var bottom=nodes[0];var appearance=bottom with { Rotation=0,PropertyElements=bottom.PropertyElements.Where(p=>!XElement.Parse(p).Name.LocalName.EndsWith(".RenderTransform",StringComparison.Ordinal)).ToImmutableArray() };
        appearance=appearance with { Properties=appearance.Properties.RemoveRange(new[]{"RenderTransform","RenderTransformOrigin","Margin","Canvas.Right","Canvas.Bottom","Canvas.ZIndex","Panel.ZIndex"}) };
        var results=paths.Where(p=>p.SegmentCount>0).Select((p,i)=>CreateNode(i==0 ? bottom.Name : session.UniqueName("PathPart")+"_"+i,p,appearance with { Id=i==0 ? bottom.Id : Guid.NewGuid() })).ToArray();
        var at=parent.Children.IndexOf(bottom);var next=parent.Children.Where(n=>!ids.Contains(n.Id)).ToImmutableArray();
        var insertion=parent.Children.Take(at).Count(n=>!ids.Contains(n.Id));next=next.InsertRange(insertion,results);
        session.Execute(label,d=>d with { Root=d.Root.Update(parent.Id,n=>n with { Children=next }) },results.Select(n=>n.Id));
    }
}
