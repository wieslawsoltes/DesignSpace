using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Engine;

/// <summary>Atomic base-value brush edits and pure gradient-stop operations, independent of Uno and Skia.</summary>
public static class BrushEditing
{
    public static readonly string[] Properties=["Fill","Stroke","Background","Foreground","BorderBrush","OpacityMask"];
    public static DesignDocument Apply(DesignDocument document,IEnumerable<Guid> targets,string property,DesignBrush? brush)
    {
        if(!Properties.Contains(property))throw new InvalidOperationException("Unsupported brush property.");
        brush?.Validate();var xml=brush is null?null:BrushCodec.Write(brush);var index=DesignIndex.For(document.Root);var ids=targets.ToHashSet();
        if(ids.Count==0)throw new InvalidOperationException("Select objects before applying a brush.");
        foreach(var id in ids)if(index.Find(id) is not { } n||index.IsLocked(id))throw new InvalidOperationException("Brush targets must exist and have unlocked ancestors.");
        var root=index.Transform(ids,n=>
        {
            var key=n.Type+"."+property;
            var kept=n.PropertyElements.Where(raw=>XElement.Parse(raw).Name.LocalName!=key).ToImmutableArray();
            if(xml is not null)kept=kept.Add(new XElement(XName.Get(key,n.Namespace),BrushCodec.ReadXml(xml)).ToString(SaveOptions.DisableFormatting));
            var props=n.Properties.Remove(property);
            if(ReferenceEquals(props,n.Properties)&&kept.SequenceEqual(n.PropertyElements))return n;
            return n with{Properties=props,PropertyElements=kept};
        });
        if(ReferenceEquals(root,document.Root))return document;var result=document with{Root=root};DocumentValidator.Validate(result);return result;
    }
    public static DesignBrush AddStop(DesignBrush brush,double offset)
    {
        brush.Validate();if(!double.IsFinite(offset)||Math.Abs(offset)>1e9)throw new ArgumentOutOfRangeException(nameof(offset));if(brush.Stops.Length>=BrushCodec.MaxStops)throw new InvalidOperationException("Gradient stop budget reached.");
        var color=Sample(brush,offset).ToHex();return brush with{Stops=brush.Stops.Add(new(offset,color))};
    }
    public static DesignBrush RemoveStop(DesignBrush brush,int index)
    {
        brush.Validate();if(index<0||index>=brush.Stops.Length)throw new ArgumentOutOfRangeException(nameof(index));return brush with{Stops=brush.Stops.RemoveAt(index)};
    }
    public static DesignBrush Reverse(DesignBrush brush)
    {
        brush.Validate();var result=brush with{Stops=brush.Stops.Reverse().Select(s=>s with{Offset=1-s.Offset}).ToImmutableArray()};result.Validate();return result;
    }
    public static BrushColor Sample(DesignBrush brush,double offset)
    {
        brush.Validate();if(!double.IsFinite(offset))throw new ArgumentOutOfRangeException(nameof(offset));
        if(brush.Kind==DesignBrushKind.Solid)return BrushColor.Parse(brush.Color);
        var stops=brush.Stops.OrderBy(s=>s.Offset).ToArray();if(stops.Length==0)return new(0,0,0,0);
        if(offset<stops[0].Offset)return BrushColor.Parse(stops[0].Color);
        var next=Array.FindIndex(stops,s=>s.Offset>offset);if(next<0)return BrushColor.Parse(stops[^1].Color);
        var a=stops[next-1];var b=stops[next];return BrushColor.Lerp(BrushColor.Parse(a.Color),BrushColor.Parse(b.Color),(offset-a.Offset)/(b.Offset-a.Offset));
    }
}
