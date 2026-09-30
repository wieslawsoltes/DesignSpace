using System.Collections.Immutable;
using System.Text.Json;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Rendering.Skia;
using SkiaSharp;

internal static class EffectScaleTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;long allocated=0;
        void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS effect scale: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL effect scale: "+name+": "+e);}}
        void Check(bool value){if(!value)throw new Exception("Assertion failed");}
        void Reject(Action run){try{run();}catch{return;}throw new Exception("Expected rejection");}
        var box=new DesignEffect{Radius=3,Kernel=DesignBlurKernel.Box};
        var xml=EffectCodec.Write(box);
        DesignDocument Document(DesignEffect effect)
        {
            var child=DesignNode.Create("Rectangle","Card",50,50,40,40).Set("Fill","Red");
            var document=new DesignDocument{Root=DesignNode.Create("Canvas","Root",0,0,180,160).Set("Background","White") with{Children=[child]}};
            return EffectEditing.Apply(document,[child.Id],effect);
        }
        SKBitmap Draw(DesignEffect effect,double x,double y,EffectRenderingMode mode=EffectRenderingMode.Native)
        {
            var document=Document(effect);using var renderer=new DesignRenderer{EffectMode=mode};
            var bitmap=new SKBitmap((int)(180*x),(int)(160*y));using var canvas=new SKCanvas(bitmap);
            canvas.Scale((float)x,(float)y);renderer.DrawScene(canvas,new LayoutEngine(renderer).Arrange(document.Root));
            Check(renderer.Diagnostics.Count==0);return bitmap;
        }
        Test("two-times export retains the Box halo in design units",()=>
        {
            using var renderer=new DesignRenderer();var layout=new LayoutEngine(renderer).Arrange(Document(box).Root);
            using var one=SKBitmap.Decode(renderer.ExportPng(layout,1));using var two=SKBitmap.Decode(renderer.ExportPng(layout,2));
            Check(one.GetPixel(48,70).Green<250);Check(two.GetPixel(96,140).Green<250);
            Check(one.GetPixel(44,70)==SKColors.White&&two.GetPixel(88,140)==SKColors.White);
        });
        Test("nonuniform device scale expands both Box axes independently",()=>
        {
            using var bitmap=Draw(box,2,3);
            Check(bitmap.GetPixel(96,210).Green<250&&bitmap.GetPixel(140,144).Green<250);
            Check(bitmap.GetPixel(86,210)==SKColors.White&&bitmap.GetPixel(140,129)==SKColors.White);
        });
        Test("finite Gaussian software-reference taps scale with output density",()=>
        {
            using var bitmap=Draw(new(){Radius=9},2,2,EffectRenderingMode.WpfSoftwareCompatible);
            Check(bitmap.GetPixel(92,140).Green<250);Check(bitmap.GetPixel(78,140)==SKColors.White);
        });
        Test("zoom and host DPI combine instead of being applied twice",()=>
        {
            using var renderer=new DesignRenderer();var layout=new LayoutEngine(renderer).Arrange(Document(box).Root);
            using var bitmap=new SKBitmap(540,480);using var canvas=new SKCanvas(bitmap);canvas.Scale(2,2);
            var view=new DesignViewport{Zoom=1.5,PanX=0,PanY=0,ShowRulers=false};
            renderer.Draw(canvas,270,240,layout,view,ImmutableHashSet<Guid>.Empty);
            Check(bitmap.GetPixel(144,210).Green<250&&bitmap.GetPixel(129,210)==SKColors.White);
        });
        Test("distinct Box device radii have independent native filters",()=>
        {
            using var cache=new EffectFilterCache();var one=cache.Get(xml);var two=cache.Get(xml,scaleX:2,scaleY:2);
            Check(one is not null&&two is not null&&!ReferenceEquals(one,two)&&cache.Builds==2&&cache.Parses==1);
            Check(ReferenceEquals(one,cache.Get(xml))&&ReferenceEquals(two,cache.Get(xml,scaleX:2,scaleY:2)));
        });
        Test("equivalent quantized radii reuse their filter",()=>
        {
            using var cache=new EffectFilterCache();var a=cache.Get(xml,scaleX:1.1,scaleY:1.1);var b=cache.Get(xml,scaleX:1.2,scaleY:1.2);
            Check(ReferenceEquals(a,b)&&cache.Builds==1);
        });
        Test("native Gaussian and shadow keep transform-independent cache keys",()=>
        {
            using var cache=new EffectFilterCache();
            foreach(var effect in new[]{new DesignEffect{Radius=5},new(){Kind=DesignEffectKind.DropShadow,Radius=5}})
            {
                var raw=EffectCodec.Write(effect);var first=cache.Get(raw);
                Check(ReferenceEquals(first,cache.Get(raw,scaleX:2,scaleY:3)));
            }
            Check(cache.Builds==2);
        });
        Test("software-reference radius uses truncated local radius and minimum scale",()=>
        {
            using var cache=new EffectFilterCache();var raw=EffectCodec.Write(box with{Radius=3.9});
            var a=cache.Get(raw,EffectRenderingMode.WpfSoftwareCompatible,2,3);
            var b=cache.Get(raw,EffectRenderingMode.WpfSoftwareCompatible,2.1,2.1);
            Check(ReferenceEquals(a,b)&&cache.Builds==1);
        });
        Test("zero device scale retains a defined no-op",()=>
        {
            using var filter=EffectFilterCache.Create(box,scaleX:0,scaleY:0);Check(filter is null);
        });
        foreach(var scale in new[]{double.NaN,double.PositiveInfinity,-1d})Test("invalid device scale rejects "+scale,()=>Reject(()=>EffectFilterCache.Create(box,scaleX:scale)));
        Test("maximum device kernel is representable by the pinned backend",()=>
        {
            using var filter=EffectFilterCache.Create(box with{Radius=1},scaleX:EffectFilterCache.MaxDeviceRadius,scaleY:1);Check(filter is not null);
        });
        Test("oversized device kernel rejects instead of silently removing the effect",()=>Reject(()=>EffectFilterCache.Create(box with{Radius=128},scaleX:16,scaleY:16)));
        Test("design-unit radius budget remains unchanged",()=>Reject(()=>EffectFilterCache.Create(box with{Radius=129})));
        Test("maximum Gaussian device taps remain finite symmetric and normalized",()=>
        {
            var taps=WpfEffectMath.GaussianKernel(EffectFilterCache.MaxDeviceRadius);
            Check(taps.Length==2047&&taps.All(float.IsFinite)&&taps.SequenceEqual(taps.Reverse())&&Math.Abs(taps.Sum(x=>(double)x)-1)<1e-6);
        });
        Test("host matrix retrieval preserves the caller canvas state",()=>
        {
            using var cache=new EffectFilterCache();using var bitmap=new SKBitmap(40,40);using var canvas=new SKCanvas(bitmap);
            canvas.Translate(5,7);canvas.Scale(2,3);var matrix=canvas.TotalMatrix;var count=canvas.SaveCount;
            var a=cache.GetForCanvas(xml,canvas);var b=cache.Get(xml,scaleX:2,scaleY:3);
            Check(ReferenceEquals(a,b)&&canvas.TotalMatrix==matrix&&canvas.SaveCount==count);
        });
        Test("scaled cache queries allocate no managed objects after warmup",()=>
        {
            using var cache=new EffectFilterCache();for(var i=0;i<20;i++)cache.Get(xml,scaleX:2,scaleY:3);
            var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<10000;i++)cache.Get(xml,scaleX:2,scaleY:3);
            allocated=GC.GetAllocatedBytesForCurrentThread()-before;Check(allocated==0&&cache.Builds==1&&cache.Parses==1);
        });
        Directory.CreateDirectory("artifacts/verification");
        File.WriteAllText("artifacts/verification/effect-scale-results.json",JsonSerializer.Serialize(new{passed,failed,warmQueries=10000,allocatedBytes=allocated,
            scope="Device-density/zoom scaling for separable convolution; not complete rotated/skewed WPF raster equivalence."}));
        return(passed,failed);
    }
}
