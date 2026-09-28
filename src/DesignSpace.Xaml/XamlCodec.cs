using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

public sealed record DesignDiagnostic(string Severity,string Message,int Line=0,int Column=0);
public sealed record XamlReadResult(DesignDocument Document,IReadOnlyList<DesignDiagnostic> Diagnostics);
/// <summary>Parses XAML into inert data. It never executes code, loads assemblies or resolves external entities.</summary>
public static class XamlCodec
{
    public const int MaxCharacters=8*1024*1024;
    private static readonly XNamespace Ns=DesignNode.PresentationNamespace;
    private static readonly XNamespace X=DesignNode.XamlNamespace;
    private static readonly HashSet<string> KnownTypes=["Page","UserControl","Window","Grid","Canvas","StackPanel","Border","Rectangle","Ellipse","Path","Line","TextBlock","Button","TextBox","CheckBox","RadioButton","ToggleSwitch","Slider","ProgressBar","Image","Viewbox","ContentControl"];
    public static XamlReadResult Parse(string text,string title="MainPage.xaml")
    {
        if(text.Length>MaxCharacters) throw new InvalidDataException("XAML exceeds the 8 MiB limit.");
        var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaxCharacters,MaxCharactersFromEntities=1024 };
        using var reader=XmlReader.Create(new StringReader(text),settings);
        var xml=XDocument.Load(reader,LoadOptions.SetLineInfo); if(xml.Root is null) throw new InvalidDataException("Missing XAML root.");
        var diagnostics=new List<DesignDiagnostic>(); var count=0;
        DesignNode Read(XElement element,int depth)
        {
            if(++count>DocumentValidator.MaxNodes || depth>DocumentValidator.MaxDepth) throw new InvalidDataException("Document exceeds the node or nesting limit.");
            if(!KnownTypes.Contains(element.Name.LocalName)) { var li=(IXmlLineInfo)element; diagnostics.Add(new("Warning","Preserved unsupported element: "+element.Name.LocalName,li.LineNumber,li.LinePosition)); }
            var n=new DesignNode { Type=element.Name.LocalName,Namespace=element.Name.NamespaceName,Properties=element.Attributes().Where(a=>!(a.IsNamespaceDeclaration && a.Name.LocalName=="xmlns")).ToImmutableDictionary(a=>a.Name.ToString(),a=>a.Value),TextContent=string.Concat(element.Nodes().OfType<XText>().Select(t=>t.Value)).Trim() };
            var properties=ImmutableArray.CreateBuilder<string>(); var children=ImmutableArray.CreateBuilder<DesignNode>();
            foreach(var child in element.Elements())
            {
                if(child.Name.LocalName.Contains('.'))
                {
                    if(child.Name.LocalName.EndsWith(".RenderTransform",StringComparison.Ordinal) && child.Elements().Count()==1 && n.Get("RenderTransformOrigin").Replace(" ","",StringComparison.Ordinal)=="0.5,0.5")
                    {
                        var transform=child.Elements().Single();
                        if(transform.Name.LocalName=="RotateTransform" && transform.Attributes().All(a=>a.IsNamespaceDeclaration || a.Name.LocalName=="Angle")) { n=n with { Rotation=Numbers.Parse((string?)transform.Attribute("Angle")) }; continue; }
                    }
                    properties.Add(child.ToString(SaveOptions.DisableFormatting));
                }
                else children.Add(Read(child,depth+1));
            }
            foreach(var p in n.Properties.Where(p=>p.Value.StartsWith("{Binding",StringComparison.Ordinal) || p.Value.StartsWith("{x:Bind",StringComparison.Ordinal))) diagnostics.Add(new("Warning","Binding preserved; only simple JSON sample-data paths are previewed: "+n.Name+"."+p.Key));
            return n with { Children=children.ToImmutable(),PropertyElements=properties.ToImmutable() };
        }
        var root=Read(xml.Root,0); var byName=root.DescendantsAndSelf().GroupBy(n=>n.Name).ToDictionary(g=>g.Key,g=>g.First());
        var boards=ImmutableArray.CreateBuilder<DesignStoryboard>(); var states=ImmutableArray.CreateBuilder<DesignState>(); var stateGroups=ImmutableArray.CreateBuilder<DesignStateGroup>(); var remaining=ImmutableArray.CreateBuilder<string>();
        foreach(var raw in root.PropertyElements)
        {
            var property=XElement.Parse(raw);
            if(property.Name.LocalName.EndsWith(".Resources",StringComparison.Ordinal))
            {
                foreach(var element in ResourceContainer(property).Elements().Where(e=>e.Name.LocalName=="Storyboard").ToArray())
                {
                    if(XamlAnimationCodec.TryReadStoryboard(element,byName,out var board)) { boards.Add(board!); element.Remove(); }
                    else diagnostics.Add(new("Warning","Unsupported storyboard preserved as XAML, outside the editable timeline."));
                }
            }
            if(property.Name.LocalName=="VisualStateManager.VisualStateGroups") foreach(var group in property.Elements().Where(e=>e.Name==Ns+"VisualStateGroup").ToArray())
            {
                if(XamlStateCodec.TryReadGroup(group,byName,out var definition,out var parsed) &&
                   !stateGroups.Any(g=>g.Name==definition!.Name) && !parsed.Any(s=>states.Any(old=>old.Name==s.Name)))
                { states.AddRange(parsed);stateGroups.Add(definition!);group.Remove(); }
                else diagnostics.Add(new("Warning","Unsupported state group preserved as XAML."));
            }
            if(property.HasElements || property.Attributes().Any(a=>!a.IsNamespaceDeclaration)) remaining.Add(property.ToString(SaveOptions.DisableFormatting));
        }
        var document=new DesignDocument { Title=title,Root=root with { PropertyElements=remaining.ToImmutable() },Storyboards=boards.ToImmutable(),States=states.ToImmutable(),StateGroups=stateGroups.ToImmutable() };
        DocumentValidator.Validate(document); return new(document,diagnostics);
    }
    private static XElement ResourceContainer(XElement property)=>property.Elements().SingleOrDefault(e=>e.Name.LocalName=="ResourceDictionary") ?? property;
    public static string Write(DesignDocument document)
    {
        DocumentValidator.Validate(document);
        var rotationTargets=document.Storyboards.SelectMany(b=>b.Tracks).Where(t=>t.Property=="Rotation").Select(t=>t.TargetId).Concat(document.States.SelectMany(s=>s.Setters).Where(s=>s.Property=="Rotation").Select(s=>s.TargetId)).ToHashSet();
        XElement WriteNode(DesignNode n)
        {
            var e=new XElement(XName.Get(n.Type,n.Namespace));
            foreach(var p in n.Properties.OrderBy(p=>p.Key,StringComparer.Ordinal)) e.SetAttributeValue(XName.Get(p.Key),p.Value);
            foreach(var raw in n.PropertyElements) e.Add(XElement.Parse(raw));
            if(n.Rotation!=0 || rotationTargets.Contains(n.Id))
            {
                if(n.PropertyElements.Any(p=>XElement.Parse(p).Name.LocalName.EndsWith(".RenderTransform",StringComparison.Ordinal))) throw new InvalidDataException("A preserved transform conflicts with editable rotation on "+n.Name);
                e.SetAttributeValue("RenderTransformOrigin","0.5,0.5"); e.Add(new XElement(Ns+(n.Type+".RenderTransform"),new XElement(Ns+"RotateTransform",new XAttribute("Angle",Numbers.Format(n.Rotation)))));
            }
            if(n.TextContent.Length>0) e.Add(new XText(n.TextContent)); foreach(var child in n.Children) e.Add(WriteNode(child)); return e;
        }
        var root=WriteNode(document.Root); root.SetAttributeValue(XNamespace.Xmlns+"x",X.NamespaceName); var nodes=document.Root.DescendantsAndSelf().ToDictionary(n=>n.Id);
        if(!document.Storyboards.IsEmpty)
        {
            var resources=root.Elements().FirstOrDefault(e=>e.Name.LocalName.EndsWith(".Resources",StringComparison.Ordinal));
            if(resources is null) { resources=new XElement(Ns+(document.Root.Type+".Resources")); root.AddFirst(resources); }
            var container=ResourceContainer(resources);
            foreach(var board in document.Storyboards)
            {
                if(container.Elements().Any(e=>(string?)e.Attribute(X+"Key")==board.Name)) throw new InvalidDataException("Resource key conflict: "+board.Name);
                container.Add(XamlAnimationCodec.WriteStoryboard(board,nodes));
            }
        }
        if(!document.States.IsEmpty || !document.StateGroups.IsEmpty)
        {
            var groups=root.Elements().FirstOrDefault(e=>e.Name.LocalName=="VisualStateManager.VisualStateGroups");
            if(groups is null) { groups=new XElement(Ns+"VisualStateManager.VisualStateGroups"); root.AddFirst(groups); }
            foreach(var group in VisualStateGroups.Get(document))
            {
                if(groups.Elements().Any(e=>(string?)e.Attribute(X+"Name")==group.Name)) throw new InvalidDataException("Preserved state group name conflict: "+group.Name);
                groups.Add(XamlStateCodec.WriteGroup(group,document.States.Where(s=>s.Group==group.Name),nodes));
            }
        }
        var settings=new XmlWriterSettings { Indent=true,IndentChars="    ",OmitXmlDeclaration=true,NewLineChars="\n" };
        var buffer=new StringBuilder(); using(var writer=XmlWriter.Create(buffer,settings)) root.WriteTo(writer); return buffer.ToString();
    }
    public static DesignDocument Reconcile(DesignDocument previous,DesignDocument parsed)
    {
        var old=previous.Root.DescendantsAndSelf().Where(n=>n.Get(DesignNode.NameKey,n.Get("Name")).Length>0).ToDictionary(n=>n.Name); var ids=new Dictionary<Guid,Guid>();
        DesignNode Walk(DesignNode n)
        {
            var id=n.Id; var locked=n.IsLocked;
            if(n.Get(DesignNode.NameKey,n.Get("Name")).Length>0 && old.TryGetValue(n.Name,out var match) && match.Type==n.Type && match.Namespace==n.Namespace) { id=match.Id; locked=match.IsLocked; }
            ids[n.Id]=id; return n with { Id=id,IsLocked=locked,Children=n.Children.Select(Walk).ToImmutableArray() };
        }
        var root=Walk(parsed.Root);
        var result=parsed with { Root=root,Storyboards=parsed.Storyboards.Select(b=>b with { Id=previous.Storyboards.FirstOrDefault(p=>p.Name==b.Name)?.Id ?? b.Id,Tracks=b.Tracks.Select(t=>t with { TargetId=ids[t.TargetId] }).ToImmutableArray() }).ToImmutableArray(),States=parsed.States.Select(s=>s with { Setters=s.Setters.Select(p=>p with { TargetId=ids[p.TargetId] }).ToImmutableArray() }).ToImmutableArray() };
        DocumentValidator.Validate(result); return result;
    }
}

[JsonSourceGenerationOptions(WriteIndented=true,PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,IgnoreReadOnlyProperties=true,MaxDepth=256)]
[JsonSerializable(typeof(DesignDocument))]
internal partial class NativeDocumentJsonContext : JsonSerializerContext { }
public static class NativeDocumentCodec
{
    public static string Write(DesignDocument document)
    {
        DocumentValidator.Validate(document); return JsonSerializer.Serialize(document,NativeDocumentJsonContext.Default.DesignDocument);
    }
    public static DesignDocument Read(string text)
    {
        if(text.Length>XamlCodec.MaxCharacters) throw new InvalidDataException("Document is too large.");
        var doc=JsonSerializer.Deserialize(text,NativeDocumentJsonContext.Default.DesignDocument) ?? throw new InvalidDataException("Empty document."); DocumentValidator.Validate(doc); return doc;
    }
}
