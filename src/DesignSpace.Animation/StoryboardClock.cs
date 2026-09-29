using DesignSpace.Core;
namespace DesignSpace.Animation;

/// <summary>A sampled clock; LocalTime is relative to this clock's own keyframe interval.</summary>
public readonly record struct StoryboardClockSample(double LocalTime,bool Applies,bool IsCompleted)
{
    /// <summary>One-based count of complete forward/reverse cycles. A parent repeat restarts its children.</summary>
    public double CurrentIteration { get; init; }=1;
    /// <summary>Intrinsic direction propagated through parent and child auto-reverse phases.</summary>
    public bool IsReversed { get; init; }
}

/// <summary>Deterministic parent and child timing. Sampling never mutates a document or starts a timer.</summary>
public static class StoryboardClock
{
    public static double CycleDuration(DesignStoryboard board)=>board.Duration*(board.AutoReverse?2:1);
    public static double ActiveDuration(DesignStoryboard board)=>board.Loop?double.PositiveInfinity:board.RepeatDuration??CycleDuration(board)*board.RepeatCount;
    public static double EndTime(DesignStoryboard board)=>board.BeginTime+ActiveDuration(board)/board.SpeedRatio;
    public static double EndTime(TrackTiming timing)=>timing.Loop?double.PositiveInfinity:timing.BeginTime+(timing.RepeatDuration??timing.Duration*(timing.AutoReverse?2:1)*timing.RepeatCount)/timing.SpeedRatio;
    public static StoryboardClockSample Sample(DesignStoryboard board,double elapsed)
    {
        ArgumentNullException.ThrowIfNull(board);
        return SampleCore(board.Duration,board.BeginTime,board.SpeedRatio,board.AutoReverse,board.RepeatCount,board.RepeatDuration,board.Loop,board.FillBehavior,elapsed);
    }
    public static StoryboardClockSample Sample(TrackTiming timing,double parentTime,bool parentReversed=false)
    {
        ArgumentNullException.ThrowIfNull(timing);timing.Validate();
        return SampleCore(timing.Duration,timing.BeginTime,timing.SpeedRatio,timing.AutoReverse,timing.RepeatCount,timing.RepeatDuration,timing.Loop,timing.FillBehavior,parentTime,parentReversed);
    }
    public static StoryboardClockSample Sample(AnimationTrack track,double parentTime,double parentDuration,bool parentReversed=false)
    {
        ArgumentNullException.ThrowIfNull(track);
        if(!double.IsFinite(parentTime)||!double.IsFinite(parentDuration)||parentDuration<=0)throw new ArgumentOutOfRangeException(nameof(parentTime));
        return track.Timing is { } timing?Sample(timing,parentTime,parentReversed):new(Math.Clamp(parentTime,0,parentDuration),true,false){IsReversed=parentReversed};
    }
    private static StoryboardClockSample SampleCore(double duration,double begin,double speed,bool reverse,double repeats,double? repeatDuration,bool forever,string fill,double elapsed,bool parentReversed=false)
    {
        if(!double.IsFinite(elapsed))throw new ArgumentOutOfRangeException(nameof(elapsed));
        if(duration<=0||!double.IsFinite(duration)||speed<=0||!double.IsFinite(speed)||!double.IsFinite(begin)||begin<0||
           !double.IsFinite(repeats)||repeats<0||repeatDuration is { } span&&(!double.IsFinite(span)||span<0)||fill is not ("HoldEnd" or "Stop"))
            throw new ArgumentException("Clock settings must be finite and valid.");
        if(elapsed<begin)return new(0,false,false);
        var cycle=duration*(reverse?2:1);
        var activeDuration=forever?double.PositiveInfinity:repeatDuration??cycle*repeats;
        if(activeDuration==0)return new(0,false,true);
        var completed=!forever&&elapsed>=begin+activeDuration/speed;
        if(completed&&fill=="Stop")return new(0,false,true);
        // Completed finite clocks do not need to multiply arbitrarily large elapsed times.
        var position=completed?activeDuration:(elapsed-begin)*speed;
        if(!double.IsFinite(position))throw new ArgumentOutOfRangeException(nameof(elapsed),"Clock time overflowed.");
        // WPF chooses the incoming segment when a parent is progressing backwards.
        // Use half-cycle indices so auto-reverse turning points retain their direction.
        var offset=position%duration;
        var segment=Math.Floor(position/duration);
        if((completed||parentReversed)&&position>0&&Math.Abs(offset)<=duration*1e-12){offset=duration;segment=Math.Max(0,segment-1);}
        var backwards=reverse&&segment%2>=1;
        var local=backwards?duration-offset:offset;
        var iteration=reverse?Math.Floor(segment/2)+1:segment+1;
        return new(Math.Clamp(local,0,duration),true,completed){CurrentIteration=iteration,IsReversed=parentReversed^backwards};
    }
}
