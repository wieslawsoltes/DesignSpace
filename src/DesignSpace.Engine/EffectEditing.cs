using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Engine;

/// <summary>One immutable, atomic base-effect edit; the caller owns its history transaction.</summary>
public static class EffectEditing
{
    public static DesignDocument Apply(DesignDocument document,IEnumerable<Guid> targets,DesignEffect? effect)
    {
        ArgumentNullException.ThrowIfNull(document);ArgumentNullException.ThrowIfNull(targets);
        var xml=effect is null?null:EffectCodec.Write(effect);var index=DesignIndex.For(document.Root);var ids=targets.ToHashSet();
        if(ids.Count==0)throw new InvalidOperationException("Select an object before editing its effect.");
        foreach(var id in ids)if(index.Find(id) is null||index.IsLocked(id))throw new InvalidOperationException("Effect targets must exist and have unlocked ancestors.");
        var root=index.Transform(ids,node=>
        {
            var key=node.Type+".Effect";
            var elements=node.PropertyElements.Where(raw=>XElement.Parse(raw).Name.LocalName!=key).ToImmutableArray();
            if(xml is not null)elements=elements.Add(new XElement(XName.Get(key,node.Namespace),BrushCodec.ReadXml(xml)).ToString(SaveOptions.DisableFormatting));
            var properties=node.Properties.Remove("Effect");
            if(ReferenceEquals(properties,node.Properties)&&elements.SequenceEqual(node.PropertyElements))return node;
            return node with{Properties=properties,PropertyElements=elements};
        });
        if(ReferenceEquals(root,document.Root))return document;
        var result=document with{Root=root};DocumentValidator.Validate(result);return result;
    }
}
