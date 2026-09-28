using DesignSpace.Core;
using DesignSpace.Engine;

internal static class DocumentOperationTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS workspace operation: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL workspace operation: "+name+": "+e);}}
        void Check(bool condition){if(!condition)throw new Exception("Assertion failed");}
        void Reject(Action run){try{run();}catch{return;}throw new Exception("Expected rejection");}
        DocumentWorkspace Create()=>new(DesignDocument.Empty());
        Guid AddShape(DesignSession s)=>s.Add("Rectangle",new(20,30,40,50));
        Test("asynchronous operation accepts its unchanged context",()=>{using var w=Create();AddShape(w.Session);var operation=w.CaptureOperation();w.RequireCurrent(operation);});
        Test("asynchronous operation rejects another active document",()=>{using var w=Create();var operation=w.CaptureOperation();w.Add(DesignDocument.Empty(),"Other.designspace");Reject(()=>w.RequireCurrent(operation));});
        Test("switching away and back still invalidates pending edits",()=>{using var w=Create();var operation=w.CaptureOperation();w.Add(DesignDocument.Empty(),"Other.designspace");w.Activate(operation.DocumentId);Reject(()=>w.RequireCurrent(operation));});
        Test("pending edit rejects a changed selection without a new revision",()=>{using var w=Create();var id=AddShape(w.Session);var operation=w.CaptureOperation();w.Session.Select(Array.Empty<Guid>());Check(w.Session.Revision==operation.Revision);Reject(()=>w.RequireCurrent(operation));});
        Test("pending edit rejects changed geometry",()=>{using var w=Create();var operation=w.CaptureOperation();AddShape(w.Session);Reject(()=>w.RequireCurrent(operation));});
        Test("pending edit survives unrelated tab metadata changes",()=>{using var w=Create();var operation=w.CaptureOperation();w.Pin(w.ActiveDocumentId,true);w.Rename(w.ActiveDocumentId,"Changed.designspace");w.RequireCurrent(operation);});
        return(passed,failed);
    }
}
