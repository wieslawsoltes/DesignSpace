using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Controls.Uno;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
namespace DesignSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private readonly DocumentTabsControl _documentTabs=new();
    private readonly StackPanel _projectDocuments=new(){Spacing=2};
    private bool _switchingDocuments,_documentIoBusy,_captureEditorQueued;
    private ContentDialog? _documentDialog;
    public UIElement? DocumentDialog=>_documentDialog;
    public bool DocumentOperationPending=>_documentIoBusy;
    private static string TabTitle(WorkspaceDocument tab)=>Path.GetFileNameWithoutExtension(tab.FileName)+".xaml";
    private IEnumerable<(string Name,IWorkspaceDraftEditor Editor)> DraftEditors()
    {
        yield return("Data",Data);
        if(_templateEditor is not null)yield return("Templates",_templateEditor);
        if(_strokeEditor is not null)yield return("Stroke",_strokeEditor);
        if(_animationTrackEditor is not null)yield return("Animation",_animationTrackEditor);
        if(_brushEditor is not null)yield return("Brush",_brushEditor);
        if(_stateTransitions is not null)yield return("Transitions",_stateTransitions);
        if(_storyboardInspector is not null)yield return("Timing",_storyboardInspector);
    }
    private void InitializeDocumentWorkspace()
    {
        _documentTabs.Selected+=(_,id)=>Guard(()=>SwitchDocument(id));
        _documentTabs.CloseRequested+=(_,id)=>_=GuardAsync(()=>CloseDocumentAsync(id));
        _documentTabs.CloseOthersRequested+=(_,id)=>_=GuardAsync(()=>CloseOtherDocumentsAsync(id));
        _documentTabs.PinRequested+=(_,id)=>Guard(()=>{Workspace.Pin(id,!Workspace.Find(id).IsPinned);QueueWorkspaceRecovery();});
        _documentTabs.MoveRequested+=(_,change)=>Guard(()=>{Workspace.Move(change.Id,change.Index);QueueWorkspaceRecovery();});
        Workspace.Changed+=WorkspaceChanged;
        AddHandler(UIElement.KeyUpEvent,new KeyEventHandler((_,_)=>ScheduleEditorCapture()),true);
        AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,_)=>ScheduleEditorCapture()),true);
        AddHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler((_,_)=>ScheduleEditorCapture()),true);
        UpdateDocumentChrome();
    }
    // Sample text accepted by native controls after their deferred input notifications.
    // One queued capture and the existing debounced save cover auxiliary drafts too.
    private void ScheduleEditorCapture()
    {
        if(_captureEditorQueued||!_initialized||_loading||_disposed)return;
        _captureEditorQueued=true;DispatcherQueue.TryEnqueue(()=>
        {
            _captureEditorQueued=false;if(_switchingDocuments||_disposed)return;
            Guard(()=>{CaptureCurrentDocument();UpdateDocumentChrome();QueueWorkspaceRecovery();});
        });
    }
    private void WorkspaceChanged(object? sender,EventArgs e){if(!_switchingDocuments){UpdateDocumentChrome();QueueWorkspaceRecovery();}}
    private void QueueWorkspaceRecovery(){if(_initialized&&!_loading&&!_switchingDocuments&&!_disposed){_saveTimer.Stop();_saveTimer.Start();}}
    private void UpdateDocumentChrome()
    {
        if(_switchingDocuments)return;
        var tabs=Workspace.Documents.Select(d=>new DocumentTabItem(d.Id,TabTitle(d),d.IsDirty||(d.Id==Workspace.ActiveDocumentId ? Source.IsDirty||d.Editor.Panels.Values.Any(p=>p.HasChanges) : d.Editor.HasDrafts),d.Id==Workspace.ActiveDocumentId,d.IsPinned)).ToArray();
        _documentTabs.SetItems(tabs);
        // Refresh only when labels or active identity change, not every frame/selection notification.
        var key=string.Join("|",tabs.Select(d=>$"{d.Id}:{d.Title}:{d.Dirty}:{d.Active}:{d.Pinned}"));
        if(_projectDocuments.Tag as string==key)return;_projectDocuments.Tag=key;_projectDocuments.Children.Clear();
        foreach(var tab in tabs)
        {
            var button=new StudioButton(tab.Title+(tab.Dirty ? " *" : ""),()=>Guard(()=>SwitchDocument(tab.Id)),"Project document "+tab.Title)
                {IsSelected=tab.Active,HorizontalAlignment=HorizontalAlignment.Stretch};_projectDocuments.Children.Add(button);
        }
    }
    private void CaptureCurrentDocument()
    {
        if(_switchingDocuments)return;
        var view=Designer.Viewport;
        Workspace.UpdateEditor(Workspace.ActiveDocumentId,new()
        {
            SourceDraft=Source.IsDirty ? Source.Text : null,DraftMatchesDesign=Source.BaseRevision==Session.Revision,
            StoryboardId=Timeline.ActiveStoryboard?.Id,TimelineTime=Timeline.Time,Mode=_mode,SplitOrientation=_splitOrientation,SplitRatio=_splitRatio,Zoom=view.Zoom,PanX=view.PanX,PanY=view.PanY,
            HasViewport=true,ShowGrid=view.ShowGrid,ShowRulers=view.ShowRulers,SnapToGrid=view.SnapToGrid,GridSize=view.GridSize,
            Panels=DraftEditors().ToImmutableDictionary(p=>p.Name,p=>p.Editor.CaptureWorkspaceDraft())
        });
    }
    public void SwitchDocument(Guid id)
    {
        if(_documentIoBusy)throw new InvalidOperationException("Finish the current file operation before switching documents.");
        if(id==Workspace.ActiveDocumentId)return;
        ChangeActiveDocument(()=>Workspace.Activate(id));
    }
    private void ChangeActiveDocument(Action change,bool capture=true)
    {
        if(capture)CaptureCurrentDocument();
        _switchingDocuments=true;
        try
        {
            Designer.CancelGesture();States.StopTransitions(false);States.Select(null);Timeline.Stop();Designer.IsPreview=false;Designer.ClearPreview();
            change();
            var tab=Workspace.Active;var state=tab.Editor;
            Source.BeginDocument(Session.Document,Session.Revision);
            if(state.SourceDraft is not null)Source.RestoreDraft(state.SourceDraft,state.DraftMatchesDesign ? Session.Revision : -1);
            Timeline.SelectStoryboard(state.StoryboardId,state.TimelineTime);
            foreach(var (name,editor) in DraftEditors())editor.RestoreWorkspaceDraft(state.Panels.GetValueOrDefault(name));
            _splitRatio=state.SplitRatio;SetSplitOrientation(state.SplitOrientation);SetMode(state.Mode);
            var viewport=Designer.Viewport;viewport.Zoom=state.Zoom;viewport.PanX=state.PanX;viewport.PanY=state.PanY;
            viewport.ShowGrid=state.ShowGrid;viewport.ShowRulers=state.ShowRulers;viewport.SnapToGrid=state.SnapToGrid;viewport.GridSize=state.GridSize;
            Designer.InvalidateLayout();UpdateZoom();
            if(!state.HasViewport)DispatcherQueue.TryEnqueue(()=>{if(Workspace.ActiveDocumentId==tab.Id)Designer.Fit();});
        }
        finally{_switchingDocuments=false;}
        UpdateTitle();Changed?.Invoke(this,EventArgs.Empty);QueueWorkspaceRecovery();
    }
    private enum CloseChoice { Cancel,Save,Discard }
    private async Task<CloseChoice> AskCloseAsync(WorkspaceDocument tab)
    {
        var choice=CloseChoice.Cancel;
        var body=new StackPanel{Spacing=14};
        body.Children.Add(new TextBlock{Text="Save changes to "+TabTitle(tab)+" before closing? Unapplied panel drafts must be applied first to include them in the document, or exported in a workspace file.",TextWrapping=TextWrapping.Wrap,MaxWidth=450});
        var commands=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};body.Children.Add(commands);
        _documentDialog=new ContentDialog{XamlRoot=XamlRoot,Title="Close document",Content=body};
        foreach(var (label,result) in new[]{("Save",CloseChoice.Save),("Discard",CloseChoice.Discard),("Cancel",CloseChoice.Cancel)})
            commands.Children.Add(new StudioButton(label,()=>{choice=result;_documentDialog.Hide();},label+" document close"));
        try{await _documentDialog.ShowAsync();return choice;}finally{_documentDialog=null;}
    }
    private async Task<bool> CloseDocumentCoreAsync(Guid id)
    {
        CaptureCurrentDocument();var tab=Workspace.Find(id);
        if(tab.NeedsAttention)
        {
            var choice=await AskCloseAsync(tab);if(choice==CloseChoice.Cancel)return false;
            if(choice==CloseChoice.Save)
            {
                if(Workspace.ActiveDocumentId!=id)ChangeActiveDocument(()=>Workspace.Activate(id));
                if(!await SaveActiveDocumentAsync())return false;CaptureCurrentDocument();
                if(Workspace.Find(id).Editor.HasDrafts)throw new InvalidOperationException("Apply the remaining panel drafts or save a workspace file before closing. The tab remains open.");
            }
        }
        ChangeActiveDocument(()=>Workspace.Close(id,discard:true));return true;
    }
    public async Task CloseDocumentAsync(Guid id)
    {
        if(_documentIoBusy)return;_documentIoBusy=true;
        try{await CloseDocumentCoreAsync(id);}finally{_documentIoBusy=false;QueueWorkspaceRecovery();}
    }
    private async Task CloseOtherDocumentsAsync(Guid keep)
    {
        if(_documentIoBusy)return;_documentIoBusy=true;
        try
        {
            foreach(var id in Workspace.Documents.Where(d=>d.Id!=keep&&!d.IsPinned).Select(d=>d.Id).ToArray())if(!await CloseDocumentCoreAsync(id))break;
        }
        finally{_documentIoBusy=false;QueueWorkspaceRecovery();}
    }
    private void CycleDocument(int delta)
    {
        var ids=Workspace.Documents.Select(d=>d.Id).ToArray();var index=Array.IndexOf(ids,Workspace.ActiveDocumentId);
        SwitchDocument(ids[(index+delta+ids.Length)%ids.Length]);
    }
    private async Task RenameActiveDocumentAsync()
    {
        var id=Workspace.ActiveDocumentId;var input=StudioTheme.Input(_nativeName,"Document file name");
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="Rename document",Content=input,PrimaryButtonText="Rename",CloseButtonText="Cancel"};
        if(await dialog.ShowAsync()==ContentDialogResult.Primary)
        {
            var name=input.Text.Trim();WorkspaceValidator.ValidateFileName(name);
            if(!name.EndsWith(".designspace",StringComparison.OrdinalIgnoreCase))name=Path.GetFileNameWithoutExtension(name)+".designspace";
            Workspace.Rename(id,name);UpdateTitle();QueueWorkspaceRecovery();
        }
    }
}
