using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Animation;
using DesignSpace.Xaml;

internal static class EasingCurveTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS easing: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL easing: "+name+": "+e);}}
        void Check(bool value){if(!value)throw new Exception("Assertion failed");}
        void Near(double a,double b,double tolerance=1e-10){if(!double.IsFinite(a)||!double.IsFinite(b)||Math.Abs(a-b)>tolerance)throw new Exception($"Expected {a:R}; got {b:R}");}
        void Reject(Action run){try{run();}catch{return;}throw new Exception("Expected rejection");}
        var n=DesignNode.Create("Rectangle","Target",40,0,50,50);
        DesignDocument Document(EasingCurve curve)=>new(){Root=DesignNode.Create("Canvas","Root",0,0,400,300) with{Children=[n]},Storyboards=[new(Guid.NewGuid(),"Motion",2,[new(n.Id,"Canvas.Left",[new(0,40),new(2,140,"Function"){Function=curve}])])]};
        foreach(var family in Enum.GetValues<EasingFamily>())foreach(var mode in Enum.GetValues<EasingDirection>())
        {
            var curve=new EasingCurve(family,mode);
            Test(family+" / "+mode+" finite endpoints and normalized samples",()=>{Near(0,curve.Evaluate(0));Near(1,curve.Evaluate(1));for(var i=0;i<=200;i++)Check(double.IsFinite(curve.Evaluate(i/200d)));});
            Test(family+" / "+mode+" native JSON and XAML round trip",()=>
            {
                var d=Document(curve);var native=NativeDocumentCodec.Read(NativeDocumentCodec.Write(d));
                Check(native.Storyboards[0].Tracks[0].Keys[1].Function==curve);
                var parsed=XamlCodec.Parse(XamlCodec.Write(d)).Document;Check(parsed.Storyboards.Length==1);
                for(var i=0;i<=10;i++)Near(AnimationEngine.Evaluate(d.Root,d.Storyboards[0],i/5d).Find(n.Id)!.Number("Canvas.Left"),AnimationEngine.Evaluate(parsed.Root,parsed.Storyboards[0],i/5d).Children[0].Number("Canvas.Left"));
            });
        }
        foreach(var family in Enum.GetValues<EasingFamily>())Test(family+" reversal and half-interval identities",()=>
        {
            var a=new EasingCurve(family,EasingDirection.EaseIn);var b=a with{Mode=EasingDirection.EaseOut};var c=a with{Mode=EasingDirection.EaseInOut};
            for(var i=0;i<=50;i++){var t=i/100d;Near(1-a.Evaluate(1-t),b.Evaluate(t));Near(a.Evaluate(t*2)*.5,c.Evaluate(t));Near((1-a.Evaluate(t*2))*.5+.5,c.Evaluate(1-t));}
        });
        foreach(var power in new[]{.25,0d,-2,2.5,1000})Test("Power parameter "+power,()=>{var curve=new EasingCurve(EasingFamily.Power,EasingDirection.EaseIn,Power:power);Near(Math.Pow(.4,Math.Max(0,power)),curve.Evaluate(.4));});
        Test("exponent zero is linear",()=>Near(.37,new EasingCurve(EasingFamily.Exponential,Exponent:0).Evaluate(.37)));
        Test("negative exponent stays finite",()=>Near((Math.Exp(-2*.4)-1)/(Math.Exp(-2)-1),new EasingCurve(EasingFamily.Exponential,EasingDirection.EaseIn,Exponent:-2).Evaluate(.4)));
        Test("negative back amplitude is coerced to zero",()=>Near(.125,new EasingCurve(EasingFamily.Back,EasingDirection.EaseIn,Amplitude:-4).Evaluate(.5)));
        Test("back overshoot is not clamped",()=>Check(new EasingCurve(EasingFamily.Back).Evaluate(.7)>1));
        Test("elastic preserves negative and positive overshoot",()=>{var curve=new EasingCurve(EasingFamily.Elastic,EasingDirection.EaseIn);Check(Enumerable.Range(1,100).Any(i=>curve.Evaluate(i/100d)<0));Check(Enumerable.Range(1,100).Any(i=>(curve with{Mode=EasingDirection.EaseOut}).Evaluate(i/100d)>1));});
        Test("degenerate power applies at the start before its first key",()=>{var track=new AnimationTrack(n.Id,"Canvas.Left",[new(2,140,"Function"){Function=new(EasingFamily.Power,EasingDirection.EaseIn,Power:0)}]);Near(140,AnimationEngine.Evaluate(track,0,40));Near(140,AnimationEngine.Evaluate(track,.01,40));});
        Test("explicit key arrival bypasses the next degenerate function",()=>{var t=new AnimationTrack(n.Id,"Canvas.Left",[new(0,40),new(1,80),new(2,140,"Function"){Function=new(EasingFamily.Power,EasingDirection.EaseIn,Power:0)}]);Near(40,AnimationEngine.Evaluate(t,0,40));Near(80,AnimationEngine.Evaluate(t,1,40));Near(140,AnimationEngine.Evaluate(t,1.01,40));});
        Test("terminal key arrival bypasses degenerate EaseOut",()=>{var t=new AnimationTrack(n.Id,"Canvas.Left",[new(2,140,"Function"){Function=new(EasingFamily.Power,EasingDirection.EaseOut,Power:0)}]);Near(40,AnimationEngine.Evaluate(t,1.99,40));Near(140,AnimationEngine.Evaluate(t,2,40));});
        Test("bounce below one uses the WPF coercion",()=>{var a=new EasingCurve(EasingFamily.Bounce,Bounciness:0);var b=a with{Bounciness=1.001};for(var i=0;i<=100;i++)Near(b.Evaluate(i/100d),a.Evaluate(i/100d));});
        Test("maximum bounce budget remains finite",()=>{var c=new EasingCurve(EasingFamily.Bounce,Bounces:32,Bounciness:1000);for(var i=0;i<=100;i++)Check(double.IsFinite(c.Evaluate(i/100d)));});
        foreach(var curve in new[]{new EasingCurve(EasingFamily.Power,Power:double.NaN),new(EasingFamily.Elastic,Oscillations:65),new(EasingFamily.Bounce,Bounces:33),new(EasingFamily.Back,Amplitude:double.PositiveInfinity),new(EasingFamily.Exponential,Exponent:101),new((EasingFamily)999)})
            Test("invalid parameter rejected "+curve,()=>Reject(curve.Validate));
        Test("nonfinite and out-of-range sample times reject",()=>{var c=new EasingCurve(EasingFamily.Cubic);Reject(()=>c.Evaluate(double.NaN));Reject(()=>c.Evaluate(-.1));Reject(()=>c.Evaluate(1.1));});
        Test("function parameters required and exclusive",()=>{Reject(()=>AnimationValidation.ValidateKey(new(1,1,"Function"),2));Reject(()=>AnimationValidation.ValidateKey(new(1,1){Function=new(EasingFamily.Back)},2));Reject(()=>AnimationValidation.ValidateKey(new(1,1,"Function"){Function=new(EasingFamily.Back),Spline=KeySpline.Linear},2));});
        Test("moving and retiming retain curve metadata",()=>{var d=Document(new(EasingFamily.Bounce,Bounces:5));var b=d.Storyboards[0];b=AnimationEngine.MoveKey(b,n.Id,"Canvas.Left",2,1);b=AnimationEngine.ChangeDuration(b,4,true);Check(b.Tracks[0].Keys[^1].Function==d.Storyboards[0].Tracks[0].Keys[^1].Function);Near(2,b.Tracks[0].Keys[^1].Time);});
        Test("value-only replacement preserves custom function",()=>{var d=Document(new(EasingFamily.Elastic,Springiness:6));var b=AnimationEngine.SetKey(d.Storyboards[0],n.Id,"Canvas.Left",2,100,"Function");Check(b.Tracks[0].Keys[^1].Function==d.Storyboards[0].Tracks[0].Keys[^1].Function);});
        Test("switching interpolation clears obsolete metadata",()=>{var d=Document(new(EasingFamily.Bounce));var b=AnimationEngine.SetKey(d.Storyboards[0],n.Id,"Canvas.Left",2,100,"Linear");Check(b.Tracks[0].Keys[^1].Function is null&&b.Tracks[0].Keys[^1].Spline is null);});
        Test("invalid curve cannot partially commit a session",()=>{var s=new DesignSession(Document(new(EasingFamily.Bounce)));var d=s.Document;Reject(()=>s.Execute("bad",_=>Document(new(EasingFamily.Elastic,Springiness:double.NaN))));Check(ReferenceEquals(d,s.Document)&&!s.CanUndo);});
        foreach(var family in Enum.GetValues<EasingFamily>())Test(family+" codec preserves relevant parameters",()=>{var c=new EasingCurve(family,EasingDirection.EaseInOut);c=family switch{EasingFamily.Back=>c with{Amplitude=2.5},EasingFamily.Bounce=>c with{Bounces=5,Bounciness=1.5},EasingFamily.Elastic=>c with{Oscillations=5,Springiness=0},EasingFamily.Exponential=>c with{Exponent=-4},EasingFamily.Power=>c with{Power=.25},_=>c};Check(EasingCurveCodec.TryRead(EasingCurveCodec.Write(c),out var next)&&next==c);});
        var ns=DesignNode.PresentationNamespace;
        string Wrap(string inner)=>$"<Canvas xmlns='{ns}' xmlns:x='{DesignNode.XamlNamespace}'><Canvas.Resources><Storyboard x:Key='B' Duration='0:0:2'>{inner}</Storyboard></Canvas.Resources><Rectangle x:Name='R' Canvas.Left='40'/></Canvas>";
        Test("simple DoubleAnimation easing imports without executing markup",()=>{var text=Wrap("<DoubleAnimation Storyboard.TargetName='R' Storyboard.TargetProperty='(Canvas.Left)' From='40' To='140' Duration='0:0:2'><DoubleAnimation.EasingFunction><BounceEase Bounces='5' Bounciness='2.5'/></DoubleAnimation.EasingFunction></DoubleAnimation>");var d=XamlCodec.Parse(text).Document;Check(d.Storyboards[0].Tracks[0].Keys[^1].Function==new EasingCurve(EasingFamily.Bounce,Bounces:5,Bounciness:2.5));});
        Test("empty EasingDoubleKeyFrame is linear",()=>{var d=XamlCodec.Parse(Wrap("<DoubleAnimationUsingKeyFrames Storyboard.TargetName='R' Storyboard.TargetProperty='(Canvas.Left)'><EasingDoubleKeyFrame KeyTime='0:0:2' Value='140'/></DoubleAnimationUsingKeyFrames>")).Document;Check(d.Storyboards[0].Tracks[0].Keys[^1].Easing=="Linear");});
        foreach(var raw in new[]{"<BounceEase Bounces='NaN'/>","<BounceEase Bounces='3.5'/>","<BounceEase Bounces='999'/>","<BounceEase Bounces='{Binding Count}'/>","<BackEase Unknown='x'/>","<PowerEase EasingMode='0'/>","<CustomEase/>","<CircleEase xmlns='urn:foreign'/>","<CubicEase><Unexpected/></CubicEase>"})
            Test("unsupported easing preserved: "+raw,()=>{var d=XamlCodec.Parse(Wrap("<DoubleAnimationUsingKeyFrames Storyboard.TargetName='R' Storyboard.TargetProperty='(Canvas.Left)'><EasingDoubleKeyFrame KeyTime='0:0:2' Value='140'><EasingDoubleKeyFrame.EasingFunction>"+raw+"</EasingDoubleKeyFrame.EasingFunction></EasingDoubleKeyFrame></DoubleAnimationUsingKeyFrames>")).Document;Check(d.Storyboards.IsEmpty);Check(XamlCodec.Write(d).Contains("EasingDoubleKeyFrame"));});
        Test("warm curve sampling allocates no per-sample objects",()=>{var curves=Enum.GetValues<EasingFamily>().Select(f=>new EasingCurve(f)).ToArray();var sum=0d;foreach(var c in curves)for(var i=0;i<1000;i++)sum+=c.Evaluate(.37);var before=GC.GetAllocatedBytesForCurrentThread();foreach(var c in curves)for(var i=0;i<10000;i++)sum+=c.Evaluate(.37);var bytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(double.IsFinite(sum)&&bytes==0);Console.WriteLine("WARM EASING bytes: "+bytes);});
        return(passed,failed);
    }
}
