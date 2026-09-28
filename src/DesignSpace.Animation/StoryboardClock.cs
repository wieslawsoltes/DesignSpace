using DesignSpace.Core;
namespace DesignSpace.Animation;

/// <summary>A sampled clock; LocalTime is in keyframe coordinates, not wall-clock seconds.</summary>
public readonly record struct StoryboardClockSample(double LocalTime,bool Applies,bool IsCompleted);

/// <summary>Deterministic flat-storyboard timing. Sampling never mutates a document or starts a timer.</summary>
public static class StoryboardClock
{
    public static double CycleDuration(DesignStoryboard board)=>board.Duration*(board.AutoReverse ? 2 : 1);
    public static double ActiveDuration(DesignStoryboard board)=>board.Loop ? double.PositiveInfinity : board.RepeatDuration ?? CycleDuration(board)*board.RepeatCount;
    public static double EndTime(DesignStoryboard board)=>board.BeginTime+ActiveDuration(board)/board.SpeedRatio;

    public static StoryboardClockSample Sample(DesignStoryboard board,double elapsed)
    {
        ArgumentNullException.ThrowIfNull(board);
        if(!double.IsFinite(elapsed)) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if(board.Duration<=0 || !double.IsFinite(board.Duration) || board.SpeedRatio<=0 || !double.IsFinite(board.SpeedRatio))
            throw new ArgumentException("A clock needs finite, positive duration and speed.",nameof(board));
        if(elapsed<board.BeginTime) return new(0,false,false);
        var activeDuration=ActiveDuration(board);
        if(activeDuration==0) return new(0,false,true);
        var active=(elapsed-board.BeginTime)*board.SpeedRatio;
        if(!double.IsFinite(active)) throw new ArgumentOutOfRangeException(nameof(elapsed),"Clock time overflowed.");
        var completed=!board.Loop && elapsed>=EndTime(board);
        if(completed && board.FillBehavior=="Stop") return new(0,false,true);
        var cycle=CycleDuration(board);
        // A completed integral repeat holds its end, unlike the beginning of a live repeat.
        var position=completed ? activeDuration : active;
        var offset=position%cycle;
        if(completed && position>0 && Math.Abs(offset)<=cycle*1e-12) offset=cycle;
        var local=board.AutoReverse ? Math.Min(offset,cycle-offset) : Math.Min(offset,board.Duration);
        return new(Math.Clamp(local,0,board.Duration),true,completed);
    }
}
