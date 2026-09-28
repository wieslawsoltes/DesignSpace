using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Xaml;
namespace DesignSpace.Controls.Uno;

/// <summary>Inert draft transfer across document tabs. The host owns persistence and revision rebasing.</summary>
public interface IWorkspaceDraftEditor
{
    DesignerPanelDraft CaptureWorkspaceDraft();
    void RestoreWorkspaceDraft(DesignerPanelDraft? state);
}
public sealed partial class TemplateEditorControl : IWorkspaceDraftEditor
{
    public DesignerPanelDraft CaptureWorkspaceDraft()=>new()
    {
        Values=ImmutableDictionary<string,string>.Empty.Add("Source",_source.Text).Add("Key",_key??""),
        Originals=ImmutableDictionary<string,string>.Empty.Add("Canonical",_canonical).Add("Initial",_initialDraft),
        HasChanges=Dirty && Normalize(_source.Text)!=_initialDraft
    };
    public void RestoreWorkspaceDraft(DesignerPanelDraft? state)
    {
        _syncing=true;_source.Text="";_canonical="";_initialDraft="";_key=null;_syncing=false;Refresh();
        if(state is null){if(_key is null)New();return;}
        _syncing=true;
        try{_key=state.Values.GetValueOrDefault("Key");_source.Text=state.Values.GetValueOrDefault("Source","");_canonical=state.Originals.GetValueOrDefault("Canonical","");_initialDraft=state.Originals.GetValueOrDefault("Initial","");_templates.SelectedItem=_key;}
        finally{_syncing=false;}
        _status.Text=Dirty ? "Retained template draft for this document" : "Template synchronized";
    }
}
public sealed partial class SampleDataControl : IWorkspaceDraftEditor
{
    private void RequireDraftCurrent(){if(_dirty&&_draftRevision!=_session.Revision)throw new InvalidOperationException("Sample data draft is stale. Reload it before applying.");}
    public DesignerPanelDraft CaptureWorkspaceDraft()=>new()
    {
        Values=ImmutableDictionary<string,string>.Empty.Add("JSON",_json.Text).Add("Path",_path.Text),
        Originals=ImmutableDictionary<string,string>.Empty.Add("Canonical",_canonical),
        HasChanges=_dirty,MatchesDesign=_draftRevision==_session.Revision
    };
    public void RestoreWorkspaceDraft(DesignerPanelDraft? state)
    {
        _dirty=false;Refresh();if(state is null)return;
        _refreshing=true;_json.Text=state.Values.GetValueOrDefault("JSON",DesignData.Read(_session.Document.Root));_path.Text=state.Values.GetValueOrDefault("Path","Title");
        _canonical=state.Originals.GetValueOrDefault("Canonical",Normalize(_json.Text));_dirty=state.HasChanges;_draftRevision=state.MatchesDesign ? _session.Revision : -1;_refreshing=false;
    }
}
public sealed partial class StrokeEditorControl : IWorkspaceDraftEditor
{
    public DesignerPanelDraft CaptureWorkspaceDraft()=>new()
    {
        Values=_values.ToImmutableDictionary(p=>p.Key,p=>p.Value()),Originals=_original.ToImmutableDictionary(),
        Targets=_targets.ToImmutableArray(),HasChanges=_dirty,MatchesDesign=_revision==_session.Revision
    };
    public void RestoreWorkspaceDraft(DesignerPanelDraft? state)
    {
        _dirty=false;_restoreTargets=state?.HasChanges==true ? state.Targets.ToImmutableHashSet() : null;
        try{Refresh(true);}finally{_restoreTargets=null;}
        if(state?.HasChanges!=true)return;
        _syncing=true;
        foreach(var p in state.Values)if(_setters.TryGetValue(p.Key,out var setter))setter(p.Value);
        foreach(var p in state.Originals)if(_original.ContainsKey(p.Key))_original[p.Key]=p.Value;
        _targets=state.Targets.ToImmutableHashSet();_revision=state.MatchesDesign ? _session.Revision : -1;_dirty=true;_syncing=false;
        _status.Text="Retained stroke draft for this document";UpdatePreview();
    }
}
public sealed partial class StateTransitionEditorControl : IWorkspaceDraftEditor
{
    public DesignerPanelDraft CaptureWorkspaceDraft()=>new()
    {
        Values=ImmutableDictionary<string,string>.Empty.Add("Group",_group.Text).Add("From",_from.Text).Add("To",_to.Text).Add("Duration",_duration.Text).Add("Easing",_easing.SelectedItem as string??"Linear"),
        HasChanges=_dirty,MatchesDesign=_revision==_session.Revision
    };
    public void RestoreWorkspaceDraft(DesignerPanelDraft? state)
    {
        _dirty=false;Reload();if(state is null)return;_refreshing=true;
        _group.Text=state.Values.GetValueOrDefault("Group",_states.SelectedGroup);_from.Text=state.Values.GetValueOrDefault("From","*");_to.Text=state.Values.GetValueOrDefault("To","*");_duration.Text=state.Values.GetValueOrDefault("Duration","0.3");_easing.SelectedItem=state.Values.GetValueOrDefault("Easing","Linear");
        _revision=state.MatchesDesign ? _session.Revision : -1;_dirty=state.HasChanges;_refreshing=false;
    }
}
public sealed partial class StoryboardSettingsControl
{
    internal ImmutableDictionary<string,string> CaptureFields()=>ImmutableDictionary<string,string>.Empty
        .Add("Duration",_duration.Text).Add("Begin",_begin.Text).Add("Speed",_speed.Text).Add("Repeat",_repeat.Text)
        .Add("RepeatMode",_repeatMode.SelectedItem as string??"Count").Add("Fill",_fill.SelectedItem as string??"HoldEnd")
        .Add("Reverse",(_reverse.IsChecked==true).ToString()).Add("Scale",(_scale.IsChecked==true).ToString());
    internal void RestoreFields(ImmutableDictionary<string,string> fields)
    {
        _duration.Text=fields.GetValueOrDefault("Duration",_duration.Text);_begin.Text=fields.GetValueOrDefault("Begin",_begin.Text);_speed.Text=fields.GetValueOrDefault("Speed",_speed.Text);_repeat.Text=fields.GetValueOrDefault("Repeat",_repeat.Text);
        _repeatMode.SelectedItem=fields.GetValueOrDefault("RepeatMode","Count");_fill.SelectedItem=fields.GetValueOrDefault("Fill","HoldEnd");_reverse.IsChecked=fields.GetValueOrDefault("Reverse")=="True";_scale.IsChecked=fields.GetValueOrDefault("Scale")=="True";
    }
}
public sealed partial class StoryboardInspectorControl : IWorkspaceDraftEditor
{
    public DesignerPanelDraft CaptureWorkspaceDraft()
    {
        if(_displayed is null||_workspaceEditor is null)return new();
        var fields=_workspaceEditor.CaptureFields();var changed=fields.Any(p=>_workspaceInitialFields.GetValueOrDefault(p.Key)!=p.Value);
        return new(){Values=fields.Add("Board",_displayed.Id.ToString()),Originals=_workspaceInitialFields,HasChanges=changed,MatchesDesign=!_workspaceStale&&ReferenceEquals(_displayed,_timeline.ActiveStoryboard)};
    }
    public void RestoreWorkspaceDraft(DesignerPanelDraft? state)
    {
        _displayed=null;_workspaceEditor=null;Refresh();
        if(state?.HasChanges==true&&_workspaceEditor is not null&&state.Values.GetValueOrDefault("Board")==_displayed?.Id.ToString())
        {
            _workspaceEditor.RestoreFields(state.Values);_workspaceInitialFields=state.Originals;_workspaceStale=!state.MatchesDesign;
            if(_workspaceStale)_workspaceEditor.ShowError("This retained timing draft is stale; reselect the storyboard to reload it.");
        }
    }
}
