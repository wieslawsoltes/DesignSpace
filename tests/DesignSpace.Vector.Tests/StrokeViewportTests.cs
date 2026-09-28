using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Rendering.Skia;
using SkiaSharp;

internal static class StrokeViewportTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS stroke viewport: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL stroke viewport: "+name+": "+e);}}
        void Check(bool condition){if(!condition)throw new Exception("Assertion failed");}
        DesignNode Node(double x=0)=>DesignNode.Create("Path","Stroke",x,0,120,100).Set("Data","M0 50L100 50").Set("Stroke","Red").Set("StrokeThickness",10).Set("StrokeStartLineCap","Round");
        DesignNode Root(params DesignNode[] nodes)=>DesignNode.Create("Canvas","Root",0,0,160,100).Set("Background","White") with{Children=nodes.ToImmutableArray()};
        SKBitmap Render(DesignRenderer renderer,DesignNode root)=>SKBitmap.Decode(renderer.ExportPng(new LayoutEngine(renderer).Arrange(root),1));
        bool Red(SKBitmap bitmap,int x,int y){var p=bitmap.GetPixel(x,y);return p.Red>230&&p.Green<30&&p.Blue<30;}
        Test("offscreen strokes reject before building native outlines",()=>
        {
            using var r=new DesignRenderer();using var b=Render(r,Root(Node(1000).Set("StrokeDashArray","2 2")));
            Check(r.CulledShapeDraws==1&&r.StrokeBuildCount==0&&!Red(b,80,50));
        });
        Test("offscreen filled shapes reject before native path construction",()=>
        {
            var n=Node(1000).Set("Data","M0 0L100 0L100 80Z").Set("Fill","Blue");
            n=n with{Properties=n.Properties.Remove("Stroke")};using var r=new DesignRenderer();using var b=Render(r,Root(n));
            Check(r.CulledShapeDraws==1&&r.PathBuildCount==0&&r.StrokeBuildCount==0);
        });
        Test("caps outside layout box stay visible and hittable",()=>
        {
            var n=Node(162);using var r=new DesignRenderer();using var b=Render(r,Root(n));
            Check(Red(b,159,50)&&r.CulledShapeDraws==0);
            Check(new LayoutEngine(r).Arrange(Root(n)).HitTest(new(159,50))?.Node.Id==n.Id);
        });
        Test("miter extending beyond geometry bounds is not culled",()=>
        {
            var n=Node().Set("Data","M140 120L170 70L140 20").Set("StrokeThickness",20).Set("StrokeMiterLimit",10);
            using var r=new DesignRenderer();using var surface=SKSurface.Create(new SKImageInfo(200,110));surface.Canvas.ClipRect(new SKRect(175,60,190,80));
            r.DrawScene(surface.Canvas,new LayoutEngine(r).Arrange(Root(n).Set("Width",200)));
            using var image=surface.Snapshot();using var bitmap=SKBitmap.FromImage(image);
            Check(Red(bitmap,178,70)&&r.CulledShapeDraws==0);
        });
        Test("offscreen instance does not evict shared visible geometry",()=>
        {
            var visible=Node();var hidden=Node(1000).Set(DesignNode.NameKey,"Offscreen");using var r=new DesignRenderer();
            using var a=Render(r,Root(visible,hidden));using var b=Render(r,Root(visible,hidden));
            Check(r.StrokeBuildCount==1&&r.CulledShapeDraws==2&&r.StrokeCacheHits>0);
        });
        Test("returning a previously offscreen node builds its stroke once",()=>
        {
            var n=Node(1000);using var r=new DesignRenderer();using var a=Render(r,Root(n));
            using var b=Render(r,Root(n.Set("Canvas.Left",30)));using var c=Render(r,Root(n.Set("Canvas.Left",40)));
            Check(r.StrokeBuildCount==1&&Red(b,70,50)&&Red(c,80,50));
        });
        Test("outline generation budget is not spent on clipped shapes",()=>
        {
            var n=Node(1000).Set("StrokeDashArray","0.000001 0.000001");using var r=new DesignRenderer();using var b=Render(r,Root(n));
            Check(r.StrokeBuildCount==0&&r.CulledShapeDraws==1&&r.Diagnostics.Count==0);
        });
        Test("clipped parent still rejects a stroke despite wide child layout",()=>
        {
            var child=Node().Set("Data","M80 50L100 50").Set("StrokeLineJoin","Round");var parent=DesignNode.Create("Canvas","Parent",0,0,30,100).Set("ClipToBounds","True") with{Children=[child]};
            using var r=new DesignRenderer();using var b=Render(r,Root(parent));
            // Default miter bounds are conservative; no visible cap is rejected.
            Check(!Red(b,90,50)&&r.StrokeBuildCount==0&&r.CulledShapeDraws==1);
        });
        return(passed,failed);
    }
}
