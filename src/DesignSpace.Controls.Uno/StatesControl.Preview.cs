using System.Collections.Immutable;
using System.Diagnostics;
using DesignSpace.Animation;
using DesignSpace.Core;
using DesignSpace.Docking.Uno;
using DesignSpace.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace DesignSpace.Controls.Uno;

public sealed partial class StatesControl
{
    private VisualStatePreview _playback=null!;
    private readonly Stopwatch _stateClock=Stopwatch.StartNew();
    private bool _frames;
    private string _group=VisualStateGroups.DefaultName;
    private StudioButton _transitionToggle=null!;
    public bool TransitionsEnabled { get; private set; }
    public bool IsTransitioning=>_frames;
    public double TransitionProgress=>_playback.Progress(_stateClock.Elapsed.TotalSeconds);
    public string SelectedGroup=>_group;
    public IReadOnlyDictionary<string,string> ActiveStates=>_playback.ActiveStates;
    public DesignState? PreviewState { get; private set; }
    public DesignState? TargetState { get; private set; }
    public event EventHandler? PreviewFrameChanged;
    public event EventHandler? GroupChanged;
    public event EventHandler? TransitionEditorRequested;
    private void InitializeGroupPreview()
    {
        _playback=new(_session.Document,DesignPreview.Resolve);
        _group=VisualStateGroups.Get(_session.Document).FirstOrDefault()?.Name??VisualStateGroups.DefaultName;
        _transitionToggle=new StudioButton("Animate",()=>SetTransitionsEnabled(!TransitionsEnabled),"Toggle state transition preview");
        Children.OfType<StackPanel>().Single(p=>GetRow(p)==3).Children.Add(_transitionToggle);
        Children.OfType<StackPanel>().Single(p=>GetRow(p)==2).Children.Add(new StudioButton("Transitions",()=>TransitionEditorRequested?.Invoke(this,EventArgs.Empty),"Edit visual transitions"));
        Unloaded+=(_,_)=>StopTransitions();
    }
    public void SetTransitionsEnabled(bool enabled)
    {
        TransitionsEnabled=enabled;_transitionToggle.IsSelected=enabled;if(!enabled)StopTransitions();
    }
    private void SelectPreview(string? name)
    {
        var now=_stateClock.Elapsed.TotalSeconds;
        if(name is null){StopFrames();_playback.Reset();}
        else
        {
            _playback.GoToState(name,now,TransitionsEnabled&&!IsRecording);
            var group=_session.Document.States.First(s=>s.Name==name).Group;
            if(_group!=group){_group=group;GroupChanged?.Invoke(this,EventArgs.Empty);}
        }
        UpdateTarget();PublishFrame(now);
        if(_playback.IsRunning(now)&&!_frames){_frames=true;CompositionTarget.Rendering+=StateFrame;}
    }
    public void SelectGroup(string group)
    {
        if(!VisualStateGroups.Get(_session.Document).Any(g=>g.Name==group))throw new InvalidOperationException("The group no longer exists.");
        _group=group;_selected=null;IsRecording=false;Refresh();GroupChanged?.Invoke(this,EventArgs.Empty);StatePreviewRequested?.Invoke(this,SelectedState);
    }
    private void BaseGroup(string group)
    {
        try
        {
            var now=_stateClock.Elapsed.TotalSeconds;_playback.GoToBase(group,now,TransitionsEnabled&&!IsRecording);
            if(SelectedState?.Group==group){_selected=null;IsRecording=false;}
            UpdateTarget();PublishFrame(now);if(_playback.IsRunning(now)&&!_frames){_frames=true;CompositionTarget.Rendering+=StateFrame;}
            Refresh();StatePreviewRequested?.Invoke(this,SelectedState);
        }
        catch(Exception error){Error?.Invoke(this,error.Message);}
    }
    private void UpdateTarget()
    {
        var active=_playback.ActiveStates.Values.ToHashSet(StringComparer.Ordinal);
        var setters=_session.Document.States.Where(s=>active.Contains(s.Name)).SelectMany(s=>s.Setters).ToImmutableArray();
        TargetState=setters.IsEmpty ? null : new("ActiveVisualStates",setters);
    }
    private void PublishFrame(double now){PreviewState=_playback.Sample(now);PreviewFrameChanged?.Invoke(this,EventArgs.Empty);}
    private void StateFrame(object? sender,object args)
    {
        var now=_stateClock.Elapsed.TotalSeconds;PublishFrame(now);if(!_playback.IsRunning(now))StopFrames();
    }
    private void StopFrames(){if(_frames)CompositionTarget.Rendering-=StateFrame;_frames=false;}
    public void StopTransitions(bool notify=true)
    {
        StopFrames();_playback.Complete();PreviewState=_playback.Sample(_stateClock.Elapsed.TotalSeconds);if(notify)PreviewFrameChanged?.Invoke(this,EventArgs.Empty);
    }
    private void RebuildGroupPreview()
    {
        StopFrames();var active=_playback.ActiveStates.Values.ToArray();_playback=new(_session.Document,DesignPreview.Resolve);
        var now=_stateClock.Elapsed.TotalSeconds;
        foreach(var name in active)if(_session.Document.States.Any(s=>s.Name==name))
        {
            try{_playback.GoToState(name,now,false);}catch(Exception error){Error?.Invoke(this,error.Message);}
        }
        if(!VisualStateGroups.Get(_session.Document).Any(g=>g.Name==_group))_group=VisualStateGroups.Get(_session.Document).FirstOrDefault()?.Name??VisualStateGroups.DefaultName;
        UpdateTarget();PreviewState=_playback.Sample(now);
    }
}
