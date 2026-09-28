using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
using DesignSpace.Engine;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

/// <summary>Converts a solid-color stroke to editable filled path data without changing the object's identity.</summary>
public static class StrokeCommands
{
    public static void Outline(DesignSession session,LayoutSnapshot layout)
    {
        var ids=session.TopLevelSelection();if(ids.Count==0)throw new InvalidOperationException("Select stroked vector shapes.");
        var replacements=new Dictionary<Guid,DesignNode>();
        foreach(var id in ids)
        {
            var n=session.Index.Find(id)!;
            if(!VectorGeometry.IsShape(n)||!n.Children.IsEmpty||session.Index.IsLocked(id)||!layout.ById.TryGetValue(id,out var entry))throw new InvalidOperationException("Select unlocked visible vector shapes without children.");
            if(session.Document.Storyboards.Any(b=>b.Tracks.Any(t=>t.TargetId==id))||session.Document.States.Any(s=>s.Setters.Any(t=>t.TargetId==id)))throw new InvalidOperationException("Remove state/animation references before outlining a stroke.");
            var stroke=n.Get("Stroke");
            if(stroke.Length==0||stroke.StartsWith('{')||!SKColor.TryParse(stroke,out _)&&DesignRenderer.Color(stroke,SKColors.Transparent)==SKColors.Transparent)throw new InvalidOperationException("Outlining requires a literal solid-color stroke.");
            if(n.Get("Fill").Length>0&&DesignRenderer.Color(n.Get("Fill"),SKColors.Black).Alpha!=0)throw new InvalidOperationException("Remove the existing fill before converting the stroke.");
            if(n.Get("Style").Length>0||n.Get("Template").Length>0||n.PropertyElements.Any(raw=>new[]{".Stroke",".Fill",".Style",".Template"}.Any(suffix=>XElement.Parse(raw).Name.LocalName.EndsWith(suffix,StringComparison.Ordinal))))throw new InvalidOperationException("Resolve style, template and brush resources to local solid values before outlining.");
            // A resolved implicit style may supply values not present on the original node.
            if(StrokeStyle.Read(entry.Node)!=StrokeStyle.Read(n)||entry.Node.Get("Stroke")!=stroke||entry.Node.Get("Fill")!=n.Get("Fill"))throw new InvalidOperationException("Resolve inherited style values before outlining.");
            var size=new DSize(entry.Bounds.Width,entry.Bounds.Height);
            using var geometry=SkiaVectorGeometry.Create(VectorGeometry.Local(n,size));
            using var outline=SkiaStrokeGeometry.Create(geometry,StrokeStyle.Read(n));
            if(outline.IsEmpty)throw new InvalidOperationException("A zero-width or empty stroke has no outline.");
            var path=VectorPathCodec.Parse((outline.FillType==SKPathFillType.EvenOdd ? "F0 " : "F1 ")+outline.ToSvgPathData());
            var node=VectorGeometry.WithPath(n,path).Set("Fill",stroke).Set("StrokeThickness",0);
            replacements[id]=node with{Properties=node.Properties.Remove("Stroke").RemoveRange(StrokeStyle.Properties.Where(p=>p!="StrokeThickness"))};
        }
        session.Execute("Convert stroke to path",d=>d with{Root=DesignIndex.For(d.Root).Transform(ids,n=>replacements[n.Id])});
    }
}
