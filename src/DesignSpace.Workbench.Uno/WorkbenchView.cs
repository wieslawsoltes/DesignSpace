using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Animation;
using DesignSpace.Xaml;
using DesignSpace.Controls.Uno;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Workbench.Uno;

/// <summary>An embeddable design workbench. Document, controls and platform services remain independently reusable.</summary>
public sealed partial class WorkbenchView : Grid,IDisposable
{
    private readonly IWorkbenchPlatform _platform;
    private DockWorkspace _dock=null!;
    private readonly StudioTabs _leftTabs=new();
    private readonly StudioTabs _rightTabs=new();
    private readonly Grid _designSplit=new();
    private readonly RowDefinition _timelineHeight=new(){Height=new GridLength(170)};
    private Border _timelineHost=null!;
    private readonly TextBlock _title=StudioTheme.Text("DesignSpace",12);
    private readonly TextBlock _documentTitle=StudioTheme.Text("MainPage.xaml",11,"#FFFFFF");
    private readonly TextBlock _status=StudioTheme.Text("Ready",11,"#FFFFFF");
    private readonly TextBlock _metrics=StudioTheme.Text("",10,"#DCEBF5");
    private readonly TextBlock _zoom=StudioTheme.Text("100%",11);
    private readonly Dictionary<string,StudioButton> _tools=[];
    private readonly Dictionary<string,StudioButton> _modes=[];
    private readonly DispatcherTimer _saveTimer=new(){Interval=TimeSpan.FromMilliseconds(800)};
    private readonly SemaphoreSlim _saveGate=new(1,1);
    private bool _initialized,_disposed,_loading;
    private string _clipboard="";
    private string _nativeName="MainPage.designspace";
    private string _mode="Design";
    private long _lastMetrics;
    public DesignSession Session { get; }
    public DesignerSurface Designer { get; }
    public PropertyInspector Properties { get; }
    public XamlEditorControl Source { get; }=new();
    public TimelineControl Timeline { get; }
    public OutlineControl Outline { get; }
    public StatesControl States { get; }
    public ResourcesControl Resources { get; }
    public SampleDataControl Data { get; }
    public string Mode=>_mode;
    public string Status=>_status.Text;
    public bool IsReady=>_initialized;
    public event EventHandler? Changed;

