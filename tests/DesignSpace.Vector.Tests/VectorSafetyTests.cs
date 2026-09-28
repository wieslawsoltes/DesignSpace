using System.Xml.Linq;
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
        return(passed,failed);
    }
}
