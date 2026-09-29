using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DesignSpace.Core;
using DesignSpace.Animation;

internal static class EasingReferenceTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;var samples=0;var animatedSamples=0;var maximum=0d;var animatedMaximum=0d;var results=new List<object>();
        const double tolerance=1e-12;const double animatedTolerance=1e-9;
        void Check(string name,Action test){try{test();passed++;results.Add(new{name,passed=true});}catch(Exception e){failed++;results.Add(new{name,passed=false,error=e.ToString()});Console.Error.WriteLine("FAIL WPF easing: "+name+": "+e);}}
        EasingFunctionBase Native(EasingCurve c)
        {
            EasingFunctionBase f=c.Family switch
            {
                EasingFamily.Back=>new BackEase{Amplitude=c.Amplitude},EasingFamily.Bounce=>new BounceEase{Bounces=c.Bounces,Bounciness=c.Bounciness},
                EasingFamily.Circle=>new CircleEase(),EasingFamily.Cubic=>new CubicEase(),EasingFamily.Elastic=>new ElasticEase{Oscillations=c.Oscillations,Springiness=c.Springiness},
                EasingFamily.Exponential=>new ExponentialEase{Exponent=c.Exponent},EasingFamily.Power=>new PowerEase{Power=c.Power},
                EasingFamily.Quadratic=>new QuadraticEase(),EasingFamily.Quartic=>new QuarticEase(),EasingFamily.Quintic=>new QuinticEase(),_=>new SineEase()
            };
            f.EasingMode=Enum.Parse<EasingMode>(c.Mode.ToString());return f;
        }
        var presets=Enum.GetValues<EasingFamily>().Select(f=>new EasingCurve(f)).Concat(new EasingCurve[]
        {
            new(EasingFamily.Back,Amplitude:0),new(EasingFamily.Back,Amplitude:-3),new(EasingFamily.Back,Amplitude:4),
            new(EasingFamily.Bounce,Bounces:0),new(EasingFamily.Bounce,Bounces:-2),new(EasingFamily.Bounce,Bounces:7,Bounciness:1),new(EasingFamily.Bounce,Bounces:5,Bounciness:.2),new(EasingFamily.Bounce,Bounces:5,Bounciness:1.5),
            new(EasingFamily.Elastic,Oscillations:0,Springiness:0),new(EasingFamily.Elastic,Oscillations:5,Springiness:5),new(EasingFamily.Elastic,Oscillations:-2,Springiness:-1),
            new(EasingFamily.Exponential,Exponent:0),new(EasingFamily.Exponential,Exponent:-4),new(EasingFamily.Exponential,Exponent:9),
            new(EasingFamily.Power,Power:0),new(EasingFamily.Power,Power:-1),new(EasingFamily.Power,Power:.3),new(EasingFamily.Power,Power:5.5)
        }).ToArray();
        foreach(var preset in presets)foreach(var mode in Enum.GetValues<EasingDirection>())
        {
            var c=preset with{Mode=mode};
            Check(c.ToString(),()=>
            {
                var native=Native(c);
                for(var i=0;i<=200;i++)
                {
                    var progress=i/200d;var expected=native.Ease(progress);var actual=c.Evaluate(progress);var error=Math.Abs(expected-actual);maximum=Math.Max(maximum,error);
                    if(!double.IsFinite(actual)||error>tolerance)throw new InvalidOperationException($"At {progress:R}: native {expected:R}, actual {actual:R}");samples++;
                }
            });
            Check("animated "+c,()=>
            {
                var target=new Rectangle{Width=20,Height=20};Canvas.SetLeft(target,40);var host=new Canvas();host.Children.Add(target);
                var native=new DoubleAnimationUsingKeyFrames{Duration=new Duration(TimeSpan.FromSeconds(2)),AutoReverse=true,BeginTime=TimeSpan.FromSeconds(.25)};
                native.KeyFrames.Add(new EasingDoubleKeyFrame(140,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2)),Native(c)));
                var storyboard=new Storyboard{Duration=new Duration(TimeSpan.FromSeconds(5))};Storyboard.SetTarget(native,target);Storyboard.SetTargetProperty(native,new PropertyPath(Canvas.LeftProperty));storyboard.Children.Add(native);
                var node=DesignNode.Create("Rectangle","Target",40,0,20,20);var root=DesignNode.Create("Canvas","Root") with{Children=[node]};
                var board=new DesignStoryboard(Guid.NewGuid(),"Test",5,[new(node.Id,"Canvas.Left",[new(2,140,"Function"){Function=c}]){Timing=new(2,BeginTime:.25,AutoReverse:true)}]);
                storyboard.Begin(host,true);
                try
                {
                    foreach(var time in new[]{0d,.1,.25,.25001,.5,.75,1,1.25,1.5,1.75,2,2.25,2.5,3,3.5,4,4.25,4.5,5,6})
                    {
                        storyboard.SeekAlignedToLastTick(host,TimeSpan.FromSeconds(time),TimeSeekOrigin.BeginTime);
                        var expected=Canvas.GetLeft(target);var actual=AnimationEngine.Evaluate(root,board,time).Find(node.Id)!.Number("Canvas.Left");
                        var error=Math.Abs(expected-actual);animatedMaximum=Math.Max(animatedMaximum,error);
                        if(!double.IsFinite(actual)||error>animatedTolerance)throw new InvalidOperationException($"At {time:R}: native {expected:R}, actual {actual:R}");animatedSamples++;
                    }
                }
                finally{storyboard.Remove(host);}
            });
        }
        Directory.CreateDirectory("artifacts/verification");
        File.WriteAllText("artifacts/verification/wpf-easing-results.json",JsonSerializer.Serialize(new{passed,failed,samples,animatedSamples,maximum,animatedMaximum,tolerance,animatedTolerance,
            description="Native WPF scalar easing and numeric keyframe/child-clock comparisons. Not Blend UI/pixel or GPU qualification.",results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"WPF easing: {passed} cases passed, {failed} failed; {samples} scalar + {animatedSamples} animated samples.");return(passed,failed);
    }
}
