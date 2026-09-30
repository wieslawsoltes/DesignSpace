using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

public static partial class TemplateLibrary
{
    private static readonly XNamespace X=DesignNode.XamlNamespace;
    private static readonly XNamespace Ns=DesignNode.PresentationNamespace;
    public static IReadOnlyDictionary<string,string> Read(DesignDocument document)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var raw in document.Root.PropertyElements)
        {
            var property=XElement.Parse(raw); if(!property.Name.LocalName.EndsWith(".Resources",StringComparison.Ordinal)) continue;
            var container=property.Elements().FirstOrDefault(e=>e.Name==Ns+"ResourceDictionary")??property;
            foreach(var template in container.Elements().Where(e=>e.Name==Ns+"ControlTemplate" && e.Attribute(X+"Key") is not null)) result[(string)template.Attribute(X+"Key")!]=template.ToString();
        }
        return result;
    }
    public static string CreateDefault(string key="ButtonTemplate")
    {
        return new XElement(Ns+"ControlTemplate",new XAttribute(XNamespace.Xmlns+"x",X.NamespaceName),new XAttribute(X+"Key",key),new XAttribute("TargetType","Button"),
            new XElement(Ns+"Border",new XAttribute("Background","{TemplateBinding Background}"),new XAttribute("CornerRadius","6"),new XAttribute("Padding","12,6"),
                new XElement(Ns+"ContentPresenter",new XAttribute("HorizontalAlignment","Center"),new XAttribute("VerticalAlignment","Center")))).ToString();
    }
    public static DesignDocument Save(DesignDocument document,string source)
    {
        if(document.Root.IsLocked)throw new InvalidOperationException("Unlock the root before changing template resources.");
        using var reader=XmlReader.Create(new StringReader(source),new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,IgnoreWhitespace=true,MaxCharactersInDocument=1024*1024 });
        var template=XElement.Load(reader); var key=(string?)template.Attribute(X+"Key");
        if(template.Name!=Ns+"ControlTemplate" || string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("Provide a ControlTemplate with x:Key.");
        if(key.Length>128)throw new InvalidDataException("Template keys are limited to 128 characters.");
        XmlConvert.VerifyNCName(key);
        var visuals=template.Elements().Where(e=>!e.Name.LocalName.Contains('.')).ToArray();
        if(visuals.Length!=1) throw new InvalidDataException("A ControlTemplate needs one visual root.");
        _=XamlCodec.Parse(visuals[0].ToString());
        var root=document.Root; var other=new List<string>(); XElement? resources=null;
        foreach(var raw in root.PropertyElements)
        {
            var property=XElement.Parse(raw); if(resources is null && property.Name.LocalName.EndsWith(".Resources",StringComparison.Ordinal)) resources=property; else other.Add(raw);
        }
        resources ??= new XElement(Ns+(root.Type+".Resources"));
        var container=resources.Elements().FirstOrDefault(e=>e.Name.LocalName=="ResourceDictionary") ?? resources;
        var existing=container.Elements().FirstOrDefault(e=>(string?)e.Attribute(X+"Key")==key);
        if(existing is not null && existing.Name.LocalName!="ControlTemplate") throw new InvalidDataException("That resource key belongs to another resource type.");
        // IgnoreWhitespace on the secure reader matches the resource parser while retaining
        // xml:space-preserved text. Standalone editor text also makes inherited xmlns explicit.
        // Compare the same standalone representation, not attached-vs-detached attribute lists.
        if(existing is not null&&XNode.DeepEquals(XElement.Parse(existing.ToString(SaveOptions.DisableFormatting)),template))return document;
        if(existing is null) container.Add(template); else existing.ReplaceWith(template);
        other.Insert(0,resources.ToString(SaveOptions.DisableFormatting));
        var result=document with { Root=root with { PropertyElements=other.ToImmutableArray() } }; DocumentValidator.Validate(result); return result;
    }
    public static DesignDocument Apply(DesignDocument document,IEnumerable<Guid> ids,string key)
    {
        ArgumentNullException.ThrowIfNull(document);ArgumentNullException.ThrowIfNull(ids);
        if(!Read(document).TryGetValue(key,out var source))throw new InvalidDataException("Save the template before applying it.");
        var template=XElement.Parse(source);var target=((string?)template.Attribute("TargetType")??"").Replace("{x:Type ","").TrimEnd('}').Split(':').Last().Trim();
        var index=DesignIndex.For(document.Root);var targets=ids.ToHashSet();
        if(targets.Count==0)throw new InvalidOperationException("Select an object before applying a template.");
        foreach(var id in targets)
        {
            var node=index.Find(id)??throw new InvalidOperationException("Template target is missing.");
            if(index.IsLocked(id))throw new InvalidOperationException("Unlock every template target and ancestor first.");
            if(target.Length>0&&target!=node.Type)throw new InvalidOperationException("Template TargetType "+target+" does not match "+node.Type+".");
        }
        var root=index.Transform(targets,node=>
        {
            var elements=node.PropertyElements.Where(raw=>!XElement.Parse(raw).Name.LocalName.EndsWith(".Template",StringComparison.Ordinal)).ToImmutableArray();
            var next=node.Set("Template","{StaticResource "+key+"}");
            return elements.SequenceEqual(node.PropertyElements)?next:next with{PropertyElements=elements};
        });
        if(ReferenceEquals(root,document.Root))return document;
        var result=document with{Root=root};DocumentValidator.Validate(result);return result;
    }
}
