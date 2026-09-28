using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

public sealed record DesignDiagnostic(string Severity,string Message,int Line=0,int Column=0);
public sealed record XamlReadResult(DesignDocument Document,IReadOnlyList<DesignDiagnostic> Diagnostics);
/// <summary>Parses XML into data. Never loads assemblies, executes markup extensions or resolves external entities.</summary>
public static class XamlCodec
{
    public const int MaxCharacters=8*1024*1024;
    private static readonly XNamespace Ns=DesignNode.PresentationNamespace;
    private static readonly XNamespace X=DesignNode.XamlNamespace;
    private static readonly HashSet<string> KnownTypes=["Page","UserControl","Window","Grid","Canvas","StackPanel","Border","Rectangle","Ellipse","Path","Line","TextBlock","Button","TextBox","CheckBox","RadioButton","ToggleSwitch","Slider","ProgressBar","Image","Viewbox","ContentControl"];
    public static XamlReadResult Parse(string text,string title="MainPage.xaml")
    {
        if (text.Length>MaxCharacters) throw new InvalidDataException("XAML exceeds the 8 MiB character limit.");
        var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaxCharacters,IgnoreWhitespace=false };
        using var reader=XmlReader.Create(new StringReader(text),settings);
        var xml=XDocument.Load(reader,LoadOptions.SetLineInfo);
        if (xml.Root is null) throw new InvalidDataException("The XAML document has no root element.");
        var diagnostics=new List<DesignDiagnostic>(); var count=0;
        DesignNode Read(XElement element,int depth)
        {
            if (++count>DocumentValidator.MaxNodes || depth>DocumentValidator.MaxDepth) throw new InvalidDataException("Document nesting or element count exceeds the safe limit.");
            if (!KnownTypes.Contains(element.Name.LocalName)) { var li=(IXmlLineInfo)element; diagnostics.Add(new("Warning","Preserved unsupported design-time element: "+element.Name.LocalName,li.LineNumber,li.LinePosition)); }
            var n=new DesignNode { Type=element.Name.LocalName,Namespace=element.Name.NamespaceName,Properties=element.Attributes().Where(a=>!(a.IsNamespaceDeclaration && a.Name.LocalName=="xmlns")).ToImmutableDictionary(a=>a.Name.ToString(),a=>a.Value),TextContent=string.Concat(element.Nodes().OfType<XText>().Select(t=>t.Value)).Trim() };
            var properties=ImmutableArray.CreateBuilder<string>(); var children=ImmutableArray.CreateBuilder<DesignNode>();
            foreach (var child in element.Elements())
            {
                if (child.Name.LocalName.Contains('.'))
                {
                    if (child.Name.LocalName.EndsWith(".RenderTransform",StringComparison.Ordinal) && child.Elements().Count()==1)
                    {
                        var transform=child.Elements().Single();
                        if ((transform.Name.LocalName is "RotateTransform" or "CompositeTransform") && transform.Attributes().All(a=>a.IsNamespaceDeclaration || a.Name.LocalName is "Angle" or "Rotation"))
                        { n=n with { Rotation=Numbers.Parse((string?)transform.Attribute("Angle") ?? (string?)transform.Attribute("Rotation")) }; continue; }
                    }
                    properties.Add(child.ToString(SaveOptions.DisableFormatting));
                }
                else children.Add(Read(child,depth+1));
            }
            foreach (var p in n.Properties.Where(p=>p.Value.StartsWith("{Binding",StringComparison.Ordinal) || p.Value.StartsWith("{x:Bind",StringComparison.Ordinal))) diagnostics.Add(new("Warning","Binding preserved, not executed by the preview: "+n.Name+"."+p.Key));
            return n with { Children=children.ToImmutable(),PropertyElements=properties.ToImmutable() };
        }
        var root=Read(xml.Root,0); var byName=root.DescendantsAndSelf().GroupBy(n=>n.Name).ToDictionary(g=>g.Key,g=>g.First());
        var boards=ImmutableArray.CreateBuilder<DesignStoryboard>(); var states=ImmutableArray.CreateBuilder<DesignState>(); var remaining=ImmutableArray.CreateBuilder<string>();
        foreach (var raw in root.PropertyElements)
        {
            var property=XElement.Parse(raw);
            if (property.Name.LocalName.EndsWith(".Resources",StringComparison.Ordinal))
            {
                foreach (var element in property.Elements().Where(e=>e.Name.LocalName=="Storyboard").ToArray())
                {
                    if (TryReadStoryboard(element,byName,out var board)) { boards.Add(board!); element.Remove(); }
                    else diagnostics.Add(new("Warning","Unsupported storyboard retained as XAML; not editable in the timeline."));
                }
            }
            if (property.Name.LocalName=="VisualStateManager.VisualStateGroups")
            {
                foreach (var group in property.Elements().Where(e=>(string?)e.Attribute(X+"Name")=="DesignSpaceStates").ToArray())
                {
                    var parsed=new List<DesignState>(); var valid=true;
                    foreach (var state in group.Elements())
                    {
                        var setters=ImmutableArray.CreateBuilder<StateSetter>();
                        foreach (var setter in state.Descendants().Where(e=>e.Name.LocalName=="Setter"))
                        {
                            var target=(string?)setter.Attribute("Target") ?? ""; var dot=target.IndexOf('.');
                            if (dot<1 || !byName.TryGetValue(target[..dot],out var node)) { valid=false; break; }
                            setters.Add(new(node.Id,target[(dot+1)..].Trim('(',')'),(string?)setter.Attribute("Value") ?? ""));
                        }
                        parsed.Add(new((string?)state.Attribute(X+"Name") ?? "State",setters.ToImmutable()));
                    }
                    if(valid) { states.AddRange(parsed); group.Remove(); }
                }
            }
            if(property.HasElements || property.HasAttributes) remaining.Add(property.ToString(SaveOptions.DisableFormatting));
        }
        var document=new DesignDocument { Title=title,Root=root with { PropertyElements=remaining.ToImmutable() },Storyboards=boards.ToImmutable(),States=states.ToImmutable() };
        DocumentValidator.Validate(document); return new(document,diagnostics);
    }
    private static bool TryReadStoryboard(XElement element,IReadOnlyDictionary<string,DesignNode> names,out DesignStoryboard? storyboard)
    {
        storyboard=null; var tracks=ImmutableArray.CreateBuilder<AnimationTrack>();
        var duration=ParseTime((string?)element.Attribute("Duration"),2);
        foreach(var animation in element.Elements())
        {
            var target=(string?)animation.Attribute("Storyboard.TargetName"); var property=((string?)animation.Attribute("Storyboard.TargetProperty") ?? "").Trim('(',')');
            if(target is null || !names.TryGetValue(target,out var node) || !new[]{"Canvas.Left","Canvas.Top","Width","Height","Opacity","Rotation"}.Contains(property)) return false;
            var keys=ImmutableArray.CreateBuilder<AnimationKey>();
            if(animation.Name.LocalName=="DoubleAnimation")
            {
                var end=ParseTime((string?)animation.Attribute("Duration"),duration); var to=(string?)animation.Attribute("To");
                if(to is null) return false;
                var from=(string?)animation.Attribute("From"); if(from is not null) keys.Add(new(0,Numbers.Parse(from)));
                keys.Add(new(end,Numbers.Parse(to))); duration=Math.Max(duration,end);
            }
            else if(animation.Name.LocalName=="DoubleAnimationUsingKeyFrames")
            {
                foreach(var key in animation.Elements())
                {
                    if(key.Name.LocalName is not ("LinearDoubleKeyFrame" or "DiscreteDoubleKeyFrame" or "EasingDoubleKeyFrame")) return false;
                    var easing=key.Name.LocalName=="DiscreteDoubleKeyFrame" ? "Discrete" : (string?)key.Descendants().FirstOrDefault(e=>e.Name.LocalName=="CubicEase")?.Attribute("EasingMode") ?? "Linear";
                    var time=ParseTime((string?)key.Attribute("KeyTime"),-1); if(time<0) return false;
                    keys.Add(new(time,Numbers.Parse((string?)key.Attribute("Value")),easing)); duration=Math.Max(duration,time);
                }
            }
            else return false;
            tracks.Add(new(node.Id,property,keys.ToImmutable()));
        }
        if(tracks.Count==0) return false;
        storyboard=new(Guid.NewGuid(),(string?)element.Attribute(X+"Key") ?? (string?)element.Attribute(X+"Name") ?? "Storyboard",Math.Max(.001,duration),tracks.ToImmutable(),(string?)element.Attribute("RepeatBehavior")=="Forever"); return true;
    }
    private static double ParseTime(string? text,double fallback) => TimeSpan.TryParse(text,System.Globalization.CultureInfo.InvariantCulture,out var span) ? span.TotalSeconds : fallback;
    private static string Time(double seconds) => TimeSpan.FromSeconds(seconds).ToString("c",System.Globalization.CultureInfo.InvariantCulture);
    public static string Write(DesignDocument document)
    {
        DocumentValidator.Validate(document);
        XElement WriteNode(DesignNode n)
        {
            var e=new XElement(XName.Get(n.Type,n.Namespace));
            foreach(var p in n.Properties.OrderBy(p=>p.Key,StringComparer.Ordinal)) e.SetAttributeValue(XName.Get(p.Key),p.Value);
            foreach(var raw in n.PropertyElements) e.Add(XElement.Parse(raw));
            if(n.Rotation!=0) { e.SetAttributeValue("RenderTransformOrigin","0.5,0.5"); e.Add(new XElement(Ns+(n.Type+".RenderTransform"),new XElement(Ns+"RotateTransform",new XAttribute("Angle",Numbers.Format(n.Rotation))))); }
            if(n.TextContent.Length>0) e.Add(new XText(n.TextContent));
            foreach(var child in n.Children) e.Add(WriteNode(child)); return e;
        }
        var root=WriteNode(document.Root); root.SetAttributeValue(XNamespace.Xmlns+"x",X.NamespaceName);
        var nodes=document.Root.DescendantsAndSelf().ToDictionary(n=>n.Id);
        if(!document.Storyboards.IsEmpty)
        {
            var resources=root.Elements().FirstOrDefault(e=>e.Name.LocalName.EndsWith(".Resources",StringComparison.Ordinal));
            if(resources is null) { resources=new XElement(Ns+(document.Root.Type+".Resources")); root.AddFirst(resources); }
            foreach(var board in document.Storyboards)
            {
                var sb=new XElement(Ns+"Storyboard",new XAttribute(X+"Key",board.Name),new XAttribute("Duration",Time(board.Duration)));
                if(board.Loop) sb.SetAttributeValue("RepeatBehavior","Forever");
                foreach(var track in board.Tracks)
                {
                    if(!nodes.TryGetValue(track.TargetId,out var target)) continue;
                    var property=track.Property.Contains('.') ? "("+track.Property+")" : track.Property;
                    if(track.Property=="Rotation") property="(UIElement.RenderTransform).(RotateTransform.Angle)";
                    var a=new XElement(Ns+"DoubleAnimationUsingKeyFrames",new XAttribute("Storyboard.TargetName",target.Name),new XAttribute("Storyboard.TargetProperty",property));
                    foreach(var key in track.Keys.OrderBy(k=>k.Time))
                    {
                        var kind=key.Easing=="Discrete" ? "DiscreteDoubleKeyFrame" : key.Easing=="Linear" ? "LinearDoubleKeyFrame" : "EasingDoubleKeyFrame";
                        var k=new XElement(Ns+kind,new XAttribute("KeyTime",Time(key.Time)),new XAttribute("Value",Numbers.Format(key.Value)));
                        if(kind=="EasingDoubleKeyFrame") k.Add(new XElement(Ns+"EasingDoubleKeyFrame.EasingFunction",new XElement(Ns+"CubicEase",new XAttribute("EasingMode",key.Easing))));
                        a.Add(k);
                    }
                    sb.Add(a);
                }
                resources.Add(sb);
            }
        }
        if(!document.States.IsEmpty)
        {
            var groups=root.Elements().FirstOrDefault(e=>e.Name.LocalName=="VisualStateManager.VisualStateGroups");
            if(groups is null) { groups=new XElement(Ns+"VisualStateManager.VisualStateGroups"); root.AddFirst(groups); }
            var group=new XElement(Ns+"VisualStateGroup",new XAttribute(X+"Name","DesignSpaceStates"));
            foreach(var state in document.States)
            {
                var element=new XElement(Ns+"VisualState",new XAttribute(X+"Name",state.Name));
                var setters=new XElement(Ns+"VisualState.Setters");
                foreach(var setter in state.Setters) if(nodes.TryGetValue(setter.TargetId,out var n)) setters.Add(new XElement(Ns+"Setter",new XAttribute("Target",n.Name+"."+(setter.Property.Contains('.') ? "("+setter.Property+")" : setter.Property)),new XAttribute("Value",setter.Value)));
                if(setters.HasElements) element.Add(setters); group.Add(element);
            }
            groups.Add(group);
        }
        var settings=new XmlWriterSettings { Indent=true,IndentChars="    ",OmitXmlDeclaration=true,NewLineChars="\n" };
        var buffer=new StringBuilder(); using(var writer=XmlWriter.Create(buffer,settings)) root.WriteTo(writer); return buffer.ToString();
    }
}
public static class NativeDocumentCodec
{
    private static readonly JsonSerializerOptions Options=new() { WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,IgnoreReadOnlyProperties=true,MaxDepth=256 };
    public static string Write(DesignDocument document) { DocumentValidator.Validate(document); return JsonSerializer.Serialize(document,Options); }
    public static DesignDocument Read(string text)
    {
        if(text.Length>XamlCodec.MaxCharacters) throw new InvalidDataException("Document is too large.");
        var doc=JsonSerializer.Deserialize<DesignDocument>(text,Options) ?? throw new InvalidDataException("Empty document."); DocumentValidator.Validate(doc); return doc;
    }
}