    public WorkbenchView(IWorkbenchPlatform platform,DesignDocument? document=null)
    {
        _platform=platform; Session=new(document);
        Designer=new(Session) { PreviewResolver=DesignData.Resolve };
        Properties=new(Session); Timeline=new(Session); Outline=new(Session); States=new(Session); Resources=new(Session); Data=new(Session);
        BuildWorkspace();
        Session.DocumentChanged+=DocumentChanged; Session.SelectionChanged+=SelectionChanged;
        Designer.Error+=(_,message)=>SetStatus(message,true); Outline.Error+=(_,message)=>SetStatus(message,true); Properties.Error+=(_,message)=>SetStatus(message,true); Timeline.Error+=(_,message)=>SetStatus(message,true); States.Error+=(_,message)=>SetStatus(message,true); Resources.Error+=(_,message)=>SetStatus(message,true); Data.Error+=(_,message)=>SetStatus(message,true); Source.Error+=(_,message)=>SetStatus(message,true);
        Properties.PropertyEdited+=(_,edit)=>EditProperty(edit.Property,edit.Value);
        Properties.ActivePropertyChanged+=(_,property)=> { Timeline.PropertyToRecord=property;States.PropertyToReset=property; };
        Timeline.TimeChanged+=(_,_)=> { Designer.SetPreview(Timeline.PreviewStoryboard,Timeline.PreviewTime,States.SelectedState); Changed?.Invoke(this,EventArgs.Empty); };
        States.StatePreviewRequested+=(_,state)=>
        {
            Timeline.Stop(); Designer.SetPreview(null,0,state);
            Properties.ValueOverride=state is null ? null : (node,key)=>States.SelectedState?.Setters.FirstOrDefault(s=>s.TargetId==node.Id && s.Property==key)?.Value;
            Properties.EditingContext=state is null ? "" : (States.IsRecording ? "Recording state: " : "Preview state: ")+state.Name;
            Properties.AllowInlineBrushEditing=!States.IsRecording;Properties.Refresh();
            SetStatus(state is null ? "Base values" : (States.IsRecording ? "Recording properties in state: " : "Previewing visual state: ")+state.Name);
        };
        Designer.PreviewClicked+=(_,id)=> { var n=Session.Document.Root.Find(id); if(n?.Type=="Button") { Timeline.Stop(); Timeline.TogglePlay(); } };
        Designer.ViewChanged+=(_,_)=> { UpdateZoom(); Changed?.Invoke(this,EventArgs.Empty); };
        Designer.Rendered+=(_,_)=>
        {
            var now=Environment.TickCount64; if(now-_lastMetrics<500) return; _lastMetrics=now;
            _metrics.Text=$"{Designer.Renderer.LastDrawnNodes} objects   ·   draw {Designer.Renderer.LastDrawMilliseconds:0.0} ms   ·   Skia";
        };
        Source.ApplyRequested+=(_,change)=>
        {
            if(change.BaseRevision!=Session.Revision) throw new InvalidOperationException("The design changed after this XAML draft began. Reload from design before applying.");
            Timeline.Stop(); Session.Execute("Apply XAML",d=>change.Document with { Title=d.Title }); Source.Synchronize(Session.Document,Session.Revision,true); SetStatus("XAML applied to the design");
        };
        Source.ReloadRequested+=async (_,_)=> { if(!Source.IsDirty || await ConfirmAsync("Discard XAML draft?","Reloading replaces the unapplied source text with the current design.")) Source.Synchronize(Session.Document,Session.Revision,true); };
        Source.DraftChanged+=(_,_)=> { if(_initialized && !_loading) { _saveTimer.Stop(); _saveTimer.Start(); } };
        _saveTimer.Tick+=async (_,_)=> { _saveTimer.Stop(); await SaveRecoveryAsync(); };
        _dock.LayoutChanged+=(_,_)=> { if(_initialized) { _saveTimer.Stop(); _saveTimer.Start(); } };
        KeyDown+=OnKeyDown;
        SetMode("Design"); SetTool("Selection"); Source.Synchronize(Session.Document,Session.Revision,true);
        var initial=Session.Document.Root.DescendantsAndSelf().FirstOrDefault(n=>n.Name=="ExploreButton"); if(initial is not null) Session.Select(initial.Id);
        UpdateTitle();
    }
    public void SetTool(string name)
    {
        Designer.Tool=name; foreach(var tool in _tools) tool.Value.IsSelected=tool.Key==name; SetStatus(name+" tool"); Designer.FocusDesigner();
    }
    public void SetMode(string mode)
    {
        _mode=mode; Designer.Visibility=mode=="XAML" ? Visibility.Collapsed : Visibility.Visible; Source.Visibility=mode=="Design" ? Visibility.Collapsed : Visibility.Visible;
        _designSplit.ColumnDefinitions[0].Width=mode=="XAML" ? new GridLength(0) : new GridLength(1,GridUnitType.Star);
        _designSplit.ColumnDefinitions[1].Width=new GridLength(mode=="Split" ? 4 : 0);
        _designSplit.ColumnDefinitions[2].Width=mode=="Design" ? new GridLength(0) : new GridLength(1,GridUnitType.Star);
        foreach(var item in _modes) item.Value.IsSelected=item.Key==mode;
        Source.Synchronize(Session.Document,Session.Revision); if(mode=="XAML") Source.FocusSource(); Changed?.Invoke(this,EventArgs.Empty);
    }
    public void AddAsset(string type)
    {
        Timeline.Stop(); Designer.ClearPreview();
        var host=Session.Selection.Select(id=>Session.Document.Root.Find(id)).FirstOrDefault(n=>n?.Type=="Canvas") ?? Session.Document.Root.DescendantsAndSelf().FirstOrDefault(n=>n.Type=="Canvas") ?? Session.Document.Root;
        Session.Add(type,new DRect(64,80,type=="TextBlock" ? 240 : 120,type is "TextBlock" or "Button" or "TextBox" ? 40 : 80),host.Id); SetTool("Selection"); SetStatus("Added "+type);
    }
    private void EditProperty(string property,string value)
    {
        if(States.IsRecording && States.SelectedState is not null) { States.RecordProperty(property,value); return; }
        if(Timeline.IsRecording && AnimationEngine.Properties.Contains(property))
        {
            if(!double.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var number) || !double.IsFinite(number)) throw new InvalidOperationException("Enter a finite numeric keyframe value.");
            Timeline.AddKey(property,number); return;
        }
        if(property is "Fill" or "Background" or "Stroke" or "Foreground")
        {
            var ids=Session.Selection.ToArray(); Session.Execute("Set "+property,d=>
            {
                var root=d.Root; foreach(var id in ids) root=root.Update(id,n=>Session.Index.IsLocked(n.Id) ? n : n.Set(property,value) with { PropertyElements=n.PropertyElements.Where(p=>!p.Contains("."+property,StringComparison.Ordinal)).ToImmutableArray() }); return d with { Root=root };
            });
        }
        else Session.SetProperty(property,value);
        SetStatus("Updated "+property);
    }
    private void DocumentChanged(object? sender,EventArgs e)
    {
        UpdateTitle(); Source.Synchronize(Session.Document,Session.Revision); Designer.InvalidateLayout();
        if(States.SelectedState is { } state) Designer.SetPreview(null,0,state);
        Properties.EditingContext=States.SelectedState is { } selected ? (States.IsRecording ? "Recording state: " : "Preview state: ")+selected.Name : "";
        Properties.AllowInlineBrushEditing=!States.IsRecording;Properties.Refresh();
        if(_initialized && !_loading) { _saveTimer.Stop(); _saveTimer.Start(); } Changed?.Invoke(this,EventArgs.Empty);
    }
    private void SelectionChanged(object? sender,EventArgs e)
    {
        if(Session.Selection.Count>0) SetStatus(string.Join(", ",Session.Selection.Select(id=>Session.Document.Root.Find(id)?.Name))); Changed?.Invoke(this,EventArgs.Empty);
    }
    private void UpdateTitle() { _title.Text=Session.Document.Title+(Session.IsDirty ? " *" : "")+" — DesignSpace"; _documentTitle.Text=Session.Document.Title+(Session.IsDirty ? " *" : ""); }
    private void UpdateZoom()=>_zoom.Text=$"{Designer.Viewport.Zoom*100:0}%";
    public void SetStatus(string message,bool error=false) { _status.Text=message; _status.Foreground=StudioTheme.Brush(error ? "#FFE1B5" : "#FFFFFF"); Changed?.Invoke(this,EventArgs.Empty); }
    public void Guard(Action action) { try { action(); } catch(Exception e) { SetStatus(e.Message,true); } }
    public async Task GuardAsync(Func<Task> action) { try { await action(); } catch(Exception e) { SetStatus(e.Message,true); } }
    public void Dispose()
    {
        if(_disposed) return; _disposed=true; _saveTimer.Stop(); Session.DocumentChanged-=DocumentChanged; Session.SelectionChanged-=SelectionChanged;
        Designer.Dispose(); Properties.Dispose(); Timeline.Dispose(); Outline.Dispose(); States.Dispose(); Resources.Dispose(); Data.Dispose();
    }
}
