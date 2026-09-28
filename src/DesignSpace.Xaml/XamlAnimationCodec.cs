using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

internal static class XamlAnimationCodec
{
    private static readonly XNamespace Ns=DesignNode.PresentationNamespace;
    private static readonly XNamespace X=DesignNode.XamlNamespace;
    private const string RotationPath="(UIElement.RenderTransform).(RotateTransform.Angle)";
    private static readonly string[] Properties=["Canvas.Left","Canvas.Top","Width","Height","Opacity","Rotation"];
    private static string ReadProperty(string value)=>value==RotationPath ? "Rotation" : value.Trim('(',')');
    private static string WriteProperty(string property)=>property=="Rotation" ? RotationPath : property.Contains('.') ? "("+property+")" : property;
    private static double ParseTime(string? text,double fallback)=>TimeSpan.TryParse(text,System.Globalization.CultureInfo.InvariantCulture,out var time) && time.TotalSeconds>=0 ? time.TotalSeconds : fallback;
    private static string Time(double seconds)=>TimeSpan.FromSeconds(seconds).ToString("c",System.Globalization.CultureInfo.InvariantCulture);
    private static bool Attributes(XElement e,params string[] names)=>e.Attributes().All(a=>a.IsNamespaceDeclaration || names.Contains(a.Name.ToString()));
    private static bool Number(string? text,out double number)=>double.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out number) && double.IsFinite(number);
    internal static bool TryReadStoryboard(XElement element,IReadOnlyDictionary<string,DesignNode> names,out DesignStoryboard? storyboard)
    {
        storyboard=null;
        if(!Attributes(element,(X+"Key").ToString(),(X+"Name").ToString(),"Duration","RepeatBehavior")) return false;
        var repeat=(string?)element.Attribute("RepeatBehavior"); if(repeat is not null && repeat!="Forever" && repeat!="1x") return false;
        var tracks=ImmutableArray.CreateBuilder<AnimationTrack>(); var duration=ParseTime((string?)element.Attribute("Duration"),2);
        foreach(var animation in element.Elements())
        {
            if(!Attributes(animation,"Storyboard.TargetName","Storyboard.TargetProperty","Duration","From","To","EnableDependentAnimation")) return false;
            var target=(string?)animation.Attribute("Storyboard.TargetName"); var property=ReadProperty((string?)animation.Attribute("Storyboard.TargetProperty") ?? "");
            if(target is null || !names.TryGetValue(target,out var node) || !Properties.Contains(property)) return false;
            if(tracks.Any(t=>t.TargetId==node.Id && t.Property==property)) return false;
            if(property=="Rotation" && node.PropertyElements.Any(p=>XElement.Parse(p).Name.LocalName.EndsWith(".RenderTransform",StringComparison.Ordinal))) return false;
            var keys=ImmutableArray.CreateBuilder<AnimationKey>();
            if(animation.Name.LocalName=="DoubleAnimation")
            {
                if(animation.HasElements) return false; var end=ParseTime((string?)animation.Attribute("Duration"),duration);
                if(!Number((string?)animation.Attribute("To"),out var to)) return false;
                var fromText=(string?)animation.Attribute("From"); if(fromText is not null) { if(!Number(fromText,out var from)) return false; keys.Add(new(0,from)); }
                if(end<=0) return false; keys.Add(new(end,to)); duration=Math.Max(duration,end);
            }
            else if(animation.Name.LocalName=="DoubleAnimationUsingKeyFrames")
            {
                foreach(var key in animation.Elements())
                {
                    if(key.Name.LocalName is not ("LinearDoubleKeyFrame" or "DiscreteDoubleKeyFrame" or "EasingDoubleKeyFrame") || !Attributes(key,"KeyTime","Value")) return false;
                    var time=ParseTime((string?)key.Attribute("KeyTime"),-1); if(time<0 || !Number((string?)key.Attribute("Value"),out var value)) return false;
                    var easing=key.Name.LocalName=="DiscreteDoubleKeyFrame" ? "Discrete" : "Linear";
                    if(key.Name.LocalName=="EasingDoubleKeyFrame")
                    {
                        var children=key.Elements().ToArray(); if(children.Length!=1 || children[0].Name.LocalName!="EasingDoubleKeyFrame.EasingFunction") return false;
                        var easingElements=children[0].Elements().ToArray(); if(easingElements.Length!=1) return false; var ease=easingElements[0];
                        if(ease.Name.LocalName!="CubicEase" || !Attributes(ease,"EasingMode")) return false;
                        easing=(string?)ease.Attribute("EasingMode") ?? "EaseOut"; if(easing is not ("EaseIn" or "EaseOut" or "EaseInOut")) return false;
                    }
                    else if(key.HasElements) return false;
                    keys.Add(new(time,value,easing)); duration=Math.Max(duration,time);
                }
            }
            else return false;
            if(keys.Count==0 || keys.GroupBy(k=>k.Time).Any(g=>g.Count()>1)) return false;
            tracks.Add(new(node.Id,property,keys.ToImmutable()));
        }
        var name=(string?)element.Attribute(X+"Key") ?? (string?)element.Attribute(X+"Name"); if(name is null) return false;
        storyboard=new(Guid.NewGuid(),name,Math.Max(.001,duration),tracks.ToImmutable(),repeat=="Forever"); return true;
    }
    internal static XElement WriteStoryboard(DesignStoryboard board,IReadOnlyDictionary<Guid,DesignNode> nodes)
    {
        var sb=new XElement(Ns+"Storyboard",new XAttribute(X+"Key",board.Name),new XAttribute("Duration",Time(board.Duration))); if(board.Loop) sb.SetAttributeValue("RepeatBehavior","Forever");
        foreach(var track in board.Tracks)
        {
            if(!Properties.Contains(track.Property)) throw new InvalidDataException("Unsupported numeric animation property: "+track.Property);
            var target=nodes[track.TargetId]; if(target.Get(DesignNode.NameKey,target.Get("Name")).Length==0) throw new InvalidDataException("Animation targets must have explicit XAML names.");
            var animation=new XElement(Ns+"DoubleAnimationUsingKeyFrames",new XAttribute("Storyboard.TargetName",target.Name),new XAttribute("Storyboard.TargetProperty",WriteProperty(track.Property)));
            if(track.Property is "Canvas.Left" or "Canvas.Top" or "Width" or "Height") animation.SetAttributeValue("EnableDependentAnimation","True");
            foreach(var key in track.Keys.OrderBy(k=>k.Time))
            {
                var kind=key.Easing=="Discrete" ? "DiscreteDoubleKeyFrame" : key.Easing=="Linear" ? "LinearDoubleKeyFrame" : "EasingDoubleKeyFrame";
                var k=new XElement(Ns+kind,new XAttribute("KeyTime",Time(key.Time)),new XAttribute("Value",Numbers.Format(key.Value)));
                if(kind=="EasingDoubleKeyFrame") k.Add(new XElement(Ns+"EasingDoubleKeyFrame.EasingFunction",new XElement(Ns+"CubicEase",new XAttribute("EasingMode",key.Easing))));
                animation.Add(k);
            }
            sb.Add(animation);
        }
        return sb;
    }
    internal static bool TryReadStates(XElement group,IReadOnlyDictionary<string,DesignNode> names,out ImmutableArray<DesignState> states)
    {
        states=[]; if(!Attributes(group,(X+"Name").ToString())) return false;
        var result=ImmutableArray.CreateBuilder<DesignState>();
        foreach(var state in group.Elements())
        {
            if(state.Name.LocalName!="VisualState" || !Attributes(state,(X+"Name").ToString()) || state.Elements().Any(e=>e.Name.LocalName!="VisualState.Setters")) return false;
            var name=(string?)state.Attribute(X+"Name"); if(name is null) return false;
            var setters=ImmutableArray.CreateBuilder<StateSetter>();
            foreach(var setter in state.Elements().SelectMany(e=>e.Elements()))
            {
                if(setter.Name.LocalName!="Setter" || !Attributes(setter,"Target","Value") || setter.HasElements) return false;
                var target=(string?)setter.Attribute("Target") ?? ""; var dot=target.IndexOf('.');
                if(dot<1 || !names.TryGetValue(target[..dot],out var node)) return false;
                var value=(string?)setter.Attribute("Value"); if(value is null) return false;
                setters.Add(new(node.Id,ReadProperty(target[(dot+1)..]),value));
            }
            result.Add(new(name,setters.ToImmutable()));
        }
        states=result.ToImmutable(); return true;
    }
    internal static XElement WriteStates(ImmutableArray<DesignState> states,IReadOnlyDictionary<Guid,DesignNode> nodes)
    {
        var group=new XElement(Ns+"VisualStateGroup",new XAttribute(X+"Name","DesignSpaceStates"));
        foreach(var state in states)
        {
            var element=new XElement(Ns+"VisualState",new XAttribute(X+"Name",state.Name)); var setters=new XElement(Ns+"VisualState.Setters");
            foreach(var setter in state.Setters)
            {
                var n=nodes[setter.TargetId]; if(n.Get(DesignNode.NameKey,n.Get("Name")).Length==0) throw new InvalidDataException("State targets must have explicit XAML names.");
                setters.Add(new XElement(Ns+"Setter",new XAttribute("Target",n.Name+"."+WriteProperty(setter.Property)),new XAttribute("Value",setter.Value)));
            }
            if(setters.HasElements) element.Add(setters); group.Add(element);
        }
        return group;
    }
}
