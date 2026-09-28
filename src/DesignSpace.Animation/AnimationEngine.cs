using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using DesignSpace.Core;
namespace DesignSpace.Animation;

public static class AnimationEngine
{
    public static readonly string[] Properties=["Canvas.Left","Canvas.Top","Width","Height","Opacity","Rotation"];
    private static readonly ConditionalWeakTable<AnimationTrack,AnimationKey[]> SortedKeys=new();
    public static double Ease(double t,string easing)=>easing switch
    {
        "Discrete"=>t>=1 ? 1 : 0,"EaseIn"=>t*t*t,"EaseOut"=>1-Math.Pow(1-t,3),
        "EaseInOut"=>t<.5 ? 4*t*t*t : 1-Math.Pow(-2*t+2,3)/2,_=>t
    };
    public static double Evaluate(AnimationTrack track,double time,double baseValue)
    {
        if(track.Keys.IsEmpty) return baseValue;
        var keys=SortedKeys.GetValue(track,t=>t.Keys.OrderBy(k=>k.Time).ToArray());
        if(time<=keys[0].Time) return keys[0].Time<=0 ? keys[0].Value : baseValue+(keys[0].Value-baseValue)*Ease(Math.Clamp(time/keys[0].Time,0,1),keys[0].Easing);
        var low=1; var high=keys.Length;
        while(low<high) { var mid=(low+high)/2; if(keys[mid].Time<=time) low=mid+1; else high=mid; }
        if(low==keys.Length) return keys[^1].Value;
        var a=keys[low-1]; var b=keys[low]; return a.Value+(b.Value-a.Value)*Ease((time-a.Time)/(b.Time-a.Time),b.Easing);
    }
    /// <summary>Coalesces every animated/state property before one structural-sharing tree update.</summary>
    public static DesignNode Evaluate(DesignNode root,DesignStoryboard? storyboard,double time,DesignState? state=null)
    {
        if(state is null && storyboard is null) return root;
        var index=DesignIndex.For(root); var changes=new Dictionary<Guid,IReadOnlyDictionary<string,string>>();
        Dictionary<string,string> Values(Guid id)
        {
            if(!changes.TryGetValue(id,out var values)) changes[id]=values=new Dictionary<string,string>(StringComparer.Ordinal);
            return (Dictionary<string,string>)values;
        }
        if(state is not null) foreach(var setter in state.Setters) if(index.Find(setter.TargetId) is not null) Values(setter.TargetId)[setter.Property]=setter.Value;
        if(storyboard is not null)
        {
            time=storyboard.Loop ? Math.Max(0,time)%storyboard.Duration : Math.Clamp(time,0,storyboard.Duration);
            foreach(var track in storyboard.Tracks)
            {
                var node=index.Find(track.TargetId); if(node is null) continue;
                var values=Values(track.TargetId);
                var baseline=track.Property=="Rotation" ? node.Rotation : node.Number(track.Property,track.Property=="Opacity" ? 1 : 0);
                if(values.TryGetValue(track.Property,out var stateValue)) baseline=Numbers.Parse(stateValue,baseline);
                values[track.Property]=Numbers.Format(Evaluate(track,time,baseline));
            }
        }
        return DesignTree.SetProperties(root,changes);
    }
    public static DesignNode Apply(DesignNode root,Guid id,string property,string value)=>root.Update(id,n=>property=="Rotation" ? n with { Rotation=Numbers.Parse(value) } : n.Set(property,value));
    public static DesignStoryboard SetKey(DesignStoryboard board,Guid target,string property,double time,double value,string easing="Linear")
    {
        if(!Properties.Contains(property)) throw new ArgumentException("This property does not support numeric animation.");
        if(!double.IsFinite(time) || !double.IsFinite(value)) throw new ArgumentException("Keyframe time and value must be finite.");
        time=Math.Clamp(time,0,board.Duration);
        var position=-1; for(var i=0;i<board.Tracks.Length;i++) if(board.Tracks[i].TargetId==target && board.Tracks[i].Property==property) { position=i; break; }
        var track=position<0 ? new AnimationTrack(target,property,[]) : board.Tracks[position];
        track=track with { Keys=track.Keys.Where(k=>Math.Abs(k.Time-time)>.00001).Append(new(time,value,easing)).OrderBy(k=>k.Time).ToImmutableArray() };
        return board with { Tracks=position<0 ? board.Tracks.Add(track) : board.Tracks.SetItem(position,track) };
    }
    public static DesignStoryboard RemoveKey(DesignStoryboard board,Guid target,string property,double time)=>board with { Tracks=board.Tracks.Select(t=>t.TargetId==target && t.Property==property ? t with { Keys=t.Keys.Where(k=>Math.Abs(k.Time-time)>.00001).ToImmutableArray() } : t).Where(t=>!t.Keys.IsEmpty).ToImmutableArray() };
}
