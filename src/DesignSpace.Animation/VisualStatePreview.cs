using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Animation;

/// <summary>Clock-free, independently reusable preview of concurrent state groups and generated transitions.</summary>
public sealed class VisualStatePreview
{
    private readonly DesignDocument _document;
    private readonly Func<DesignNode,DesignNode>? _resolver;
    private readonly Dictionary<string,DesignStateGroup> _groups;
    private readonly Dictionary<string,Run> _runs=new(StringComparer.Ordinal);
    private readonly Dictionary<string,string> _names=new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string,string> _readOnlyNames;
    private DesignState? _cached;
    private double _sampleTime=double.NaN,_lastChange;
    private bool _settled;
    private readonly record struct Key(Guid Target,string Property);
    private sealed record Channel(Key Key,string? Start,double? NumberStart,double NumberEnd,uint? ColorStart,uint ColorEnd);
    private sealed record Run(DesignState? Target,DesignState? Source,double Start,double Duration,string Easing,Channel[] Channels,HashSet<Key> Keys);
    public IReadOnlyDictionary<string,string> ActiveStates=>_readOnlyNames;
    public int CompiledChannelCount=>_runs.Values.Sum(r=>r.Channels.Length);
    public VisualStatePreview(DesignDocument document,Func<DesignNode,DesignNode>? resolver=null)
    {
        DocumentValidator.Validate(document);_document=document;_resolver=resolver;
        _groups=VisualStateGroups.Get(document).ToDictionary(g=>g.Name,StringComparer.Ordinal);
        _readOnlyNames=new ReadOnlyDictionary<string,string>(_names);
    }
    private static void Time(double time){if(!double.IsFinite(time)||time<0)throw new ArgumentOutOfRangeException(nameof(time));}
    public bool IsRunning(double time){Time(time);return _runs.Values.Any(r=>r.Duration>0&&time<r.Start+r.Duration);}
    public double Progress(double time)
    {
        Time(time);return _runs.Count==0 ? 1 : _runs.Values.Min(r=>r.Duration==0 ? 1 : Math.Clamp((time-r.Start)/r.Duration,0,1));
    }
    public bool GoToState(string name,double time,bool useTransitions=true)
    {
        var state=_document.States.FirstOrDefault(s=>s.Name==name) ?? throw new InvalidOperationException("The requested visual state does not exist.");
        return Change(state.Group,state,time,useTransitions);
    }
    public bool GoToBase(string group,double time,bool useTransitions=true)=>Change(group,null,time,useTransitions);
    private bool Change(string group,DesignState? target,double time,bool useTransitions)
    {
        Time(time);if(time<_lastChange)throw new ArgumentOutOfRangeException(nameof(time),"State changes require a monotonic host clock.");
        if(!_groups.TryGetValue(group,out var definition))throw new InvalidOperationException("The requested state group does not exist.");
        var previous=_runs.GetValueOrDefault(group);
        if(previous?.Target?.Name==target?.Name)return false;
        var source=previous is null ? null : Sample(previous,time);
        var transition=useTransitions ? VisualStateGroups.Match(definition,previous?.Target?.Name,target?.Name) : null;
        var sourceValues=(source?.Setters ?? []).ToDictionary(s=>new Key(s.TargetId,s.Property));
        var targetValues=(target?.Setters ?? []).ToDictionary(s=>new Key(s.TargetId,s.Property));
        var keys=sourceValues.Keys.Concat(targetValues.Keys).ToHashSet();
        foreach(var other in _runs.Where(p=>p.Key!=group))
        {
            var otherKeys=time>=other.Value.Start+other.Value.Duration ? (other.Value.Target?.Setters ?? []).Select(s=>new Key(s.TargetId,s.Property)).ToHashSet() : other.Value.Keys;
            if(keys.Overlaps(otherKeys))throw new InvalidOperationException("Active visual-state groups target the same property. Use disjoint properties or return the other group to Base.");
        }
        DesignNode Resolve(DesignState? state)=>_resolver?.Invoke(AnimationEngine.EvaluateLocal(_document.Root,null,0,state)) ?? AnimationEngine.EvaluateLocal(_document.Root,null,0,state);
        // Resource/style resolution is performed once per state change, never on each sampled frame.
        var start=DesignIndex.For(Resolve(source));var end=DesignIndex.For(Resolve(target));
        var channels=keys.Select(key=>
        {
            var a=Value(start.Find(key.Target),key.Property);var b=Value(end.Find(key.Target),key.Property);
            double? na=null;var nb=0d;uint? ca=null;uint cb=0;
            if(NumericProperty(key.Property)&&Number(a,out var number)&&Number(b,out nb))na=number;
            if(ColorProperty(key.Property)&&TransitionColor.TryParse(a,out var color)&&TransitionColor.TryParse(b,out cb))ca=color;
            return new Channel(key,a,na,nb,ca,cb);
        }).ToArray();
        _runs[group]=new(target,source,time,transition?.Duration ?? 0,transition?.Easing ?? "Linear",channels,keys);
        if(target is null)_names.Remove(group);else _names[group]=target.Name;
        _lastChange=time;_settled=false;_sampleTime=double.NaN;return true;
    }
    private static bool NumericProperty(string property)=>property is "Canvas.Left" or "Canvas.Top" or "Canvas.Right" or "Canvas.Bottom" or "Opacity" or "Rotation" or "Width" or "Height" or "MinWidth" or "MinHeight" or "MaxWidth" or "MaxHeight" or "FontSize" or "StrokeThickness" or "Spacing" or "RowSpacing" or "ColumnSpacing";
    private static bool ColorProperty(string property)=>property is "Fill" or "Stroke" or "Background" or "Foreground" or "BorderBrush" or "Color";
    private static bool Number(string? text,out double value)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&double.IsFinite(value);
    private static string? Value(DesignNode? node,string property)
    {
        if(node is null)return null;if(property=="Rotation")return Numbers.Format(node.Rotation);
        if(node.Properties.TryGetValue(property,out var value))return value;
        if(ColorProperty(property))foreach(var raw in node.PropertyElements)
        {
            var e=XElement.Parse(raw);if(e.Name.LocalName!=node.Type+"."+property)continue;
            var brush=e.Elements().SingleOrDefault();return brush?.Name.LocalName=="SolidColorBrush" ? (string?)brush.Attribute("Color") : null;
        }
        return property=="Opacity" ? "1" : property is "Canvas.Left" or "Canvas.Top" ? "0" : null;
    }
    private static DesignState? Sample(Run run,double time)
    {
        if(run.Duration==0||time>=run.Start+run.Duration)return run.Target;
        if(time<=run.Start)return run.Source;
        var progress=AnimationEngine.Ease(Math.Clamp((time-run.Start)/run.Duration,0,1),run.Easing);
        var setters=ImmutableArray.CreateBuilder<StateSetter>(run.Channels.Length);
        foreach(var channel in run.Channels)
        {
            var value=channel.Start;
            if(channel.NumberStart is { } a) value=(a*(1-progress)+channel.NumberEnd*progress).ToString("R",CultureInfo.InvariantCulture);
            else if(channel.ColorStart is { } color) value=TransitionColor.Interpolate(color,channel.ColorEnd,progress);
            if(value is not null)setters.Add(new(channel.Key.Target,channel.Key.Property,value));
        }
        return setters.Count==0 ? null : new("TransitionPreview",setters.ToImmutable());
    }
    /// <summary>Returns only the affected property overlay. Stable/repeated samples reuse the same overlay.</summary>
    public DesignState? Sample(double time)
    {
        Time(time);var running=IsRunning(time);
        if(time==_sampleTime||!running&&_settled)return _cached;
        var setters=ImmutableArray.CreateBuilder<StateSetter>();
        foreach(var group in _groups.Keys)if(_runs.TryGetValue(group,out var run)&&Sample(run,time) is { } state)setters.AddRange(state.Setters);
        _cached=setters.Count==0 ? null : new("VisualStatePreview",setters.ToImmutable());_sampleTime=time;_settled=!running;return _cached;
    }
    /// <summary>Completes all active transitions without changing document/history.</summary>
    public void Complete()
    {
        foreach(var group in _runs.Keys.ToArray()){var run=_runs[group];_runs[group]=run with { Duration=0,Source=null,Channels=[],Keys=(run.Target?.Setters ?? []).Select(s=>new Key(s.TargetId,s.Property)).ToHashSet() };}
        _sampleTime=double.NaN;_settled=false;
    }
    public void Reset(){_runs.Clear();_names.Clear();_cached=null;_sampleTime=double.NaN;_settled=false;_lastChange=0;}
}

