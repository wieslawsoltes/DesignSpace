using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Rendering.Skia;
using DesignSpace.Xaml;
using SkiaSharp;

internal static class StrokeTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action test){try{test();passed++;Console.WriteLine("PASS stroke: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL stroke: "+name+": "+e);}}
        void Check(bool ok,string message="Assertion failed"){if(!ok)throw new Exception(message);}
        void Near(double expected,double actual){if(Math.Abs(expected-actual)>.02)throw new Exception($"Expected {expected}, got {actual}");}
        void Reject(Action test){try{test();}catch{return;}throw new Exception("Expected rejection");}
        VectorPath Path(string data)=>VectorPathCodec.Parse(data);
        SKPath Outline(string data,StrokeStyle style){using var source=SkiaVectorGeometry.Create(Path(data));return SkiaStrokeGeometry.Create(source,style);}
        DesignNode Node(string data="M30 50L230 50")=>DesignNode.Create("Path","Line",0,0,260,120).Set("Data",data).Set("Stretch","None").Set("Stroke","Red").Set("StrokeThickness","10");
        DesignDocument Doc(DesignNode n)=>new(){Root=DesignNode.Create("Canvas","Root",0,0,300,200).Set("Background","White") with{Children=[n]}};
        SKBitmap Render(DesignNode n){using var renderer=new DesignRenderer();return SKBitmap.Decode(renderer.ExportPng(new LayoutEngine(renderer).Arrange(Doc(n).Root),1));}
        bool Red(SKBitmap image,int x,int y)=>image.GetPixel(x,y).Red>220&&image.GetPixel(x,y).Green<40;
        Test("default settings match shape semantics",()=>{var s=StrokeStyle.Read(new DesignNode());Check(s.Thickness==1&&s.MiterLimit==10&&s.StartCap==DesignLineCap.Flat&&s.Dashes.IsEmpty);});
        Test("odd dash arrays repeat to even length",()=>Check(new StrokeStyle(DashArray:"2,3,5").Dashes.SequenceEqual(new double[]{2,3,5,2,3,5})));
        Test("negative dash lengths are magnitudes",()=>Check(new StrokeStyle(DashArray:"-2,-3").Dashes.SequenceEqual(new double[]{2,3})));
        Test("invariant whitespace exponent parsing",()=>Check(new StrokeStyle(DashArray:"2e0\t3\n0,4").Dashes.SequenceEqual(new double[]{2,3,0,4})));
        Test("empty array is solid",()=>Check(new StrokeStyle(DashArray:"  ").GetDashes(10).Single()==new StrokeDashSpan(0,10)));
        Test("stroke width scales dash intervals",()=>{var spans=new StrokeStyle(2,DashArray:"2,1").GetDashes(10);Check(spans.SequenceEqual(new[]{new StrokeDashSpan(0,4),new(6,10)}));});
        Test("positive offset consumes pattern",()=>{var spans=new StrokeStyle(2,DashArray:"2 1",DashOffset:1).GetDashes(10);Check(spans[0]==new StrokeDashSpan(0,2));Check(spans[1]==new StrokeDashSpan(4,8));});
        Test("negative offset wraps pattern",()=>Check(new StrokeStyle(2,DashArray:"2 1",DashOffset:-1).GetDashes(10)[0]==new StrokeDashSpan(2,6)));
        Test("offset wraps without stepping huge cycles",()=>Check(new StrokeStyle(2,DashArray:"2 1",DashOffset:300000001).GetDashes(10)[0]==new StrokeDashSpan(0,2)));
        Test("zero-length dashes are retained as dots",()=>Check(new StrokeStyle(2,DashArray:"0 2").GetDashes(10).Select(s=>s.Start).SequenceEqual(new double[]{0,4,8})));
        Test("zero gaps coalesce painted intervals",()=>Check(new StrokeStyle(2,DashArray:"2 0").GetDashes(10).Single()==new StrokeDashSpan(0,10)));
        Test("zero thickness creates no intervals",()=>Check(new StrokeStyle(0,DashArray:"2 1").GetDashes(100).IsEmpty));
        Test("all-zero patterns reject instead of looping",()=>Reject(()=>new StrokeStyle(DashArray:"0,0").Validate()));
        Test("nonfinite dash values reject",()=>Reject(()=>new StrokeStyle(DashArray:"NaN,2").Validate()));
        Test("negative thickness rejects",()=>Reject(()=>new StrokeStyle(-1).Validate()));
        Test("nonfinite thickness rejects",()=>Reject(()=>new StrokeStyle(double.PositiveInfinity).Validate()));
        Test("miter limit below one rejects",()=>Reject(()=>new StrokeStyle(MiterLimit:.5).Validate()));
        Test("nonfinite dash offset rejects",()=>Reject(()=>new StrokeStyle(DashOffset:double.NaN).Validate()));
        Test("numeric enum aliases reject",()=>Reject(()=>StrokeStyle.Read(Node().Set("StrokeLineJoin","0"))));
        Test("unknown cap rejects",()=>Reject(()=>StrokeStyle.Read(Node().Set("StrokeEndLineCap","Spiky"))));
        Test("too many dash values reject",()=>Reject(()=>new StrokeStyle(DashArray:string.Join(' ',Enumerable.Repeat("1",129))).Validate()));
        Test("subpixel explosion budget rejects",()=>Reject(()=>new StrokeStyle(1,DashArray:"0.000001 0.000001").GetDashes(100)));
        Test("zero native stroke is not a hairline",()=>{using var p=Outline("M30 50L230 50",new(0));Check(p.IsEmpty);});
        foreach(var cap in Enum.GetValues<DesignLineCap>())
            Test("native solid cap "+cap,()=>{using var p=Outline("M30 50L230 50",new(10,cap,cap));Near(cap==DesignLineCap.Flat?30:25,p.Bounds.Left);Near(cap==DesignLineCap.Flat?230:235,p.Bounds.Right);Check(p.Contains(50,50));});
        Test("asymmetric flat and square caps",()=>{using var p=Outline("M30 50L230 50",new(10,DesignLineCap.Flat,DesignLineCap.Square));Near(30,p.Bounds.Left);Near(235,p.Bounds.Right);Check(!p.Contains(27,50)&&p.Contains(233,50));});
        Test("asymmetric triangle and round caps",()=>{using var p=Outline("M30 50L230 50",new(10,DesignLineCap.Triangle,DesignLineCap.Round));Check(p.Contains(27,50)&&!p.Contains(27,46));Check(p.Contains(233,50)&&!p.Contains(234,46));});
        Test("caps rotate with endpoint tangent",()=>{using var p=Outline("M50 30L50 130",new(10,DesignLineCap.Triangle,DesignLineCap.Square));Check(p.Contains(50,27)&&p.Contains(54,133));Check(!p.Contains(54,27));});
        Test("dashes produce true holes",()=>{using var p=Outline("M30 50L230 50",new(10,DashArray:"2 2"));Check(p.Contains(40,50)&&!p.Contains(60,50)&&p.Contains(80,50));});
        Test("round zero-length dots render",()=>{using var p=Outline("M30 50L230 50",new(10,DashCap:DesignLineCap.Round,DashArray:"0 2"));Check(p.Contains(50,50)&&p.Contains(53,50)&&!p.Contains(60,50));});
        Test("square zero-length dots render",()=>{using var p=Outline("M30 50L230 50",new(10,DashCap:DesignLineCap.Square,DashArray:"0 2"));Check(p.Contains(54,54)&&!p.Contains(56,50));});
        Test("triangle dash caps form diamonds",()=>{using var p=Outline("M30 50L230 50",new(10,DashCap:DesignLineCap.Triangle,DashArray:"0 2"));Check(p.Contains(53,50)&&!p.Contains(53,53));});
        Test("open ends use line caps not dash caps",()=>{using var p=Outline("M30 50L130 50",new(10,DesignLineCap.Triangle,DesignLineCap.Square,DesignLineCap.Flat,DashArray:"2 2"));Check(p.Contains(27,50)&&p.Contains(133,53)&&!p.Contains(53,50));});
        Test("gap at open start receives no phantom end cap",()=>{using var p=Outline("M30 50L130 50",new(10,DesignLineCap.Round,DesignLineCap.Round,DashArray:"1 2",DashOffset:1));Check(!p.Contains(30,50)&&p.Contains(55,50));});
        Test("a positive dash starting at contour end is not a phantom cap",()=>{using var p=Outline("M30 50L70 50",new(10,DesignLineCap.Round,DesignLineCap.Round,DashArray:"2 2"));Check(p.Contains(35,50)&&!p.Contains(70,50)&&!p.Contains(73,50));});
        Test("a genuine zero dash at contour end keeps its cap",()=>{using var p=Outline("M30 50L70 50",new(10,DesignLineCap.Round,DesignLineCap.Round,DesignLineCap.Round,DashArray:"0 2"));Check(p.Contains(70,50)&&p.Contains(73,50));});
        Test("pattern restarts at each contour",()=>{using var p=Outline("M30 40L230 40M30 80L230 80",new(10,DashArray:"2 2"));Check(p.Contains(40,40)&&p.Contains(40,80)&&!p.Contains(60,80));});
        Test("closed solid paths ignore endpoint caps",()=>{using var a=Outline("M30 30L130 30L130 130L30 130Z",new(10));using var b=Outline("M30 30L130 30L130 130L30 130Z",new(10,DesignLineCap.Triangle,DesignLineCap.Round));Check(a.Contains(26,26)==b.Contains(26,26));Check(!b.Contains(80,80));});
        Test("closed dash crossing seam has a continuous join",()=>{using var p=Outline("M30 30L130 30L130 130L30 130Z",new(10,DashArray:"12 8",DashOffset:2));Check(p.Contains(27,27)&&p.Contains(31,31));});
        Test("native miter round bevel joins differ",()=>{using var m=Outline("M30 100L100 100L100 30",new(20));using var b=Outline("M30 100L100 100L100 30",new(20,LineJoin:DesignLineJoin.Bevel));using var r=Outline("M30 100L100 100L100 30",new(20,LineJoin:DesignLineJoin.Round));Check(m.Contains(108,108)&&!b.Contains(108,108)&&!r.Contains(108,108));Check(r.Contains(106,106)&&!b.Contains(106,106));});
        Test("miter limit suppresses spikes",()=>{using var a=Outline("M30 100L100 100L100 30",new(20,MiterLimit:10));using var b=Outline("M30 100L100 100L100 30",new(20,MiterLimit:1));Check(a.Contains(109,109)&&!b.Contains(109,109));});
        Test("curved independent caps do not flatten centerline",()=>{using var p=Outline("M30 60C70 10 150 10 200 60",new(10,DesignLineCap.Round,DesignLineCap.Triangle));Check(p.PointCount>10&&p.Bounds.Top<30);});
        Test("multiple solid contours use each local cap direction",()=>{using var p=Outline("M30 30L130 30M60 60L60 130",new(10,DesignLineCap.Square,DesignLineCap.Triangle));Check(p.Contains(27,34)&&p.Contains(60,133)&&!p.Contains(64,133));});
        Test("outline cache returns the same geometry",()=>{using var cache=new StrokeGeometryCache();var g=Path("M30 50L230 50");var s=new StrokeStyle(10,DashArray:"2 2");var a=cache.Get(g,DMatrix.Identity,s);var b=cache.Get(g,DMatrix.Identity,s with{});Check(ReferenceEquals(a,b)&&cache.Builds==1&&cache.Hits==1);});
        Test("stroke geometry changes with width and cap",()=>{using var cache=new StrokeGeometryCache();var g=Path("M30 50L230 50");cache.Get(g,DMatrix.Identity,new(10));cache.Get(g,DMatrix.Identity,new(20));cache.Get(g,DMatrix.Identity,new(20,DesignLineCap.Round));Check(cache.Builds==3);});
        Test("zero stroke renders no pixels",()=>{using var bitmap=Render(Node().Set("StrokeThickness","0"));Check(!Red(bitmap,70,50));});
        Test("stroke dash pixels match outline holes",()=>{using var bitmap=Render(Node().Set("StrokeDashArray","2 2"));Check(Red(bitmap,40,50)&&!Red(bitmap,60,50));});
        Test("independent caps render their intended pixels",()=>{using var bitmap=Render(Node().Set("StrokeStartLineCap","Triangle").Set("StrokeEndLineCap","Square"));Check(Red(bitmap,27,50)&&!Red(bitmap,27,46)&&Red(bitmap,233,53));});
        Test("native picking rejects painted dash gaps",()=>{var n=Node().Set("StrokeDashArray","2 2");using var renderer=new DesignRenderer();var l=new LayoutEngine(renderer).Arrange(Doc(n).Root);Check(l.HitTest(new(40,50))?.Node.Id==n.Id);Check(l.HitTest(new(60,50)) is null);});
        Test("native picking includes cap outside layout box",()=>{var n=Node("M0 50L260 50").Set("Canvas.Left",20).Set("StrokeStartLineCap","Round");using var renderer=new DesignRenderer();var l=new LayoutEngine(renderer).Arrange(Doc(n).Root);Check(l.HitTest(new(17,50))?.Node.Id==n.Id);});
        Test("transparent declared fill remains hittable",()=>{var n=Node("M30 30L130 30L130 130Z").Set("Fill","Transparent");using var r=new DesignRenderer();Check(new LayoutEngine(r).Arrange(Doc(n).Root).HitTest(new(100,60))?.Node.Id==n.Id);});
        Test("zero native stroke cannot hit",()=>{var n=Node().Set("StrokeThickness","0");using var r=new DesignRenderer();Check(new LayoutEngine(r).Arrange(Doc(n).Root).HitTest(new(50,50)) is null);});
        Test("stroke cache reused across repaint and picking",()=>{var n=Node().Set("StrokeDashArray","2 2");using var r=new DesignRenderer();var l=new LayoutEngine(r).Arrange(Doc(n).Root);r.ExportPng(l,1);var count=r.StrokeBuildCount;l.HitTest(new(40,50));r.ExportPng(l,1);Check(count==r.StrokeBuildCount&&r.StrokeCacheHits>0);});
        Test("recolor keeps cached stroke geometry",()=>{var n=Node();using var r=new DesignRenderer();r.ExportPng(new LayoutEngine(r).Arrange(Doc(n).Root),1);var count=r.StrokeBuildCount;r.ExportPng(new LayoutEngine(r).Arrange(Doc(n.Set("Stroke","Blue")).Root),1);Check(count==r.StrokeBuildCount);});
        Test("shape stroke style cannot leak into later controls",()=>{var n=Node().Set("StrokeDashArray","2 2").Set("StrokeLineJoin","Round");using var r=new DesignRenderer();var doc=Doc(n);doc=doc with{Root=doc.Root with{Children=doc.Root.Children.Add(DesignNode.Create("Border","Box",20,140,200,30).Set("BorderBrush","Red").Set("BorderThickness","2"))}};using var b=SKBitmap.Decode(r.ExportPng(new LayoutEngine(r).Arrange(doc.Root),1));Check(Red(b,80,140));});
        Test("inline gradient stroke is visible",()=>{var n=Node() with{Properties=Node().Properties.Remove("Stroke"),PropertyElements=[$"<Path.Stroke xmlns='{DesignNode.PresentationNamespace}'><LinearGradientBrush><GradientStop Color='Red' Offset='0'/><GradientStop Color='Blue' Offset='1'/></LinearGradientBrush></Path.Stroke>"]};using var b=Render(n);Check(b.GetPixel(40,50).Red>b.GetPixel(200,50).Red);Check(b.GetPixel(200,50).Blue>b.GetPixel(40,50).Blue);});
        Test("native stroke picking respects transformed ancestors",()=>{var n=Node().Set("StrokeDashArray","2 2");var parent=DesignNode.Create("Canvas","Parent",0,0,260,120) with{Rotation=10,Children=[n]};using var r=new DesignRenderer();var l=new LayoutEngine(r).Arrange(Doc(parent).Root);var matrix=l.ById[n.Id].WorldTransform;Check(l.HitTest(matrix.Map(new(40,50)))?.Node.Id==n.Id);Check(l.HitTest(matrix.Map(new(60,50)))?.Node.Id!=n.Id);});
        Test("stroke edits are one atomic history transaction",()=>{var s=new DesignSession(Doc(Node()));var id=s.Document.Root.Children[0].Id;s.Select(id);s.Execute("Stroke",d=>StrokeEditing.Apply(d,[id],new Dictionary<string,string>{{"StrokeDashArray","2 2"},{"StrokeEndLineCap","Round"}}));Check(s.Revision==1&&s.Index.Find(id)!.Get("StrokeDashArray")=="2 2");s.Undo();Check(s.Index.Find(id)!.Get("StrokeDashArray")=="");s.Redo();Check(s.Index.Find(id)!.Get("StrokeEndLineCap")=="Round");});
        Test("invalid stroke draft cannot partially modify selection",()=>{var s=new DesignSession(Doc(Node()));var id=s.Document.Root.Children[0].Id;var before=s.Document;Reject(()=>s.Execute("Bad",d=>StrokeEditing.Apply(d,[id],new Dictionary<string,string>{{"Stroke","Blue"},{"StrokeDashArray","0 0"}})));Check(ReferenceEquals(before,s.Document)&&s.Revision==0);});
        Test("locked ancestors prevent stroke editing",()=>{var d=Doc(Node());d=d with{Root=d.Root with{IsLocked=true}};Reject(()=>StrokeEditing.Apply(d,[d.Root.Children[0].Id],new Dictionary<string,string>{{"Stroke","Blue"}}));});
        Test("unchanged stroke edit retains document reference",()=>{var d=Doc(Node());Check(ReferenceEquals(d,StrokeEditing.Apply(d,[d.Root.Children[0].Id],new Dictionary<string,string>{{"Stroke","Red"}})));});
        Test("stroke settings roundtrip through XAML and native JSON",()=>{var d=Doc(Node().Set("StrokeDashArray","2,3,5").Set("StrokeStartLineCap","Triangle").Set("StrokeMiterLimit","2").Set("StrokeDashOffset","-1"));var x=XamlCodec.Parse(XamlCodec.Write(d)).Document;var n=NativeDocumentCodec.Read(NativeDocumentCodec.Write(d));Check(StrokeStyle.Read(x.Root.Children[0])==StrokeStyle.Read(d.Root.Children[0]));Check(StrokeStyle.Read(n.Root.Children[0])==StrokeStyle.Read(d.Root.Children[0]));});
        Test("unresolved stroke setting is preserved but not guessed",()=>{var d=Doc(Node().Set("StrokeDashOffset","{Binding Phase}"));DocumentValidator.Validate(d);Check(XamlCodec.Write(d).Contains("{Binding Phase}"));Reject(()=>StrokeStyle.Read(d.Root.Children[0]));});
        Test("outline conversion preserves identity and pixels",()=>{var n=Node().Set("StrokeDashArray","2 2").Set("StrokeDashCap","Round");var s=new DesignSession(Doc(n));s.Select(n.Id);using var r=new DesignRenderer();var before=r.ExportPng(new LayoutEngine(r).Arrange(s.Document.Root),1);StrokeCommands.Outline(s,new LayoutEngine(r).Arrange(s.Document.Root));var after=r.ExportPng(new LayoutEngine(r).Arrange(s.Document.Root),1);using var a=SKBitmap.Decode(before);using var b=SKBitmap.Decode(after);Check(s.Selection.Contains(n.Id)&&s.Index.Find(n.Id)!.Get("Stroke")=="");for(var x=20;x<245;x++)Check(Red(a,x,50)==Red(b,x,50));s.Undo();Check(s.Index.Find(n.Id)!.Get("StrokeDashArray")=="2 2");});
        Test("outline conversion protects existing fill",()=>{var n=Node("M30 30L130 30L130 130Z").Set("Fill","Blue");var s=new DesignSession(Doc(n));s.Select(n.Id);Reject(()=>StrokeCommands.Outline(s,new LayoutEngine().Arrange(s.Document.Root)));Check(!s.CanUndo);});
        Test("outline conversion protects state references",()=>{var n=Node();var d=Doc(n) with{States=[new("Hover",[new(n.Id,"Opacity","0.5")])]};var s=new DesignSession(d);s.Select(n.Id);Reject(()=>StrokeCommands.Outline(s,new LayoutEngine().Arrange(s.Document.Root)));});
        Test("stroke cache bounded under style churn",()=>{using var cache=new StrokeGeometryCache();var g=Path("M30 50L230 50");for(var i=0;i<180;i++)cache.Get(g,DMatrix.Identity,new(1+i*.05));Check(cache.EstimatedBytes<=16*1024*1024);});
        return(passed,failed);
    }
}
