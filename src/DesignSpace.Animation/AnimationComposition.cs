using DesignSpace.Core;
namespace DesignSpace.Animation;

public static partial class AnimationEngine
{
    /// <summary>Samples the first child iteration. Use EvaluateIteration for repeat-aware numeric sampling.</summary>
    public static double Evaluate(AnimationTrack track,double time,double baseValue,AnimationSamplingContext? context=null)
        =>EvaluateIteration(track,time,baseValue,1,context);

    /// <summary>Samples a one-based child iteration, independently of parent repeats. Empty tracks leave the base unchanged.</summary>
    public static double EvaluateIteration(AnimationTrack track,double time,double baseValue,double currentIteration,AnimationSamplingContext? context=null)
    {
        ArgumentNullException.ThrowIfNull(track);
        if(!double.IsFinite(time)||!double.IsFinite(baseValue))throw new ArgumentOutOfRangeException(nameof(time));
        if(!double.IsFinite(currentIteration)||currentIteration<1||currentIteration>9007199254740991d||Math.Truncate(currentIteration)!=currentIteration)
            throw new ArgumentOutOfRangeException(nameof(currentIteration),"An iteration must be a positive exactly representable integer.");
        if(track.Keys.IsEmpty)return baseValue;
        var value=EvaluateKeys(track,time,track.IsAdditive?0:baseValue,context);
        // DoubleAnimationUsingKeyFrames accumulates its final KEY VALUE, not the
        // From/To delta used by DoubleAnimation. Keep these semantics distinct.
        if(track.IsCumulative&&currentIteration>1)
        {
            var keys=SortedKeys.GetValue(track,t=>t.Keys.OrderBy(k=>k.Time).ToArray());
            value+=keys[^1].Value*(currentIteration-1);
        }
        if(track.IsAdditive)value=baseValue+value;
        if(!double.IsFinite(value))throw new InvalidOperationException("Animation composition exceeded the finite numeric range.");
        return value;
    }

    /// <summary>Changes only the composition flags, preserving clocks, key metadata and unaffected tracks.</summary>
    public static DesignStoryboard ChangeTrackComposition(DesignStoryboard board,Guid target,string property,bool additive,bool cumulative)
    {
        ArgumentNullException.ThrowIfNull(board);
        var index=TrackIndex(board,target,property);
        if(index<0)throw new InvalidOperationException("Select an existing animation track.");
        var track=board.Tracks[index];
        if(track.IsAdditive==additive&&track.IsCumulative==cumulative)return board;
        return board with{Tracks=board.Tracks.SetItem(index,track with{IsAdditive=additive,IsCumulative=cumulative})};
    }
}
