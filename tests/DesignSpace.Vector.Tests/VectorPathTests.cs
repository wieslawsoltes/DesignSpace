using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
using DesignSpace.Engine;

internal static class VectorPathTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action test){try{test();passed++;Console.WriteLine("PASS path: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL path: "+name+": "+e);}}
        void Check(bool condition){if(!condition)throw new Exception("Assertion failed");}
        void Near(double a,double b,double tolerance=1e-6){if(Math.Abs(a-b)>tolerance)throw new Exception($"Expected {a}, got {b}");}
        void Point(DPoint a,DPoint b,double tolerance=1e-6){Near(a.X,b.X,tolerance);Near(a.Y,b.Y,tolerance);}
        void Reject(Action action){try{action();}catch(Exception){return;}throw new Exception("Expected rejection");}
        VectorPath P(string s)=>VectorPathCodec.Parse(s);
        Test("relative and repeated line commands",()=>{var p=P("m10 20 10 0 h5 v10 l-2-3z");Check(p.SegmentCount==4);Point(new(23,27),p.Figures[0].Segments[^1].End);Check(p.Figures[0].Closed);});
        Test("compact signed decimals and exponents",()=>{var p=P("M2..3L+1.e2-2e1");Point(new(2,.3),p.Figures[0].Start);Point(new(100,-20),p.Figures[0].Segments[0].End);});
        Test("default fill rule is even odd",()=>Check(!P("M0 0L10 10").NonZero));
        Test("nonzero fill prefix",()=>Check(P("F1 M0 0L10 10").NonZero));
        Test("smooth cubic expands reflected control",()=>{var p=P("M0 0 C10 20 20 20 30 0 S50-20 60 0");Point(new(40,-20),p.Figures[0].Segments[1].Control1);});
        Test("smooth quadratic expands reflected control",()=>{var p=P("M0 0Q10 20 20 0T40 0");Point(new(30,-20),p.Figures[0].Segments[1].Control1);});
        Test("line resets smooth tangent",()=>{var p=P("M0 0Q10 20 20 0L30 0T50 0");Point(new(30,0),p.Figures[0].Segments[2].Control1);});
        Test("relative cubic controls share previous endpoint",()=>{var p=P("M10 10 c5 0 10 10 20 0");var s=p.Figures[0].Segments[0];Point(new(15,10),s.Control1);Point(new(20,20),s.Control2);Point(new(30,10),s.End);});
        Test("multiple figures and relative move after close",()=>{var p=P("M10 10L20 20z m5 6l7 8");Check(p.Figures.Length==2);Point(new(15,16),p.Figures[1].Start);});
        Test("arc flags and corrected radii",()=>{var p=P("M0 0A10 5 0 01100 0");var s=p.Figures[0].Segments[0];Check(s.Clockwise&&!s.LargeArc);Check(VectorArc.TryCreate(new(0,0),s,out var arc));Near(50,arc.Radius.Width);Near(25,arc.Radius.Height);});
        Test("roundtrip all finite commands",()=>{var p=P("F1 M2 3H10V20Q30 40 50 60T70 80C10 20 30 40 50 60S70 80 90 100A30 20 15 1 0 20 30Z");var text=VectorPathCodec.Write(p);Check(text==VectorPathCodec.Write(P(text)));});
        Test("empty path is stable",()=>Check(P(VectorPathCodec.Write(VectorPath.Empty)).Figures.IsEmpty));
        foreach(var invalid in new[]{"M","M0","L0 0","M0 0C1 2","M0 0A-1 3 0 0 1 5 5","M0 0A1 1 0 2 0 5 5","M0 0LNaN 2","M0 0L1e999 0","M0 0,","M0 0L1 2 junk","F2 M0 0","M0 0F1","M0 0Z1 2","M0 0LL1 2"})
            Test("reject "+invalid,()=>Reject(()=>P(invalid)));
        Test("coordinate safety budget",()=>Reject(()=>P("M1000000001 0")));
        Test("segment safety budget",()=>Reject(()=>P("M0 0"+string.Concat(Enumerable.Repeat("L1 1",8193)))));
        Test("exact cubic extrema",()=>{var b=VectorMath.Bounds(P("M0 0C0 100 100 100 100 0"));Near(100,b.Width);Near(75,b.Height);});
        Test("exact quadratic extrema",()=>Near(50,VectorMath.Bounds(P("M0 0Q50 100 100 0")).Height));
        Test("ellipse arc extrema",()=>{var p=VectorGeometry.Local(new DesignNode{Type="Ellipse"},new(100,60));var b=VectorMath.Bounds(p);Near(100,b.Width);Near(60,b.Height);});
        Test("even odd and nonzero holes differ",()=>{var p=P("M0 0H100V100H0ZM20 20H80V80H20Z");Check(!VectorMath.Contains(p,new(50,50)));Check(VectorMath.Contains(p with { NonZero=true },new(50,50)));});
        Test("open figure fills with implicit closing edge",()=>Check(VectorMath.Contains(P("M0 0L100 0L50 100"),new(50,20))));
        foreach(var command in new[]{"L100 20","Q20 100 100 20","C0 100 100 100 100 20","A80 50 20 1 1 100 20"})
            Test("subdivision retains curve "+command,()=>
            {
                var path=P("M0 0"+command);var original=path.Figures[0].Segments[0];var edited=PathEditing.Insert(path,0,0,.37);var a=edited.Figures[0].Segments[0];var b=edited.Figures[0].Segments[1];
                for(var i=0;i<=20;i++){var t=i/20d;var actual=t<=.37 ? VectorMath.Evaluate(new(0,0),a,t/.37) : VectorMath.Evaluate(a.End,b,(t-.37)/.63);Point(VectorMath.Evaluate(new(0,0),original,t),actual,1e-5);}
            });
        Test("insert closing line point",()=>{var p=P("M0 0L100 0L100 100z");var next=PathEditing.Insert(p,0,2,.5);Point(new(50,50),next.Figures[0].Segments[^1].End);Check(next.Figures[0].Closed);});
        Test("anchor movement translates adjacent tangents",()=>{var p=P("M0 0C10 0 20 0 30 0C40 0 50 0 60 0");var next=PathEditing.Move(p,new(0,0,PathHandleKind.Anchor),new(30,10));Point(new(20,10),next.Figures[0].Segments[0].Control2);Point(new(40,10),next.Figures[0].Segments[1].Control1);Point(new(30,0),p.Figures[0].Segments[0].End);});
        Test("linked tangent remains symmetric",()=>{var p=P("M0 0C10 0 20 0 30 0C40 0 50 0 60 0");var next=PathEditing.Move(p,new(0,0,PathHandleKind.Control2),new(20,10),true);Point(new(40,-10),next.Figures[0].Segments[1].Control1);});
        Test("unlinked tangent leaves neighbor unchanged",()=>{var p=P("M0 0C10 0 20 0 30 0C40 0 50 0 60 0");var next=PathEditing.Move(p,new(0,0,PathHandleKind.Control2),new(20,10));Point(new(40,0),next.Figures[0].Segments[1].Control1);});
        Test("moving closed start retains seam",()=>{var p=P("M0 0C10 0 10 10 0 0Z");var next=PathEditing.Move(p,new(0,-1,PathHandleKind.Anchor),new(5,5));Point(new(5,5),next.Figures[0].Segments[^1].End);Check(PathEditing.Handles(next).Count(h=>h.Handle.Kind==PathHandleKind.Anchor)==1);});
        Test("straight-to-curve conversion preserves line",()=>{var p=P("M0 0L90 30");var next=PathEditing.SetSegmentKind(p,0,0,true);Point(new(45,15),VectorMath.Evaluate(new(0,0),next.Figures[0].Segments[0],.5));});
        Test("deleting edge splits open figure without connecting gap",()=>{var p=P("M0 0L10 0L20 0L30 0");var next=PathEditing.RemoveSegment(p,0,1);Check(next.Figures.Length==2);Check(next.SegmentCount==2);Point(new(20,0),next.Figures[1].Start);});
        Test("deleting edge opens closed figure",()=>{var next=PathEditing.RemoveSegment(P("M0 0L10 0L10 10Z"),0,0);Check(!next.Figures[0].Closed);Point(new(10,0),next.Figures[0].Start);Point(new(0,0),next.Figures[0].Segments[^1].End);});
        Test("removing last usable anchor leaves empty geometry",()=>Check(PathEditing.RemoveAnchor(P("M0 0L10 10"),new(0,0,PathHandleKind.Anchor)).Figures.IsEmpty));
        Test("same point edit preserves reference",()=>{var p=P("M0 0L10 10");Check(ReferenceEquals(p,PathEditing.Move(p,new(0,0,PathHandleKind.Anchor),new(10,10))));});
        Test("freehand simplification retains endpoints",()=>{var points=Enumerable.Range(0,200).Select(i=>new DPoint(i,Math.Sin(i*.1)*.1)).ToArray();var p=PathEditing.Freehand(points,.5);Check(p.SegmentCount==1);Point(points[0],p.Figures[0].Start);Point(points[^1],p.Figures[0].Segments[^1].End);});
        Test("freehand retains significant corners",()=>{var p=PathEditing.Freehand([new(0,0),new(50,0),new(50,50)],.5);Check(p.SegmentCount==2);});
        Test("nearest uses caller transform",()=>{var p=P("M0 0L100 0");var hit=VectorMath.Nearest(p,new(60,25),DMatrix.Translate(10,20),6);Check(hit is not null);Near(.5,hit!.Value.Time);});
        Test("shared path identity survives style-only edits",()=>{var n=new DesignNode{Type="Path"}.Set("Data","M0 0L100 0");Check(ReferenceEquals(VectorGeometry.ReadPath(n),VectorGeometry.ReadPath(n.Set("Fill","Red"))));});
        Test("translation preserves exact arc type",()=>{var p=P("M0 0A50 20 10 0 1 100 0");Check(VectorMath.Transform(p,DMatrix.Translate(10,20)).Figures[0].Segments[0].Kind==VectorSegmentKind.Arc);});
        Test("arbitrary affine arc conversion remains close",()=>{var p=P("M0 0A50 20 10 0 1 100 0");var m=DMatrix.Rotate(30)*DMatrix.Scale(2,1);var next=VectorMath.Transform(p,m);Check(next.Figures[0].Segments.All(s=>s.Kind==VectorSegmentKind.Cubic));Point(m.Map(p.Figures[0].Segments[^1].End),next.Figures[0].Segments[^1].End);});
        Test("uniform stretch keeps aspect and centers geometry",()=>{var n=new DesignNode{Type="Path"}.Set("Stretch","Uniform").Set("StrokeThickness","0");var p=P("M0 0H100V50H0Z");var m=VectorGeometry.Mapping(n,new(200,200),p);Point(new(0,50),m.Map(new(0,0)));Point(new(200,150),m.Map(new(100,50)));});
        Test("object-form Bezier and arc parsing",()=>{var ns=DesignNode.PresentationNamespace;var xml=XElement.Parse($"<PathGeometry xmlns='{ns}'><PathFigure StartPoint='0,0' IsClosed='True'><BezierSegment Point1='10,0' Point2='10,10' Point3='20,20'/><ArcSegment Point='0,0' Size='20,20' SweepDirection='Clockwise'/></PathFigure></PathGeometry>");var p=VectorGeometry.ReadElement(xml);Check(p.SegmentCount==2&&p.Figures[0].Closed);});
        Test("unsupported segment metadata is not silently dropped",()=>Reject(()=>VectorGeometry.ReadElement(XElement.Parse($"<PathGeometry xmlns='{DesignNode.PresentationNamespace}'><PathFigure><LineSegment Point='10,0' IsStroked='False'/></PathFigure></PathGeometry>"))));
        Test("shape conversion requalifies brush property owner",()=>{var n=new DesignNode{Type="Rectangle",PropertyElements=[$"<Rectangle.Fill xmlns='{DesignNode.PresentationNamespace}'><SolidColorBrush Color='Red'/></Rectangle.Fill>"]};var next=VectorGeometry.WithPath(n,P("M0 0L10 10"));Check(next.PropertyElements[0].Contains("Path.Fill"));});
        Console.WriteLine($"Path geometry: {passed} passed; {failed} failed.");return(passed,failed);
    }
}
