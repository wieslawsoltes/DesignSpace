using System.Collections.Immutable;
using DesignSpace.Core;
namespace DesignSpace.Animation;

public static class AnimationEngine
{
    public static readonly string[] Properties = ["Canvas.Left","Canvas.Top","Width","Height","Opacity","Rotation"];
    public static double Ease(double t, string easing) => easing switch
    {
        "Discrete" => t >= 1 ? 1 : 0,
        "EaseIn" => t*t*t,
        "EaseOut" => 1-Math.Pow(1-t,3),
        "EaseInOut" => t < .5 ? 4*t*t*t : 1-Math.Pow(-2*t+2,3)/2,
        _ => t
    };
    public static double Evaluate(AnimationTrack track, double time, double baseValue)
    {
        if (track.Keys.IsEmpty) return baseValue;
        var keys = track.Keys.OrderBy(k => k.Time).ToArray();
        if (time <= keys[0].Time)
        {
            if (keys[0].Time <= 0) return keys[0].Value;
            return baseValue + (keys[0].Value-baseValue)*Ease(Math.Clamp(time/keys[0].Time,0,1),keys[0].Easing);
        }
        for (var i=1;i<keys.Length;i++) if (time < keys[i].Time)
        {
            var a=keys[i-1]; var b=keys[i]; return a.Value+(b.Value-a.Value)*Ease((time-a.Time)/(b.Time-a.Time),b.Easing);
        }
        return keys[^1].Value;
    }
    public static DesignNode Evaluate(DesignNode root, DesignStoryboard? storyboard, double time, DesignState? state = null)
    {
        if (state is not null) foreach (var setter in state.Setters) root = Apply(root,setter.TargetId,setter.Property,setter.Value);
        if (storyboard is null) return root;
        time = storyboard.Loop ? Math.Max(0,time)%storyboard.Duration : Math.Clamp(time,0,storyboard.Duration);
        foreach (var track in storyboard.Tracks)
        {
            var node=root.Find(track.TargetId); if (node is null) continue;
            var baseline=track.Property=="Rotation" ? node.Rotation : node.Number(track.Property,track.Property=="Opacity" ? 1 : 0);
            root=Apply(root,track.TargetId,track.Property,Numbers.Format(Evaluate(track,time,baseline)));
        }
        return root;
    }
    public static DesignNode Apply(DesignNode root, Guid id, string property, string value) => root.Update(id,n => property == "Rotation" ? n with { Rotation = Numbers.Parse(value) } : n.Set(property,value));
    public static DesignStoryboard SetKey(DesignStoryboard board, Guid target, string property, double time, double value, string easing = "Linear")
    {
        if (!Properties.Contains(property)) throw new ArgumentException("This property does not support numeric animation.");
        time=Math.Clamp(time,0,board.Duration);
        var track=board.Tracks.FirstOrDefault(t => t.TargetId==target && t.Property==property) ?? new(target,property,[]);
        track=track with { Keys=track.Keys.Where(k => Math.Abs(k.Time-time)>.00001).Append(new(time,value,easing)).OrderBy(k=>k.Time).ToImmutableArray() };
        return board with { Tracks=board.Tracks.Where(t=>t.TargetId!=target || t.Property!=property).Append(track).ToImmutableArray() };
    }
    public static DesignStoryboard RemoveKey(DesignStoryboard board,Guid target,string property,double time) => board with { Tracks=board.Tracks.Select(t=>t.TargetId==target && t.Property==property ? t with { Keys=t.Keys.Where(k=>Math.Abs(k.Time-time)>.00001).ToImmutableArray() } : t).Where(t=>!t.Keys.IsEmpty).ToImmutableArray() };
}