internal static class TransitionColor
{
    public static bool TryParse(string? text,out uint color)
    {
        color=0;if(string.IsNullOrWhiteSpace(text))return false;text=text.Trim();
        if(text.Equals("Transparent",StringComparison.OrdinalIgnoreCase))return true;
        if(text.StartsWith('#'))
        {
            var hex=text[1..];if(hex.Length is 3 or 4)hex=string.Concat(hex.Select(c=>new string(c,2)));
            if(hex.Length is not (6 or 8)||!uint.TryParse(hex,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out color))return false;
            if(hex.Length==6)color|=0xff000000;return true;
        }
        if(text.StartsWith("sc#",StringComparison.OrdinalIgnoreCase))
        {
            var values=text[3..].Split(',',StringSplitOptions.TrimEntries);if(values.Length is not (3 or 4))return false;
            var numbers=new double[values.Length];
            for(var i=0;i<values.Length;i++)if(!double.TryParse(values[i],NumberStyles.Float,CultureInfo.InvariantCulture,out numbers[i])||!double.IsFinite(numbers[i]))return false;
            byte Srgb(double linear){linear=Math.Clamp(linear,0,1);return (byte)Math.Round(255*(linear<=.0031308 ? 12.92*linear : 1.055*Math.Pow(linear,1/2.4)-.055));}
            var offset=values.Length==4 ? 1 : 0;var alpha=values.Length==4 ? (byte)Math.Round(Math.Clamp(numbers[0],0,1)*255) : (byte)255;
            color=(uint)(alpha<<24|Srgb(numbers[offset])<<16|Srgb(numbers[offset+1])<<8|Srgb(numbers[offset+2]));return true;
        }
        var known=System.Drawing.Color.FromName(text);if(!known.IsKnownColor)return false;
        color=(uint)known.ToArgb();return true;
    }
    public static string Interpolate(uint a,uint b,double t)
    {
        uint color=0;for(var shift=0;shift<32;shift+=8)color|=(uint)Math.Round(((a>>shift)&255)*(1-t)+((b>>shift)&255)*t,MidpointRounding.AwayFromZero)<<shift;
        return "#"+color.ToString("X8",CultureInfo.InvariantCulture);
    }
}
