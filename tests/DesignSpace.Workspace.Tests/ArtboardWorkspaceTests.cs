using System.Text.Json.Nodes;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Xaml;
internal static class ArtboardWorkspaceTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS artboard workspace: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL artboard workspace: "+name+": "+e);}}
        void Check(bool value){if(!value)throw new Exception("Assertion failed");}
        void Reject(Action run){try{run();}catch{return;}throw new Exception("Expected rejection");}
        Test("new snapping fields survive reflection-free serialization",()=>
        {
            using var w=new DocumentWorkspace();w.UpdateEditor(w.ActiveDocumentId,new(){SnapToSnaplines=true,SnapTolerance=3,DefaultMargin=12,DefaultPadding=20});
            var state=WorkspaceCodec.Read(WorkspaceCodec.Write(w.Capture())).Documents[0].Editor;
            Check(state.SnapToSnaplines&&state.SnapTolerance==3&&state.DefaultMargin==12&&state.DefaultPadding==20);
        });
        Test("old files missing the new fields use compatible defaults",()=>
        {
            using var w=new DocumentWorkspace();var json=JsonNode.Parse(WorkspaceCodec.Write(w.Capture()))!;
            var editor=json["documents"]![0]!["editor"]!.AsObject();foreach(var field in new[]{"snapToSnaplines","snapTolerance","defaultMargin","defaultPadding"})Check(editor.Remove(field));
            var state=WorkspaceCodec.Read(json.ToJsonString()).Documents[0].Editor;Check(!state.SnapToSnaplines&&state.SnapTolerance==6&&state.DefaultMargin==8&&state.DefaultPadding==8);
        });
        Test("documents retain separate snap settings without geometry history",()=>
        {
            using var w=new DocumentWorkspace();var first=w.ActiveDocumentId;var root=w.Session.Document.Root;var revision=w.Session.Revision;
            w.UpdateEditor(first,new(){SnapToSnaplines=true,DefaultMargin=13});Check(w.Session.Revision==revision&&ReferenceEquals(root,w.Session.Document.Root));
            w.Add(DesignDocument.Empty(),"Second.designspace");Check(!w.Active.Editor.SnapToSnaplines);w.Activate(first);Check(w.Active.Editor.DefaultMargin==13&&w.Active.Editor.SnapToSnaplines);
        });
        foreach(var bad in new[]{new WorkspaceEditorState{SnapTolerance=0},new(){SnapTolerance=double.NaN},new(){DefaultMargin=-1},new(){DefaultPadding=10001}})
            Test("invalid settings cannot partially replace a workspace "+bad.SnapTolerance+"/"+bad.DefaultMargin+"/"+bad.DefaultPadding,()=>
            {
                using var w=new DocumentWorkspace();var before=w.Session.Document;var state=w.Capture();Reject(()=>w.Replace(state with{Documents=[state.Documents[0] with{Editor=bad}]}));Check(ReferenceEquals(before,w.Session.Document));
            });
        return(passed,failed);
    }
}
