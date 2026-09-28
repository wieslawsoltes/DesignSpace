using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Engine;

/// <summary>Atomic multi-shape stroke editing independent of the rendering backend and UI.</summary>
public static class StrokeEditing
{
    public static DesignDocument Apply(DesignDocument document,IEnumerable<Guid> targets,IReadOnlyDictionary<string,string> changes)
    {
        var index=DesignIndex.For(document.Root);var ids=targets.ToHashSet();
        if(ids.Count==0)throw new InvalidOperationException("Select vector shapes to edit their strokes.");
        foreach(var pair in changes)
            if(pair.Value is null||pair.Key!="Stroke"&&!StrokeStyle.Properties.Contains(pair.Key))throw new ArgumentException("Only stroke properties are accepted.",nameof(changes));
        foreach(var id in ids)
            if(index.Find(id) is not { } n||!VectorGeometry.IsShape(n)||index.IsLocked(id))throw new InvalidOperationException("Select unlocked vector shapes, including unlocked ancestors.");
        var root=index.Transform(ids,n=>
        {
            var next=n;
            foreach(var pair in changes)
                next=pair.Key=="Stroke" && string.IsNullOrWhiteSpace(pair.Value)
                    ? next.Properties.ContainsKey("Stroke") ? next with { Properties=next.Properties.Remove("Stroke") } : next
                    : next.Set(pair.Key,pair.Value);
            var raw=next.PropertyElements.Where(p=>!changes.Keys.Any(k=>XElement.Parse(p).Name.LocalName.EndsWith("."+k,StringComparison.Ordinal))).ToImmutableArray();
            if(raw.Length!=next.PropertyElements.Length)next=next with{PropertyElements=raw};
            StrokeStyle.Read(next).Validate();return next;
        });
        if(ReferenceEquals(root,document.Root))return document;
        var result=document with{Root=root};DocumentValidator.Validate(result);return result;
    }
}
