using System.Collections.Immutable;
using DesignSpace.Core;
namespace DesignSpace.Engine;

/// <summary>A document tab backed by an isolated checkpoint and editor state.</summary>
public sealed class WorkspaceDocument
{
    internal readonly DocumentWorkspace Owner;
    internal DesignSessionCheckpoint Checkpoint;
    internal WorkspaceDocument(DocumentWorkspace owner,WorkspaceFile file)
    {
        Owner=owner;Id=file.Id;FileName=file.FileName;IsPinned=file.IsPinned;Editor=file.Editor;
        Checkpoint=DesignSessionCheckpoint.Open(file.Document,file.HasUnsavedChanges,file.Selection);
    }
    public Guid Id { get; }
    public string FileName { get; internal set; }
    public bool IsPinned { get; internal set; }
    public WorkspaceEditorState Editor { get; internal set; }
    public DesignDocument Document=>Owner.ActiveDocumentId==Id ? Owner.Session.Document : Checkpoint.Document;
    public bool IsDirty=>Owner.ActiveDocumentId==Id ? Owner.Session.IsDirty : Checkpoint.IsDirty;
    public bool NeedsAttention=>IsDirty||Editor.HasDrafts;
    internal WorkspaceFile Snapshot()
    {
        var state=Owner.ActiveDocumentId==Id ? Owner.Session.CaptureCheckpoint() : Checkpoint;
        return new(Id,state.Document,FileName,state.IsDirty,IsPinned,Editor,state.Selection.ToImmutableArray());
    }
}
/// <summary>UI-thread multi-document coordinator. One stable session drives existing controls; inactive tabs own isolated immutable history.</summary>
public sealed class DocumentWorkspace : IDisposable
{
    private readonly List<WorkspaceDocument> _documents=[];
    private bool _changing;
    public DesignSession Session { get; }
    public Guid ActiveDocumentId { get; private set; }
    public WorkspaceDocument Active=>_documents.First(d=>d.Id==ActiveDocumentId);
    public IReadOnlyList<WorkspaceDocument> Documents { get; }
    public event EventHandler? Changed;
    public DocumentWorkspace(DesignDocument? initial=null)
    {
        Session=new(initial);Documents=_documents.AsReadOnly();
        var file=new WorkspaceFile(Guid.NewGuid(),Session.Document,"MainPage.designspace",false,false,new(),[]);
        _documents.Add(new(this,file));ActiveDocumentId=file.Id;Session.DocumentChanged+=OnSessionChanged;
    }
    private void OnSessionChanged(object? sender,EventArgs e){if(!_changing)Changed?.Invoke(this,EventArgs.Empty);}
    public WorkspaceDocument Add(DesignDocument document,string fileName,bool unsaved=false)
    {
        var file=new WorkspaceFile(Guid.NewGuid(),document,fileName,unsaved,false,new(),[]);
        var captured=Capture();var proposed=captured with{Documents=captured.Documents.Add(file),ActiveDocumentId=file.Id};WorkspaceValidator.Validate(proposed);
        Active.Checkpoint=Session.CaptureCheckpoint();var tab=new WorkspaceDocument(this,file);_documents.Add(tab);Activate(tab.Id);return tab;
    }
    public bool Activate(Guid id)
    {
        var next=_documents.FirstOrDefault(d=>d.Id==id)??throw new KeyNotFoundException("The document tab no longer exists.");
        if(id==ActiveDocumentId)return false;
        Active.Checkpoint=Session.CaptureCheckpoint();ActiveDocumentId=id;_changing=true;
        try{Session.RestoreCheckpoint(next.Checkpoint);}finally{_changing=false;}
        Changed?.Invoke(this,EventArgs.Empty);return true;
    }
    public bool Close(Guid id,bool discard=false)
    {
        var index=_documents.FindIndex(d=>d.Id==id);if(index<0)return false;var tab=_documents[index];
        if(tab.NeedsAttention&&!discard)throw new InvalidOperationException("Save or explicitly discard this document and its drafts before closing.");
        if(_documents.Count==1)
        {
            var blank=DesignDocument.Empty();var file=new WorkspaceFile(Guid.NewGuid(),blank,"MainPage.designspace",false,false,new(),[]);
            Replace(new(1,file.Id,[file]));return true;
        }
        if(id==ActiveDocumentId)Activate(_documents[index==_documents.Count-1 ? index-1 : index+1].Id);
        _documents.Remove(tab);Changed?.Invoke(this,EventArgs.Empty);return true;
    }
    public void UpdateEditor(Guid id,WorkspaceEditorState editor)
    {
        WorkspaceValidator.ValidateEditor(editor);Find(id).Editor=editor;
    }
    public WorkspaceDocument Find(Guid id)=>_documents.FirstOrDefault(d=>d.Id==id)??throw new KeyNotFoundException("The document tab no longer exists.");
    public void Rename(Guid id,string fileName)
    {
        WorkspaceValidator.ValidateFileName(fileName);var tab=Find(id);if(tab.FileName==fileName)return;if(_documents.Any(d=>d.Id!=id&&d.FileName.Equals(fileName,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Another document already uses that file name.");tab.FileName=fileName;Changed?.Invoke(this,EventArgs.Empty);
    }
    public void Pin(Guid id,bool pinned){var tab=Find(id);if(tab.IsPinned==pinned)return;tab.IsPinned=pinned;Changed?.Invoke(this,EventArgs.Empty);}
    public void Move(Guid id,int position)
    {
        var tab=Find(id);position=Math.Clamp(position,0,_documents.Count-1);if(_documents.IndexOf(tab)==position)return;
        _documents.Remove(tab);_documents.Insert(position,tab);Changed?.Invoke(this,EventArgs.Empty);
    }
    public void MarkSaved(Guid id,DesignDocument writtenVersion)
    {
        var tab=Find(id);
        if(id==ActiveDocumentId)Session.MarkSavedVersion(writtenVersion);
        else{tab.Checkpoint=tab.Checkpoint.WithSaved(writtenVersion);Changed?.Invoke(this,EventArgs.Empty);}
    }
    public DesignWorkspaceSnapshot Capture()=>new(1,ActiveDocumentId,_documents.Select(d=>d.Snapshot()).ToImmutableArray());
    public void Replace(DesignWorkspaceSnapshot snapshot)
    {
        WorkspaceValidator.Validate(snapshot); // Validate everything before touching live documents.
        var tabs=snapshot.Documents.Select(f=>new WorkspaceDocument(this,f)).ToArray();
        _documents.Clear();_documents.AddRange(tabs);ActiveDocumentId=snapshot.ActiveDocumentId;_changing=true;
        try{Session.RestoreCheckpoint(Active.Checkpoint);}finally{_changing=false;}
        Changed?.Invoke(this,EventArgs.Empty);
    }
    public string UniqueFileName(string stem="Page",string extension=".designspace")
    {
        for(var i=1;;i++){var name=stem+i+extension;if(!_documents.Any(d=>d.FileName.Equals(name,StringComparison.OrdinalIgnoreCase)))return name;}
    }
    public void Dispose()=>Session.DocumentChanged-=OnSessionChanged;
}
