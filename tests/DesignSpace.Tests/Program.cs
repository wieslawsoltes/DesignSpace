using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Animation;

var passed=0; var failures=new List<string>();
void Test(string name,Action test) { try { test(); passed++; Console.WriteLine("PASS "+name); } catch(Exception ex) { failures.Add(name+": "+ex); Console.WriteLine("FAIL "+name+": "+ex.Message); } }
void Equal<T>(T expected,T actual) { if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}; got {actual}"); }
void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected rejection"); }
Test("sample validates",()=>DocumentValidator.Validate(SampleDocument.Create()));
Test("add selects",()=>{ var s=new DesignSession(DesignDocument.Empty()); var id=s.Add("Rectangle",new(10,20,100,50)); Equal(id,s.Selection.Single()); Equal(2,s.Document.Root.DescendantsAndSelf().Count()); });
Test("undo redo document and selection",()=>{ var s=new DesignSession(); var before=s.Document; var id=s.Add("Button",new(1,2,3,4)); s.Undo(); Equal(before,s.Document); s.Redo(); Equal(id,s.Selection.Single()); });
Test("saved-state history",()=>{ var s=new DesignSession(); Check(!s.IsDirty); s.Add("Rectangle",new(1,2,3,4)); Check(s.IsDirty); s.Undo(); Check(!s.IsDirty); });
Test("redo invalidation",()=>{ var s=new DesignSession(); s.Add("Ellipse",new(1,2,3,4)); s.Undo(); s.Add("Rectangle",new(1,2,3,4)); Check(!s.CanRedo); });
Test("bounded history",()=>{ var s=new DesignSession(){HistoryLimit=2}; for(var i=0;i<3;i++) s.Add("Rectangle",new(1,2,3,4)); s.Undo(); s.Undo(); Check(!s.CanUndo); });
Test("invalid edit is atomic",()=>{ var s=new DesignSession(); s.Select(s.Document.Root.Id); var before=s.Document; Throws(()=>s.SetProperty("Width","NaN")); Equal(before,s.Document); Check(!s.CanUndo); });
Test("duplicate names rejected",()=>{ var s=new DesignSession(); var id=s.Add("Button",new(1,2,3,4)); Throws(()=>s.SetProperty(DesignNode.NameKey,"LayoutRoot")); Equal(id,s.Selection.Single()); });
Test("lock prevents move",()=>{ var s=new DesignSession(); var id=s.Add("Rectangle",new(10,20,30,40)); s.ToggleLock(id); s.Move(5,5); Equal(10d,s.Document.Root.Find(id)!.Number("Canvas.Left")); });
Test("lock prevents delete",()=>{ var s=new DesignSession(); var id=s.Add("Rectangle",new(10,20,30,40)); s.ToggleLock(id); s.Delete(); Check(s.Document.Root.Find(id)!=null); });
Test("duplicate remaps all identities",()=>{ var s=new DesignSession(); s.Select(s.Document.Root.Children.First(n=>n.Type=="Border").Id); s.Duplicate(); DocumentValidator.Validate(s.Document); });
Test("group ungroup coordinates",()=>{ var s=new DesignSession(DesignDocument.Empty()); var a=s.Add("Rectangle",new(20,30,80,40)); var b=s.Add("Ellipse",new(130,50,50,50)); s.Select([a,b]); s.Group(); Equal(20d,s.Document.Root.Find(s.Selection.Single())!.Number("Canvas.Left")); s.Ungroup(); Equal(20d,s.Document.Root.Find(a)!.Number("Canvas.Left")); Equal(130d,s.Document.Root.Find(b)!.Number("Canvas.Left")); });
Test("delete cleans animation targets",()=>{ var s=new DesignSession(); var target=s.Document.Storyboards[0].Tracks[0].TargetId; s.Select(target); s.Delete(); DocumentValidator.Validate(s.Document); Check(!s.Document.Storyboards[0].Tracks.Any(t=>t.TargetId==target)); });
Test("layout canvas positions",()=>{ var s=new DesignSession(DesignDocument.Empty()); var id=s.Add("Rectangle",new(14,26,100,80)); var l=new LayoutEngine().Arrange(s.Document.Root); Equal(new DRect(14,26,100,80),l.ById[id].Bounds); });
Test("hit test reverse z",()=>{ var s=new DesignSession(DesignDocument.Empty()); s.Add("Rectangle",new(0,0,50,50)); var b=s.Add("Ellipse",new(0,0,50,50)); Equal(b,new LayoutEngine().Arrange(s.Document.Root).HitTest(new(25,25))!.Node.Id); });
Test("hidden excluded",()=>{ var s=new DesignSession(); var id=s.Document.Root.Children[0].Id; s.ToggleVisibility(id); Check(!new LayoutEngine().Arrange(s.Document.Root).ById.ContainsKey(id)); });
Test("rotated bounds",()=>Check(new DRect(0,0,100,20).ContainsRotated(new(50,50),90)));
Test("rect union",()=>Equal(new DRect(0,0,40,50),DRect.Union([new(0,0,10,10),new(20,20,20,30)])));
Test("grid snapping",()=>Equal(16d,Numbers.Snap(18,8)));
Test("margin shorthand",()=>Equal(new Insets(2,3,2,3),Insets.Parse("2,3")));
Test("linear interpolation",()=>Equal(50d,AnimationEngine.Evaluate(new(Guid.NewGuid(),"Width",[new(0,0),new(1,100)]),.5,0)));
Test("discrete interpolation",()=>Equal(0d,AnimationEngine.Evaluate(new(Guid.NewGuid(),"Width",[new(0,0),new(1,100,"Discrete")]),.5,0)));
Test("ease endpoint",()=>Equal(1d,AnimationEngine.Ease(1,"EaseInOut")));
Test("implicit animation base",()=>Equal(50d,AnimationEngine.Evaluate(new(Guid.NewGuid(),"Width",[new(1,100)]),.5,0)));
Test("key replacement",()=>{ var id=Guid.NewGuid(); var b=new DesignStoryboard(Guid.NewGuid(),"Test",2,[]); b=AnimationEngine.SetKey(b,id,"Opacity",1,.5); b=AnimationEngine.SetKey(b,id,"Opacity",1,.75); Equal(1,b.Tracks[0].Keys.Length); Equal(.75,b.Tracks[0].Keys[0].Value); });
Test("animation does not mutate base",()=>{ var d=SampleDocument.Create(); var n=d.Root; _=AnimationEngine.Evaluate(n,d.Storyboards[0],.3); Equal(n,d.Root); });
Test("invalid key rejected",()=>{ var d=SampleDocument.Create(); var b=d.Storyboards[0] with { Duration=-1 }; Throws(()=>DocumentValidator.Validate(d with { Storyboards=[b] })); });
Test("selected ancestor normalization",()=>{ var s=new DesignSession(); var p=s.Document.Root.Children.First(n=>n.Children.Length>0); s.Select([p.Id,p.Children[0].Id]); Equal(1,s.TopLevelSelection().Count); });
Test("alignment",()=>{ var s=new DesignSession(DesignDocument.Empty()); var a=s.Add("Rectangle",new(10,10,40,40)); var b=s.Add("Rectangle",new(30,40,30,30)); s.Select([a,b]); s.Align("Left"); Equal(10d,s.Document.Root.Find(b)!.Number("Canvas.Left")); });
Test("load validates before replacing",()=>{ var s=new DesignSession(); var d=s.Document; Throws(()=>s.Load(d with { FormatVersion=100 })); Equal(d,s.Document); });
Console.WriteLine($"{passed} passed; {failures.Count} failed.");
if(failures.Count>0) { foreach(var failure in failures) Console.Error.WriteLine(failure); return 1; }
return 0;
