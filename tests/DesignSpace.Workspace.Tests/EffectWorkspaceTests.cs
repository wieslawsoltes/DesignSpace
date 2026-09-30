using System.Collections.Immutable;
using System.Text.Json.Nodes;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Xaml;
internal static class EffectWorkspaceTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS effect workspace: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL effect workspace: "+name+": "+e);}}
        void Check(bool value){if(!value)throw new Exception("Assertion failed");}
        void Reject(Action run){try{run();}catch{return;}throw new Exception("Expected rejection");}
        Test("effect preferences persist with generated metadata",()=>{using var w=new DocumentWorkspace();w.UpdateEditor(w.ActiveDocumentId,new(){RenderEffects=false,EffectsZoomThreshold=2});var s=WorkspaceCodec.Read(WorkspaceCodec.Write(w.Capture())).Documents[0].Editor;Check(!s.RenderEffects&&s.EffectsZoomThreshold==2);});
        Test("old workspaces enable effects when the new fields are missing",()=>{using var w=new DocumentWorkspace();var json=JsonNode.Parse(WorkspaceCodec.Write(w.Capture()))!;var editor=json["documents"]![0]!["editor"]!.AsObject();Check(editor.Remove("renderEffects")&&editor.Remove("effectsZoomThreshold"));var s=WorkspaceCodec.Read(json.ToJsonString()).Documents[0].Editor;Check(s.RenderEffects&&s.EffectsZoomThreshold==8);});
        foreach(var text in new[]{"0","9","null","\"invalid\""})Test("invalid threshold is not repaired: "+text,()=>{using var w=new DocumentWorkspace();var json=JsonNode.Parse(WorkspaceCodec.Write(w.Capture()))!;json["documents"]![0]!["editor"]!["effectsZoomThreshold"]=JsonNode.Parse(text);Reject(()=>WorkspaceCodec.Read(json.ToJsonString()));});
        Test("null preview enablement is not silently migrated",()=>{using var w=new DocumentWorkspace();var json=JsonNode.Parse(WorkspaceCodec.Write(w.Capture()))!;json["documents"]![0]!["editor"]!["renderEffects"]=null;Reject(()=>WorkspaceCodec.Read(json.ToJsonString()));});
        Test("invalid effect draft text is inert and isolated per document",()=>
        {
            using var w=new DocumentWorkspace();var id=w.ActiveDocumentId;var revision=w.Session.Revision;var draft=new DesignerPanelDraft{HasChanges=true,Targets=[w.Session.Document.Root.Id],Values=ImmutableDictionary<string,string>.Empty.Add("Type","Blur").Add("Radius","invalid"),Originals=ImmutableDictionary<string,string>.Empty.Add("Radius","5")};
            w.UpdateEditor(id,new(){RenderEffects=false,EffectsZoomThreshold=1,Panels=ImmutableDictionary<string,DesignerPanelDraft>.Empty.Add("Effects",draft)});Check(w.Session.Revision==revision);
            w.Add(DesignDocument.Empty(),"Other.designspace");Check(w.Active.Editor.RenderEffects&&w.Active.Editor.Panels.Count==0);
            var restored=WorkspaceCodec.Read(WorkspaceCodec.Write(w.Capture()));var s=restored.Documents.Single(d=>d.Id==id).Editor;Check(!s.RenderEffects&&s.Panels["Effects"].Values["Radius"]=="invalid");
        });
        return(passed,failed);
    }
}
