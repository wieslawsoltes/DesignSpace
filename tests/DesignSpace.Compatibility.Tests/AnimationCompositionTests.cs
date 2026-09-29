using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Animation;
using DesignSpace.Xaml;

internal static class AnimationCompositionTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS composition: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL composition: "+name+": "+e);}}
        void Check(bool value){if(!value)throw new Exception("Assertion failed");}
        void Near(double expected,double actual){if(!double.IsFinite(actual)||Math.Abs(expected-actual)>1e-8)throw new Exception($"Expected {expected:R}; got {actual:R}");}
        void Reject(Action run){try{run();}catch{return;}throw new Exception("Expected rejection");}
        var node=DesignNode.Create("Rectangle","Target",40,0,20,20);
        var root=DesignNode.Create("Canvas","Root") with{Children=[node]};
        AnimationTrack Track(params AnimationKey[] keys)=>new(node.Id,"Canvas.Left",keys.ToImmutableArray());
        DesignStoryboard Board(AnimationTrack track,double duration=12)=>new(Guid.NewGuid(),"Composition",duration,[track]);
        double Value(DesignStoryboard board,double time,DesignState? state=null)=>AnimationEngine.Evaluate(root,board,time,state).Find(node.Id)!.Number("Canvas.Left");
        var simple=Track(new AnimationKey(0,10),new(2,30));
        foreach(var additive in new[]{false,true})foreach(var cumulative in new[]{false,true})
        {
            var track=simple with{IsAdditive=additive,IsCumulative=cumulative,Timing=new(2,RepeatCount:3)};
            var board=Board(track);
            foreach(var (time,local,iteration) in new[]{(0d,0d,1d),(1,1,1),(2,0,2),(3,1,2),(4,0,3),(5,1,3),(6,2,3),(10,2,3)})
                Test($"flags {additive}/{cumulative} at {time}",()=>Near(10+local*10+(additive?40:0)+(cumulative?(iteration-1)*30:0),Value(board,time)));
        }
        Test("first delayed additive key interpolates from zero",()=>Near(50,AnimationEngine.Evaluate(Track(new AnimationKey(2,20)) with{IsAdditive=true},1,40)));
        Test("first delayed absolute key interpolates from base",()=>Near(30,AnimationEngine.Evaluate(Track(new AnimationKey(2,20)),1,40)));
        Test("additive exact zero-time key offsets base",()=>Near(50,AnimationEngine.Evaluate(simple with{IsAdditive=true},0,40)));
        Test("empty additive cumulative track leaves base",()=>Near(40,AnimationEngine.EvaluateIteration(Track() with{IsAdditive=true,IsCumulative=true},1,40,4)));
        Test("cumulative uses final value not endpoint delta",()=>Near(50,AnimationEngine.EvaluateIteration(simple with{IsCumulative=true},1,40,2)));
        Test("cumulative zero final key contributes nothing",()=>Near(5,AnimationEngine.EvaluateIteration(Track(new AnimationKey(0,10),new(2,0)) with{IsCumulative=true},1,40,10)));
        Test("negative final key accumulates signed offsets",()=>Near(-25,AnimationEngine.EvaluateIteration(Track(new AnimationKey(0,10),new(2,-20)) with{IsCumulative=true},1,40,2)));
        Test("unsorted keys use time order not storage order",()=>Near(50,AnimationEngine.EvaluateIteration(Track(new AnimationKey(2,30),new(0,10)) with{IsCumulative=true},1,40,2)));
        Test("state value is additive base, document root unchanged",()=>{var before=root;var state=new DesignState("Active",[new(node.Id,"Canvas.Left","100")]);Near(120,Value(Board(simple with{IsAdditive=true}),1,state));Near(40,before.Find(node.Id)!.Number("Canvas.Left"));});
        Test("child delay leaves base before additive start",()=>{var b=Board(simple with{IsAdditive=true,Timing=new(2,BeginTime:1)});Near(40,Value(b,.5));Near(50,Value(b,1));});
        Test("Stop removes additive and cumulative contributions",()=>Near(40,Value(Board(simple with{IsAdditive=true,IsCumulative=true,Timing=new(2,RepeatCount:2,FillBehavior:"Stop")}),4)));
        Test("reverse half stays in same cumulative iteration",()=>{var b=Board(simple with{IsCumulative=true,Timing=new(2,AutoReverse:true,RepeatCount:2)});Near(20,Value(b,3));Near(40,Value(b,4));Near(50,Value(b,5));Near(40,Value(b,8));});
        Test("fractional repeat holds its partial final iteration",()=>Near(45,Value(Board(simple with{IsCumulative=true,Timing=new(2,RepeatCount:1.25)}),8)));
        Test("duration repeat uses completed cycles",()=>Near(50,Value(Board(simple with{IsCumulative=true,Timing=new(2,RepeatDuration:3)}),8)));
        Test("speed and delay do not change keyframe units",()=>Near(90,Value(Board(simple with{IsAdditive=true,IsCumulative=true,Timing=new(2,BeginTime:1,SpeedRatio:2,RepeatCount:3)}),2.5)));
        Test("parent repeat restarts child accumulation",()=>{var b=Board(simple with{IsCumulative=true,Timing=new(2,RepeatCount:2)},4) with{RepeatCount=2};Near(50,Value(b,3));Near(10,Value(b,4));Near(20,Value(b,5));Near(60,Value(b,8));});
        Test("parent reverse samples child accumulation backwards",()=>{var b=Board(simple with{IsCumulative=true,Timing=new(2,RepeatCount:2)},4) with{AutoReverse=true};Near(60,Value(b,4));Near(50,Value(b,5));Near(10,Value(b,8));});
        Test("parent clipping holds the sampled child iteration",()=>Near(45,Value(Board(simple with{IsCumulative=true,Timing=new(2,Loop:true)},2.5),10)));
        Test("legacy shared interval repeats parent without accumulation",()=>{var b=Board(simple with{IsCumulative=true},2) with{RepeatCount=3};Near(20,Value(b,3));Near(30,Value(b,6));});
        Test("explicit local scrubbing remains repeat aware",()=>Near(50,AnimationEngine.EvaluateLocal(root,Board(simple with{IsCumulative=true,Timing=new(2,RepeatCount:3)}),3).Find(node.Id)!.Number("Canvas.Left")));
        Test("clock reports live and completed iterations",()=>{var t=new TrackTiming(2,RepeatCount:3);Near(2,StoryboardClock.Sample(t,2).CurrentIteration);Near(3,StoryboardClock.Sample(t,6).CurrentIteration);Near(3,StoryboardClock.Sample(t,10).CurrentIteration);});
        Test("one reverse cycle counts once",()=>{var t=new TrackTiming(2,AutoReverse:true,RepeatCount:2);Near(1,StoryboardClock.Sample(t,3).CurrentIteration);Near(2,StoryboardClock.Sample(t,4).CurrentIteration);Near(2,StoryboardClock.Sample(t,8).CurrentIteration);});
        foreach(var iteration in new[]{0d,-1,.5,double.NaN,double.PositiveInfinity,9007199254740992d})Test("reject invalid iteration "+iteration,()=>Reject(()=>AnimationEngine.EvaluateIteration(simple,1,40,iteration)));
        Test("composition overflow is explicit",()=>Reject(()=>AnimationEngine.EvaluateIteration(Track(new AnimationKey(0,double.MaxValue)) with{IsAdditive=true},0,double.MaxValue,1)));
        Test("accumulation overflow is explicit",()=>Reject(()=>AnimationEngine.EvaluateIteration(Track(new AnimationKey(0,double.MaxValue)) with{IsCumulative=true},0,0,3)));
        Test("flags are a no-op when already set",()=>{var b=Board(simple);Check(ReferenceEquals(b,AnimationEngine.ChangeTrackComposition(b,node.Id,"Canvas.Left",false,false)));});
        Test("composition edit is one reversible transaction",()=>{var d=new DesignDocument{Root=root,Storyboards=[Board(simple)]};var session=new DesignSession(d);session.Execute("Composition",v=>v with{Storyboards=[AnimationEngine.ChangeTrackComposition(v.Storyboards[0],node.Id,"Canvas.Left",true,true)]});Check(session.Revision==1&&session.Document.Storyboards[0].Tracks[0].IsCumulative);session.Undo();Check(ReferenceEquals(d,session.Document));session.Redo();Check(session.Document.Storyboards[0].Tracks[0].IsAdditive);});
        Test("key edits preserve composition metadata",()=>{var b=Board(simple with{IsAdditive=true,IsCumulative=true});b=AnimationEngine.SetKey(b,node.Id,"Canvas.Left",1,20,"Function",function:new(EasingFamily.Sine));b=AnimationEngine.MoveKey(b,node.Id,"Canvas.Left",1,1.5);b=AnimationEngine.ChangeTrackTiming(b,node.Id,"Canvas.Left",new(3));Check(b.Tracks[0].IsAdditive&&b.Tracks[0].IsCumulative);});
        Test("composition edit retains key and clock references",()=>{var t=simple with{Timing=new(2,RepeatCount:2)};var b=AnimationEngine.ChangeTrackComposition(Board(t),node.Id,"Canvas.Left",true,true);Check(ReferenceEquals(t.Timing,b.Tracks[0].Timing)&&t.Keys==b.Tracks[0].Keys);});
        foreach(var flags in new[]{(false,false),(true,false),(false,true),(true,true)})Test("native and XAML flags roundtrip "+flags,()=>
        {
            var d=new DesignDocument{Root=root,Storyboards=[Board(simple with{IsAdditive=flags.Item1,IsCumulative=flags.Item2,Timing=new(2,RepeatCount:3)})]};
            foreach(var next in new[]{NativeDocumentCodec.Read(NativeDocumentCodec.Write(d)),XamlCodec.Reconcile(d,XamlCodec.Parse(XamlCodec.Write(d)).Document)})
            {var t=next.Storyboards[0].Tracks[0];Check(t.IsAdditive==flags.Item1&&t.IsCumulative==flags.Item2);Near(Value(d.Storyboards[0],3),Value(next.Storyboards[0],3));}
        });
        Test("old native JSON has false defaults without new fields",()=>{var d=new DesignDocument{Root=root,Storyboards=[Board(simple)]};var json=NativeDocumentCodec.Write(d);Check(!json.Contains("isAdditive")&&!json.Contains("isCumulative"));var t=NativeDocumentCodec.Read(json).Storyboards[0].Tracks[0];Check(!t.IsAdditive&&!t.IsCumulative);});
        string Xml(string animation)=>$"<Canvas xmlns='{DesignNode.PresentationNamespace}' xmlns:x='{DesignNode.XamlNamespace}' x:Name='Root'><Canvas.Resources><Storyboard x:Key='B' Duration='0:0:8'>{animation}</Storyboard></Canvas.Resources><Rectangle x:Name='Target'/></Canvas>";
        foreach(var attribute in new[]{"IsAdditive='1'","IsCumulative='invalid'","IsAdditive='{Binding Enabled}'"})Test("unsupported flag preserved "+attribute,()=>{var d=XamlCodec.Parse(Xml($"<DoubleAnimationUsingKeyFrames {attribute} Storyboard.TargetName='Target' Storyboard.TargetProperty='(Canvas.Left)'><LinearDoubleKeyFrame KeyTime='0:0:1' Value='20'/></DoubleAnimationUsingKeyFrames>")).Document;Check(d.Storyboards.IsEmpty&&XamlCodec.Write(d).Contains("DoubleAnimationUsingKeyFrames"));});
        Test("simple cumulative animations stay inert rather than acquiring keyframe semantics",()=>{var d=XamlCodec.Parse(Xml("<DoubleAnimation IsCumulative='True' From='10' To='30' Storyboard.TargetName='Target' Storyboard.TargetProperty='(Canvas.Left)' Duration='0:0:2'/>" )).Document;Check(d.Storyboards.IsEmpty&&XamlCodec.Write(d).Contains("IsCumulative=\"True\""));});
        Test("warmed scalar composition does not allocate per sample",()=>{var t=simple with{IsAdditive=true,IsCumulative=true};for(var i=0;i<10;i++)AnimationEngine.EvaluateIteration(t,1,40,2);var bytes=GC.GetAllocatedBytesForCurrentThread();var sum=0d;for(var i=0;i<10000;i++)sum+=AnimationEngine.EvaluateIteration(t,1,40,2);Check(GC.GetAllocatedBytesForCurrentThread()-bytes==0);Near(900000,sum);});
        return(passed,failed);
    }
}
