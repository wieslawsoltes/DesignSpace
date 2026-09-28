using System.Text;
using System.Text.Json;
using DesignSpace.Core;
using DesignSpace.Xaml;
using DesignSpace.Docking.Uno;
namespace DesignSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private sealed record RecoveryEnvelope(int Version,string? Document=null,string? SourceDraft=null,bool DraftMatchesDesign=true,DockLayout? Layout=null,string? Workspace=null,string Profile="Design",double TimelineHeight=170);
    public async Task InitializeAsync()
    {
        _loading=true;
        try
        {
            var text=await _platform.ReadLocalAsync("recovery.json");
            if(!string.IsNullOrWhiteSpace(text))
            {
                if(text.Length>WorkspaceValidator.MaxCharacters*2)throw new InvalidDataException("Recovery envelope is too large.");
                var recovery=JsonSerializer.Deserialize<RecoveryEnvelope>(text)??throw new InvalidDataException("Empty recovery envelope.");
                DesignWorkspaceSnapshot restored;
                if(recovery.Version==2&&recovery.Workspace is not null)restored=WorkspaceCodec.Read(recovery.Workspace);
                else if(recovery.Version==1&&recovery.Document is not null)
                {
                    var id=Guid.NewGuid();var document=NativeDocumentCodec.Read(recovery.Document);
                    restored=new(1,id,[new(id,document,"MainPage.designspace",true,false,new(){SourceDraft=recovery.SourceDraft,DraftMatchesDesign=recovery.DraftMatchesDesign},[])]);
                }
                else throw new InvalidDataException("Unsupported workspace recovery version.");
                ChangeActiveDocument(()=>Workspace.Replace(restored),capture:false);
                if(recovery.Layout is not null)_dock.Apply(recovery.Layout);
                if(recovery.Profile is "Design" or "Animation")_workspaceProfile=recovery.Profile;
                SetTimelineHeight(double.IsFinite(recovery.TimelineHeight) ? Math.Clamp(recovery.TimelineHeight,0,600) : 170);
                SetStatus("Recovered "+restored.Documents.Length+" document(s) and their drafts");
            }
        }
        catch(Exception e){SetStatus("Local recovery could not be restored: "+e.Message,true);}
        finally{_loading=false;_initialized=true;UpdateDocumentChrome();Changed?.Invoke(this,EventArgs.Empty);}
    }
    private async Task SaveRecoveryAsync()
    {
        if(!_initialized||_loading||_disposed||_switchingDocuments)return;
        await _saveGate.WaitAsync();
        try
        {
            if(_disposed)return;CaptureCurrentDocument();
            var envelope=new RecoveryEnvelope(2,Layout:_dock.Capture(),Workspace:WorkspaceCodec.Write(Workspace.Capture()),Profile:_workspaceProfile,TimelineHeight:_timelineHeight.Height.Value);
            await _platform.WriteLocalAsync("recovery.json",JsonSerializer.Serialize(envelope));
        }
        catch(Exception e){SetStatus("Local recovery could not be saved: "+e.Message,true);}
        finally{_saveGate.Release();}
    }
    private void EnsureSourceApplied()
    {
        if(!Source.IsDirty)return;Source.Apply();
        if(Source.IsDirty)throw new InvalidOperationException("Resolve and apply the XAML draft before saving or exporting this document. Save workspace preserves unapplied drafts.");
    }
    private async Task NewAsync(bool sample=false)
    {
        if(_documentIoBusy)return;
        var name=Workspace.UniqueFileName(sample?"Sample":"Page");
        var document=(sample?SampleDocument.Create():DesignDocument.Empty()) with{Title=Path.GetFileNameWithoutExtension(name)+".xaml"};
        ChangeActiveDocument(()=>Workspace.Add(document,name,unsaved:true));SetStatus("Opened "+document.Title+" in a new tab");await SaveRecoveryAsync();
    }
    private async Task OpenAsync()
    {
        if(_documentIoBusy)return;_documentIoBusy=true;
        try
        {
            var file=await _platform.OpenAsync();if(file is null)return;
            if(file.Name.EndsWith(".designspace-workspace",StringComparison.OrdinalIgnoreCase))
            {
                var restored=WorkspaceCodec.Read(file.Text);CaptureCurrentDocument();
                if(Workspace.Documents.Any(d=>d.NeedsAttention)&&!await ConfirmAsync("Replace the open workspace?","Save the current workspace before replacing its documents and drafts."))return;
                ChangeActiveDocument(()=>Workspace.Replace(restored),capture:false);SetStatus("Opened workspace with "+restored.Documents.Length+" documents");
            }
            else
            {
                DesignDocument document;var warnings=0;
                if(file.Name.EndsWith(".xaml",StringComparison.OrdinalIgnoreCase)){var parsed=XamlCodec.Parse(file.Text,file.Name);document=parsed.Document;warnings=parsed.Diagnostics.Count;}
                else document=NativeDocumentCodec.Read(file.Text);
                var stem=Path.GetFileNameWithoutExtension(file.Name);var name=stem+".designspace";
                if(Workspace.Documents.Any(d=>d.FileName.Equals(name,StringComparison.OrdinalIgnoreCase)))name=Workspace.UniqueFileName(stem+"Copy");
                ChangeActiveDocument(()=>Workspace.Add(document,name));SetStatus("Opened "+file.Name+(warnings>0?$" · {warnings} compatibility warning(s)":""));
            }
            await SaveRecoveryAsync();
        }
        finally{_documentIoBusy=false;}
    }
    private async Task<bool> SaveActiveDocumentAsync()
    {
        EnsureSourceApplied();var id=Workspace.ActiveDocumentId;var document=Session.Document;var name=_nativeName;
        if(!await _platform.SaveAsync(name,Encoding.UTF8.GetBytes(NativeDocumentCodec.Write(document)),"application/json"))return false;
        Workspace.MarkSaved(id,document);CaptureCurrentDocument();SetStatus("Saved "+name);return true;
    }
    private async Task SaveAsync()
    {
        if(_documentIoBusy)return;_documentIoBusy=true;
        try{if(await SaveActiveDocumentAsync())await SaveRecoveryAsync();}finally{_documentIoBusy=false;}
    }
    private async Task SaveAllAsync()
    {
        if(_documentIoBusy)return;_documentIoBusy=true;var active=Workspace.ActiveDocumentId;var saved=0;
        try
        {
            CaptureCurrentDocument();
            foreach(var id in Workspace.Documents.Where(d=>d.IsDirty||d.Editor.SourceDraft is not null).Select(d=>d.Id).ToArray())
            {
                if(id!=Workspace.ActiveDocumentId)ChangeActiveDocument(()=>Workspace.Activate(id));
                if(!await SaveActiveDocumentAsync())return;saved++;
            }
            SetStatus("Saved "+saved+" document(s)");
        }
        finally
        {
            if(Workspace.ActiveDocumentId!=active)ChangeActiveDocument(()=>Workspace.Activate(active));
            _documentIoBusy=false;await SaveRecoveryAsync();
        }
    }
    private async Task SaveWorkspaceAsync()
    {
        if(_documentIoBusy)return;_documentIoBusy=true;
        try
        {
            CaptureCurrentDocument();var bytes=Encoding.UTF8.GetBytes(WorkspaceCodec.Write(Workspace.Capture()));
            if(await _platform.SaveAsync("DesignSpace.designspace-workspace",bytes,"application/json"))SetStatus("Saved all documents and drafts in the workspace file");
        }
        finally{_documentIoBusy=false;}
    }
    private async Task ExportXamlAsync()
    {
        EnsureSourceApplied();var name=Path.GetFileNameWithoutExtension(_nativeName)+".xaml";
        if(await _platform.SaveAsync(name,Encoding.UTF8.GetBytes(XamlCodec.Write(Session.Document)),"application/xml"))SetStatus("Exported "+name);
    }
    private async Task ExportPngAsync()
    {
        EnsureSourceApplied();var name=Path.GetFileNameWithoutExtension(_nativeName)+".png";var bytes=Designer.Renderer.ExportPng(Designer.Layout);
        if(await _platform.SaveAsync(name,bytes,"image/png"))SetStatus("Exported a 2× PNG of the current preview");
    }
}
