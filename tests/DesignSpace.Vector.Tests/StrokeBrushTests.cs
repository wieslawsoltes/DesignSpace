using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Rendering.Skia;
using SkiaSharp;

internal static class StrokeBrushTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS stroke brush: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL stroke brush: "+name+": "+e);}}
        void Check(bool value){if(!value)throw new Exception("Assertion failed");}
        DesignNode Node()=>DesignNode.Create("Path","Path",0,0,100,50).Set("Data","M10 25L90 25").Set("Stroke","Red").Set("StrokeThickness",10);
        DesignDocument Doc(DesignNode n)=>new(){Root=DesignNode.Create("Canvas","Root",0,0,120,80) with{Children=[n]}};
        Test("empty and x:Null brushes are absent",()=>{Check(!StrokeStyle.HasBrush(Node().Set("Stroke",""),"Stroke"));Check(!StrokeStyle.HasBrush(Node().Set("Stroke","{x:Null}"),"Stroke"));});
        Test("transparent brush remains a brush",()=>Check(StrokeStyle.HasBrush(Node().Set("Stroke","Transparent"),"Stroke")));
        Test("property-element null is absent",()=>{var n=Node() with{PropertyElements=[$"<Path.Stroke xmlns='{DesignNode.PresentationNamespace}' xmlns:x='{DesignNode.XamlNamespace}'><x:Null/></Path.Stroke>"]};Check(!StrokeStyle.HasBrush(n,"Stroke"));});
        Test("empty brush editing removes the XAML attribute",()=>{var n=Node();var d=StrokeEditing.Apply(Doc(n),[n.Id],new Dictionary<string,string>{{"Stroke",""}});Check(!d.Root.Children[0].Properties.ContainsKey("Stroke"));});
        Test("removing an absent brush is a no-op",()=>{var n=Node();n=n with{Properties=n.Properties.Remove("Stroke")};var d=Doc(n);Check(ReferenceEquals(d,StrokeEditing.Apply(d,[n.Id],new Dictionary<string,string>{{"Stroke",""}})));});
        Test("native picker does not select a null stroke",()=>{var n=Node().Set("Stroke","{x:Null}");using var r=new DesignRenderer();Check(new LayoutEngine(r).Arrange(Doc(n).Root).HitTest(new(50,25)) is null);});
        Test("native picker retains transparent stroke hit geometry",()=>{var n=Node().Set("Stroke","Transparent");using var r=new DesignRenderer();Check(new LayoutEngine(r).Arrange(Doc(n).Root).HitTest(new(50,25))?.Node.Id==n.Id);});
        Test("multi-shape edit preserves selection-local identities",()=>{var a=Node();var b=Node().Set(DesignNode.NameKey,"Other");var d=Doc(a);d=d with{Root=d.Root with{Children=[a,b]}};var updated=StrokeEditing.Apply(d,[a.Id,b.Id],new Dictionary<string,string>{{"StrokeDashArray","3 2"}});Check(updated.Root.Children.All(n=>n.Get("StrokeDashArray")=="3 2"));Check(updated.Root.Children[0].Id==a.Id&&updated.Root.Children[1].Id==b.Id);});
        Test("warm stroke lookups allocate a bounded amount",()=>{using var cache=new StrokeGeometryCache();var path=VectorPathCodec.Parse("M0 0C30 60 80 60 100 0");var style=new StrokeStyle(4,DashCap:DesignLineCap.Round,DashArray:"2 2");var original=cache.Get(path,DMatrix.Identity,style);for(var i=0;i<20;i++)cache.Get(path,DMatrix.Identity,style);var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)Check(ReferenceEquals(original,cache.Get(path,DMatrix.Identity,style)));var allocation=GC.GetAllocatedBytesForCurrentThread()-before;Check(allocation<16384);Check(cache.Builds==1);Console.WriteLine("STROKE WARM LOOKUPS allocated bytes: "+allocation);});
        Test("placed rectangles share native stroke geometry",()=>{var nodes=Enumerable.Range(0,80).Select(i=>DesignNode.Create("Rectangle","R"+i,i%10*10,i/10*10,8,8).Set("Stroke","Red")).ToImmutableArray();using var r=new DesignRenderer();var d=Doc(Node());d=d with{Root=d.Root with{Children=nodes}};r.ExportPng(new LayoutEngine(r).Arrange(d.Root),1);Check(r.StrokeBuildCount==1);});
        Test("rectangle recolor and opacity edits reuse warm geometry",()=>{var n=DesignNode.Create("Rectangle","Rect",10,10,60,30).Set("Stroke","Red");using var r=new DesignRenderer();r.ExportPng(new LayoutEngine(r).Arrange(Doc(n).Root),1);var count=r.StrokeBuildCount;for(var i=0;i<20;i++){var next=n.Set("Canvas.Left",i).Set("Stroke",i%2==0?"Red":"Blue").Set("Opacity",0.5+i*0.01);r.ExportPng(new LayoutEngine(r).Arrange(Doc(next).Root),1);}Check(r.StrokeBuildCount==count);});
        return(passed,failed);
    }
}
