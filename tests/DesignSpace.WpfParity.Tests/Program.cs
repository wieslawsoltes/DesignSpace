using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DesignSpace.Core;
using DesignSpace.Animation;
using ReferenceSpline=System.Windows.Media.Animation.KeySpline;
using DesignSpline=DesignSpace.Core.KeySpline;

internal static class Program
{
    // Native WPF runs on a real STA Windows thread, independently of the Uno implementation.
    [STAThread]
    private static int Main()
    {
        var passed=0;var failed=0;var splineSamples=0;var clockSamples=0;
        var results=new List<object>();
        void Check(string name,Action run)
        {
            try{run();passed++;results.Add(new{name,passed=true});Console.WriteLine("PASS WPF: "+name);}
            catch(Exception e){failed++;results.Add(new{name,passed=false,error=e.ToString()});Console.Error.WriteLine("FAIL WPF: "+name+": "+e);}
        }
        void Near(double expected,double actual,double tolerance)
        {
            if(!double.IsFinite(actual)||Math.Abs(expected-actual)>tolerance)throw new InvalidOperationException($"WPF {expected:R}; DesignSpace {actual:R}; tolerance {tolerance:R}");
        }
        var curves=new DesignSpline[]{new(0,0,1,1),new(.42,0,.58,1),new(.8,.1,.1,.9),new(0,1d/3,0,2d/3),new(1,0,0,1)};
        foreach(var curve in curves)Check("spline "+curve.ToXaml(),()=>
        {
            var reference=new ReferenceSpline(curve.X1,curve.Y1,curve.X2,curve.Y2);
            for(var i=0;i<=100;i++){var progress=i/100d;Near(reference.GetSplineProgress(progress),curve.Evaluate(progress),.0003);splineSamples++;}
        });
        var node=DesignNode.Create("Rectangle","Target",0,0,25,20);
        var root=DesignNode.Create("Canvas","Root",0,0,400,200) with{Children=[node]};
        DesignStoryboard Board(TrackTiming timing,ImmutableArray<AnimationKey>? keys=null)=>new(Guid.NewGuid(),"NativeReference",6,
            [new(node.Id,"Width",keys??[new(0,0),new(2,100)]){Timing=timing}]);
        var scenarios=new (string Name,DesignStoryboard Board)[]
        {
            ("delay",Board(new(2,BeginTime:1))),
            ("speed",Board(new(2,BeginTime:1,SpeedRatio:2))),
            ("reverse",Board(new(2,AutoReverse:true))),
            ("repeat",Board(new(2,RepeatCount:2))),
            ("fractional repeat",Board(new(2,RepeatCount:1.25))),
            ("duration repeat",Board(new(2,RepeatDuration:3))),
            ("child Stop",Board(new(2,FillBehavior:"Stop"))),
            ("parent Stop",Board(new(2)) with{FillBehavior="Stop"}),
            ("clipped child",Board(new(2)) with{Duration=1}),
            ("clipped forever child",Board(new(2,Loop:true)) with{Duration=3}),
            ("parent reverse",Board(new(2,BeginTime:1)) with{Duration=4,AutoReverse=true}),
            ("parent repeat",Board(new(2,BeginTime:1)) with{Duration=4,RepeatCount=2}),
            ("delayed implicit baseline",Board(new(2),[new(2,100)])),
            ("spline keys",Board(new(2,BeginTime:1),[new(0,0),new(2,100,"Spline"){Spline=new(.42,0,.58,1)}])),
            ("discrete keys",Board(new(2),[new(0,0),new(2,100,"Discrete")])),
            ("cubic ease keys",Board(new(2),[new(0,0),new(2,100,"EaseInOut")]))
        };
        foreach(var (name,board) in scenarios)Check(name,()=>
        {
            var target=new Rectangle{Width=25,Height=20};var host=new Canvas();host.Children.Add(target);
            var reference=new Storyboard{Duration=new Duration(TimeSpan.FromSeconds(board.Duration)),AutoReverse=board.AutoReverse,
                RepeatBehavior=new RepeatBehavior(board.RepeatCount),FillBehavior=board.FillBehavior=="Stop"?FillBehavior.Stop:FillBehavior.HoldEnd};
            var track=board.Tracks[0];var t=track.Timing!;
            var animation=new DoubleAnimationUsingKeyFrames{Duration=new Duration(TimeSpan.FromSeconds(t.Duration)),BeginTime=TimeSpan.FromSeconds(t.BeginTime),SpeedRatio=t.SpeedRatio,
                AutoReverse=t.AutoReverse,RepeatBehavior=t.Loop?RepeatBehavior.Forever:t.RepeatDuration is { } span?new RepeatBehavior(TimeSpan.FromSeconds(span)):new RepeatBehavior(t.RepeatCount),
                FillBehavior=t.FillBehavior=="Stop"?FillBehavior.Stop:FillBehavior.HoldEnd};
            foreach(var key in track.Keys)
            {
                var time=KeyTime.FromTimeSpan(TimeSpan.FromSeconds(key.Time));
                DoubleKeyFrame frame=key.Easing switch
                {
                    "Spline"=>new SplineDoubleKeyFrame(key.Value,time,new ReferenceSpline(key.Spline!.X1,key.Spline.Y1,key.Spline.X2,key.Spline.Y2)),
                    "Discrete"=>new DiscreteDoubleKeyFrame(key.Value,time),
                    "EaseIn" or "EaseOut" or "EaseInOut"=>new EasingDoubleKeyFrame(key.Value,time,new CubicEase{EasingMode=Enum.Parse<EasingMode>(key.Easing)}),
                    _=>new LinearDoubleKeyFrame(key.Value,time)
                };
                animation.KeyFrames.Add(frame);
            }
            Storyboard.SetTarget(animation,target);Storyboard.SetTargetProperty(animation,new PropertyPath(FrameworkElement.WidthProperty));reference.Children.Add(animation);
            reference.Begin(host,true);
            try
            {
                foreach(var time in new[]{0d,.25,.5,.75,1,1.25,1.5,1.75,2,2.25,2.5,3,3.5,4,4.5,5,6,7,8,9})
                {
                    reference.SeekAlignedToLastTick(host,TimeSpan.FromSeconds(time),TimeSeekOrigin.BeginTime);
                    var actual=AnimationEngine.Evaluate(root,board,time).Find(node.Id)!.Number("Width");
                    try{Near(target.Width,actual,track.Keys.Any(k=>k.Spline is not null)?.03:.000001);}
                    catch(Exception e){throw new InvalidOperationException($"Sample at parent time {time:R}: "+e.Message,e);}
                    clockSamples++;
                }
            }
            finally{reference.Remove(host);}
        });
        Directory.CreateDirectory("artifacts/verification");
        File.WriteAllText("artifacts/verification/wpf-animation-results.json",JsonSerializer.Serialize(new
        {
            passed,failed,splineSamples,clockSamples,
            description="Native Windows WPF numerical reference, not Blend UI/pixel or physical-GPU qualification. Root seek is in parent time; root delay/speed are covered separately by portable tests.",
            splineTolerance=.0003,animatedSplineTolerance=.03,linearTolerance=.000001,results
        },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"WPF: {passed} cases passed, {failed} failed; {splineSamples} spline + {clockSamples} clock samples.");return failed==0?0:1;
    }
}
