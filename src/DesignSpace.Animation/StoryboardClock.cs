using DesignSpace.Core;
namespace DesignSpace.Animation;

/// <summary>A sampled clock; LocalTime is relative to this clock's own keyframe interval.</summary>
public readonly record struct StoryboardClockSample(double LocalTime,bool Applies,bool IsCompleted);

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
    public static StoryboardClockSample Sample(TrackTiming timing,double parentTime)
    {
        ArgumentNullException.ThrowIfNull(timing);timing.Validate();
        return SampleCore(timing.Duration,timing.BeginTime,timing.SpeedRatio,timing.AutoReverse,timing.RepeatCount,timing.RepeatDuration,timing.Loop,timing.FillBehavior,parentTime);
    }
    public static StoryboardClockSample Sample(AnimationTrack track,double parentTime,double parentDuration)=>
        track.Timing is { } timing?Sample(timing,parentTime):new(Math.Clamp(parentTime,0,parentDuration),true,false);
    private static StoryboardClockSample SampleCore(double duration,double begin,double speed,bool reverse,double repeats,double? repeatDuration,bool forever,string fill,double elapsed)
    {
        if(!double.IsFinite(elapsed))throw new ArgumentOutOfRangeException(nameof(elapsed));
        if(duration<=0||!double.IsFinite(duration)||speed<=0||!double.IsFinite(speed)||!double.IsFinite(begin)||begin<0||
           !double.IsFinite(repeats)||repeats<0||repeatDuration is { } span&&(!double.IsFinite(span)||span<0)||fill is not ("HoldEnd" or "Stop"))
            throw new ArgumentException("Clock settings must be finite and valid.");
        if(elapsed<begin)return new(0,false,false);
        var cycle=duration*(reverse?2:1);
        var activeDuration=forever?double.PositiveInfinity:repeatDuration??cycle*repeats;
        if(activeDuration==0)return new(0,false,true);
        var active=(elapsed-begin)*speed;
        if(!double.IsFinite(active))throw new ArgumentOutOfRangeException(nameof(elapsed),"Clock time overflowed.");
        var completed=!forever&&elapsed>=begin+activeDuration/speed;
        if(completed&&fill=="Stop")return new(0,false,true);
        var position=completed?activeDuration:active;
        var offset=position%cycle;
        if(completed&&position>0&&Math.Abs(offset)<=cycle*1e-12)offset=cycle;
        var local=reverse?Math.Min(offset,cycle-offset):Math.Min(offset,duration);
        return new(Math.Clamp(local,0,duration),true,completed);
    }
}
