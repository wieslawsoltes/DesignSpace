using System.Collections.Immutable;
using DesignSpace.Animation;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Xaml;

internal static class AnimationTimelineTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS animation timeline: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL animation timeline: "+name+": "+e);}}
        void Check(bool value){if(!value)throw new Exception("Assertion failed");}
        void Near(double expected,double actual,double tolerance=1e-7){if(!double.IsFinite(actual)||Math.Abs(expected-actual)>tolerance)throw new Exception($"Expected {expected}, got {actual}");}
        void Reject(Action run){try{run();}catch{return;}throw new Exception("Expected rejection");}
        var n=DesignNode.Create("Rectangle","Target",0,0,40,30).Set("Opacity",.25);
        var root=DesignNode.Create("Canvas","Root",0,0,400,300) with{Children=[n]};
        AnimationTrack Track(TrackTiming? timing=null)=>new(n.Id,"Opacity",[new(0,0),new(2,1)]){Timing=timing};
        DesignStoryboard Board(TrackTiming? timing=null)=>new(Guid.NewGuid(),"Motion",8,[Track(timing)]);
        DesignDocument Doc(DesignStoryboard b)=>new(){Root=root,Storyboards=[b]};
        double Value(DesignStoryboard b,double time)=>AnimationEngine.Evaluate(root,b,time).Find(n.Id)!.Number("Opacity");
        DesignDocument Import(string child,string timing="")=>XamlCodec.Parse($"<Canvas xmlns='{DesignNode.PresentationNamespace}' xmlns:x='{DesignNode.XamlNamespace}' x:Name='Root'><Canvas.Resources><Storyboard x:Key='Motion' {timing}>{child}</Storyboard></Canvas.Resources><Rectangle x:Name='Target' Opacity='0.25'/></Canvas>").Document;
        string Animate(string timing="",string keys="<LinearDoubleKeyFrame KeyTime='0:0:0' Value='0'/><LinearDoubleKeyFrame KeyTime='0:0:2' Value='1'/>")=>$"<DoubleAnimationUsingKeyFrames Storyboard.TargetName='Target' Storyboard.TargetProperty='Opacity' {timing}>{keys}</DoubleAnimationUsingKeyFrames>";
        Test("spline inverts x rather than using progress as parameter",()=>Near(.5,new KeySpline(0,1d/3,0,2d/3).Evaluate(.125)));
        Test("spline endpoints are exact",()=>{var s=new KeySpline(.4,0,.2,1);Near(0,s.Evaluate(0));Near(1,s.Evaluate(1));});
        Test("linear diagonal shortcuts preserve progress",()=>Near(.123,KeySpline.Linear.Evaluate(.123)));
        Test("spline text is culture-invariant and roundtrips",()=>{var s=KeySpline.Parse(".25,.5 .75,1");Check(KeySpline.Parse(s.ToXaml())==s);});
        Test("spline accepts all valid corner control points",()=>{foreach(var x1 in new[]{0d,1d})foreach(var y1 in new[]{0d,1d})foreach(var x2 in new[]{0d,1d})foreach(var y2 in new[]{0d,1d}){var s=new KeySpline(x1,y1,x2,y2);var prior=0d;for(var i=0;i<=100;i++){var v=s.Evaluate(i/100d);Check(v>=prior-1e-9&&v>=0&&v<=1);prior=v;}}});
        Test("spline solve matches independently parameterized curves",()=>{double C(double t,double a,double b)=>3*(1-t)*(1-t)*t*a+3*(1-t)*t*t*b+t*t*t;var s=new KeySpline(.85,.13,.12,.91);for(var i=1;i<100;i++){var t=i/100d;Near(C(t,s.Y1,s.Y2),s.Evaluate(C(t,s.X1,s.X2)));}});
        foreach(var text in new[]{"", "0,0,1", "0,0,1,1,0", "-1,0,1,1", "0,2,1,1", "NaN,0,1,1", "0,0,Infinity,1"})Test("invalid spline rejected: "+text,()=>Reject(()=>KeySpline.Parse(text)));
        Test("nonfinite spline sample rejected",()=>Reject(()=>KeySpline.Linear.Evaluate(double.NaN)));
        Test("first delayed spline key interpolates from supplied base",()=>{var t=new AnimationTrack(n.Id,"Opacity",[new(2,1,"Spline"){Spline=new(0,1d/3,0,2d/3)}]);Near(.6,AnimationEngine.Evaluate(t,.25,.2));});
        Test("the destination key selects segment interpolation",()=>{var t=Track() with{Keys=[new(0,0),new(2,1,"Spline"){Spline=new(0,1d/3,0,2d/3)}]};Near(.5,AnimationEngine.Evaluate(t,.25,0));});
        Test("legacy track uses shared parent interval",()=>Near(.5,Value(Board(),1)));
        Test("child delay retains base without prematurely applying first key",()=>Near(.25,Value(Board(new(2,BeginTime:2)),1)));
        Test("child clock begins at its own first key",()=>Near(0,Value(Board(new(2,BeginTime:2)),2)));
        Test("child speed maps parent time",()=>Near(.5,Value(Board(new(2,BeginTime:1,SpeedRatio:2)),1.5)));
        Test("parent and child speeds multiply with relative delays",()=>{var b=Board(new(2,BeginTime:2,SpeedRatio:2)) with{BeginTime=1,SpeedRatio=2};Near(.5,Value(b,2.25));});
        Test("child HoldEnd retains the final key",()=>Near(1,Value(Board(new(2,BeginTime:1)),6)));
        Test("child Stop removes its contribution",()=>Near(.25,Value(Board(new(2,BeginTime:1,FillBehavior:"Stop")),3)));
        Test("child Stop reveals the state baseline",()=>{var b=Board(new(2,FillBehavior:"Stop"));var s=new DesignState("Dim",[new(n.Id,"Opacity","0.4")]);Near(.4,AnimationEngine.Evaluate(root,b,3,s).Find(n.Id)!.Number("Opacity"));});
        Test("parent Stop overrides child HoldEnd",()=>Near(.25,Value(Board(new(2)) with{FillBehavior="Stop"},8)));
        Test("parent reverses child clock coordinates",()=>{var b=Board(new(2,BeginTime:2)) with{Duration=6,AutoReverse=true};Near(.5,Value(b,9));});
        Test("parent repeat restarts relative child delay",()=>{var b=Board(new(2,BeginTime:1)) with{Duration=4,RepeatCount=2};Near(.25,Value(b,4.5));Near(.5,Value(b,6));});
        Test("child auto reverse samples backward phase",()=>Near(.5,Value(Board(new(2,AutoReverse:true)),3)));
        Test("child reverse finishes at its start",()=>Near(0,Value(Board(new(2,AutoReverse:true)),4)));
        Test("child live repeat wraps and final repeat holds",()=>{var b=Board(new(2,RepeatCount:2));Near(0,Value(b,2));Near(1,Value(b,4));});
        Test("fractional repeats finish inside local interval",()=>Near(.25,Value(Board(new(2,RepeatCount:1.25)),3)));
        Test("duration repeats truncate the final iteration",()=>Near(.5,Value(Board(new(2,RepeatDuration:3)),5)));
        Test("forever child obeys explicit parent clipping",()=>{var b=Board(new(2,Loop:true)) with{Duration=3};Near(.5,Value(b,7));});
        Test("zero repeats never apply even at begin",()=>Near(.25,Value(Board(new(2,RepeatCount:0)),0)));
        Test("skipped child returns unchanged root reference",()=>Check(ReferenceEquals(root,AnimationEngine.Evaluate(root,Board(new(2,BeginTime:4)),1))));
        Test("scrub bypasses parent timing but keeps child delay",()=>{var b=Board(new(2,BeginTime:1)) with{BeginTime=10};Near(.5,AnimationEngine.EvaluateLocal(root,b,2).Find(n.Id)!.Number("Opacity"));Near(.25,Value(b,2));});
        Test("independent tracks retain separate clocks",()=>{var b=Board(new(2,BeginTime:1));b=b with{Tracks=b.Tracks.Add(new(n.Id,"Width",[new(0,40),new(2,80)]){Timing=new(2,BeginTime:3)})};var p=AnimationEngine.Evaluate(root,b,2).Find(n.Id)!;Near(.5,p.Number("Opacity"));Near(40,p.Number("Width"));});
        Test("inferred parent duration includes delay speed reverse and repeats",()=>{var d=Import(Animate("Duration='0:0:2' BeginTime='0:0:1' SpeedRatio='2' AutoReverse='True' RepeatBehavior='3x'"));Near(7,d.Storyboards.Single().Duration);});
        Test("child timing imports as editable data",()=>{var d=Import(Animate("Duration='0:0:2' BeginTime='0:0:1' FillBehavior='Stop'"));Check(d.Storyboards[0].Tracks[0].Timing==new TrackTiming(2,BeginTime:1,FillBehavior:"Stop"));});
        Test("explicit parent clipping does not discard child keys",()=>{var d=Import(Animate("Duration='0:0:2'"),"Duration='0:0:1'");Near(1,d.Storyboards.Single().Duration);Near(2,d.Storyboards[0].Tracks[0].Keys[^1].Time);});
        Test("infinite automatic duration is preserved rather than fabricated",()=>{var d=Import(Animate("RepeatBehavior='Forever'"));Check(d.Storyboards.IsEmpty&&XamlCodec.Write(d).Contains("Forever"));});
        Test("infinite child with finite parent is editable",()=>Check(Import(Animate("RepeatBehavior='Forever'"),"Duration='0:0:4'").Storyboards.Single().Tracks[0].Timing!.Loop));
        Test("unsupported nested clock remains preserved",()=>{var d=Import("<Storyboard>"+Animate()+"</Storyboard>");Check(d.Storyboards.IsEmpty&&XamlCodec.Write(d).Contains("DoubleAnimationUsingKeyFrames"));});
        Test("unsupported child acceleration remains preserved",()=>Check(Import(Animate("AccelerationRatio='0.2'")).Storyboards.IsEmpty));
        Test("custom namespace cannot impersonate known animation",()=>{var text=Animate().Replace("<DoubleAnimationUsingKeyFrames ","<DoubleAnimationUsingKeyFrames xmlns='using:Custom' ");Check(Import(text).Storyboards.IsEmpty);});
        Test("unknown key attributes cannot be silently lost",()=>Check(Import(Animate(keys:"<LinearDoubleKeyFrame KeyTime='0:0:2' Value='1' Custom='keep'/>" )).Storyboards.IsEmpty));
        Test("spline attribute roundtrips",()=>{var d=Import(Animate(keys:"<SplineDoubleKeyFrame KeyTime='0:0:2' Value='1' KeySpline='.25,.5 .75,1'/>"));var t=d.Storyboards.Single().Tracks[0];Check(t.Keys[0].Spline==new KeySpline(.25,.5,.75,1));var round=XamlCodec.Parse(XamlCodec.Write(d)).Document;Check(round.Storyboards[0].Tracks[0].Keys[0]==t.Keys[0]);});
        Test("spline property element imports",()=>{var d=Import(Animate(keys:"<SplineDoubleKeyFrame KeyTime='0:0:2' Value='1'><SplineDoubleKeyFrame.KeySpline><KeySpline ControlPoint1='.2,.4' ControlPoint2='.8,.9'/></SplineDoubleKeyFrame.KeySpline></SplineDoubleKeyFrame>"));Check(d.Storyboards.Single().Tracks[0].Keys[0].Spline==new KeySpline(.2,.4,.8,.9));});
        Test("omitted KeySpline means linear curve",()=>Check(Import(Animate(keys:"<SplineDoubleKeyFrame KeyTime='0:0:2' Value='1'/>")).Storyboards[0].Tracks[0].Keys[0].Spline==KeySpline.Linear));
        Test("invalid spline preserves the complete storyboard",()=>Check(Import(Animate(keys:"<SplineDoubleKeyFrame KeyTime='0:0:2' Value='1' KeySpline='0,0,1,2'/>")).Storyboards.IsEmpty));
        Test("fixed-duration percent keys resolve to local time",()=>{var d=Import(Animate("Duration='0:0:4'", "<LinearDoubleKeyFrame KeyTime='50%' Value='1'/>"));Near(2,d.Storyboards[0].Tracks[0].Keys[0].Time);});
        Test("percent with Automatic duration remains preserved",()=>Check(Import(Animate(keys:"<LinearDoubleKeyFrame KeyTime='50%' Value='1'/>")).Storyboards.IsEmpty));
        Test("native timing and spline survive source-generated serialization",()=>{var b=Board(new(2,1,2,true,3,null,false,"Stop"));b=b with{Tracks=[b.Tracks[0] with{Keys=[new(2,.123456789,"Spline"){Spline=new(.2,.3,.8,.9)}]}]};var d=NativeDocumentCodec.Read(NativeDocumentCodec.Write(Doc(b)));Check(d.Storyboards[0].Tracks[0].Timing==b.Tracks[0].Timing&&d.Storyboards[0].Tracks[0].Keys[0]==b.Tracks[0].Keys[0]);});
        Test("old native tracks without new fields still read",()=>{var json=NativeDocumentCodec.Write(Doc(Board()));Check(!json.Contains("\"timing\""));Check(NativeDocumentCodec.Read(json).Storyboards[0].Tracks[0].Timing is null);});
        Test("XAML value precision is retained",()=>{var d=Doc(Board() with{Tracks=[Track() with{Keys=[new(1,.123456789123)]}]});Near(.123456789123,XamlCodec.Parse(XamlCodec.Write(d)).Document.Storyboards[0].Tracks[0].Keys[0].Value,1e-13);});
        Test("spline without coordinates is rejected atomically",()=>Reject(()=>DocumentValidator.Validate(Doc(Board() with{Tracks=[Track() with{Keys=[new(1,1,"Spline")]}]}))));
        Test("stray spline on linear key is rejected",()=>Reject(()=>DocumentValidator.Validate(Doc(Board() with{Tracks=[Track() with{Keys=[new(1,1){Spline=KeySpline.Linear}]}]}))));
        Test("key beyond child duration is rejected",()=>Reject(()=>DocumentValidator.Validate(Doc(Board(new(1))))));
        foreach(var t in new TrackTiming[]{new(-1),new(2,SpeedRatio:0),new(2,BeginTime:-1),new(2,RepeatCount:-1),new(2,RepeatDuration:double.NaN),new(2,FillBehavior:"Bad"),new(2,RepeatDuration:3,Loop:true)})Test("invalid clock "+t,()=>Reject(()=>t.Validate()));
        Test("moving only key retains timing and spline",()=>{var t=Track(new(2,BeginTime:1)) with{Keys=[new(1,.5,"Spline"){Spline=new(.2,.4,.6,.8)}]};var b=Board() with{Tracks=[t]};var moved=AnimationEngine.MoveKey(b,n.Id,"Opacity",1,1.5).Tracks[0];Check(moved.Timing==t.Timing&&moved.Keys[0].Spline==t.Keys[0].Spline);Near(1.5,moved.Keys[0].Time);});
        Test("same key move is a no-op",()=>{var b=Board();Check(ReferenceEquals(b,AnimationEngine.MoveKey(b,n.Id,"Opacity",2,2)));});
        Test("track duration scaling preserves spline metadata",()=>{var b=Board() with{Tracks=[Track() with{Keys=[new(2,1,"Spline"){Spline=KeySpline.Linear}]}]};var next=AnimationEngine.ChangeTrackTiming(b,n.Id,"Opacity",new(4),true);Near(1,next.Tracks[0].Keys[0].Time);Check(next.Tracks[0].Keys[0].Spline==KeySpline.Linear);});
        Test("shortening track rejects excluded keys",()=>Reject(()=>AnimationEngine.ChangeTrackTiming(Board(),n.Id,"Opacity",new(1))));
        Test("same track timing keeps board reference",()=>{var b=Board(new(2));Check(ReferenceEquals(b,AnimationEngine.ChangeTrackTiming(b,n.Id,"Opacity",new(2))));});
        Test("scaling parent retimes child delay and key interval together",()=>{var b=Board(new(2,BeginTime:1,RepeatDuration:3));var next=AnimationEngine.ChangeDuration(b,16,true);Near(4,next.Tracks[0].Timing!.Duration);Near(2,next.Tracks[0].Timing!.BeginTime);Near(6,next.Tracks[0].Timing!.RepeatDuration!.Value);Near(4,next.Tracks[0].Keys[^1].Time);});
        Test("shorter parent clips explicit child without destroying keys",()=>{var b=AnimationEngine.ChangeDuration(Board(new(2)),1,false);Near(2,b.Tracks[0].Keys[^1].Time);Near(.5,Value(b,3));});
        Test("key editing maps between parent and first child cycle",()=>{var t=Track(new(2,BeginTime:1,SpeedRatio:2));Near(1.5,AnimationEngine.KeyToParentTime(t,1));Near(1,AnimationEngine.ParentToKeyTime(t,1.5,8));});
        Test("session undo restores timing transaction",()=>{var s=new DesignSession(Doc(Board()));var before=s.Document;s.Execute("Timing",d=>d with{Storyboards=[AnimationEngine.ChangeTrackTiming(d.Storyboards[0],n.Id,"Opacity",new(2,BeginTime:1))]});Check(s.Revision==1);s.Undo();Check(ReferenceEquals(before,s.Document));});
        Test("warm spline and clock sampling avoid per-frame allocation",()=>{var t=Track(new(2,BeginTime:1)) with{Keys=[new(2,1,"Spline"){Spline=new(.42,0,.58,1)}]};for(var i=0;i<30;i++){_ =AnimationEngine.Evaluate(t,1,0);_ =StoryboardClock.Sample(t.Timing!,2);}var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++){_ =AnimationEngine.Evaluate(t,1,0);_ =StoryboardClock.Sample(t.Timing!,2);}var bytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(bytes<16384);Console.WriteLine("ANIMATION WARM sampled bytes: "+bytes);});
        Test("root preview retains sub-mill precision from native cubic-easing comparison",()=>{var b=Board() with{Tracks=[new(n.Id,"Width",[new(0,0),new(2,100,"EaseInOut")]){Timing=new(2)}]};Near(.78125,AnimationEngine.Evaluate(root,b,.25).Find(n.Id)!.Number("Width"),1e-12);});
        Test("root preview does not quantize tiny interpolated values",()=>{var b=Board() with{Tracks=[new(n.Id,"Opacity",[new(0,0),new(2,.000002)]){Timing=new(2)}]};Near(.00000025,Value(b,.25),1e-16);});
        Test("completed fast finite child holds without elapsed-time overflow",()=>Near(1,Value(Board(new(2,SpeedRatio:1e308)),3)));
        Test("completed fast stopped child restores base without overflow",()=>Near(.25,Value(Board(new(2,SpeedRatio:1e308,FillBehavior:"Stop")),3)));
        Test("forever overflow still rejects unrepresentable samples",()=>Reject(()=>StoryboardClock.Sample(new TrackTiming(2,SpeedRatio:1e308,Loop:true),3)));
        Test("legacy track rejects nonfinite parent sample",()=>Reject(()=>StoryboardClock.Sample(Track(),double.NaN,8)));
        return(passed,failed);
    }
}
