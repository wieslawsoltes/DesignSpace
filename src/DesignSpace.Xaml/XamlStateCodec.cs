using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

/// <summary>All-or-nothing extraction: unsupported group behavior stays intact as inert XAML.</summary>
internal static class XamlStateCodec
{
    private static readonly XNamespace Ns=DesignNode.PresentationNamespace,X=DesignNode.XamlNamespace;
    private const string Rotation="(UIElement.RenderTransform).(RotateTransform.Angle)";
    private static bool Attributes(XElement e,params XName[] allowed)=>e.Attributes().All(a=>a.IsNamespaceDeclaration||allowed.Contains(a.Name)) && !e.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value));
    private static bool Name(string? value)=>value is not null&&Regex.IsMatch(value,"^[A-Za-z_][A-Za-z0-9_]*$");
    internal static bool TryReadGroup(XElement element,IReadOnlyDictionary<string,DesignNode> names,out DesignStateGroup? group,out ImmutableArray<DesignState> states)
    {
        group=null;states=[];var name=(string?)element.Attribute(X+"Name");
        if(element.Name!=Ns+"VisualStateGroup"||!Name(name)||!Attributes(element,X+"Name"))return false;
        var result=ImmutableArray.CreateBuilder<DesignState>();var transitions=ImmutableArray.CreateBuilder<DesignTransition>();
        if(element.Elements(Ns+"VisualStateGroup.Transitions").Count()>1||element.Elements(Ns+"VisualStateGroup.States").Count()>1)return false;
        foreach(var child in element.Elements())
        {
            if(child.Name==Ns+"VisualStateGroup.Transitions")
            {
                if(!Attributes(child))return false;
                foreach(var transition in child.Elements())
                {
                    if(transition.Name!=Ns+"VisualTransition"||!Attributes(transition,"From","To","GeneratedDuration"))return false;
                    var duration=0d;
                    if(transition.Attribute("GeneratedDuration") is { } span)
                    {
                        if(!TimeSpan.TryParse(span.Value,CultureInfo.InvariantCulture,out var time)||time.TotalSeconds<0||time.TotalSeconds>86400)return false;
                        duration=time.TotalSeconds;
                    }
                    var easing="Linear";
                    var children=transition.Elements().ToArray();if(children.Length>1)return false;
                    if(children.Length==1)
                    {
                        var property=children[0];var functions=property.Elements().ToArray();
                        if(property.Name!=Ns+"VisualTransition.GeneratedEasingFunction"||!Attributes(property)||functions.Length!=1)return false;
                        var function=functions[0];if(function.Name!=Ns+"CubicEase"||!Attributes(function,"EasingMode")||function.HasElements)return false;
                        easing=(string?)function.Attribute("EasingMode")??"EaseOut";
                        if(easing is not ("EaseIn" or "EaseOut" or "EaseInOut"))return false;
                    }
                    var from=(string?)transition.Attribute("From");var to=(string?)transition.Attribute("To");
                    if((from is not null&&!Name(from))||(to is not null&&!Name(to)))return false;
                    if(transitions.Any(t=>t.From==from&&t.To==to))return false;
                    transitions.Add(new(from,to,duration,easing));
                }
            }
            else
            {
                if(child.Name==Ns+"VisualStateGroup.States"&&!Attributes(child))return false;
                var elements=child.Name==Ns+"VisualStateGroup.States" ? child.Elements() : new[]{child};
                foreach(var state in elements)
                {
                    var stateName=(string?)state.Attribute(X+"Name");
                    if(state.Name!=Ns+"VisualState"||!Name(stateName)||!Attributes(state,X+"Name")||result.Any(s=>s.Name==stateName))return false;
                    var properties=state.Elements().ToArray();if(properties.Length>1||properties.Any(p=>p.Name!=Ns+"VisualState.Setters"||!Attributes(p)))return false;
                    var setters=ImmutableArray.CreateBuilder<StateSetter>();
                    foreach(var setter in properties.SelectMany(p=>p.Elements()))
                    {
                        if(setter.Name!=Ns+"Setter"||!Attributes(setter,"Target","Value")||setter.HasElements)return false;
                        var target=(string?)setter.Attribute("Target")??"";var dot=target.IndexOf('.');
                        if(dot<1||!names.TryGetValue(target[..dot],out var node)||setter.Attribute("Value") is not { } value)return false;
                        var path=target[(dot+1)..];var property=path==Rotation ? "Rotation" : path.Trim('(',')');
                        if(string.IsNullOrWhiteSpace(property)||setters.Any(s=>s.TargetId==node.Id&&s.Property==property))return false;
                        if(property=="Rotation"&&node.PropertyElements.Any(p=>XElement.Parse(p).Name.LocalName.EndsWith(".RenderTransform",StringComparison.Ordinal)))return false;
                        setters.Add(new(node.Id,property,value.Value));
                    }
                    result.Add(new(stateName!,setters.ToImmutable()){Group=name!});
                }
            }
        }
        if(result.Count>4096||transitions.Count>4096||transitions.Any(t=>(t.From is not null&&!result.Any(s=>s.Name==t.From))||(t.To is not null&&!result.Any(s=>s.Name==t.To))))return false;
        group=new(name!,transitions.ToImmutable());states=result.ToImmutable();return true;
    }
    internal static XElement WriteGroup(DesignStateGroup group,IEnumerable<DesignState> states,IReadOnlyDictionary<Guid,DesignNode> nodes)
    {
        var element=new XElement(Ns+"VisualStateGroup",new XAttribute(X+"Name",group.Name));
        if(!group.Transitions.IsEmpty)
        {
            var transitions=new XElement(Ns+"VisualStateGroup.Transitions");
            foreach(var t in group.Transitions)
            {
                var transition=new XElement(Ns+"VisualTransition",new XAttribute("GeneratedDuration",TimeSpan.FromSeconds(t.Duration).ToString("c",CultureInfo.InvariantCulture)));
                if(t.From is not null)transition.SetAttributeValue("From",t.From);if(t.To is not null)transition.SetAttributeValue("To",t.To);
                if(t.Easing!="Linear")transition.Add(new XElement(Ns+"VisualTransition.GeneratedEasingFunction",new XElement(Ns+"CubicEase",new XAttribute("EasingMode",t.Easing))));
                transitions.Add(transition);
            }
            element.Add(transitions);
        }
        foreach(var state in states)
        {
            var visual=new XElement(Ns+"VisualState",new XAttribute(X+"Name",state.Name));var setters=new XElement(Ns+"VisualState.Setters");
            foreach(var setter in state.Setters)
            {
                var node=nodes[setter.TargetId];if(node.Get(DesignNode.NameKey,node.Get("Name")).Length==0)throw new InvalidDataException("State targets need explicit XAML names.");
                var property=setter.Property=="Rotation" ? Rotation : setter.Property.Contains('.') ? "("+setter.Property+")" : setter.Property;
                setters.Add(new XElement(Ns+"Setter",new XAttribute("Target",node.Name+"."+property),new XAttribute("Value",setter.Value)));
            }
            if(setters.HasElements)visual.Add(setters);element.Add(visual);
        }
        return element;
    }
}
