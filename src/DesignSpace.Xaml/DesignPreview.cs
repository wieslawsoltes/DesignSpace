using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

public sealed record PreviewResult(DesignNode Root,IReadOnlyList<string> Diagnostics);
/// <summary>Non-executing style/template expansion. The saved document remains unchanged.</summary>
public static class DesignPreview
{
    public const string OwnerKey="{https://designspace.dev/designer}TemplateOwner";
    public const string ExpandedKey="{https://designspace.dev/designer}TemplateExpanded";
    private static readonly ConditionalWeakTable<DesignNode,PreviewResult> Cache=new();
    private static readonly XNamespace X=DesignNode.XamlNamespace;
    public static DesignNode Resolve(DesignNode root)=>Get(root).Root;
    public static PreviewResult Get(DesignNode root)=>Cache.GetValue(root,Build);
    private sealed class Scope(Scope? parent)
    {
        public Dictionary<string,XElement> Resources { get; }=new(StringComparer.Ordinal);
        public XElement? Find(string key)=>Resources.GetValueOrDefault(key) ?? parent?.Find(key);
    }
    private static string? ResourceKey(string value)
    {
        if(!(value.StartsWith("{StaticResource ",StringComparison.Ordinal) || value.StartsWith("{ThemeResource ",StringComparison.Ordinal))) return null;
        return value[(value.IndexOf(' ')+1)..].TrimEnd('}').Trim();
    }
    private static string Target(string text)=>text.Replace("{x:Type ","",StringComparison.Ordinal).TrimEnd('}').Split(':').Last().Trim();
    private static PreviewResult Build(DesignNode root)
    {
        var warnings=new List<string>(); var expanded=0;
        Scope Resources(DesignNode n,Scope? parent)
        {
            var scope=new Scope(parent);
            foreach(var raw in n.PropertyElements)
            {
                var property=XElement.Parse(raw); if(!property.Name.LocalName.EndsWith(".Resources",StringComparison.Ordinal)) continue;
                var container=property.Elements().FirstOrDefault(e=>e.Name.LocalName=="ResourceDictionary") ?? property;
                foreach(var e in container.Elements())
                {
                    var key=(string?)e.Attribute(X+"Key");
                    if(key is null && e.Name.LocalName=="Style") key="@"+Target((string?)e.Attribute("TargetType") ?? "");
                    if(key is not null) scope.Resources[key]=e;
                }
            }
            return scope;
        }
        DesignNode Style(DesignNode n,Scope scope)
        {
            var local=n.Properties.Keys.ToHashSet(); var localElements=n.PropertyElements.Select(p=>XElement.Parse(p).Name.LocalName.Split('.').Last()).ToHashSet();
            var setters=new Dictionary<string,XElement>(StringComparer.Ordinal); var seen=new HashSet<XElement>();
            void Collect(XElement? style,int depth)
            {
                if(style is null || style.Name.LocalName!="Style") return;
                if(depth>32 || !seen.Add(style)) throw new InvalidDataException("Cyclic or over-deep BasedOn style chain.");
                var based=ResourceKey((string?)style.Attribute("BasedOn") ?? ""); if(based is not null) Collect(scope.Find(based),depth+1);
                foreach(var setter in style.Elements().SelectMany(e=>e.Name.LocalName=="Style.Setters" ? e.Elements() : new[]{e}).Where(e=>e.Name.LocalName=="Setter"))
                {
                    var property=(string?)setter.Attribute("Property"); if(property is not null) setters[property]=setter;
                }
            }
            var styleKey=ResourceKey(n.Get("Style"));
            var inline=n.PropertyElements.FirstOrDefault(p=>XElement.Parse(p).Name.LocalName.EndsWith(".Style",StringComparison.Ordinal));
            Collect(inline is not null ? XElement.Parse(inline).Elements().FirstOrDefault() : scope.Find(styleKey ?? "@"+n.Type),0);
            foreach(var (property,setter) in setters)
            {
                if(local.Contains(property) || localElements.Contains(property)) continue;
                if(setter.Attribute("Value") is { } value) n=n.Set(property,value.Value);
                else if(setter.Elements().FirstOrDefault()?.Elements().FirstOrDefault() is { } element)
                    n=n with { PropertyElements=n.PropertyElements.Add(new XElement(XName.Get(n.Type+"."+property,n.Namespace),new XElement(element)).ToString(SaveOptions.DisableFormatting)) };
            }
            foreach(var p in n.Properties)
            {
                var key=ResourceKey(p.Value); var resource=key is null ? null : scope.Find(key);
                if(resource?.Name.LocalName=="SolidColorBrush" && !resource.HasElements && resource.Attributes().All(a=>a.IsNamespaceDeclaration||a.Name.LocalName is "Key" or "Name" or "Color")) n=n.Set(p.Key,(string?)resource.Attribute("Color") ?? "Transparent");
                else if(resource?.Name.LocalName is "String" or "Double" or "Color" or "Thickness") n=n.Set(p.Key,resource.Value);
                // Keep complete brush references for lexical resource resolution, including opacity and transforms.
            }
            return n;
        }
        DesignNode Walk(DesignNode source,Scope? parent,int depth,bool allowTemplate=true)
        {
            if(depth>64 || ++expanded>40000) throw new InvalidDataException("Preview expansion exceeds its node/depth budget.");
            var scope=Resources(source,parent); var n=Style(source,scope);
            var children=n.Children.Select(c=>Walk(c,scope,depth+1)).ToImmutableArray();
            if(!children.SequenceEqual(n.Children)) n=n with { Children=children };
            if(!allowTemplate) return n;
            var inline=n.PropertyElements.FirstOrDefault(p=>XElement.Parse(p).Name.LocalName.EndsWith(".Template",StringComparison.Ordinal));
            var templateKey=ResourceKey(n.Get("Template")); var template=inline is not null ? XElement.Parse(inline).Elements().FirstOrDefault() : templateKey is null ? null : scope.Find(templateKey);
            if(template?.Name.LocalName!="ControlTemplate") return n;
            var visuals=template.Elements().Where(e=>!e.Name.LocalName.Contains('.')).ToArray();
            if(visuals.Length!=1) throw new InvalidDataException("A ControlTemplate requires one visual root.");
            var parsed=XamlCodec.Parse(visuals[0].ToString()).Document.Root; var ordinal=0; var contentUsed=false; var owner=n;
            DesignNode Instantiate(DesignNode part)
            {
                var hash=SHA256.HashData(Encoding.UTF8.GetBytes(owner.Id.ToString("N")+":"+ordinal++));
                var id=new Guid(hash.AsSpan(0,16)); var props=part.Properties;
                foreach(var p in props) if(p.Value.StartsWith("{TemplateBinding ",StringComparison.Ordinal)) props=props.SetItem(p.Key,owner.Get(p.Value[17..].TrimEnd('}').Trim()));
                props=props.SetItem(OwnerKey,owner.Id.ToString());
                if(props.TryGetValue(DesignNode.NameKey,out var name)) props=props.SetItem(DesignNode.NameKey,owner.Name+"_"+name);
                var next=part with { Id=id,Properties=props,Children=part.Children.Select(Instantiate).ToImmutableArray() };
                if(part.Type=="ContentPresenter")
                {
                    if(!contentUsed && !owner.Children.IsEmpty) { next=next with { Type="Grid",Children=owner.Children }; contentUsed=true; }
                    else next=(next with { Type="TextBlock" }).Set("Text",owner.Get("Content",owner.Get("Text"))).Set("Foreground",owner.Get("Foreground","#FF202838")).Set("FontSize",owner.Number("FontSize",14));
                }
                return next;
            }
            var visual=Walk(Instantiate(parsed),scope,depth+1,false);
            return n.Set(ExpandedKey,"True") with { Children=[visual] };
        }
        DesignNode resolved;
        try { resolved=Walk(root,null,0); }
        catch(Exception e) when(e is InvalidDataException or System.Xml.XmlException or ArgumentException) { warnings.Add(e.Message); resolved=root; }
        return new(DesignData.Resolve(resolved),warnings);
    }
}
