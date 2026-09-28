using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

public sealed partial class StatesControl : Grid,IDisposable
{
    private readonly DesignSession _session;
    private readonly StackPanel _states=new(){Spacing=2,Padding=new Thickness(6)};
    private readonly TextBox _name=StudioTheme.Input("NewState","New visual state name",118);
    private string? _selected;
    private readonly StudioButton _record;
    public bool IsRecording { get; private set; }
    public string PropertyToReset { get; set; }="Opacity";
    public DesignState? SelectedState=>_session.Document.States.FirstOrDefault(s=>s.Name==_selected);
    public event EventHandler<DesignState?>? StatePreviewRequested;
    public event EventHandler<string>? Error;
    public StatesControl(DesignSession session)
    {
        _session=session; RowDefinitions.Add(new(){Height=new GridLength(29)}); RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)}); RowDefinitions.Add(new(){Height=new GridLength(29)}); RowDefinitions.Add(new(){Height=new GridLength(29)});
        var toolbar=new StackPanel { Orientation=Orientation.Horizontal,Spacing=2,Padding=new Thickness(4,2,4,2) }; toolbar.Children.Add(_name); toolbar.Children.Add(new StudioButton("+",AddState,"Add visual state")); toolbar.Children.Add(new StudioButton("−",RemoveState,"Remove visual state")); toolbar.Children.Add(new StudioButton("Rename",RenameState,"Rename visual state")); Children.Add(toolbar);
        var scroll=new ScrollViewer { Content=_states,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; SetRow(scroll,1); Children.Add(scroll);
        var footer=new StackPanel { Orientation=Orientation.Horizontal }; footer.Children.Add(new StudioButton("Capture selection",Capture,"Capture selected objects into state")); footer.Children.Add(new StudioButton("Base",()=>Select(null),"Return to base state")); SetRow(footer,2); Children.Add(footer);
        var recording=new StackPanel { Orientation=Orientation.Horizontal };
        _record=new StudioButton("Record state",()=> { StopTransitions(false);IsRecording=!IsRecording && SelectedState is not null;_record!.IsSelected=IsRecording;StatePreviewRequested?.Invoke(this,SelectedState); },"Record visual state properties");
        recording.Children.Add(_record);recording.Children.Add(new StudioButton("Reset property",ResetProperty,"Reset selected state property"));SetRow(recording,3);Children.Add(recording);
        InitializeGroupPreview();_session.DocumentChanged+=Changed; Refresh();
    }
    private void Changed(object? sender,EventArgs e){RebuildGroupPreview();Refresh();}
    private void Refresh()
    {
        _states.Children.Clear(); var baseButton=new StudioButton("○   Base",()=>Select(null),"Preview base state") { IsSelected=_selected is null,HorizontalAlignment=HorizontalAlignment.Stretch }; _states.Children.Add(baseButton);
        foreach(var group in VisualStateGroups.Get(_session.Document))
        {
            var header=new StackPanel{Orientation=Orientation.Horizontal,Spacing=2};
            header.Children.Add(new StudioButton(group.Name,()=>SelectGroup(group.Name),"Select state group "+group.Name){Width=155,IsSelected=SelectedGroup==group.Name});
            header.Children.Add(new StudioButton("Base",()=>BaseGroup(group.Name),"Preview group base "+group.Name));_states.Children.Add(header);
            foreach(var state in _session.Document.States.Where(s=>s.Group==group.Name))
            {
                var active=ActiveStates.GetValueOrDefault(group.Name)==state.Name;
                var button=new StudioButton((active ? "●   " : "○   ")+state.Name+"    "+state.Setters.Length+" setters",()=>Select(state.Name),"Preview state "+state.Name){IsSelected=_selected==state.Name,HorizontalAlignment=HorizontalAlignment.Stretch};_states.Children.Add(button);
            }
        }
        if(_selected is not null && SelectedState is null) {_selected=null;IsRecording=false;}
        _record.IsSelected=IsRecording;
    }
    public void Select(string? name)
    {
        try { SelectPreview(name);_selected=name;if(name is null) IsRecording=false;Refresh();StatePreviewRequested?.Invoke(this,SelectedState); }
        catch(Exception error){Error?.Invoke(this,error.Message);}
    }
    public void RecordProperty(string property,string value)
    {
        if(!IsRecording || SelectedState is not { } state) throw new InvalidOperationException("Select a state and enable Record state first.");
        var targets=_session.Selection;
        _session.Execute("Set state "+state.Name+"."+property,d=>StateEditing.SetProperty(d,state.Name,targets,property,value));
        StatePreviewRequested?.Invoke(this,SelectedState);
    }
    private void ResetProperty()
    {
        if(SelectedState is not { } state) return;
        var targets=_session.Selection;
        _session.Execute("Reset state "+PropertyToReset,d=>StateEditing.RemoveProperty(d,state.Name,targets,PropertyToReset));
        StatePreviewRequested?.Invoke(this,SelectedState);
    }
    private void AddState()
    {
        try
        {
            var name=_name.Text.Trim(); if(name.Length==0 || !System.Text.RegularExpressions.Regex.IsMatch(name,"^[A-Za-z_][A-Za-z0-9_]*$") || _session.Document.States.Any(s=>s.Name==name)) throw new InvalidOperationException("Choose a unique XAML state name.");
            _session.Execute("Add visual state",d=>StateGroupEditing.AddState(d,SelectedGroup,name)); Select(name);
        }
        catch(Exception e) { Error?.Invoke(this,e.Message); }
    }
    private void RemoveState() { if(_selected is null) return; var name=_selected; _session.Execute("Delete visual state",d=>StateGroupEditing.RemoveState(d,name)); Select(null); }
    private void Capture()
    {
        try
        {
            if(SelectedState is not { } state) throw new InvalidOperationException("Create or select a state before capturing values.");
            if(_session.Selection.Count==0) throw new InvalidOperationException("Select the objects to capture.");
            var targets=_session.Selection.Where(id=>!_session.Index.IsLocked(id)).ToHashSet();
            if(targets.Count==0)return;
            var setters=state.Setters.Where(s=>!targets.Contains(s.TargetId)).ToList();
            foreach(var id in targets)
            {
                var n=_session.Document.Root.Find(id)!;
                foreach(var property in new[]{"Opacity","Canvas.Left","Canvas.Top","Width","Height","Fill","Background","Foreground"})
                    if(n.Properties.TryGetValue(property,out var value)) setters.Add(new(id,property,value));
            }
            _session.Execute("Capture state values",d=>d with { States=d.States.Select(s=>s.Name==state.Name ? s with { Setters=setters.ToImmutableArray() } : s).ToImmutableArray() });
            StatePreviewRequested?.Invoke(this,SelectedState);
        }
        catch(Exception e) { Error?.Invoke(this,e.Message); }
    }
    private void RenameState()
    {
        try { if(SelectedState is not { } state)return;var name=_name.Text.Trim();_session.Execute("Rename visual state",d=>StateGroupEditing.RenameState(d,state.Name,name));Select(name); }
        catch(Exception error){Error?.Invoke(this,error.Message);}
    }
    public void Dispose(){StopFrames();_session.DocumentChanged-=Changed;}

}
