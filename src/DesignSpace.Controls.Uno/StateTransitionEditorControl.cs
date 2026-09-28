using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

/// <summary>Embeddable group and generated-transition authoring with revision-checked drafts.</summary>
public sealed class StateTransitionEditorControl : ScrollViewer,IDisposable
{
    private readonly DesignSession _session;
    private readonly StatesControl _states;
    private readonly TextBox _group=StudioTheme.Input(VisualStateGroups.DefaultName,"Visual state group name",200);
    private readonly TextBox _from=StudioTheme.Input("*","Transition from state",110),_to=StudioTheme.Input("*","Transition to state",110);
    private readonly TextBox _duration=StudioTheme.Input("0.3","Transition duration",110);
    private readonly ComboBox _easing=new(){ItemsSource=new[]{"Linear","EaseIn","EaseOut","EaseInOut"},SelectedIndex=0,MinWidth=110,MinHeight=25,FontSize=11,Background=StudioTheme.Field};
    private readonly StackPanel _rules=new(){Spacing=3};
    private readonly TextBlock _message=StudioTheme.Text("",11,"#FFD09B");
    private long _revision;
    private bool _refreshing,_dirty;
    public StateTransitionEditorControl(DesignSession session,StatesControl states)
    {
        _session=session;_states=states;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        var body=new StackPanel{Spacing=7,Padding=new Thickness(8)};Content=body;
        void Label(string text){var label=StudioTheme.Text(text,11);label.TextWrapping=TextWrapping.Wrap;body.Children.Add(label);}
        void Row(params (string Label,string Name,Action Action)[] commands)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=3};foreach(var c in commands)row.Children.Add(new StudioButton(c.Label,()=>Guard(c.Action),c.Name));body.Children.Add(row);
        }
        Label("VISUAL STATE GROUPS");Label("Select a group in States. Each group can keep one active state; different groups can preview together.");body.Children.Add(_group);
        Row(("Add group","Add visual state group",AddGroup),("Rename","Rename visual state group",RenameGroup),("Delete","Delete visual state group",DeleteGroup));
        Label("GENERATED TRANSITION");Label("From / To: use * for any state. An exact pair wins, then To, From, and the default rule.");
        var selectors=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4};selectors.Children.Add(_from);selectors.Children.Add(_to);body.Children.Add(selectors);
        Label("Duration in seconds / cubic easing");var timing=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4};timing.Children.Add(_duration);timing.Children.Add(_easing);body.Children.Add(timing);AutomationProperties.SetName(_easing,"Transition easing");
        Row(("Save rule","Save visual transition",Save),("Remove","Remove visual transition",Remove),("Reload","Reload visual transition draft",Reload));
        Row(("Preview on/off","Toggle transition preview in editor",()=>states.SetTransitionsEnabled(!states.TransitionsEnabled)),("Stop","Complete visual transitions",()=>states.StopTransitions()));
        Label("Select a saved rule to edit it. Enable Animate in States, then select states to preview. Recording or editing the document completes the preview without writing animation frames to history.");
        body.Children.Add(_rules);_message.TextWrapping=TextWrapping.Wrap;body.Children.Add(_message);
        foreach(var input in new[]{_group,_from,_to,_duration})input.TextChanged+=(_,_)=>{if(!_refreshing)_dirty=true;};
        _easing.SelectionChanged+=(_,_)=>{if(!_refreshing)_dirty=true;};
        _session.DocumentChanged+=Changed;states.GroupChanged+=GroupChanged;Reload();
    }
    private void Guard(Action action){try{action();_message.Text="";}catch(Exception e){_message.Text=e.Message;}}
    private void GroupChanged(object? sender,EventArgs e)=>Reload();
    private void Changed(object? sender,EventArgs e)
    {
        if(_dirty){_message.Text="The document changed. Reload this draft before saving.";return;}Reload();
    }
    private void RequireCurrent(){if(_session.Revision!=_revision)throw new InvalidOperationException("This draft is stale. Reload it before saving.");}
    private void Reload()
    {
        _refreshing=true;_group.Text=_states.SelectedGroup;_from.Text="*";_to.Text="*";_duration.Text="0.3";_easing.SelectedItem="Linear";
        _rules.Children.Clear();var group=VisualStateGroups.Get(_session.Document).FirstOrDefault(g=>g.Name==_states.SelectedGroup);
        foreach(var rule in group?.Transitions??[])
        {
            var label=(rule.From??"Any")+" to "+(rule.To??"Any")+"   "+Numbers.Format(rule.Duration)+"s | "+rule.Easing;
            _rules.Children.Add(new StudioButton(label,()=>LoadRule(rule),"Edit visual transition "+(rule.From??"*")+" to "+(rule.To??"*")));
        }
        _revision=_session.Revision;_dirty=false;_refreshing=false;_message.Text="";
    }
    private void LoadRule(DesignTransition rule)
    {
        _refreshing=true;_from.Text=rule.From??"*";_to.Text=rule.To??"*";_duration.Text=Numbers.Format(rule.Duration);_easing.SelectedItem=rule.Easing;
        _revision=_session.Revision;_dirty=false;_refreshing=false;_message.Text="";
    }
    private static string? Selector(string value)=>value.Trim() is "" or "*" ? null : value.Trim();
    private void Save()
    {
        RequireCurrent();var name=_group.Text.Trim();var duration=Numbers.Parse(_duration.Text,double.NaN);
        var rule=new DesignTransition(Selector(_from.Text),Selector(_to.Text),duration,_easing.SelectedItem as string??"Linear");
        _session.Execute("Set visual transition",d=>StateGroupEditing.SetTransition(d,name,rule));_dirty=false;Reload();LoadRule(rule);
    }
    private void Remove(){RequireCurrent();var group=_group.Text.Trim();var from=Selector(_from.Text);var to=Selector(_to.Text);_session.Execute("Remove visual transition",d=>StateGroupEditing.RemoveTransition(d,group,from,to));_dirty=false;Reload();}
    private void AddGroup(){RequireCurrent();var name=_group.Text.Trim();_session.Execute("Add visual state group",d=>StateGroupEditing.AddGroup(d,name));_dirty=false;_states.SelectGroup(name);Reload();}
    private void RenameGroup(){RequireCurrent();var name=_group.Text.Trim();var old=_states.SelectedGroup;_session.Execute("Rename visual state group",d=>StateGroupEditing.RenameGroup(d,old,name));_dirty=false;_states.SelectGroup(name);Reload();}
    private void DeleteGroup(){RequireCurrent();var name=_states.SelectedGroup;_session.Execute("Delete visual state group",d=>StateGroupEditing.RemoveGroup(d,name));_dirty=false;Reload();}
    public new void Dispose(){_session.DocumentChanged-=Changed;_states.GroupChanged-=GroupChanged;}
}
