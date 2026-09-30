using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Engine;

/// <summary>Immutable atomic base-effect edits. Null applies an explicit no-effect override; Reset restores style lookup.</summary>
public static class EffectEditing
{
    public static DesignDocument Apply(DesignDocument document,IEnumerable<Guid> targets,DesignEffect? effect)
        =>Change(document,targets,effect is null?null:EffectCodec.Write(effect),disable:effect is null);
    public static DesignDocument Reset(DesignDocument document,IEnumerable<Guid> targets)=>Change(document,targets,null,false);
    private static DesignDocument Change(DesignDocument document,IEnumerable<Guid> targets,string? xml,bool disable)
    {
        ArgumentNullException.ThrowIfNull(document);ArgumentNullException.ThrowIfNull(targets);
        var index=DesignIndex.For(document.Root);var ids=targets.ToHashSet();
        if(ids.Count==0)throw new InvalidOperationException("Select an object before editing its effect.");
        foreach(var id in ids)if(index.Find(id) is null||index.IsLocked(id))throw new InvalidOperationException("Effect targets must exist and have unlocked ancestors.");
        var root=index.Transform(ids,node=>
        {
            var elements=node.PropertyElements.Where(raw=>!XElement.Parse(raw).Name.LocalName.EndsWith(".Effect",StringComparison.Ordinal)).ToImmutableArray();
            if(xml is not null)elements=elements.Add(new XElement(XName.Get(node.Type+".Effect",node.Namespace),BrushCodec.ReadXml(xml)).ToString(SaveOptions.DisableFormatting));
            var properties=disable?node.Properties.SetItem("Effect","{x:Null}"):node.Properties.Remove("Effect");
            if(ReferenceEquals(properties,node.Properties)&&elements.SequenceEqual(node.PropertyElements))return node;
            return node with{Properties=properties,PropertyElements=elements};
        });
        if(ReferenceEquals(root,document.Root))return document;
        var result=document with{Root=root};DocumentValidator.Validate(result);return result;
    }
}
