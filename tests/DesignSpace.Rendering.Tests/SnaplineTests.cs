using System.Collections.Immutable;
using System.Text.Json;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Xaml;
using DesignSpace.Rendering.Skia;
using SkiaSharp;

internal static class SnaplineTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS snaplines: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL snaplines: "+name+": "+e);}}
        void Check(bool condition){if(!condition)throw new Exception("Assertion failed");}
        void Near(double a,double b){if(!double.IsFinite(b)||Math.Abs(a-b)>1e-8)throw new Exception($"Expected {a:R}, got {b:R}");}
        void Reject(Action run){try{run();}catch{return;}throw new Exception("Expected rejection");}
        var target=new DRect(100,100,100,100);var index=new SnaplineIndex([target],margin:0,padding:0);
        Test("empty scene leaves geometry unchanged",()=>{var b=new DRect(13,29,20,20);var s=new SnaplineIndex([]).Move(b,6,6);Check(s.Bounds==b&&!s.SnappedX&&!s.SnappedY);});
        Test("left edge snaps without touching the other axis",()=>{var s=index.Move(new(97,250,20,20),4,4);Near(100,s.Bounds.X);Near(250,s.Bounds.Y);Check(s.SnappedX&&!s.SnappedY);});
        Test("center alignment uses the smallest correction",()=>Near(140,index.Move(new(139,250,20,20),4,4).Bounds.X));
        Test("right edge alignment retains width",()=>{var s=index.Move(new(178,250,20,20),4,4);Near(180,s.Bounds.X);Near(20,s.Bounds.Width);});
        Test("vertical alignment is symmetric",()=>{var s=index.Move(new(250,97,20,20),4,4);Near(100,s.Bounds.Y);Near(250,s.Bounds.X);});
        Test("screen tolerance scales through zoom",()=>{Check(index.Move(new(94,250,20,20),6,6).SnappedX);Check(!index.Move(new(94,250,20,20),3,3).SnappedX);});
        Test("nonuniform hosts supply separate tolerances",()=>{var s=index.Move(new(95,95,20,20),3,6);Check(!s.SnappedX&&s.SnappedY);});
        Test("out-of-radius coordinates never magnetize",()=>Check(!index.Move(new(80,250,5,5),4,4).SnappedX));
        Test("margin snaps only the facing edge",()=>{var s=new SnaplineIndex([new DRect(100,100,50,50)]).Move(new(157,110,20,20),3,3);Near(158,s.Bounds.X);Near(8,s.XGuide!.Value.Distance);Check(s.XGuide.Value.Kind==SnapGuideKind.Margin);});
        Test("preceding margin retains the opposite target edge",()=>{var s=new SnaplineIndex([new DRect(100,100,50,50)]).Move(new(73,110,20,20),3,3);Near(72,s.Bounds.X);Near(8,s.XGuide!.Value.Distance);});
        Test("spacing does not attract unrelated rows",()=>{var s=new SnaplineIndex([new DRect(100,100,50,50)]).Move(new(157,300,20,20),3,3);Check(!s.SnappedX);});
        Test("padding respects the inner container edge",()=>{var s=new SnaplineIndex([],new(0,0,500,500),padding:12).Move(new(10,80,20,20),3,3);Near(12,s.Bounds.X);Near(12,s.XGuide!.Value.Distance);Check(s.XGuide.Value.Kind==SnapGuideKind.Padding);});
        Test("too-large padding cannot create inverted guides",()=>{var s=new SnaplineIndex([],new(0,0,20,20),padding:20).Move(new(19,40,5,5),2,2);Check(s.XGuide is null||s.XGuide.Value.Kind==SnapGuideKind.Alignment);});
        Test("alignment wins a coincident spacing tie",()=>{var s=new SnaplineIndex([new DRect(100,100,50,50),new(158,200,30,30)]).Move(new(157,110,20,20),3,3);Check(s.XGuide!.Value.Kind==SnapGuideKind.Alignment);});
        Test("enumeration order cannot alter the result",()=>{var boxes=new[]{target,new DRect(140,110,50,50),new(10,160,20,20)};var b=new DRect(95,109,20,20);Check(new SnaplineIndex(boxes).Move(b,6,6)==new SnaplineIndex(boxes.Reverse()).Move(b,6,6));});
        Test("right resize holds the left edge",()=>{var s=index.Resize(new(25,250,72,20),SnapEdges.Right,4,4);Near(25,s.Bounds.X);Near(100,s.Bounds.Right);Near(20,s.Bounds.Height);});
        Test("left resize holds the right edge",()=>{var s=index.Resize(new(103,250,57,20),SnapEdges.Left,4,4);Near(100,s.Bounds.X);Near(160,s.Bounds.Right);});
        Test("bottom resize holds the top edge",()=>{var s=index.Resize(new(250,25,20,72),SnapEdges.Bottom,4,4);Near(25,s.Bounds.Y);Near(100,s.Bounds.Bottom);});
        Test("top resize holds the bottom edge",()=>{var s=index.Resize(new(250,103,20,57),SnapEdges.Top,4,4);Near(100,s.Bounds.Y);Near(160,s.Bounds.Bottom);});
        Test("corner resize corrects both active edges",()=>{var s=index.Resize(new(25,25,72,72),SnapEdges.Right|SnapEdges.Bottom,4,4);Near(100,s.Bounds.Right);Near(100,s.Bounds.Bottom);});
        Test("resize cannot collapse across fixed edge",()=>{var s=index.Resize(new(100,250,2,20),SnapEdges.Right,3,3);Near(2,s.Bounds.Width);Check(!s.SnappedX);});
        Test("no active edge means no resize",()=>{var b=new DRect(97,97,20,20);Check(index.Resize(b,SnapEdges.None,4,4).Bounds==b);});
        Test("opposite edges reject ambiguous resize",()=>Reject(()=>index.Resize(target,SnapEdges.Left|SnapEdges.Right,4,4)));
        foreach(var bad in new[]{double.NaN,double.PositiveInfinity,0d,-1})Test("invalid tolerance "+bad,()=>Reject(()=>index.Move(target,bad,6)));
        Test("invalid bounds reject",()=>Reject(()=>index.Move(new(0,0,double.NaN,5),6,6)));
        Test("negative target extent rejects",()=>Reject(()=>new SnaplineIndex([new DRect(0,0,-1,5)])));
        Test("large finite widths retain their finite right anchor",()=>
        {
            var large=new SnaplineIndex([new DRect(0,100,1e308,10)],margin:0,padding:0);
            var sample=large.Move(new DRect(1e308,200,1,1),6,6);
            Check(sample.SnappedX&&double.IsFinite(sample.XGuide!.Value.Start.X)&&sample.XGuide.Value.Start.X==1e308);
        });
        Test("unrepresentable guide unions fail before retaining invalid geometry",()=>
            Reject(()=>new SnaplineIndex([new DRect(100,-1e308,10,1),new DRect(100,1e308,10,1)],margin:0,padding:0)));
        Test("target budget is enforced",()=>Reject(()=>new SnaplineIndex(Enumerable.Repeat(target,DocumentValidator.MaxNodes+1))));
        foreach(var settings in new[]{new ArtboardSettings{GridSize=0},new(){SnapTolerance=33},new(){DefaultMargin=-1},new(){DefaultPadding=double.NaN}})
            Test("invalid settings reject "+settings,()=>Reject(settings.Validate));
        Test("viewport applies preferences atomically",()=>{var view=new DesignViewport();var before=view.CaptureArtboardSettings();Reject(()=>view.ApplyArtboardSettings(before with{ShowGrid=true,SnapTolerance=0}));Check(view.CaptureArtboardSettings()==before);});
        Test("workspace settings roundtrip without changing document",()=>
        {
            using var workspace=new DocumentWorkspace();var revision=workspace.Session.Revision;var root=workspace.Session.Document.Root;
            var settings=new WorkspaceEditorState{SnapToSnaplines=true,SnapTolerance=4,DefaultMargin=12,DefaultPadding=16};workspace.UpdateEditor(workspace.ActiveDocumentId,settings);
            var next=WorkspaceCodec.Read(WorkspaceCodec.Write(workspace.Capture()));var state=next.Documents[0].Editor;
            Check(state.SnapToSnaplines);Near(4,state.SnapTolerance);Near(12,state.DefaultMargin);Near(16,state.DefaultPadding);Check(workspace.Session.Revision==revision&&ReferenceEquals(root,workspace.Session.Document.Root));
        });
        Test("old workspace files retain grid-only behavior",()=>{using var w=new DocumentWorkspace();var json=WorkspaceCodec.Write(w.Capture());var state=WorkspaceCodec.Read(json).Documents[0].Editor;Check(!state.SnapToSnaplines);Near(6,state.SnapTolerance);});
        Test("invalid restored settings reject before state replacement",()=>{using var w=new DocumentWorkspace();var s=w.Capture();Reject(()=>WorkspaceValidator.Validate(s with{Documents=[s.Documents[0] with{Editor=new(){SnapTolerance=double.NaN}}]}));});
        Test("indexed alignment agrees with an independent exhaustive search",()=>
        {
            var random=new Random(317);var targets=Enumerable.Range(0,200).Select(_=>new DRect(random.Next(-500,500),random.Next(-500,500),random.Next(1,80),random.Next(1,80))).ToArray();var scene=new SnaplineIndex(targets,margin:0,padding:0);
            double Expected(DRect box,bool x)
            {
                var matches=from t in targets from p in new[]{0d,.5,1} from q in new[]{0d,.5,1}
                    let coordinate=x?t.X+t.Width*p:t.Y+t.Height*p let delta=coordinate-(x?box.X+box.Width*q:box.Y+box.Height*q)
                    where Math.Abs(delta)<=6 orderby Math.Abs(delta),coordinate,q select delta;
                return (x?box.X:box.Y)+matches.FirstOrDefault();
            }
            for(var i=0;i<500;i++){var box=new DRect(random.Next(-600,600),random.Next(-600,600),random.Next(1,90),random.Next(1,90));var s=scene.Move(box,6,6);Near(Expected(box,true),s.Bounds.X);Near(Expected(box,false),s.Bounds.Y);}
        });
        long bytes=0;
        Test("20000 targets have allocation-free warm queries",()=>
        {
            var scene=new SnaplineIndex(Enumerable.Range(0,20000).Select(i=>new DRect(i*20,100,10,10)),margin:0,padding:0);var box=new DRect(399977,97,10,10);
            for(var i=0;i<20;i++)scene.Move(box,4,4);var before=GC.GetAllocatedBytesForCurrentThread();var sum=0d;
            for(var i=0;i<10000;i++)sum+=scene.Move(box,4,4).Bounds.X;bytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(bytes==0&&sum>0);
        });
        Test("guide renderer preserves host state and paints exact coordinate",()=>
        {
            using var bitmap=new SKBitmap(250,250);using var canvas=new SKCanvas(bitmap);using var renderer=new SnaplineRenderer();canvas.Clear(SKColors.White);var before=canvas.SaveCount;
            var result=index.Move(new(97,220,20,20),4,4);renderer.Draw(canvas,result,DMatrix.Identity,new(){Zoom=1,PanX=0,PanY=0,ShowRulers=false});
            var pixel=bitmap.GetPixel(100,210);Check(pixel.Red>200&&pixel.Green<130&&canvas.SaveCount==before);Check(bitmap.GetPixel(102,210)==SKColors.White);
        });
        Directory.CreateDirectory("artifacts/verification");File.WriteAllText("artifacts/verification/snapline-results.json",JsonSerializer.Serialize(new{passed,failed,warmQueryBytes=bytes,randomizedSamples=500,targets=20000,description="Portable geometry and host-canvas pixels; not native Blend screenshot or GPU qualification."}));
        return(passed,failed);
    }
}
