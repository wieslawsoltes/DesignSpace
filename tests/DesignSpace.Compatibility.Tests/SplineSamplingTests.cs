using DesignSpace.Core;
using DesignSpace.Animation;
using DesignSpace.Engine;
namespace DesignSpace.Compatibility.Tests;

internal static class SplineSamplingTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS sampling: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL sampling: "+name+": "+e);}}
        void Check(bool condition){if(!condition)throw new InvalidOperationException("Assertion failed");}
        void Near(double expected,double actual,double tolerance=1e-12){if(!double.IsFinite(actual)||Math.Abs(expected-actual)>tolerance)throw new InvalidOperationException($"Expected {expected:R}, got {actual:R}");}
        void Reject(Action run){try{run();}catch{return;}throw new InvalidOperationException("Expected rejection");}
        var curve=new KeySpline(1,0,0,1);
        var key=new AnimationKey(2,1,"Spline"){Spline=curve};
        var id=Guid.NewGuid();var track=new AnimationTrack(id,"Opacity",[new(0,0),key]){Timing=new(2)};
        Test("exact sampler stays deterministic across out-of-order samples",()=>
        {
            var context=new AnimationSamplingContext();
            for(var i=0;i<=100;i++){var x=i*37%101/100d;Near(curve.Evaluate(x),context.Sample(key,x));}
            Near(.5,context.Sample(key,.5));
        });
        Test("WPF stationary tangent matches recorded Windows reference",()=>
        {
            var sampler=new WpfKeySplineSampler(curve);
            for(var i=0;i<50;i++)sampler.Evaluate(i/100d);
            Near(.49646132294676715,sampler.Evaluate(.5));Near(.5,curve.Evaluate(.5));
        });
        Test("Reset reproduces a new WPF playback sequence",()=>
        {
            var context=new AnimationSamplingContext(SplineSamplingMode.WpfCompatible);var expected=context.Sample(key,.49);
            context.Sample(key,.8);context.Reset();Near(expected,context.Sample(key,.49));
        });
        Test("independent keys never share previous-guess state",()=>
        {
            var context=new AnimationSamplingContext(SplineSamplingMode.WpfCompatible);var other=key with{};
            for(var i=0;i<50;i++)context.Sample(key,i/100d);
            Near(new WpfKeySplineSampler(curve).Evaluate(.5),context.Sample(other,.5));
            Near(.49646132294676715,context.Sample(key,.5));
        });
        Test("independent playbacks do not share cached guesses",()=>
        {
            var a=new AnimationSamplingContext(SplineSamplingMode.WpfCompatible);var b=new AnimationSamplingContext(SplineSamplingMode.WpfCompatible);
            for(var i=0;i<50;i++)a.Sample(key,i/100d);
            Near(new WpfKeySplineSampler(curve).Evaluate(.5),b.Sample(key,.5));
        });
        Test("track context reaches the destination spline",()=>
        {
            var context=new AnimationSamplingContext(SplineSamplingMode.WpfCompatible);var expected=new WpfKeySplineSampler(curve);
            for(var i=1;i<100;i++)Near(expected.Evaluate(i/100d),AnimationEngine.Evaluate(track,i/50d,0,context));
        });
        Test("document and local sampling forward optional context",()=>
        {
            var node=DesignNode.Create("Rectangle","Target") with{Id=id};var root=DesignNode.Create("Canvas","Root") with{Children=[node]};
            var board=new DesignStoryboard(Guid.NewGuid(),"Motion",4,[track]);
            var context=new AnimationSamplingContext(SplineSamplingMode.WpfCompatible);var expected=new WpfKeySplineSampler(curve);
            for(var i=1;i<100;i++)Near(expected.Evaluate(i/100d),AnimationEngine.EvaluateLocal(root,board,i/50d,context:context).Find(id)!.Number("Opacity"));
            context.Reset();var first=new WpfKeySplineSampler(curve).Evaluate(.5);
            Near(first,AnimationEngine.Evaluate(root,board,1,context:context).Find(id)!.Number("Opacity"));
        });
        Test("WPF default curve returns exact linear progress",()=>
        {
            var sampler=new WpfKeySplineSampler(KeySpline.Linear);for(var i=0;i<=100;i++)Near(i/100d,sampler.Evaluate(i/100d));
        });
        Test("invalid modes and samples reject",()=>
        {
            Reject(()=>new AnimationSamplingContext((SplineSamplingMode)99));
            Reject(()=>new WpfKeySplineSampler(curve).Evaluate(double.NaN));
            Reject(()=>new WpfKeySplineSampler(curve).Evaluate(double.PositiveInfinity));
            Reject(()=>new AnimationSamplingContext().Sample(new(1,0),.5));
        });
        Test("warm WPF sampling has bounded allocation",()=>
        {
            var c=new AnimationSamplingContext(SplineSamplingMode.WpfCompatible);
            for(var i=0;i<50;i++)AnimationEngine.Evaluate(track,i/50d,0,c);
            var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)AnimationEngine.Evaluate(track,(i%100)/50d,0,c);
            var allocation=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(allocation<16384);Console.WriteLine("WPF COMPATIBLE WARM sample bytes: "+allocation);
        });
        Test("key collision rejects instead of silently deleting another key",()=>
        {
            var board=new DesignStoryboard(Guid.NewGuid(),"Motion",4,[track]);
            Reject(()=>AnimationEngine.MoveKey(board,id,"Opacity",2,0));Check(board.Tracks[0].Keys.Length==2);
            Reject(()=>AnimationEngine.MoveKey(board,id,"Opacity",2,.000001));
        });
        Test("rejected key collision does not modify session history",()=>
        {
            var node=DesignNode.Create("Rectangle","Target") with{Id=id};var board=new DesignStoryboard(Guid.NewGuid(),"Motion",4,[track]);
            var session=new DesignSession(new(){Root=DesignNode.Create("Canvas","Root") with{Children=[node]},Storyboards=[board]});var previous=session.Document;
            Reject(()=>session.Execute("Move key",d=>d with{Storyboards=[AnimationEngine.MoveKey(board,id,"Opacity",2,0)]}));
            Check(ReferenceEquals(previous,session.Document)&&!session.CanUndo&&session.Revision==0);
        });
        return(passed,failed);
    }
}
