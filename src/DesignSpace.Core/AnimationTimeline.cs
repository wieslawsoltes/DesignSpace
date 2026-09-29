using System.Globalization;
using System.Text.Json.Serialization;
namespace DesignSpace.Core;

/// <summary>Immutable, timer-free child clock settings. Times are relative to the parent storyboard.</summary>
public sealed record TrackTiming(double Duration,double BeginTime=0,double SpeedRatio=1,bool AutoReverse=false,
    double RepeatCount=1,double? RepeatDuration=null,bool Loop=false,string FillBehavior="HoldEnd")
{
    public void Validate()
    {
        const double limit=86400*365;
        if(!double.IsFinite(Duration)||Duration<=0||Duration>limit||!double.IsFinite(BeginTime)||BeginTime<0||BeginTime>limit||
           !double.IsFinite(SpeedRatio)||SpeedRatio<=0||!double.IsFinite(RepeatCount)||RepeatCount<0||
           RepeatDuration is { } span&&(!double.IsFinite(span)||span<0||span>limit)||
           Loop&&RepeatDuration is not null||FillBehavior is not ("HoldEnd" or "Stop"))
            throw new InvalidDataException("Invalid track timing. Duration/delay are bounded to one year; speed must be positive.");
        var active=RepeatDuration??Duration*(AutoReverse?2:1)*RepeatCount;
        if(!Loop&&(!double.IsFinite(active)||!double.IsFinite(BeginTime+active/SpeedRatio)))
            throw new InvalidDataException("Track timing exceeds the supported numeric range.");
    }
}

/// <summary>Cubic timing curve from (0,0) to (1,1). Evaluate inverts x; it does not mistake curve parameter for time.</summary>
public sealed record KeySpline(double X1,double Y1,double X2,double Y2)
{
    public static KeySpline Linear { get; }=new(0,0,1,1);
    public void Validate()
    {
        if(!double.IsFinite(X1)||!double.IsFinite(Y1)||!double.IsFinite(X2)||!double.IsFinite(Y2)||
           X1<0||X1>1||Y1<0||Y1>1||X2<0||X2>1||Y2<0||Y2>1)
            throw new InvalidDataException("KeySpline control coordinates must be finite values between zero and one.");
    }
    public static KeySpline Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if(text.Length>256)throw new InvalidDataException("KeySpline text is too long.");
        var parts=text.Split([',',' ','\t','\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length!=4)throw new InvalidDataException("Enter four KeySpline control coordinates.");
        var values=parts.Select(p=>double.Parse(p,NumberStyles.Float,CultureInfo.InvariantCulture)).ToArray();
        var spline=new KeySpline(values[0],values[1],values[2],values[3]);spline.Validate();return spline;
    }
    public string ToXaml()=>FormattableString.Invariant($"{X1:R},{Y1:R} {X2:R},{Y2:R}");
    public double Evaluate(double progress)
    {
        Validate();if(!double.IsFinite(progress))throw new ArgumentOutOfRangeException(nameof(progress));
        if(progress<=0)return 0;if(progress>=1)return 1;if(X1==Y1&&X2==Y2)return progress;
        static double Curve(double t,double a,double b)=>3*(1-t)*(1-t)*t*a+3*(1-t)*t*t*b+t*t*t;
        static double Derivative(double t,double a,double b)=>3*(1-t)*(1-t)*a+6*(1-t)*t*(b-a)+3*t*t*(1-b);
        var lower=0d;var upper=1d;var t=progress;
        // Safeguarded Newton iteration with a bracket handles vertical tangents and x1 > x2.
        for(var i=0;i<48;i++)
        {
            var x=Curve(t,X1,X2);var error=x-progress;
            if(Math.Abs(error)<1e-13)break;
            if(error<0)lower=t;else upper=t;
            var slope=Derivative(t,X1,X2);var candidate=slope>1e-10?t-error/slope:double.NaN;
            t=double.IsFinite(candidate)&&candidate>lower&&candidate<upper ? candidate : (lower+upper)/2;
        }
        return Math.Clamp(Curve(t,Y1,Y2),0,1);
    }
}

public sealed partial record AnimationKey
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public KeySpline? Spline { get; init; }
}
public sealed partial record AnimationTrack
{
    /// <summary>Null keeps the legacy shared parent interval; explicit timing adds a child clock.</summary>
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public TrackTiming? Timing { get; init; }
}
public static class AnimationValidation
{
    public static void ValidateKey(AnimationKey key,double duration)
    {
        if(key is null||!double.IsFinite(key.Time)||!double.IsFinite(key.Value)||key.Time<0||key.Time>duration)
            throw new InvalidDataException("Invalid animation keyframe.");
        if(key.Easing is not ("Linear" or "Discrete" or "EaseIn" or "EaseOut" or "EaseInOut" or "Spline"))
            throw new InvalidDataException("Unsupported keyframe easing.");
        if((key.Easing=="Spline")!=(key.Spline is not null))throw new InvalidDataException("Spline easing requires its control points; other easing modes must not contain a spline.");
        key.Spline?.Validate();
    }
    public static void ValidateTrack(AnimationTrack track,double parentDuration)
    {
        track.Timing?.Validate();
        foreach(var key in track.Keys)ValidateKey(key,track.Timing?.Duration??parentDuration);
        if(track.Keys.GroupBy(k=>k.Time).Any(g=>g.Count()>1))throw new InvalidDataException("Duplicate keyframe time.");
    }
}
