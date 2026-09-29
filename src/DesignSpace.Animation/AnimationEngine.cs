using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using DesignSpace.Core;
namespace DesignSpace.Animation;

public static partial class AnimationEngine
{
    public static readonly string[] Properties=["Canvas.Left","Canvas.Top","Width","Height","Opacity","Rotation"];
    private static readonly ConditionalWeakTable<AnimationTrack,AnimationKey[]> SortedKeys=new();
    public static double Ease(double t,string easing)=>easing switch
    {
        "Discrete"=>t>=1?1:0,"EaseIn"=>t*t*t,"EaseOut"=>1-Math.Pow(1-t,3),
        "EaseInOut"=>t<.5?4*t*t*t:1-Math.Pow(-2*t+2,3)/2,_=>t
    };
    private static double Ease(double progress,AnimationKey key,AnimationSamplingContext? context)
    {
        // A first positive-time key evaluates its easing at progress zero: Power=0
        // deliberately jumps. Exact key arrivals are handled by Evaluate, not here.
        if(progress>=1)return 1;
        if(progress<=0&&key.Easing!="Function")return 0;
        return key.Easing switch
        {
            "Spline"=>context?.Sample(key,progress)??(key.Spline??throw new InvalidDataException("Missing KeySpline.")).Evaluate(progress),
            "Function"=>(key.Function??throw new InvalidDataException("Missing easing function.")).Evaluate(progress),
            _=>Ease(progress,key.Easing)
        };
    }
    /// <summary>Evaluates in the track's own keyframe time; parent clock mapping is deliberately separate.</summary>
    private static double EvaluateKeys(AnimationTrack track,double time,double baseValue,AnimationSamplingContext? context)
    {
        if(!double.IsFinite(time)||!double.IsFinite(baseValue))throw new ArgumentOutOfRangeException(nameof(time));
        if(track.Keys.IsEmpty)return baseValue;
        var keys=SortedKeys.GetValue(track,t=>t.Keys.OrderBy(k=>k.Time).ToArray());
        if(time<=keys[0].Time)return time==keys[0].Time||keys[0].Time<=0?keys[0].Value:baseValue+(keys[0].Value-baseValue)*Ease(Math.Clamp(time/keys[0].Time,0,1),keys[0],context);
        var low=1;var high=keys.Length;
        while(low<high){var mid=(low+high)/2;if(keys[mid].Time<=time)low=mid+1;else high=mid;}
        if(low==keys.Length)return keys[^1].Value;
        var a=keys[low-1];var b=keys[low];if(time==a.Time)return a.Value;return a.Value+(b.Value-a.Value)*Ease((time-a.Time)/(b.Time-a.Time),b,context);
    }
    /// <summary>Maps elapsed time through the parent clock, then through every independently timed child.</summary>
    public static DesignNode Evaluate(DesignNode root,DesignStoryboard? storyboard,double time,DesignState? state=null,AnimationSamplingContext? context=null)
    {
        if(storyboard is null)return EvaluateLocal(root,null,0,state,context);
        var sample=StoryboardClock.Sample(storyboard,time);
        return EvaluateLocal(root,sample.Applies?storyboard:null,sample.LocalTime,state,context);
    }
    /// <summary>Scrubs the parent interval, bypassing parent delay/repeats but retaining child timing.</summary>
    public static DesignNode EvaluateLocal(DesignNode root,DesignStoryboard? storyboard,double time,DesignState? state=null,AnimationSamplingContext? context=null)
    {
        if(!double.IsFinite(time))throw new ArgumentOutOfRangeException(nameof(time));
        if(state is null&&storyboard is null)return root;
        var index=DesignIndex.For(root);var changes=new Dictionary<Guid,IReadOnlyDictionary<string,string>>();
        Dictionary<string,string> Values(Guid id)
        {
            if(!changes.TryGetValue(id,out var values))changes[id]=values=new Dictionary<string,string>(StringComparer.Ordinal);
            return (Dictionary<string,string>)values;
        }
        if(state is not null)foreach(var setter in state.Setters)if(index.Find(setter.TargetId) is not null)Values(setter.TargetId)[setter.Property]=setter.Value;
        if(storyboard is not null)
        {
            time=Math.Clamp(time,0,storyboard.Duration);
            foreach(var track in storyboard.Tracks)
            {
                var node=index.Find(track.TargetId);if(node is null||track.Keys.IsEmpty)continue;
                var sample=StoryboardClock.Sample(track,time,storyboard.Duration);if(!sample.Applies)continue;
                var values=Values(track.TargetId);
                var baseline=track.Property=="Rotation"?node.Rotation:node.Number(track.Property,track.Property=="Opacity"?1:0);
                if(values.TryGetValue(track.Property,out var stateValue))baseline=Numbers.Parse(stateValue,baseline);
                values[track.Property]=EvaluateIteration(track,sample.LocalTime,baseline,sample.CurrentIteration,context).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        return DesignTree.SetProperties(root,changes,replacePropertyElements:true);
    }
    public static DesignNode Apply(DesignNode root,Guid id,string property,string value)=>root.Update(id,n=>property=="Rotation"?n with{Rotation=Numbers.Parse(value)}:n.Set(property,value));
    private static int TrackIndex(DesignStoryboard board,Guid target,string property)
    {
        for(var i=0;i<board.Tracks.Length;i++)if(board.Tracks[i].TargetId==target&&board.Tracks[i].Property==property)return i;
        return -1;
    }
    /// <summary>Adds/replaces a key in child-local time, preserving timing and any existing spline on value-only edits.</summary>
    public static DesignStoryboard SetKey(DesignStoryboard board,Guid target,string property,double time,double value,string easing="Linear",KeySpline? spline=null,EasingCurve? function=null)
    {
        if(!Properties.Contains(property))throw new ArgumentException("This property does not support numeric animation.");
        if(!double.IsFinite(time)||!double.IsFinite(value))throw new ArgumentException("Keyframe time and value must be finite.");
        var position=TrackIndex(board,target,property);var track=position<0?new AnimationTrack(target,property,[]):board.Tracks[position];
        time=Math.Clamp(time,0,track.Timing?.Duration??board.Duration);
        var old=track.Keys.FirstOrDefault(k=>Math.Abs(k.Time-time)<.00001);
        var key=new AnimationKey(time,value,easing){Spline=easing=="Spline"?spline??old?.Spline??KeySpline.Linear:null,Function=easing=="Function"?function??old?.Function??new(EasingFamily.Cubic):null};
        AnimationValidation.ValidateKey(key,track.Timing?.Duration??board.Duration);
        if(old==key)return board;
        track=track with{Keys=track.Keys.Where(k=>Math.Abs(k.Time-time)>.00001).Append(key).OrderBy(k=>k.Time).ToImmutableArray()};
        return board with{Tracks=position<0?board.Tracks.Add(track):board.Tracks.SetItem(position,track)};
    }
    public static DesignStoryboard MoveKey(DesignStoryboard board,Guid target,string property,double oldTime,double newTime)
    {
        var position=TrackIndex(board,target,property);if(position<0)throw new InvalidOperationException("The animation track no longer exists.");
        var track=board.Tracks[position];var old=track.Keys.FirstOrDefault(k=>Math.Abs(k.Time-oldTime)<.00001)??throw new InvalidOperationException("The keyframe no longer exists.");
        if(!double.IsFinite(newTime))throw new ArgumentOutOfRangeException(nameof(newTime));
        newTime=Math.Clamp(newTime,0,track.Timing?.Duration??board.Duration);if(newTime==old.Time)return board;
        if(track.Keys.Any(k=>k!=old&&Math.Abs(k.Time-newTime)<.00001))
            throw new InvalidOperationException("A keyframe already occupies that time. Move it or explicitly replace its value first.");
        var keys=track.Keys.Where(k=>k!=old).Append(old with{Time=newTime}).OrderBy(k=>k.Time).ToImmutableArray();
        return board with{Tracks=board.Tracks.SetItem(position,track with{Keys=keys})};
    }
    public static DesignStoryboard ChangeTrackTiming(DesignStoryboard board,Guid target,string property,TrackTiming? timing,bool scaleKeys=false)
    {
        timing?.Validate();var position=TrackIndex(board,target,property);if(position<0)throw new InvalidOperationException("Select an existing animation track.");
        var track=board.Tracks[position];if(track.Timing==timing)return board;
        var duration=timing?.Duration??board.Duration;var oldDuration=track.Timing?.Duration??board.Duration;
        if(!scaleKeys&&track.Keys.Any(k=>k.Time>duration))throw new InvalidOperationException("The duration would exclude keys. Enable Scale track keys or move them first.");
        var next=track with{Timing=timing,Keys=scaleKeys?track.Keys.Select(k=>k with{Time=k.Time/oldDuration*duration}).ToImmutableArray():track.Keys};
        AnimationValidation.ValidateTrack(next,board.Duration);return board with{Tracks=board.Tracks.SetItem(position,next)};
    }
    /// <summary>Rescaling the parent also retimes explicit child delays/durations, not child speed or repeat counts.</summary>
    public static DesignStoryboard ChangeDuration(DesignStoryboard board,double duration,bool scaleKeys)
    {
        if(!double.IsFinite(duration)||duration<=0)throw new ArgumentOutOfRangeException(nameof(duration));
        if(duration==board.Duration)return board;
        if(!scaleKeys&&board.Tracks.Any(t=>t.Timing is null&&t.Keys.Any(k=>k.Time>duration)))
            throw new InvalidOperationException("The new duration would exclude keyframes. Enable Scale keyframes or move them first.");
        var tracks=scaleKeys?board.Tracks.Select(t=>t with
        {
            Keys=t.Keys.Select(k=>k with{Time=k.Time/board.Duration*duration}).ToImmutableArray(),
            Timing=t.Timing is { } clock?clock with{Duration=clock.Duration/board.Duration*duration,BeginTime=clock.BeginTime/board.Duration*duration,RepeatDuration=clock.RepeatDuration/board.Duration*duration}:null
        }).ToImmutableArray():board.Tracks;
        foreach(var track in tracks)AnimationValidation.ValidateTrack(track,duration);
        return board with{Duration=duration,Tracks=tracks};
    }
    /// <summary>Position of a key in the first forward iteration, used by the editable timeline diamonds.</summary>
    public static double KeyToParentTime(AnimationTrack track,double keyTime)=>track.Timing is { } t?t.BeginTime+keyTime/t.SpeedRatio:keyTime;
    public static double ParentToKeyTime(AnimationTrack track,double parentTime,double parentDuration)=>Math.Clamp(track.Timing is { } t?(parentTime-t.BeginTime)*t.SpeedRatio:parentTime,0,track.Timing?.Duration??parentDuration);
    public static DesignStoryboard RemoveKey(DesignStoryboard board,Guid target,string property,double time)=>board with{Tracks=board.Tracks.Select(t=>t.TargetId==target&&t.Property==property?t with{Keys=t.Keys.Where(k=>Math.Abs(k.Time-time)>.00001).ToImmutableArray()}:t).Where(t=>!t.Keys.IsEmpty).ToImmutableArray()};
}
