using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Xaml;

internal static class AnchorSelectionTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0; var failed=0;
        void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS anchors: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL anchors: "+name+": "+e);}}
        void Check(bool condition){if(!condition)throw new Exception("Assertion failed");}
        void Near(double expected,double actual){if(Math.Abs(expected-actual)>1e-6)throw new Exception($"Expected {expected}, got {actual}");}
        void Point(DPoint expected,DPoint actual){Near(expected.X,actual.X);Near(expected.Y,actual.Y);}
        void Reject(Action run){try{run();}catch{return;}throw new Exception("Expected rejection");}
        PathHandle H(int s,int f=0)=>new(f,s,PathHandleKind.Anchor);
        VectorPath P(string text)=>VectorPathCodec.Parse(text);
        string Data(VectorPath p)=>VectorPathCodec.Write(p);
        var cubic=P("M0 0C10 20 20 20 30 0C40-20 50-20 60 0");
        Test("all anchors exclude tangent controls",()=>Check(PathAnchorEditing.Anchors(cubic).SequenceEqual(new[]{H(-1),H(0),H(1)})));
        Test("closed seam aliases the first anchor",()=>{var p=P("M0 0L30 0L30 30L0 0Z");var s=PathAnchorEditing.Selection(p,[H(-1),H(2)]);Check(s.Count==1&&s.Contains(H(-1)));});
        Test("a coincident open endpoint remains independent",()=>Check(PathAnchorEditing.Anchors(P("M0 0L10 0L0 0")).Count()==3));
        Test("invalid figure rejected",()=>Reject(()=>PathAnchorEditing.Selection(cubic,[H(0,9)])));
        Test("invalid segment rejected",()=>Reject(()=>PathAnchorEditing.Selection(cubic,[H(9)])));
        Test("tangent is not an anchor selection",()=>Reject(()=>PathAnchorEditing.Selection(cubic,[new(0,0,PathHandleKind.Control1)])));
        Test("duplicate selection is normalized",()=>Check(PathAnchorEditing.Selection(cubic,[H(0),H(0)]).Count==1));
        Test("empty move keeps reference",()=>Check(ReferenceEquals(cubic,PathAnchorEditing.Translate(cubic,[],new(4,5)))));
        Test("zero move keeps reference",()=>Check(ReferenceEquals(cubic,PathAnchorEditing.Translate(cubic,[H(0)],default))));
        Test("nonfinite delta rejected",()=>Reject(()=>PathAnchorEditing.Translate(cubic,[H(0)],new(double.NaN,0))));
        Test("out of budget position rejected without changing source",()=>{var text=Data(cubic);Reject(()=>PathAnchorEditing.MoveTo(cubic,new Dictionary<PathHandle,DPoint>{{H(0),new(1e12,0)}}));Check(Data(cubic)==text);});
        Test("seam cannot have conflicting destinations",()=>Reject(()=>PathAnchorEditing.MoveTo(P("M0 0L20 0L0 0Z"),new Dictionary<PathHandle,DPoint>{{H(-1),new(1,0)},{H(1),new(2,0)}})));
        Test("cubic anchors move adjacent handles only once",()=>{var p=PathAnchorEditing.Translate(cubic,[H(-1),H(0)],new(3,5));Point(new(3,5),p.Figures[0].Start);Point(new(13,25),p.Figures[0].Segments[0].Control1);Point(new(23,25),p.Figures[0].Segments[0].Control2);Point(new(43,-15),p.Figures[0].Segments[1].Control1);Point(new(50,-20),p.Figures[0].Segments[1].Control2);});
        Test("full cubic selection is exact translation",()=>{var p=PathAnchorEditing.Translate(cubic,PathAnchorEditing.Anchors(cubic),new(5,8));Check(Data(p)==Data(VectorMath.Transform(cubic,DMatrix.Translate(5,8))));});
        Test("quadratic shared handle does not double translate",()=>{var p=P("M0 0Q15 30 30 0");var n=PathAnchorEditing.Translate(p,[H(-1),H(0)],new(5,8));Point(new(20,38),n.Figures[0].Segments[0].Control1);});
        Test("quadratic follows average of distinct endpoint movements",()=>{var p=P("M0 0Q15 30 30 0");var n=PathAnchorEditing.MoveTo(p,new Dictionary<PathHandle,DPoint>{{H(-1),new(0,10)},{H(0),new(30,20)}});Point(new(15,45),n.Figures[0].Segments[0].Control1);});
        Test("single quadratic endpoint retains existing tangent behavior",()=>{var p=P("M0 0Q15 30 30 0");Point(new(15,40),PathAnchorEditing.Translate(p,[H(0)],new(0,10)).Figures[0].Segments[0].Control1);});
        Test("arc batch translation retains native parameters",()=>{var p=P("M0 0A40 20 30 1 1 60 20");var n=PathAnchorEditing.Translate(p,[H(-1),H(0)],new(7,-2));var s=n.Figures[0].Segments[0];Check(s.Kind==VectorSegmentKind.Arc&&s.Radius==p.Figures[0].Segments[0].Radius&&s.Angle==30&&s.LargeArc&&s.Clockwise);Point(new(67,18),s.End);});
        Test("closed seam and tangents move once",()=>{var p=P("M0 0C10 20 20 20 30 0C20-20 10-20 0 0Z");var n=PathAnchorEditing.Translate(p,[H(-1),H(0),H(1)],new(4,5));Point(new(4,5),n.Figures[0].Segments[^1].End);Point(new(14,-15),n.Figures[0].Segments[^1].Control2);});
        Test("unaffected figures retain references",()=>{var p=P("M0 0L20 0M50 50L60 60");var n=PathAnchorEditing.Translate(p,[H(0)],new(1,2));Check(ReferenceEquals(p.Figures[1],n.Figures[1]));});
        Test("same destinations retain source reference",()=>Check(ReferenceEquals(cubic,PathAnchorEditing.MoveTo(cubic,new Dictionary<PathHandle,DPoint>{{H(0),new(30,0)}}))));
        Test("rectangle selects inclusive boundary anchors only",()=>{var p=P("M0 0L20 0L20 20L0 20Z");Check(PathAnchorEditing.SelectInRectangle(p,new(0,0,20,0),DMatrix.Identity).SetEquals(new[]{H(-1),H(0)}));});
        Test("marquee uses transformed coordinates",()=>{var p=P("M0 0L20 0L20 20");var m=DMatrix.Translate(100,50)*DMatrix.Rotate(90);var s=PathAnchorEditing.SelectInRectangle(p,new(99,49,2,22),m);Check(s.SetEquals(new[]{H(-1),H(0)}));});
        Test("invalid rectangle rejected",()=>Reject(()=>PathAnchorEditing.SelectInRectangle(cubic,new(0,0,-1,2),DMatrix.Identity)));
        Test("nonfinite selection matrix rejected",()=>Reject(()=>PathAnchorEditing.SelectInRectangle(cubic,new(0,0,10,10),DMatrix.Translate(double.NaN,0))));
        Test("alignment is calculated in world space",()=>{var p=P("M0 0L20 10L30 30");var m=DMatrix.Translate(50,30)*DMatrix.Rotate(90);var n=PathAnchorEditing.Align(p,PathAnchorEditing.Anchors(p),"Left",m);var x=PathAnchorEditing.Anchors(n).Select(h=>m.Map(PathEditing.Position(n,h)).X).ToArray();foreach(var v in x)Near(20,v);});
        foreach(var alignment in new[]{"Left","Center","Right","Top","Middle","Bottom"})Test("alignment "+alignment,()=>{var p=P("M10 20L30 60L50 100");var n=PathAnchorEditing.Align(p,PathAnchorEditing.Anchors(p),alignment,DMatrix.Identity);var horizontal=alignment is "Left" or "Center" or "Right";var expected=alignment switch{"Left"=>10d,"Center"=>30,"Right"=>50,"Top"=>20,"Middle"=>60,_=>100};foreach(var h in PathAnchorEditing.Anchors(n)){var pos=PathEditing.Position(n,h);Near(expected,horizontal?pos.X:pos.Y);}});
        Test("singular alignment space rejected",()=>Reject(()=>PathAnchorEditing.Align(cubic,[H(-1),H(0)],"Left",DMatrix.Scale(0,1))));
        Test("unknown alignment rejected",()=>Reject(()=>PathAnchorEditing.Align(cubic,[H(-1),H(0)],"Invalid",DMatrix.Identity)));
        Test("distribution uses equal anchor intervals",()=>{var p=P("M0 10L10 30L90 50L120 80");var n=PathAnchorEditing.Distribute(p,PathAnchorEditing.Anchors(p),true,DMatrix.Identity);Near(40,n.Figures[0].Segments[0].End.X);Near(80,n.Figures[0].Segments[1].End.X);Near(30,n.Figures[0].Segments[0].End.Y);});
        Test("two-anchor distribution is a no-op",()=>Check(ReferenceEquals(cubic,PathAnchorEditing.Distribute(cubic,[H(-1),H(0)],true,DMatrix.Identity))));
        Test("deleting an empty selection is a no-op",()=>Check(ReferenceEquals(cubic,PathAnchorEditing.Remove(cubic,[]))));
        Test("bulk deletion bridges only skipped anchors",()=>{var p=P("M0 0L10 0L20 0L30 0L40 0");var n=PathAnchorEditing.Remove(p,[H(0),H(2)]);Check(n.SegmentCount==2);Point(new(20,0),n.Figures[0].Segments[0].End);Point(new(40,0),n.Figures[0].Segments[1].End);});
        Test("bulk deletion retains adjacent segment reference",()=>{var p=P("M0 0L10 0L20 0L30 0");var n=PathAnchorEditing.Remove(p,[H(-1)]);Check(ReferenceEquals(p.Figures[0].Segments[1],n.Figures[0].Segments[0]));});
        Test("deletion joins surviving cubic tangents",()=>{var p=P("M0 0C5 10 15 10 20 0C25-10 35-10 40 0C45 10 55 10 60 0");var n=PathAnchorEditing.Remove(p,[H(0),H(1)]);var s=n.Figures[0].Segments.Single();Point(new(5,10),s.Control1);Point(new(55,10),s.Control2);Point(new(60,0),s.End);});
        Test("delete all anchors removes figure",()=>Check(PathAnchorEditing.Remove(cubic,PathAnchorEditing.Anchors(cubic)).Figures.IsEmpty));
        Test("less than two surviving anchors removes figure",()=>Check(PathAnchorEditing.Remove(cubic,[H(-1),H(0)]).Figures.IsEmpty));
        Test("delete across figures preserves unaffected references",()=>{var p=P("M0 0L10 0M20 20L30 20L40 20M50 50L60 50");var n=PathAnchorEditing.Remove(p,[H(-1),H(0,1)]);Check(n.Figures.Length==2&&n.Figures[0].Segments.Length==1);Check(ReferenceEquals(p.Figures[2],n.Figures[1]));});
        Test("closed deletion keeps the closing seam",()=>{var p=P("M0 0L20 0L20 20L0 20L0 0Z");var n=PathAnchorEditing.Remove(p,[H(-1)]);Check(n.Figures[0].Closed);Check(PathAnchorEditing.Anchors(n).Count()==3);Point(new(20,0),n.Figures[0].Start);});
        Test("closed cubic deletion retains closing curve",()=>{var p=P("M0 0C10-10 20-10 30 0C40 10 40 20 30 30C20 40 10 40 0 30C-10 20-10 10 0 0Z");var n=PathAnchorEditing.Remove(p,[H(1)]);Check(n.Figures[0].Closed);Check(n.Figures[0].Segments[^1].Kind==VectorSegmentKind.Cubic);Point(n.Figures[0].Start,n.Figures[0].Segments[^1].End);});
        Test("batch edit participates in a single reversible session transaction",()=>{var node=VectorGeometry.WithPath(DesignNode.Create("Path","P"),cubic);var session=new DesignSession(new(){Root=DesignNode.Create("Canvas","Root") with { Children=[node] }});var before=session.Document;var n=PathAnchorEditing.Translate(cubic,[H(-1),H(0)],new(1,2));session.Execute("Move selected anchors",d=>d with{Root=d.Root.Update(node.Id,p=>VectorGeometry.WithPath(p,n))});Check(session.Revision==1);session.Undo();Check(ReferenceEquals(before,session.Document));session.Redo();Check(Data(VectorGeometry.ReadPath(session.Index.Find(node.Id)!))==Data(n));});
        Test("batch edit roundtrips through XAML and native persistence",()=>{var n=PathAnchorEditing.Translate(cubic,PathAnchorEditing.Anchors(cubic),new(4,5));var doc=new DesignDocument{Root=DesignNode.Create("Canvas","Root") with{Children=[VectorGeometry.WithPath(DesignNode.Create("Path","P"),n)]}};var xml=XamlCodec.Parse(XamlCodec.Write(doc)).Document;var native=NativeDocumentCodec.Read(NativeDocumentCodec.Write(doc));Check(Data(VectorGeometry.ReadPath(xml.Root.Children[0]))==Data(n));Check(Data(VectorGeometry.ReadPath(native.Root.Children[0]))==Data(n));});
        Test("8192-segment batch translation has bounded allocation",()=>{var p=P("M0 0"+string.Concat(Enumerable.Range(1,8192).Select(i=>"L"+i+" "+i%17)));var all=PathAnchorEditing.Anchors(p).ToArray();_ =PathAnchorEditing.Translate(p,all,new(1,1));var allocated=GC.GetAllocatedBytesForCurrentThread();var n=PathAnchorEditing.Translate(p,all,new(2,3));var bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;Check(bytes<16*1024*1024);Check(n.SegmentCount==8192);Point(new(8194,8192%17+3),n.Figures[0].Segments[^1].End);Console.WriteLine("ANCHOR BATCH allocated bytes: "+bytes);});
        Test("8192-segment bulk deletion stays bounded",()=>{var p=P("M0 0"+string.Concat(Enumerable.Range(1,8192).Select(i=>"L"+i+" 0")));var selected=PathAnchorEditing.Anchors(p).Where(h=>h.Segment%2==0).ToArray();var n=PathAnchorEditing.Remove(p,selected);Check(n.SegmentCount==4096);});
        return(passed,failed);
    }
}
