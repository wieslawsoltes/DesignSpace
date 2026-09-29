using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DesignSpace.Core;
using DesignSpace.Animation;

internal static class CompositionReferenceTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;var samples=0;var maximum=0d;const double tolerance=1e-8;
        var results=new List<object>();
        // Independent native WPF clocks and the portable engine are sampled against
        // each other; no snapshots generated from DesignSpace become expected values.
        var keySets=new (string Name,ImmutableArray<AnimationKey> Keys)[]
        {
            ("nonzero start",[new(0,10),new(2,30)]),
            ("implicit first interval",[new(2,30)]),
            ("negative end",[new(0,10),new(2,-20)]),
            ("zero end",[new(0,10),new(2,0)]),
            ("intermediate discrete",[new(0,10),new(1,20,"Discrete"),new(2,30)]),
            ("cubic easing",[new(0,10),new(2,30,"EaseInOut")])
        };
        var timings=new (string Name,TrackTiming Timing,double Parent,bool Reverse,double Repeat)[]
        {
            ("forward repeats",new(2,RepeatCount:3),8,false,1),
            ("auto reverse",new(2,AutoReverse:true,RepeatCount:2),10,false,1),
            ("fractional",new(2,RepeatCount:1.25),6,false,1),
            ("duration",new(2,RepeatDuration:3),6,false,1),
            ("delayed fast",new(2,BeginTime:.5,SpeedRatio:2,RepeatCount:3),6,false,1),
            ("child Stop",new(2,RepeatCount:2,FillBehavior:"Stop"),6,false,1),
            ("clipped forever",new(2,Loop:true),3,false,1),
            ("parent repeats",new(2,RepeatCount:2),4,false,2),
            ("parent reverse",new(2,RepeatCount:2),4,true,1)
        };
        foreach(var keySet in keySets)foreach(var timing in timings)foreach(var additive in new[]{false,true})foreach(var cumulative in new[]{false,true})
        {
            var name=$"{keySet.Name} / {timing.Name} / additive={additive} cumulative={cumulative}";
            try
            {
                var target=new Rectangle{Width=20,Height=20};Canvas.SetLeft(target,40);var host=new Canvas();host.Children.Add(target);
                var t=timing.Timing;
                var native=new DoubleAnimationUsingKeyFrames{Duration=new Duration(TimeSpan.FromSeconds(t.Duration)),BeginTime=TimeSpan.FromSeconds(t.BeginTime),SpeedRatio=t.SpeedRatio,
                    AutoReverse=t.AutoReverse,RepeatBehavior=t.Loop?RepeatBehavior.Forever:t.RepeatDuration is { } span?new RepeatBehavior(TimeSpan.FromSeconds(span)):new RepeatBehavior(t.RepeatCount),
                    FillBehavior=t.FillBehavior=="Stop"?FillBehavior.Stop:FillBehavior.HoldEnd,IsAdditive=additive,IsCumulative=cumulative};
                foreach(var k in keySet.Keys)
                {
                    var time=KeyTime.FromTimeSpan(TimeSpan.FromSeconds(k.Time));
                    native.KeyFrames.Add(k.Easing=="Discrete"?new DiscreteDoubleKeyFrame(k.Value,time):k.Easing=="EaseInOut"?new EasingDoubleKeyFrame(k.Value,time,new CubicEase{EasingMode=EasingMode.EaseInOut}):new LinearDoubleKeyFrame(k.Value,time));
                }
                var storyboard=new Storyboard{Duration=new Duration(TimeSpan.FromSeconds(timing.Parent)),AutoReverse=timing.Reverse,RepeatBehavior=new RepeatBehavior(timing.Repeat)};
                Storyboard.SetTarget(native,target);Storyboard.SetTargetProperty(native,new PropertyPath(Canvas.LeftProperty));storyboard.Children.Add(native);
                var node=DesignNode.Create("Rectangle","Target",40,0,20,20);var root=DesignNode.Create("Canvas","Root") with{Children=[node]};
                var board=new DesignStoryboard(Guid.NewGuid(),"Reference",timing.Parent,[new(node.Id,"Canvas.Left",keySet.Keys){Timing=t,IsAdditive=additive,IsCumulative=cumulative}]){AutoReverse=timing.Reverse,RepeatCount=timing.Repeat};
                storyboard.Begin(host,true);
                try
                {
                    var times=Enumerable.Range(0,41).Select(i=>i*.25).Concat(new[]{1.9999,2.0001,3.9999,4.0001,5.9999,6.0001}).ToArray();
                    // Repeat in reverse to exercise arbitrary seeks rather than a stateful accumulation shortcut.
                    foreach(var time in times.Concat(times.Reverse()))
                    {
                        storyboard.SeekAlignedToLastTick(host,TimeSpan.FromSeconds(time),TimeSeekOrigin.BeginTime);
                        var expected=Canvas.GetLeft(target);var actual=AnimationEngine.Evaluate(root,board,time).Find(node.Id)!.Number("Canvas.Left");
                        var error=Math.Abs(expected-actual);maximum=Math.Max(maximum,error);samples++;
                        if(!double.IsFinite(actual)||error>tolerance)throw new InvalidOperationException($"At {time:R}: native {expected:R}, portable {actual:R}; error {error:R}");
                    }
                }
                finally{storyboard.Remove(host);}
                passed++;results.Add(new{name,passed=true});
            }
            catch(Exception e){failed++;results.Add(new{name,passed=false,error=e.ToString()});Console.Error.WriteLine("FAIL WPF composition: "+name+": "+e);}
        }
        Directory.CreateDirectory("artifacts/verification");
        File.WriteAllText("artifacts/verification/wpf-composition-results.json",JsonSerializer.Serialize(new{passed,failed,samples,maximum,tolerance,
            description="Native WPF numeric keyframes: additive/cumulative child iterations, reverse, repeat, clipping and seeks. Does not qualify simple DoubleAnimation, arbitrary nested timelines, Blend UI pixels or GPU speed.",results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"WPF composition: {passed} passed; {failed} failed; {samples} samples; maximum error {maximum:R}.");return(passed,failed);
    }
}
