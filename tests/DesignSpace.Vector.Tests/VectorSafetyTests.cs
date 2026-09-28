using System.Xml.Linq;
using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Rendering.Skia;
using SkiaSharp;

internal static class VectorSafetyTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS geometry safety: "+name);}catch(Exception ex){failed++;Console.Error.WriteLine("FAIL geometry safety: "+name+": "+ex);}}
        void Check(bool condition){if(!condition)throw new Exception("Assertion failed");}
        void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Expected a geometry diagnostic, not a runtime exception.");}
        Test("null figure is rejected at validation boundary",()=>Reject(()=>VectorPathCodec.Validate(new([null!]))));
        foreach(var markup in new[]{
            "<RectangleGeometry Rect='0,0,10,10 M20,20'/>",
            "<EllipseGeometry Center='0,0,10,10' RadiusX='10' RadiusY='10'/>",
            "<PathGeometry><PathFigure><LineSegment Point='10,10' Point1='20,20'/></PathFigure></PathGeometry>",
            "<PathGeometry><PathFigure><PolyLineSegment Points='0,0 L10,10'/></PathFigure></PathGeometry>",
            "<StreamGeometry><Invalid/></StreamGeometry>"})
            Test("reject malformed object "+markup,()=>Reject(()=>VectorGeometry.ReadElement(XElement.Parse("<Root xmlns='"+DesignNode.PresentationNamespace+"'>"+markup+"</Root>").Elements().Single())));
        Test("empty poly point collection is a valid empty contour",()=>
        {
            var xml=XElement.Parse($"<PathGeometry xmlns='{DesignNode.PresentationNamespace}'><PathFigure><PolyLineSegment Points=''/></PathFigure></PathGeometry>");
            Check(VectorGeometry.ReadElement(xml).SegmentCount==0);
        });
        Test("malformed clip is diagnosed and cannot render unclipped",()=>
        {
            var n=DesignNode.Create("Rectangle","Target",20,20,100,100).Set("Fill","Red") with { PropertyElements=[$"<Rectangle.Clip xmlns='{DesignNode.PresentationNamespace}'/>"] };
            var root=DesignNode.Create("Canvas","Root",0,0,200,200).Set("Background","White") with { Children=[n] };
            using var renderer=new DesignRenderer();var layout=new LayoutEngine(renderer).Arrange(root);
            using var bitmap=SKBitmap.Decode(renderer.ExportPng(layout,1));
            Check(bitmap.GetPixel(50,50).Green>220);Check(renderer.Diagnostics.Count==1);Check(layout.HitTest(new(50,50)) is null);
        });
        Test("namespaced segment metadata is not silently ignored",()=>Reject(()=>VectorGeometry.ReadElement(XElement.Parse($"<PathGeometry xmlns='{DesignNode.PresentationNamespace}' xmlns:q='urn:custom'><PathFigure><LineSegment Point='10,10' q:Point='20,20'/></PathFigure></PathGeometry>"))));
        Test("placed instances share one native path instead of exhausting the cache",()=>
        {
            var nodes=Enumerable.Range(0,300).Select(i=>DesignNode.Create("Path","P"+i,i%20*10,i/20*10,8,8).Set("Data","M0 0H8V8H0Z").Set("Fill","Red")).ToImmutableArray();
            var root=DesignNode.Create("Canvas","Root",0,0,220,180) with { Children=nodes };
            using var renderer=new DesignRenderer();var layout=new LayoutEngine(renderer).Arrange(root);
            renderer.ExportPng(layout,1);Check(renderer.PathBuildCount==1);renderer.ExportPng(layout,1);Check(renderer.PathBuildCount==1);Check(renderer.PathCacheHits>=599);
        });
        Test("moving paths does not rebuild their native geometry",()=>
        {
            var shape=DesignNode.Create("Path","P",0,20,80,80).Set("Data","M0 0H80V80H0Z").Set("Fill","Red");
            var root=DesignNode.Create("Canvas","Root",0,0,220,180);
            using var renderer=new DesignRenderer();var engine=new LayoutEngine(renderer);
            for(var i=0;i<12;i++)renderer.ExportPng(engine.Arrange(root with { Children=[shape.Set("Canvas.Left",i*5)] }),1);
            Check(renderer.PathBuildCount==1);
        });
        Test("local path caching preserves translated gradient coordinates",()=>
        {
            var shape=DesignNode.Create("Path","P",50,60,100,100).Set("Data","M0 0H100V100H0Z") with
            {
                PropertyElements=[$"<Path.Fill xmlns='{DesignNode.PresentationNamespace}'><LinearGradientBrush StartPoint='0,0' EndPoint='1,0'><GradientStop Offset='0' Color='Red'/><GradientStop Offset='1' Color='Blue'/></LinearGradientBrush></Path.Fill>"]
            };
            var root=DesignNode.Create("Canvas","Root",0,0,220,180) with { Children=[shape] };
            using var renderer=new DesignRenderer();using var bitmap=SKBitmap.Decode(renderer.ExportPng(new LayoutEngine(renderer).Arrange(root),1));
            Check(bitmap.GetPixel(55,100).Red>220);Check(bitmap.GetPixel(145,100).Blue>220);
        });
        return(passed,failed);
    }
}
