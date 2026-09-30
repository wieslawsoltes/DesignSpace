using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Xaml;
using DesignSpace.Rendering.Skia;
using SkiaSharp;
internal static class EffectAuthoringTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action test){try{test();passed++;Console.WriteLine("PASS effect authoring: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL effect authoring: "+name+": "+e);}}
        void Check(bool result){if(!result)throw new Exception("Assertion failed");}
        void Reject(Action test){try{test();}catch{return;}throw new Exception("Expected rejection");}
        const string ns=DesignNode.PresentationNamespace;const string x=DesignNode.XamlNamespace;
        var n=DesignNode.Create("Rectangle","R",50,50,40,40).Set("Fill","Red");
        DesignDocument Doc(DesignNode node)=>new(){Root=DesignNode.Create("Canvas","Root",0,0,300,220).Set("Background","White") with{Children=[node]}};
        string? Effect(DesignDocument d)=>new BrushResolver(DesignPreview.Resolve(d.Root)).Resolve(n.Id,"Effect");
        var d=Doc(n);d=d with{Root=d.Root with{PropertyElements=[$"<Canvas.Resources xmlns='{ns}'><Style TargetType='Rectangle'><Setter Property='Effect'><Setter.Value><DropShadowEffect BlurRadius='0' Direction='0' ShadowDepth='50'/></Setter.Value></Setter></Style></Canvas.Resources>"]}};
        Test("None overrides an implicit style without changing the resource",()=>{Check(Effect(d) is not null);var changed=EffectEditing.Apply(d,[n.Id],null);Check(Effect(changed) is null);Check(changed.Root.PropertyElements==d.Root.PropertyElements);Check(changed.Root.Children[0].Get("Effect")=="{x:Null}");});
        Test("Reset restores the implicit effect",()=>{var changed=EffectEditing.Apply(d,[n.Id],null);var reset=EffectEditing.Reset(changed,[n.Id]);Check(Effect(reset) is not null);Check(reset.Root.Children[0].Get("Effect")=="");});
        Test("None and Reset are independently idempotent",()=>{var none=EffectEditing.Apply(d,[n.Id],null);Check(ReferenceEquals(none,EffectEditing.Apply(none,[n.Id],null)));var reset=EffectEditing.Reset(none,[n.Id]);Check(ReferenceEquals(reset,EffectEditing.Reset(reset,[n.Id])));});
        Test("base owner Effect property is replaced, not duplicated",()=>{var input=Doc(n with{PropertyElements=[$"<UIElement.Effect xmlns='{ns}'><BlurEffect Radius='4'/></UIElement.Effect>"]});var next=EffectEditing.Apply(input,[n.Id],new(){Radius=8});Check(next.Root.Children[0].PropertyElements.Length==1);Check(EffectCodec.Parse(Effect(next)!).Radius==8);});
        Test("multi-selection writes all effects atomically",()=>{var b=n with{Id=Guid.NewGuid(),Properties=n.Properties.SetItem(DesignNode.NameKey,"Other")};var input=Doc(n);input=input with{Root=input.Root with{Children=[n,b]}};var next=EffectEditing.Apply(input,[n.Id,b.Id],new(){Radius=6});Check(next.Root.Children.All(c=>c.PropertyElements.Length==1));Check(input.Root.Children.All(c=>c.PropertyElements.Length==0));});
        Test("effect replacement rejects inherited locks",()=>Reject(()=>EffectEditing.Apply(d with{Root=d.Root with{IsLocked=true}},[n.Id],new())));
        Test("reset rejects inherited locks",()=>Reject(()=>EffectEditing.Reset(d with{Root=d.Root with{IsLocked=true}},[n.Id])));
        Test("effect must validate before any target changes",()=>Reject(()=>EffectEditing.Apply(d,[n.Id],new(){Radius=double.NaN})));
        Test("None and Reset undo exactly",()=>{var session=new DesignSession(d);session.Execute("None",v=>EffectEditing.Apply(v,[n.Id],null));Check(Effect(session.Document) is null);session.Execute("Reset",v=>EffectEditing.Reset(v,[n.Id]));Check(Effect(session.Document) is not null);session.Undo();Check(Effect(session.Document) is null);session.Undo();Check(ReferenceEquals(session.Document,d));});
        Test("effect resource edits copy locally without overwriting scoped definitions",()=>{var input=Doc(n.Set("Effect","{StaticResource Blur}"));input=input with{Root=input.Root with{PropertyElements=[$"<Canvas.Resources xmlns='{ns}' xmlns:x='{x}'><BlurEffect x:Key='Blur' Radius='4'/></Canvas.Resources>"]}};var next=EffectEditing.Apply(input,[n.Id],new(){Radius=7});Check(EffectCodec.Parse(Effect(next)!).Radius==7);Check(input.Root.PropertyElements==next.Root.PropertyElements);Check(EffectCodec.Parse(Effect(input)!).Radius==4);});
        Test("nested resource shadowing keeps the nearest effect",()=>{var inner=DesignNode.Create("Canvas","Inner") with{Children=[n.Set("Effect","{StaticResource Blur}")],PropertyElements=[$"<Canvas.Resources xmlns='{ns}' xmlns:x='{x}'><BlurEffect x:Key='Blur' Radius='9'/></Canvas.Resources>"]};var input=Doc(inner);input=input with{Root=input.Root with{PropertyElements=[$"<Canvas.Resources xmlns='{ns}' xmlns:x='{x}'><BlurEffect x:Key='Blur' Radius='2'/></Canvas.Resources>"]}};Check(EffectCodec.Parse(Effect(input)!).Radius==9);});
        Test("unsupported effect expressions stay inert",()=>{var input=Doc(n.Set("Effect","{Binding Danger}"));var text=XamlCodec.Write(input);Check(XamlCodec.Parse(text).Document.Root.Children[0].Get("Effect")=="{Binding Danger}");Reject(()=>Effect(input));});
        Test("offscreen source outside artboard can cast an onscreen shadow",()=>{var node=n.Set("Canvas.Left",310);var input=EffectEditing.Apply(Doc(node),[n.Id],new(){Kind=DesignEffectKind.DropShadow,Radius=0,Direction=180,ShadowDepth=60});using var renderer=new DesignRenderer();using var bitmap=SKBitmap.Decode(renderer.ExportPng(new LayoutEngine(renderer).Arrange(input.Root),1));Check(bitmap.GetPixel(270,70).Red<10);});
        Test("draw preview obeys effect switch and zoom threshold without affecting export",()=>
        {
            var input=EffectEditing.Apply(Doc(n),[n.Id],new(){Kind=DesignEffectKind.DropShadow,Radius=0,Direction=0,ShadowDepth=60});using var renderer=new DesignRenderer();var layout=new LayoutEngine(renderer).Arrange(input.Root);
            var viewport=new DesignViewport{Zoom=1,PanX=0,PanY=0,ShowRulers=false,RenderEffects=false};using var bitmap=new SKBitmap(300,220);using var canvas=new SKCanvas(bitmap);
            renderer.Draw(canvas,300,220,layout,viewport,ImmutableHashSet<Guid>.Empty);Check(bitmap.GetPixel(130,70)==SKColors.White);
            viewport.RenderEffects=true;viewport.EffectsZoomThreshold=.5;renderer.Draw(canvas,300,220,layout,viewport,ImmutableHashSet<Guid>.Empty);Check(bitmap.GetPixel(130,70)==SKColors.White);
            viewport.EffectsZoomThreshold=1;renderer.Draw(canvas,300,220,layout,viewport,ImmutableHashSet<Guid>.Empty);Check(bitmap.GetPixel(130,70).Red<10);
            viewport.RenderEffects=false;using var exported=SKBitmap.Decode(renderer.ExportPng(layout,1));Check(exported.GetPixel(130,70).Red<10);
        });
        Test("parent clip prevents filtered descendants painting outside it",()=>{var child=EffectEditing.Apply(Doc(n),[n.Id],new(){Kind=DesignEffectKind.DropShadow,Radius=0,Direction=0,ShadowDepth=60}).Root.Children[0];var host=DesignNode.Create("Canvas","Clip",0,0,100,150).Set("ClipToBounds","True") with{Children=[child]};using var r=new DesignRenderer();using var b=SKBitmap.Decode(r.ExportPng(new LayoutEngine(r).Arrange(Doc(host).Root),1));Check(b.GetPixel(130,70)==SKColors.White);});
        Test("software-reference opacity follows documented fixed-point arithmetic",()=>{Check(WpfEffectMath.ShadowAlpha(1)==253);Check(WpfEffectMath.ShadowAlpha(.5)==126);Check(WpfEffectMath.ShadowAlpha(0)==0);Reject(()=>WpfEffectMath.ShadowAlpha(double.NaN));});
        Test("Gaussian taps are symmetric normalized and bounded",()=>{foreach(var radius in new[]{0,1,3,9,18,128}){var taps=WpfEffectMath.GaussianKernel(radius);Check(Math.Abs(taps.Sum(v=>(double)v)-1)<1e-6);Check(taps.SequenceEqual(taps.Reverse()));Check(taps.Length==2*radius+1);}Reject(()=>WpfEffectMath.GaussianKernel(129));});
        Test("native and reference filters cannot share a cache entry",()=>{using var cache=new EffectFilterCache();var raw=EffectCodec.Write(new(){Radius=3});var native=cache.Get(raw);var reference=cache.Get(raw,EffectRenderingMode.WpfSoftwareCompatible);Check(!ReferenceEquals(native,reference)&&cache.Builds==2);Check(ReferenceEquals(reference,cache.Get(raw,EffectRenderingMode.WpfSoftwareCompatible)));});
        Test("software reference truncates subpixel local blur radii",()=>{using var filter=EffectFilterCache.Create(new(){Radius=.5},EffectRenderingMode.WpfSoftwareCompatible);Check(filter is null);});
        return(passed,failed);
    }
}
