using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
using DesignSpace.Animation;
using DesignSpace.Engine;
using DesignSpace.Xaml;

var passed=0;var failed=0;
void Test(string name,Action action) { try { action();passed++;Console.WriteLine("PASS "+name); } catch(Exception ex) { failed++;Console.Error.WriteLine("FAIL "+name+": "+ex); } }
void Check(bool condition) { if(!condition) throw new Exception("Assertion failed"); }
void Equal<T>(T expected,T actual) { if(!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}"); }
void Reject(Action action) { try { action(); } catch(Exception) { return; } throw new Exception("Expected rejection"); }
var ns=DesignNode.PresentationNamespace;var x=DesignNode.XamlNamespace;
string Wrap(string content,string attributes="")=>$"<Canvas xmlns='{ns}' xmlns:x='{x}' x:Name='Root' Width='960' Height='640' {attributes}>{content}</Canvas>";
DesignDocument Sample()=>SampleDocument.Create();
DesignDocument RoundTrip(DesignDocument d)=>XamlCodec.Parse(XamlCodec.Write(d)).Document;
Test("native immutable JSON without reflection",()=>{var d=Sample();var text=NativeDocumentCodec.Write(d);var p=NativeDocumentCodec.Read(text);Equal(d.Root.Id,p.Root.Id);Equal(d.Root.Children.Length,p.Root.Children.Length);Equal(d.Storyboards[0].Tracks[0].TargetId,p.Storyboards[0].Tracks[0].TargetId);Equal(text,NativeDocumentCodec.Write(p));});
Test("native preserves locks",()=>{var d=Sample();d=d with { Root=d.Root with { IsLocked=true } };Check(NativeDocumentCodec.Read(NativeDocumentCodec.Write(d)).Root.IsLocked);});
Test("XAML node count round-trip",()=>Equal(Sample().Root.DescendantsAndSelf().Count(),RoundTrip(Sample()).Root.DescendantsAndSelf().Count()));
Test("rotation animation round-trip",()=>{var p=RoundTrip(Sample());Equal(2,p.Storyboards[0].Tracks.Length);Equal("Rotation",p.Storyboards[0].Tracks[1].Property);Equal(360d,p.Storyboards[0].Tracks[1].Keys[1].Value);});
Test("animated zero rotation emits a transform",()=>{var xml=XElement.Parse(XamlCodec.Write(Sample()));var node=xml.Descendants().Single(e=>(string?)e.Attribute(XName.Get("Name",x))=="OrbitCore");Check(node.Elements().Any(e=>e.Name.LocalName=="Ellipse.RenderTransform"));});
Test("easing round-trip",()=>Equal("EaseInOut",RoundTrip(Sample()).Storyboards[0].Tracks[1].Keys[1].Easing));
Test("visual-state setter round-trip",()=>{var d=RoundTrip(Sample());Equal(4,d.States.Length);Equal("#FF005A9E",d.States.Single(s=>s.Name=="Pressed").Setters[0].Value);});
Test("empty storyboard survives",()=>{var d=DesignDocument.Empty() with { Storyboards=[new(Guid.NewGuid(),"Empty",2,[])] };Equal(1,RoundTrip(d).Storyboards.Length);});
Test("native rejects unsupported version",()=>Reject(()=>NativeDocumentCodec.Read(NativeDocumentCodec.Write(Sample()).Replace("\"formatVersion\": 1","\"formatVersion\": 99"))));
Test("DTD and external entities prohibited",()=>Reject(()=>XamlCodec.Parse("<!DOCTYPE Canvas [<!ENTITY bad SYSTEM 'file:///etc/passwd'>]>"+Wrap("&bad;"))));
Test("malformed XAML rejected",()=>Reject(()=>XamlCodec.Parse("<Canvas>")));
Test("excessive nesting rejected",()=>Reject(()=>XamlCodec.Parse(Wrap(string.Concat(Enumerable.Repeat("<Canvas>",70))+string.Concat(Enumerable.Repeat("</Canvas>",70))))));
Test("custom namespaced element preserved",()=>{var p=XamlCodec.Parse(Wrap("<custom:Widget custom:Mode='Safe' />","xmlns:custom='using:Example'"));Check(p.Diagnostics.Any());Check(XamlCodec.Write(p.Document).Contains("Widget"));Equal("using:Example",p.Document.Root.Children[0].Namespace);});
Test("gradient property element preserved",()=>{var p=XamlCodec.Parse(Wrap("<Rectangle Width='20' Height='20'><Rectangle.Fill><LinearGradientBrush><GradientStop Color='Red' Offset='0'/><GradientStop Color='Blue' Offset='1'/></LinearGradientBrush></Rectangle.Fill></Rectangle>"));Check(XamlCodec.Write(p.Document).Contains("GradientStop"));});
Test("unsupported storyboard semantics preserved",()=>{var text=Wrap("<Canvas.Resources><Storyboard x:Key='Reverse' AccelerationRatio='0.2'><DoubleAnimation Storyboard.TargetName='R' Storyboard.TargetProperty='Opacity' To='1'/></Storyboard></Canvas.Resources><Rectangle x:Name='R' Width='10' Height='10'/>");var d=XamlCodec.Parse(text).Document;Equal(0,d.Storyboards.Length);Check(XamlCodec.Write(d).Contains("AccelerationRatio=\"0.2\""));});
Test("resource dictionary wrapper preserved",()=>{var d=XamlCodec.Parse(Wrap("<Canvas.Resources><ResourceDictionary><SolidColorBrush x:Key='Accent' Color='Red'/><Storyboard x:Key='Empty' Duration='0:0:2'/></ResourceDictionary></Canvas.Resources>")).Document;Equal(1,d.Storyboards.Length);var xml=XElement.Parse(XamlCodec.Write(d));Equal(2,xml.Descendants().Single(e=>e.Name.LocalName=="ResourceDictionary").Elements().Count());});
Test("source reconciliation retains named identities",()=>{var original=Sample();var parsed=RoundTrip(original);var d=XamlCodec.Reconcile(original,parsed);Equal(original.Root.Id,d.Root.Id);Equal(original.Storyboards[0].Id,d.Storyboards[0].Id);Equal(original.Storyboards[0].Tracks[0].TargetId,d.Storyboards[0].Tracks[0].TargetId);});
Test("source reconciliation retains lock state",()=>{var original=Sample();original=original with { Root=original.Root with { IsLocked=true } };Check(XamlCodec.Reconcile(original,RoundTrip(original)).Root.IsLocked);});
Test("sample data binding resolution is non-mutating",()=>{var n=DesignNode.Create("TextBlock","Label").Set("Text","{Binding Customer.Name}");var root=DesignData.Set(DesignNode.Create("Canvas","Root") with { Children=[n] },"{\"Customer\":{\"Name\":\"Ada\"}}");Equal("Ada",DesignData.Resolve(root).Children[0].Get("Text"));Equal("{Binding Customer.Name}",root.Children[0].Get("Text"));});
Test("sample data survives XAML",()=>{var d=DesignDocument.Empty();d=d with { Root=DesignData.Set(d.Root,"{\"Title\":\"Hello\"}") };Check(RoundTrip(d).Root.Get(DesignData.DataKey).Contains("Hello"));});
Test("invalid sample data rejected",()=>Reject(()=>DesignData.Set(DesignDocument.Empty().Root,"[1,2]")));
Test("unsupported markup extensions remain inert",()=>{var text="{danger:Run Command=Launch}";var d=XamlCodec.Parse(Wrap("<TextBlock Text='"+text+"'/>"));Equal(text,d.Document.Root.Children[0].Get("Text"));});

DesignStoryboard Timed()=>new(Guid.NewGuid(),"Timed",2,[]);
void Near(double expected,double actual) { if(Math.Abs(expected-actual)>.000001) throw new Exception($"Expected {expected}, got {actual}"); }
Test("clock holds the terminal value instead of wrapping",()=>{var s=StoryboardClock.Sample(Timed(),2);Near(2,s.LocalTime);Check(s.Applies&&s.IsCompleted);});
Test("live repeated clock wraps at an iteration boundary",()=>{var s=StoryboardClock.Sample(Timed() with { RepeatCount=2 },2);Near(0,s.LocalTime);Check(!s.IsCompleted);});
Test("finite repeated clock holds last iteration end",()=>Near(2,StoryboardClock.Sample(Timed() with { RepeatCount=2 },4).LocalTime));
Test("auto reverse samples the backward half",()=>Near(1,StoryboardClock.Sample(Timed() with { AutoReverse=true },3).LocalTime));
Test("auto reverse completes at start value",()=>{var s=StoryboardClock.Sample(Timed() with { AutoReverse=true },4);Near(0,s.LocalTime);Check(s.IsCompleted);});
Test("auto reverse reaches the turning point",()=>Near(2,StoryboardClock.Sample(Timed() with { AutoReverse=true },2).LocalTime));
Test("clock delay does not apply keys prematurely",()=>Check(!StoryboardClock.Sample(Timed() with { BeginTime=1 },.75).Applies));
Test("delay applies once not per repeat",()=>Near(.5,StoryboardClock.Sample(Timed() with { BeginTime=1,RepeatCount=2 },3.5).LocalTime));
Test("speed ratio changes elapsed duration",()=>{var b=Timed() with { BeginTime=1,SpeedRatio=2 };Near(2,StoryboardClock.EndTime(b));Near(1,StoryboardClock.Sample(b,1.5).LocalTime);});
Test("fractional repeat count retains partial terminal value",()=>{var b=Timed() with { RepeatCount=1.25 };Near(.5,StoryboardClock.Sample(b,3).LocalTime);});
Test("duration-based repeats truncate the last iteration",()=>{var b=Timed() with { RepeatDuration=3 };Near(1,StoryboardClock.Sample(b,3).LocalTime);});
Test("duration-based reverse repeat samples correct phase",()=>Near(1,StoryboardClock.Sample(Timed() with { AutoReverse=true,RepeatDuration=3 },3).LocalTime));
Test("fractional reverse count ends at the turnaround",()=>Near(2,StoryboardClock.Sample(Timed() with { AutoReverse=true,RepeatCount=1.5 },6).LocalTime));
Test("zero repetitions never apply animation",()=>{var s=StoryboardClock.Sample(Timed() with { RepeatCount=0 },0);Check(s.IsCompleted&&!s.Applies);});
Test("forever has no finite end",()=>{var b=Timed() with { Loop=true,AutoReverse=true };Check(double.IsPositiveInfinity(StoryboardClock.EndTime(b)));Check(!StoryboardClock.Sample(b,100).IsCompleted);});
Test("FillBehavior Stop removes clock contribution",()=>{var s=StoryboardClock.Sample(Timed() with { FillBehavior="Stop" },2);Check(!s.Applies&&s.IsCompleted);});
Test("nonfinite clock input rejected",()=>Reject(()=>StoryboardClock.Sample(Timed(),double.NaN)));
Test("local scrubbing ignores playback timing",()=>{var d=Sample();var b=d.Storyboards[0] with { BeginTime=10,Loop=true };var id=b.Tracks[1].TargetId;Near(360,AnimationEngine.EvaluateLocal(d.Root,b,2).Find(id)!.Rotation);Near(0,AnimationEngine.Evaluate(d.Root,b,2).Find(id)!.Rotation);});
Test("stopped animation restores state baseline",()=>{var d=Sample();var b=d.Storyboards[0] with { FillBehavior="Stop" };var id=b.Tracks[0].TargetId;var state=new DesignState("Fade",[new(id,"Opacity","0.35")]);Near(.35,AnimationEngine.Evaluate(d.Root,b,3,state).Find(id)!.Number("Opacity"));});
Test("duration scaling preserves values and easing",()=>{var b=Sample().Storyboards[0];var next=AnimationEngine.ChangeDuration(b,4,true);Near(4,next.Tracks[1].Keys[1].Time);Near(360,next.Tracks[1].Keys[1].Value);Equal("EaseInOut",next.Tracks[1].Keys[1].Easing);Equal(b.Id,next.Id);});
Test("shortening without scaling cannot silently drop keys",()=>Reject(()=>AnimationEngine.ChangeDuration(Sample().Storyboards[0],1,false)));
Test("same duration preserves reference",()=>{var b=Timed();Check(ReferenceEquals(b,AnimationEngine.ChangeDuration(b,2,true)));});
Test("timing options survive native serialization",()=>{var d=Sample();var b=d.Storyboards[0] with { AutoReverse=true,BeginTime=.25,SpeedRatio=2,RepeatCount=2.5,FillBehavior="Stop" };var next=NativeDocumentCodec.Read(NativeDocumentCodec.Write(d with { Storyboards=[b] })).Storyboards[0];Near(.25,next.BeginTime);Near(2,next.SpeedRatio);Near(2.5,next.RepeatCount);Check(next.AutoReverse);Equal("Stop",next.FillBehavior);});
Test("XAML timing values preserve targets",()=>{var d=Sample();var b=d.Storyboards[0] with { AutoReverse=true,BeginTime=.25,SpeedRatio=2,RepeatCount=2.5,FillBehavior="Stop" };var next=RoundTrip(d with { Storyboards=[b] }).Storyboards[0];Check(next.AutoReverse);Near(.25,next.BeginTime);Near(2,next.SpeedRatio);Near(2.5,next.RepeatCount);Equal("Stop",next.FillBehavior);});
Test("duration repeat survives XAML",()=>{var d=Sample();var next=RoundTrip(d with { Storyboards=[d.Storyboards[0] with { RepeatDuration=3.5 }] }).Storyboards[0];Near(3.5,next.RepeatDuration!.Value);});
Test("forever repeat survives XAML",()=>{var d=Sample();Check(RoundTrip(d with { Storyboards=[d.Storyboards[0] with { Loop=true }] }).Storyboards[0].Loop);});
Test("invalid speed rejected atomically",()=>{var s=new DesignSession();var before=s.Document;Reject(()=>s.Execute("Bad speed",d=>d with { Storyboards=[d.Storyboards[0] with { SpeedRatio=0 }] }));Check(ReferenceEquals(before,s.Document));});
Test("invalid repeat count rejected",()=>{var d=Sample();Reject(()=>DocumentValidator.Validate(d with { Storyboards=[d.Storyboards[0] with { RepeatCount=-1 }] }));});
Test("unknown fill behavior rejected",()=>{var d=Sample();Reject(()=>DocumentValidator.Validate(d with { Storyboards=[d.Storyboards[0] with { FillBehavior="Unknown" }] }));});
Test("source clipped parent keeps duration and child keys",()=>{var text=Wrap("<Canvas.Resources><Storyboard x:Key='Clipped' Duration='0:0:1'><DoubleAnimation Storyboard.TargetName='R' Storyboard.TargetProperty='Opacity' Duration='0:0:2' To='1'/></Storyboard></Canvas.Resources><Rectangle x:Name='R'/>");var d=XamlCodec.Parse(text).Document;Equal(1,d.Storyboards.Length);Near(1,d.Storyboards[0].Duration);Near(2,d.Storyboards[0].Tracks[0].Keys[^1].Time);});
Test("default DoubleAnimation duration is one second",()=>{var d=XamlCodec.Parse(Wrap("<Canvas.Resources><Storyboard x:Key='Default'><DoubleAnimation Storyboard.TargetName='R' Storyboard.TargetProperty='Opacity' To='1'/></Storyboard></Canvas.Resources><Rectangle x:Name='R'/>")).Document;Near(1,d.Storyboards[0].Duration);});
Test("per-child delay is editable and retained",()=>{var d=XamlCodec.Parse(Wrap("<Canvas.Resources><Storyboard x:Key='ChildTiming'><DoubleAnimation BeginTime='0:0:1' Storyboard.TargetName='R' Storyboard.TargetProperty='Opacity' To='1'/></Storyboard></Canvas.Resources><Rectangle x:Name='R'/>")).Document;Equal(1,d.Storyboards.Length);Near(1,d.Storyboards[0].Tracks[0].Timing!.BeginTime);Check(XamlCodec.Write(d).Contains("BeginTime"));});
Test("state edit preserves base tree",()=>{var d=Sample();var id=d.Storyboards[0].Tracks[0].TargetId;var next=StateEditing.SetProperty(d,"Pressed",[id],"Opacity","0.3");Check(ReferenceEquals(d.Root,next.Root));Equal("0.3",next.States.Single(s=>s.Name=="Pressed").Setters.Single(s=>s.Property=="Opacity").Value);});
Test("state setter replacement does not duplicate properties",()=>{var d=Sample();var id=d.Storyboards[0].Tracks[0].TargetId;var next=StateEditing.SetProperty(d,"Pressed",[id],"Background","Red");Equal(1,next.States.Single(s=>s.Name=="Pressed").Setters.Length);});
Test("identical state setter is a true no-op",()=>{var d=Sample();var state=d.States.Single(s=>s.Name=="Pressed");var setter=state.Setters[0];Check(ReferenceEquals(d,StateEditing.SetProperty(d,state.Name,[setter.TargetId],setter.Property,setter.Value)));});
Test("state editing respects locked ancestors",()=>{var d=Sample();d=d with { Root=d.Root with { IsLocked=true } };Check(ReferenceEquals(d,StateEditing.SetProperty(d,"Pressed",[d.Storyboards[0].Tracks[0].TargetId],"Opacity","0.3")));});
Test("invalid state numeric value rejected",()=>{var d=Sample();Reject(()=>StateEditing.SetProperty(d,"Pressed",[d.Storyboards[0].Tracks[0].TargetId],"Opacity","4"));});
Test("state cannot rename an element",()=>{var d=Sample();Reject(()=>StateEditing.SetProperty(d,"Pressed",[d.Root.Id],DesignNode.NameKey,"NewName"));});
Test("reset state property retains other setters",()=>{var d=Sample();var id=d.Storyboards[0].Tracks[0].TargetId;var updated=StateEditing.SetProperty(d,"Pressed",[id],"Opacity","0.3");var reset=StateEditing.RemoveProperty(updated,"Pressed",[id],"Opacity");Equal(1,reset.States.Single(s=>s.Name=="Pressed").Setters.Length);Check(ReferenceEquals(d.Root,reset.Root));});
Test("state override supersedes inline brush without modifying source",()=>{var d=XamlCodec.Parse(Wrap("<Rectangle x:Name='R' Width='40' Height='40'><Rectangle.Fill><SolidColorBrush Color='Blue'/></Rectangle.Fill></Rectangle>")).Document;var id=d.Root.Children[0].Id;var root=AnimationEngine.EvaluateLocal(d.Root,null,0,new("State",[new(id,"Fill","Red")]));Equal("Red",root.Find(id)!.Get("Fill"));Equal(0,root.Find(id)!.PropertyElements.Length);Equal(1,d.Root.Children[0].PropertyElements.Length);});

var animation=AnimationTimelineTests.Run();passed+=animation.Passed;failed+=animation.Failed;
var states=StateTransitionTests.Run();passed+=states.Passed;failed+=states.Failed;
Console.WriteLine($"{passed} passed; {failed} failed.");return failed==0 ? 0 : 1;
