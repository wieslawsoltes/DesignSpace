using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

public sealed class StatesControl : Grid,IDisposable
{
    private readonly DesignSession _session;
    private readonly StackPanel _states=new(){Spacing=2,Padding=new Thickness(6)};
    private readonly TextBox _name=StudioTheme.Input("NewState","New visual state name",118);
    private string? _selected;
    public DesignState? SelectedState=>_session.Document.States.FirstOrDefault(s=>s.Name==_selected);
    public event EventHandler<DesignState?>? StatePreviewRequested;
    public event EventHandler<string>? Error;
    public StatesControl(DesignSession session)
    {
        _session=session; RowDefinitions.Add(new(){Height=new GridLength(29)}); RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)}); RowDefinitions.Add(new(){Height=new GridLength(29)});
        var toolbar=new StackPanel { Orientation=Orientation.Horizontal,Spacing=2,Padding=new Thickness(4,2,4,2) }; toolbar.Children.Add(_name); toolbar.Children.Add(new StudioButton("+",AddState,"Add visual state")); toolbar.Children.Add(new StudioButton("−",RemoveState,"Remove visual state")); Children.Add(toolbar);
        var scroll=new ScrollViewer { Content=_states,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; SetRow(scroll,1); Children.Add(scroll);
        var footer=new StackPanel { Orientation=Orientation.Horizontal }; footer.Children.Add(new StudioButton("Capture selection",Capture,"Capture selected objects into state")); footer.Children.Add(new StudioButton("Base",()=>Select(null),"Return to base state")); SetRow(footer,2); Children.Add(footer);
        _session.DocumentChanged+=Changed; Refresh();
    }
    private void Changed(object? sender,EventArgs e)=>Refresh();
    private void Refresh()
    {
        _states.Children.Clear(); var baseButton=new StudioButton("○   Base",()=>Select(null),"Preview base state") { IsSelected=_selected is null,HorizontalAlignment=HorizontalAlignment.Stretch }; _states.Children.Add(baseButton);
        foreach(var state in _session.Document.States)
        {
            var button=new StudioButton("○   "+state.Name+"    "+state.Setters.Length+" setters",()=>Select(state.Name),"Preview state "+state.Name) { IsSelected=_selected==state.Name,HorizontalAlignment=HorizontalAlignment.Stretch }; _states.Children.Add(button);
        }
        if(_selected is not null && SelectedState is null) _selected=null;
    }
    public void Select(string? name) { _selected=name; Refresh(); StatePreviewRequested?.Invoke(this,SelectedState); }
    private void AddState()
    {
        try
        {
            var name=_name.Text.Trim(); if(name.Length==0 || !System.Text.RegularExpressions.Regex.IsMatch(name,"^[A-Za-z_][A-Za-z0-9_]*$") || _session.Document.States.Any(s=>s.Name==name)) throw new InvalidOperationException("Choose a unique XAML state name.");
            _session.Execute("Add visual state",d=>d with { States=d.States.Add(new(name,[])) }); Select(name);
        }
        catch(Exception e) { Error?.Invoke(this,e.Message); }
    }
    private void RemoveState() { if(_selected is null) return; var name=_selected; _session.Execute("Delete visual state",d=>d with { States=d.States.Where(s=>s.Name!=name).ToImmutableArray() }); Select(null); }
    private void Capture()
    {
        try
        {
            if(SelectedState is not { } state) throw new InvalidOperationException("Create or select a state before capturing values.");
            if(_session.Selection.Count==0) throw new InvalidOperationException("Select the objects to capture.");
            var targets=_session.Selection;
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
    public void Dispose()=>_session.DocumentChanged-=Changed;
}
