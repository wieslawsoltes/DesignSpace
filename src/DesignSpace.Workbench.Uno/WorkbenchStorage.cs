using System.Text;
using System.Text.Json;
using DesignSpace.Core;
using DesignSpace.Xaml;
using DesignSpace.Docking.Uno;
namespace DesignSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private sealed record RecoveryEnvelope(int Version,string Document,string? SourceDraft,bool DraftMatchesDesign,DockLayout? Layout);
    public async Task InitializeAsync()
    {
        _loading=true;
        try
        {
            var text=await _platform.ReadLocalAsync("recovery.json");
            if(!string.IsNullOrWhiteSpace(text))
            {
                var recovery=JsonSerializer.Deserialize<RecoveryEnvelope>(text);
                if(recovery?.Version==1)
                {
                    Session.Load(NativeDocumentCodec.Read(recovery.Document)); Source.Synchronize(Session.Document,Session.Revision,true);
                    if(recovery.SourceDraft is not null) Source.RestoreDraft(recovery.SourceDraft,recovery.DraftMatchesDesign ? Session.Revision : -1);
                    if(recovery.Layout is not null) _dock.Apply(recovery.Layout);
                    SetStatus("Recovered the last local workspace");
                }
            }
        }
        catch(Exception e) { SetStatus("Local recovery could not be restored: "+e.Message,true); }
        finally { _loading=false; _initialized=true; Changed?.Invoke(this,EventArgs.Empty); }
    }
    private async Task SaveRecoveryAsync()
    {
        if(!_initialized || _loading || _disposed) return;
        await _saveGate.WaitAsync();
        try
        {
            var envelope=new RecoveryEnvelope(1,NativeDocumentCodec.Write(Session.Document),Source.IsDirty ? Source.Text : null,Source.BaseRevision==Session.Revision,_dock.Capture());
            await _platform.WriteLocalAsync("recovery.json",JsonSerializer.Serialize(envelope));
        }
        catch(Exception e) { SetStatus("Local recovery could not be saved: "+e.Message,true); }
        finally { _saveGate.Release(); }
    }
    private void EnsureSourceApplied()
    {
        if(!Source.IsDirty) return; Source.Apply();
        if(Source.IsDirty) throw new InvalidOperationException("Resolve and apply the XAML draft before saving or exporting. Your draft remains in the source editor.");
    }
    private async Task NewAsync(bool sample=false)
    {
        if((Session.IsDirty || Source.IsDirty) && !await ConfirmAsync("Replace the current design?","Save or export any work you need before replacing this document.")) return;
        Timeline.Stop(); _loading=true;
        try { Session.Load(sample ? SampleDocument.Create() : DesignDocument.Empty()); Source.Synchronize(Session.Document,Session.Revision,true); States.Select(null); _nativeName="MainPage.designspace"; SetMode("Design"); }
        finally { _loading=false; }
        Designer.Fit(); SetStatus(sample ? "Loaded the interaction sample" : "New blank design"); await SaveRecoveryAsync();
    }
    private async Task OpenAsync()
    {
        if((Session.IsDirty || Source.IsDirty) && !await ConfirmAsync("Open another document?","Opening replaces this design. Save or export any work you need first.")) return;
        var file=await _platform.OpenAsync(); if(file is null) return;
        DesignDocument document; var warnings=0;
        if(file.Name.EndsWith(".xaml",StringComparison.OrdinalIgnoreCase)) { var parsed=XamlCodec.Parse(file.Text,file.Name); document=parsed.Document; warnings=parsed.Diagnostics.Count; }
        else document=NativeDocumentCodec.Read(file.Text);
        Timeline.Stop(); _loading=true;
        try { Session.Load(document); Source.Synchronize(Session.Document,Session.Revision,true); States.Select(null); _nativeName=Path.GetFileNameWithoutExtension(file.Name)+".designspace"; }
        finally { _loading=false; }
        Designer.Fit(); SetStatus("Opened "+file.Name+(warnings>0 ? $" · {warnings} compatibility warning(s); unsupported markup is preserved." : "")); await SaveRecoveryAsync();
    }
    private async Task SaveAsync()
    {
        EnsureSourceApplied();
        if(await _platform.SaveAsync(_nativeName,Encoding.UTF8.GetBytes(NativeDocumentCodec.Write(Session.Document)),"application/json")) { Session.MarkSaved(); SetStatus("Saved "+_nativeName); await SaveRecoveryAsync(); }
    }
    private async Task ExportXamlAsync()
    {
        EnsureSourceApplied(); var name=Path.GetFileNameWithoutExtension(_nativeName)+".xaml";
        if(await _platform.SaveAsync(name,Encoding.UTF8.GetBytes(XamlCodec.Write(Session.Document)),"application/xml")) SetStatus("Exported "+name);
    }
    private async Task ExportPngAsync()
    {
        EnsureSourceApplied(); var name=Path.GetFileNameWithoutExtension(_nativeName)+".png";
        var bytes=Designer.Renderer.ExportPng(Designer.Layout);
        if(await _platform.SaveAsync(name,bytes,"image/png")) SetStatus("Exported a 2× PNG of the current preview");
    }
}
