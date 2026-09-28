using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

public static class TemplateLibrary
{
    private static readonly XNamespace X=DesignNode.XamlNamespace;
    private static readonly XNamespace Ns=DesignNode.PresentationNamespace;
    public static IReadOnlyDictionary<string,string> Read(DesignDocument document)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var raw in document.Root.PropertyElements)
        {
            var property=XElement.Parse(raw); if(!property.Name.LocalName.EndsWith(".Resources",StringComparison.Ordinal)) continue;
            foreach(var template in property.Descendants().Where(e=>e.Name.LocalName=="ControlTemplate" && e.Attribute(X+"Key") is not null)) result[(string)template.Attribute(X+"Key")!]=template.ToString();
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
        using var reader=XmlReader.Create(new StringReader(source),new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024 });
        var template=XElement.Load(reader); var key=(string?)template.Attribute(X+"Key");
        if(template.Name.LocalName!="ControlTemplate" || string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("Provide a ControlTemplate with x:Key.");
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
        if(existing is null) container.Add(template); else existing.ReplaceWith(template);
        other.Insert(0,resources.ToString(SaveOptions.DisableFormatting));
        var result=document with { Root=root with { PropertyElements=other.ToImmutableArray() } }; DocumentValidator.Validate(result); return result;
    }
    public static DesignDocument Apply(DesignDocument document,IEnumerable<Guid> ids,string key)
    {
        if(!Read(document).ContainsKey(key)) throw new InvalidDataException("Save the template before applying it.");
        var root=DesignIndex.For(document.Root).Transform(ids.ToHashSet(),n=>n.Set("Template","{StaticResource "+key+"}"),respectLocks:true);
        return document with { Root=root };
    }
}
