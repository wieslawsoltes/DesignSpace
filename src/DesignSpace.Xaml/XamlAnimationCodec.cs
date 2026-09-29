using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

internal static class XamlAnimationCodec
{
    private static readonly XNamespace Ns=DesignNode.PresentationNamespace;
    private static readonly XNamespace X=DesignNode.XamlNamespace;
    private const string RotationPath="(UIElement.RenderTransform).(RotateTransform.Angle)";
    private static readonly string[] Properties=["Canvas.Left","Canvas.Top","Width","Height","Opacity","Rotation"];
    private static readonly string[] TimingAttributes=["Duration","RepeatBehavior","AutoReverse","BeginTime","SpeedRatio","FillBehavior"];
    private static string ReadProperty(string value)=>value==RotationPath?"Rotation":value.Trim('(',')');
    private static string WriteProperty(string property)=>property=="Rotation"?RotationPath:property.Contains('.')?"("+property+")":property;
    private static double ParseTime(string? text,double fallback)=>TimeSpan.TryParse(text,CultureInfo.InvariantCulture,out var time)&&time.TotalSeconds>=0?time.TotalSeconds:fallback;
    private static string Time(double seconds)=>TimeSpan.FromSeconds(seconds).ToString("c",CultureInfo.InvariantCulture);
    private static bool Attributes(XElement e,params string[] names)=>e.Attributes().All(a=>a.IsNamespaceDeclaration||names.Contains(a.Name.ToString()));
    private static bool Number(string? text,out double number)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out number)&&double.IsFinite(number);
    private static bool TryTiming(XElement element,double duration,out TrackTiming timing)
    {
        timing=new(duration);var repeat=(string?)element.Attribute("RepeatBehavior");var forever=repeat=="Forever";var count=1d;double? span=null;
        if(repeat is not null&&!forever)
        {
            if(repeat.EndsWith('x')){if(!Number(repeat[..^1],out count)||count<0)return false;}
            else{var value=ParseTime(repeat,-1);if(value<0)return false;span=value;}
        }
        var reverse=false;var reverseText=(string?)element.Attribute("AutoReverse");if(reverseText is not null&&!bool.TryParse(reverseText,out reverse))return false;
        var begin=ParseTime((string?)element.Attribute("BeginTime"),element.Attribute("BeginTime") is null?0:-1);if(begin<0)return false;
        var speed=1d;if(element.Attribute("SpeedRatio") is { } rate&&(!Number(rate.Value,out speed)||speed<=0))return false;
        var fill=(string?)element.Attribute("FillBehavior")??"HoldEnd";
        timing=new(duration,begin,speed,reverse,count,span,forever,fill);
        try{timing.Validate();return true;}catch(InvalidDataException){return false;}
    }
    internal static bool TryReadStoryboard(XElement element,IReadOnlyDictionary<string,DesignNode> names,out DesignStoryboard? storyboard)
    {
        storyboard=null;
        if(element.Name!=Ns+"Storyboard"||!Attributes(element,TimingAttributes.Concat(new[]{(X+"Key").ToString(),(X+"Name").ToString()}).ToArray()))return false;
        var name=(string?)element.Attribute(X+"Key")??(string?)element.Attribute(X+"Name");if(name is null)return false;
        var durationText=(string?)element.Attribute("Duration");var explicitDuration=durationText is not null&&durationText!="Automatic";
        var duration=explicitDuration?ParseTime(durationText,-1):0;if(explicitDuration&&duration<=0)return false;
        var naturalDuration=0d;var tracks=ImmutableArray.CreateBuilder<AnimationTrack>();
        foreach(var animation in element.Elements())
        {
            var simple=animation.Name==Ns+"DoubleAnimation";
            if(!simple&&animation.Name!=Ns+"DoubleAnimationUsingKeyFrames")return false;
            var allowed=TimingAttributes.Concat(new[]{"Storyboard.TargetName","Storyboard.TargetProperty","EnableDependentAnimation"});
            if(simple)allowed=allowed.Concat(new[]{"From","To"});
            if(!Attributes(animation,allowed.ToArray()))return false;
            if(animation.Attribute("EnableDependentAnimation") is { } dependent&&!bool.TryParse(dependent.Value,out _))return false;
            var target=(string?)animation.Attribute("Storyboard.TargetName");var property=ReadProperty((string?)animation.Attribute("Storyboard.TargetProperty")??"");
            if(target is null||!names.TryGetValue(target,out var node)||!Properties.Contains(property)||tracks.Any(t=>t.TargetId==node.Id&&t.Property==property))return false;
            if(property=="Rotation"&&node.PropertyElements.Any(p=>XElement.Parse(p).Name.LocalName.EndsWith(".RenderTransform",StringComparison.Ordinal)))return false;
            var childText=(string?)animation.Attribute("Duration");var fixedChild=childText is not null&&childText!="Automatic";
            var childDuration=fixedChild?ParseTime(childText,-1):simple?1:0;
            if(fixedChild&&childDuration<=0)return false;
            var keys=ImmutableArray.CreateBuilder<AnimationKey>();
            if(simple)
            {
                if(!Number((string?)animation.Attribute("To"),out var to))return false;
                EasingCurve? function=null;
                if(animation.HasElements)
                {
                    var wrappers=animation.Elements().ToArray();
                    if(wrappers.Length!=1||wrappers[0].Name!=Ns+"DoubleAnimation.EasingFunction"||!Attributes(wrappers[0]))return false;
                    var functions=wrappers[0].Elements().ToArray();
                    if(functions.Length!=1||!EasingCurveCodec.TryRead(functions[0],out function))return false;
                }
                // Simple DoubleAnimation evaluates the function at its clock endpoints;
                // keyframe tracks instead honor explicit key arrivals. Preserve discontinuous
                // functions (for example Power=0) rather than changing those semantics.
                if(function is not null&&(Math.Abs(function.Evaluate(0))>1e-12||Math.Abs(function.Evaluate(1)-1)>1e-12))return false;
                if(animation.Attribute("From") is { } fromText){if(!Number(fromText.Value,out var from))return false;keys.Add(new(0,from));}
                keys.Add(CurveKey(childDuration,to,function));
            }
            else
            {
                foreach(var key in animation.Elements())
                {
                    var kind=key.Name.LocalName;
                    if(key.Name.Namespace!=Ns||kind is not ("LinearDoubleKeyFrame" or "DiscreteDoubleKeyFrame" or "EasingDoubleKeyFrame" or "SplineDoubleKeyFrame"))return false;
                    if(!Attributes(key,kind=="SplineDoubleKeyFrame"?["KeyTime","Value","KeySpline"]:["KeyTime","Value"]))return false;
                    var keyText=(string?)key.Attribute("KeyTime");double time;
                    if(keyText?.EndsWith('%')==true)
                    {
                        if(!fixedChild||!Number(keyText[..^1],out var percent)||percent<0||percent>100)return false;
                        time=percent/100*childDuration;
                    }
                    else time=ParseTime(keyText,-1);
                    if(time<0||!Number((string?)key.Attribute("Value"),out var value))return false;
                    var easing=kind=="DiscreteDoubleKeyFrame"?"Discrete":"Linear";KeySpline? spline=null;EasingCurve? function=null;
                    if(kind=="EasingDoubleKeyFrame")
                    {
                        if(key.HasElements)
                        {
                            var wrappers=key.Elements().ToArray();if(wrappers.Length!=1||wrappers[0].Name!=Ns+"EasingDoubleKeyFrame.EasingFunction"||!Attributes(wrappers[0]))return false;
                            var elements=wrappers[0].Elements().ToArray();if(elements.Length!=1||!EasingCurveCodec.TryRead(elements[0],out function))return false;
                            // Keep the legacy cubic aliases readable in existing version-1 documents.
                            if(function!.Family==EasingFamily.Cubic){easing=function.Mode.ToString();function=null;}
                            else easing="Function";
                        }
                    }
                    else if(kind=="SplineDoubleKeyFrame")
                    {
                        easing="Spline";var raw=(string?)key.Attribute("KeySpline");
                        if(key.HasElements)
                        {
                            if(raw is not null)return false;var wrappers=key.Elements().ToArray();
                            if(wrappers.Length!=1||wrappers[0].Name!=Ns+"SplineDoubleKeyFrame.KeySpline"||!Attributes(wrappers[0]))return false;
                            var elements=wrappers[0].Elements().ToArray();if(elements.Length!=1)return false;var curve=elements[0];
                            if(curve.Name!=Ns+"KeySpline"||curve.HasElements||!Attributes(curve,"ControlPoint1","ControlPoint2"))return false;
                            raw=((string?)curve.Attribute("ControlPoint1")??"0,0")+" "+((string?)curve.Attribute("ControlPoint2")??"1,1");
                        }
                        try{spline=raw is null?KeySpline.Linear:KeySpline.Parse(raw);}catch(Exception e)when(e is FormatException or OverflowException or InvalidDataException){return false;}
                    }
                    else if(key.HasElements)return false;
                    keys.Add(new(time,value,easing){Spline=spline,Function=function});
                }
                if(!fixedChild)childDuration=keys.Select(k=>k.Time).DefaultIfEmpty(0).Max();
                if(childDuration<=0)childDuration=1;
            }
            if(keys.Count==0||keys.GroupBy(k=>k.Time).Any(g=>g.Count()>1)||keys.Any(k=>k.Time>childDuration)||!TryTiming(animation,childDuration,out var timing))return false;
            var end=timing.Loop?double.PositiveInfinity:timing.BeginTime+(timing.RepeatDuration??timing.Duration*(timing.AutoReverse?2:1)*timing.RepeatCount)/timing.SpeedRatio;
            naturalDuration=Math.Max(naturalDuration,end);
            tracks.Add(new(node.Id,property,keys.ToImmutable()){Timing=timing});
        }
        // A finite parent may clip a repeating/longer child; Automatic + infinite child cannot be represented by a finite ruler.
        if(!explicitDuration&&!double.IsFinite(naturalDuration))return false;
        if(!explicitDuration)duration=naturalDuration==0?2:naturalDuration;
        if(!TryTiming(element,duration,out var parent))return false;
        storyboard=new(Guid.NewGuid(),name,duration,tracks.ToImmutable(),parent.Loop)
        {AutoReverse=parent.AutoReverse,BeginTime=parent.BeginTime,SpeedRatio=parent.SpeedRatio,RepeatCount=parent.RepeatCount,RepeatDuration=parent.RepeatDuration,FillBehavior=parent.FillBehavior};
        return true;
    }
    private static AnimationKey CurveKey(double time,double value,EasingCurve? curve)=>curve is null?new(time,value):
        curve.Family==EasingFamily.Cubic?new(time,value,curve.Mode.ToString()):new(time,value,"Function"){Function=curve};
    private static void WriteTiming(XElement element,TrackTiming timing)
    {
        element.SetAttributeValue("Duration",Time(timing.Duration));
        if(timing.Loop)element.SetAttributeValue("RepeatBehavior","Forever");
        else if(timing.RepeatDuration is { } span)element.SetAttributeValue("RepeatBehavior",Time(span));
        else if(timing.RepeatCount!=1)element.SetAttributeValue("RepeatBehavior",timing.RepeatCount.ToString("R",CultureInfo.InvariantCulture)+"x");
        if(timing.AutoReverse)element.SetAttributeValue("AutoReverse","True");
        if(timing.BeginTime!=0)element.SetAttributeValue("BeginTime",Time(timing.BeginTime));
        if(timing.SpeedRatio!=1)element.SetAttributeValue("SpeedRatio",timing.SpeedRatio.ToString("R",CultureInfo.InvariantCulture));
        if(timing.FillBehavior!="HoldEnd")element.SetAttributeValue("FillBehavior",timing.FillBehavior);
    }
    internal static XElement WriteStoryboard(DesignStoryboard board,IReadOnlyDictionary<Guid,DesignNode> nodes)
    {
        var sb=new XElement(Ns+"Storyboard",new XAttribute(X+"Key",board.Name));
        WriteTiming(sb,new(board.Duration,board.BeginTime,board.SpeedRatio,board.AutoReverse,board.RepeatCount,board.RepeatDuration,board.Loop,board.FillBehavior));
        foreach(var track in board.Tracks)
        {
            AnimationValidation.ValidateTrack(track,board.Duration);
            if(!Properties.Contains(track.Property))throw new InvalidDataException("Unsupported numeric animation property: "+track.Property);
            var target=nodes[track.TargetId];if(target.Get(DesignNode.NameKey,target.Get("Name")).Length==0)throw new InvalidDataException("Animation targets must have explicit XAML names.");
            var animation=new XElement(Ns+"DoubleAnimationUsingKeyFrames",new XAttribute("Storyboard.TargetName",target.Name),new XAttribute("Storyboard.TargetProperty",WriteProperty(track.Property)));
            WriteTiming(animation,track.Timing??new TrackTiming(board.Duration));
            if(track.Property is "Canvas.Left" or "Canvas.Top" or "Width" or "Height")animation.SetAttributeValue("EnableDependentAnimation","True");
            foreach(var key in track.Keys.OrderBy(k=>k.Time))
            {
                var kind=key.Easing switch{"Discrete"=>"DiscreteDoubleKeyFrame","Linear"=>"LinearDoubleKeyFrame","Spline"=>"SplineDoubleKeyFrame",_=>"EasingDoubleKeyFrame"};
                var k=new XElement(Ns+kind,new XAttribute("KeyTime",Time(key.Time)),new XAttribute("Value",key.Value.ToString("R",CultureInfo.InvariantCulture)));
                if(kind=="SplineDoubleKeyFrame")k.SetAttributeValue("KeySpline",key.Spline!.ToXaml());
                if(kind=="EasingDoubleKeyFrame")k.Add(new XElement(Ns+"EasingDoubleKeyFrame.EasingFunction",key.Function is { } function?EasingCurveCodec.Write(function):new XElement(Ns+"CubicEase",new XAttribute("EasingMode",key.Easing))));
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
