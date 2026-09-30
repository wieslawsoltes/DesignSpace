using System.Collections.Immutable;
using System.Text.Json;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Rendering.Skia;
using DesignSpace.Xaml;
using SkiaSharp;

internal static class EffectTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;long warmBytes=0;
        void Test(string name,Action test){try{test();passed++;Console.WriteLine("PASS effect: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL effect: "+name+": "+e);}}
        void Check(bool value,string message="Assertion failed"){if(!value)throw new Exception(message);}
        void Reject(Action test){try{test();}catch{return;}throw new Exception("Expected rejection");}
        void Near(double a,double b,double tolerance=1e-8){Check(double.IsFinite(b)&&Math.Abs(a-b)<=tolerance,$"Expected {a}; got {b}");}
        var ns=DesignNode.PresentationNamespace;
        DesignNode Box(string name="Box")=>DesignNode.Create("Rectangle",name,50,50,40,40).Set("Fill","Red");
        DesignDocument Doc(DesignNode child)=>new(){Root=DesignNode.Create("Canvas","Root",0,0,300,220).Set("Background","White") with{Children=[child]}};
        DesignNode With(DesignNode n,DesignEffect e)=>n with{PropertyElements=n.PropertyElements.Add($"<{n.Type}.Effect xmlns='{ns}'>{EffectCodec.Write(e)}</{n.Type}.Effect>")};
        DesignEffect Shadow(double direction=0,double opacity=1)=>new(){Kind=DesignEffectKind.DropShadow,Radius=0,ShadowDepth=50,Direction=direction,Opacity=opacity};
        SKBitmap Render(DesignDocument d){using var r=new DesignRenderer();return SKBitmap.Decode(r.ExportPng(new LayoutEngine(r).Arrange(DesignPreview.Resolve(d.Root)),1));}
        Test("blur defaults and exact literal roundtrip",()=>{var e=EffectCodec.Parse($"<BlurEffect xmlns='{ns}'/>");Check(e==new DesignEffect());Check(EffectCodec.Parse(EffectCodec.Write(e))==e);});
        Test("shadow defaults",()=>{var e=EffectCodec.Parse($"<DropShadowEffect xmlns='{ns}'/>");Check(e.Kind==DesignEffectKind.DropShadow&&e.Direction==315&&e.Radius==5&&e.ShadowDepth==5&&e.Opacity==1);});
        foreach(var k in Enum.GetValues<DesignBlurKernel>())Test("kernel roundtrip "+k,()=>{var e=new DesignEffect{Radius=12.25,Kernel=k,RenderingBias="Quality"};Check(EffectCodec.Parse(EffectCodec.Write(e))==e);});
        Test("shadow roundtrip preserves precision and transparent color",()=>{var e=Shadow(-721.125,.123456789) with{Color="#00123456"};Check(EffectCodec.Parse(EffectCodec.Write(e))==e);});
        foreach(var (angle,x,y) in new[]{(0d,50d,0d),(90,0,-50),(180,-50,0),(270,0,50),(360,50,0),(-90,0,50)})
            Test("counterclockwise direction "+angle,()=>{var p=Shadow(angle).ShadowOffset;Near(x,p.X);Near(y,p.Y);});
        foreach(var xml in new[]{"<CustomEffect/>",$"<BlurEffect xmlns='{ns}' Radius='NaN'/>",$"<BlurEffect xmlns='{ns}' Radius='129'/>",$"<BlurEffect xmlns='{ns}' KernelType='1'/>",$"<BlurEffect xmlns='{ns}' Radius='{{Binding Radius}}'/>",$"<BlurEffect xmlns='{ns}' Extra='1'/>",$"<BlurEffect xmlns='{ns}'><BlurEffect.Radius>2</BlurEffect.Radius></BlurEffect>","<BlurEffect xmlns='urn:other'/>",$"<BlurEffect xmlns='{ns}' xmlns:x='{DesignNode.XamlNamespace}' x:Name='Referenced'/>","Not XML"})
            Test("unsupported metadata rejects "+xml,()=>Reject(()=>EffectCodec.Parse(xml)));
        Test("effect XML cannot use external entities",()=>Reject(()=>EffectCodec.Parse($"<!DOCTYPE BlurEffect [<!ENTITY x SYSTEM 'file:///no-read'>]><BlurEffect xmlns='{ns}' Radius='&x;'/>") ));
        Test("oversized effect rejects before parsing",()=>Reject(()=>EffectCodec.Parse(new string(' ',16385))));
        foreach(var e in new[]{new DesignEffect{Radius=-1},new(){ShadowDepth=10001},new(){Opacity=2},new(){Direction=double.NaN},new(){Color="{Binding Color}"},new(){RenderingBias="Unknown"}})
            Test("invalid descriptor "+e,()=>Reject(e.Validate));
        Test("zero blur uses no filter",()=>{using var f=EffectFilterCache.Create(new(){Radius=0});Check(f is null);});
        Test("zero opacity shadow uses no filter",()=>{using var f=EffectFilterCache.Create(Shadow(0,0));Check(f is null);});
        foreach(var (angle,x,y) in new[]{(0d,120,70),(90,70,20),(180,20,70),(270,70,120)})
            Test("hard shadow paints the correct direction "+angle,()=>{using var b=Render(Doc(With(Box(),Shadow(angle))));Check(b.GetPixel(x,y).Red<10);Check(b.GetPixel(70,70).Red>245&&b.GetPixel(70,70).Green<10);});
        Test("shadow opacity composites once",()=>{using var b=Render(Doc(With(Box(),Shadow(0,.5))));Near(127,b.GetPixel(120,70).Red,2);});
        Test("shadow color alpha does not suppress its RGB",()=>{using var b=Render(Doc(With(Box(),Shadow() with{Color="#000000FF"})));var p=b.GetPixel(120,70);Check(p.Blue>245&&p.Red<10);});
        foreach(var kernel in Enum.GetValues<DesignBlurKernel>())Test("blur softens a hard edge "+kernel,()=>{using var b=Render(Doc(With(Box(),new(){Radius=9,Kernel=kernel})));var edge=b.GetPixel(49,70);Check(edge.Red>240&&edge.Green>40&&edge.Green<245);Check(b.GetPixel(70,70).Green<10);Check(b.GetPixel(30,70).Green>245);});
        Test("transparent source casts no opaque rectangle shadow",()=>{using var b=Render(Doc(With(Box().Set("Fill","Transparent"),Shadow())));Check(b.GetPixel(120,70)==SKColors.White);});
        Test("group effect covers child content, not just parent background",()=>{var parent=DesignNode.Create("Canvas","Group",0,0,180,130) with{Children=[Box()]};using var b=Render(Doc(With(parent,Shadow())));Check(b.GetPixel(120,70).Red<10);});
        Test("group opacity affects source and shadow once",()=>{var parent=DesignNode.Create("Canvas","Group",0,0,180,130).Set("Opacity",.5) with{Children=[Box()]};using var b=Render(Doc(With(parent,Shadow())));Near(128,b.GetPixel(70,70).Green,2);Near(128,b.GetPixel(120,70).Red,2);});
        Test("opacity mask is included in the filtered subtree",()=>{var n=With(Box(),Shadow()).Set("OpacityMask","#80000000");using var b=Render(Doc(n));Near(127,b.GetPixel(120,70).Red,2);Near(127,b.GetPixel(70,70).Green,2);});
        Test("ancestor clip cuts a descendant shadow",()=>{var p=DesignNode.Create("Canvas","Group",0,0,100,130).Set("ClipToBounds","True") with{Children=[With(Box(),Shadow())]};using var b=Render(Doc(p));Check(b.GetPixel(120,70)==SKColors.White);});
        Test("shadow outside source layout still renders inside viewport",()=>{var n=Box().Set("Canvas.Left",310);using var b=Render(Doc(With(n,Shadow(180)))) ;Check(b.GetPixel(280,70).Red<10);});
        Test("effect does not enlarge selectable geometry",()=>{var n=With(Box(),Shadow());using var r=new DesignRenderer();var l=new LayoutEngine(r).Arrange(Doc(n).Root);Check(l.HitTest(new(120,70)) is null);Check(l.HitTest(new(70,70))?.Node.Id==n.Id);});
        Test("effect resources resolve at declaration scope",()=>{var n=Box().Set("Effect","{StaticResource Shadow}");var d=Doc(n);d=d with{Root=d.Root with{PropertyElements=[$"<Canvas.Resources xmlns='{ns}' xmlns:x='{DesignNode.XamlNamespace}'><Color x:Key='Tint'>Blue</Color><DropShadowEffect x:Key='Shadow' Color='{{StaticResource Tint}}' BlurRadius='0' ShadowDepth='50' Direction='0'/></Canvas.Resources>"]}};using var b=Render(d);Check(b.GetPixel(120,70).Blue>245&&b.GetPixel(120,70).Red<10);});
        Test("unsupported effect is diagnosed without losing source pixels",()=>{var n=Box() with{PropertyElements=[$"<Rectangle.Effect xmlns='{ns}'><CustomEffect/></Rectangle.Effect>"]};using var r=new DesignRenderer();using var b=SKBitmap.Decode(r.ExportPng(new LayoutEngine(r).Arrange(Doc(n).Root),1));Check(r.Diagnostics.Count==1);Check(b.GetPixel(70,70).Red>245&&b.GetPixel(70,70).Green<10);});
        Test("native and XAML keep effect markup without executing it",()=>{var d=Doc(With(Box(),Shadow(315,.4) with{Radius=12}));foreach(var next in new[]{NativeDocumentCodec.Read(NativeDocumentCodec.Write(d)),XamlCodec.Parse(XamlCodec.Write(d)).Document})Check(new BrushResolver(next.Root).Resolve(next.Root.Children[0].Id,"Effect") is { } xml&&EffectCodec.Parse(xml).Radius==12);});
        Test("effect edits are one transaction with exact undo",()=>{var d=Doc(Box());var session=new DesignSession(d);session.Execute("Effect",v=>EffectEditing.Apply(v,[v.Root.Children[0].Id],Shadow()));Check(session.Revision==1);session.Undo();Check(ReferenceEquals(d,session.Document));session.Redo();Check(session.Document.Root.Children[0].PropertyElements.Length==1);});
        Test("repeated effect application is a no-op",()=>{var d=Doc(Box());d=EffectEditing.Apply(d,[d.Root.Children[0].Id],Shadow());Check(ReferenceEquals(d,EffectEditing.Apply(d,[d.Root.Children[0].Id],Shadow())));});
        Test("remove effect preserves unrelated properties",()=>{var d=Doc(Box().Set("Fill","Blue"));d=EffectEditing.Apply(d,[d.Root.Children[0].Id],Shadow());d=EffectEditing.Apply(d,[d.Root.Children[0].Id],null);Check(d.Root.Children[0].PropertyElements.IsEmpty&&d.Root.Children[0].Get("Fill")=="Blue");});
        Test("one locked target rejects the complete multi-selection edit",()=>{var a=Box();var b=Box("Other") with{IsLocked=true};var d=Doc(a);d=d with{Root=d.Root with{Children=[a,b]}};Reject(()=>EffectEditing.Apply(d,[a.Id,b.Id],Shadow()));Check(d.Root.Children.All(n=>n.PropertyElements.IsEmpty));});
        Test("cached filters reuse native identity",()=>{using var cache=new EffectFilterCache();var xml=EffectCodec.Write(Shadow());var first=cache.Get(xml);Check(ReferenceEquals(first,cache.Get(xml))&&cache.Builds==1&&cache.Hits==1);});
        Test("filter cache entry count stays bounded",()=>{using var cache=new EffectFilterCache();for(var i=0;i<160;i++)cache.Get(EffectCodec.Write(Shadow() with{ShadowDepth=i}));Check(cache.Count<=64);});
        Test("warm filter lookups allocate no managed objects",()=>{using var cache=new EffectFilterCache();var xml=EffectCodec.Write(Shadow());for(var i=0;i<20;i++)cache.Get(xml);var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<10000;i++)cache.Get(xml);warmBytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(warmBytes==0);});
        Test("parent filter survives child cache eviction",()=>{var children=Enumerable.Range(0,80).Select(i=>With(Box("Child"+i),Shadow() with{ShadowDepth=i+1})).ToImmutableArray();var parent=With(DesignNode.Create("Canvas","Group",0,0,200,160) with{Children=children},Shadow(270));using var b=Render(Doc(parent));Check(b.GetPixel(70,120).Red<10);});
        Test("preview effect suppression does not change export or model",()=>{var d=Doc(With(Box(),Shadow()));using var r=new DesignRenderer();var l=new LayoutEngine(r).Arrange(d.Root);using var b=new SKBitmap(300,220);using var c=new SKCanvas(b);c.Clear(SKColors.White);r.DrawScene(c,l,false);Check(b.GetPixel(120,70)==SKColors.White);using var exported=SKBitmap.Decode(r.ExportPng(l,1));Check(exported.GetPixel(120,70).Red<10);});
        Test("host canvas save stack survives filtered rendering",()=>{using var r=new DesignRenderer();using var bitmap=new SKBitmap(300,220);using var c=new SKCanvas(bitmap);var count=c.SaveCount;var d=Doc(With(Box(),Shadow()));r.DrawScene(c,new LayoutEngine(r).Arrange(d.Root));Check(c.SaveCount==count);});
        Directory.CreateDirectory("artifacts/verification");File.WriteAllText("artifacts/verification/effect-results.json",JsonSerializer.Serialize(new{passed,failed,warmLookupBytes=warmBytes,description="Skia effect authoring, pixels and cache contracts. Gaussian sigma=radius/3; not bitwise WPF blur or Blend UI equivalence."}));return(passed,failed);
    }
}
