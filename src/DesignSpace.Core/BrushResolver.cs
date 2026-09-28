using System.Runtime.CompilerServices;
using System.Xml.Linq;
namespace DesignSpace.Core;

/// <summary>Lexically scoped inert resource lookup. No external dictionaries, assemblies or markup extensions execute.</summary>
public sealed class BrushResolver
{
    private sealed record NodeInfo(Dictionary<string,string?> Assignments,Dictionary<string,XElement> Resources);
    private sealed class Scope(Scope? parent,Dictionary<string,XElement> resources)
    {
        public (XElement Value,Scope Scope)? Find(string key)=>resources.TryGetValue(key,out var value)?(value,this):parent?.Find(key);
    }
    private static readonly ConditionalWeakTable<DesignNode,NodeInfo> Infos=new();
    private readonly Dictionary<Guid,(DesignNode Node,Scope Scope)> _nodes=[];
    private readonly Dictionary<(Guid,string),string?> _resolved=[];
    public BrushResolver(DesignNode root)
    {
        var count=0;
        void Walk(DesignNode node,Scope? parent,int depth)
        {
            if(++count>DocumentValidator.MaxNodes||depth>DocumentValidator.MaxDepth)throw new InvalidDataException("Brush scope budget exceeded.");
            var resources=Infos.GetValue(node,ReadNode).Resources;var scope=resources.Count==0&&parent is not null?parent:new Scope(parent,resources);
            _nodes.Add(node.Id,(node,scope));foreach(var child in node.Children)Walk(child,scope,depth+1);
        }
        Walk(root,null,0);
    }
    public static string? LocalSource(DesignNode node,string property)
    {
        var info=Infos.GetValue(node,ReadNode);return info.Assignments.TryGetValue(property,out var value)?value:node.Get(property) is {Length:>0} text?text:null;
    }
    private static NodeInfo ReadNode(DesignNode node)
    {
        var assignments=new Dictionary<string,string?>(StringComparer.Ordinal);var resources=new Dictionary<string,XElement>(StringComparer.Ordinal);
        foreach(var raw in node.PropertyElements)
        {
            var e=XElement.Parse(raw);var property=e.Name.LocalName.Split('.').Last();
            if(property=="Resources")
            {
                var container=e.Elements().FirstOrDefault(c=>c.Name.LocalName=="ResourceDictionary")??e;
                foreach(var item in container.Elements())if(item.Attribute(XName.Get("Key",DesignNode.XamlNamespace)) is { } key)resources[key.Value]=item;
            }
            else if(new[]{"Fill","Stroke","Background","Foreground","BorderBrush","OpacityMask"}.Contains(property))
            {
                var children=e.Elements().ToArray();if(children.Length>1)throw new InvalidDataException("A brush property accepts one value.");
                assignments[property]=children.Length==0||children[0].Name==XName.Get("Null",DesignNode.XamlNamespace)?null:children[0].ToString(SaveOptions.DisableFormatting);
            }
        }
        return new(assignments,resources);
    }
    public string? Resolve(Guid nodeId,string property)
    {
        var key=(nodeId,property);if(_resolved.TryGetValue(key,out var cached))return cached;
        if(!_nodes.TryGetValue(nodeId,out var entry))throw new InvalidOperationException("Brush target is outside this resource scope.");
        var source=LocalSource(entry.Node,property);if(string.IsNullOrWhiteSpace(source)||source.Trim()=="{x:Null}")return _resolved[key]=null;
        var scope=entry.Scope;XElement? brush=null;
        if(ReferenceKey(source) is { } resource)
        {
            var found=scope.Find(resource)??throw new InvalidDataException("Brush resource not found: "+resource);
            brush=new XElement(found.Value);scope=found.Scope;
        }
        else if(source.TrimStart().StartsWith('<'))brush=BrushCodec.ReadXml(source);
        else if(source.StartsWith('{'))throw new InvalidDataException("Unresolved brush expression: "+source);
        if(brush is not null)
        {
            foreach(var element in brush.DescendantsAndSelf())foreach(var attribute in element.Attributes().Where(a=>!a.IsNamespaceDeclaration).ToArray())
            {
                if(ReferenceKey(attribute.Value) is not { } name)continue;
                var value=scope.Find(name)??throw new InvalidDataException("Brush value resource not found: "+name);
                if(value.Value.Name.LocalName is not ("Color" or "Double" or "Point" or "String")||value.Value.HasElements)throw new InvalidDataException("Only literal value resources are supported inside brushes.");
                attribute.Value=value.Value.Value;
            }
            source=brush.ToString(SaveOptions.DisableFormatting);
        }
        _resolved[key]=source;return source;
    }
    private static string? ReferenceKey(string text)
    {
        text=text.Trim();if(!text.EndsWith('}'))return null;
        var prefix=text.StartsWith("{StaticResource ",StringComparison.Ordinal)?16:text.StartsWith("{ThemeResource ",StringComparison.Ordinal)?15:0;
        if(prefix==0)return null;var key=text[prefix..^1].Trim();if(key.StartsWith("ResourceKey=",StringComparison.Ordinal))key=key[12..].Trim();
        if(key.Length==0||key.IndexOfAny(['{','}',','])>=0)throw new InvalidDataException("Unsupported resource key syntax.");return key;
    }
}
