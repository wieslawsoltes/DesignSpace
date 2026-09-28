using DesignSpace.Core;
namespace DesignSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private bool _changingStatePreview;
    private string StateEditingContext=>States.SelectedState is { } state ? (States.IsRecording ? "Recording state: " : "Preview state: ")+state.Name
        : States.ActiveStates.Count>0 ? "Preview groups: "+string.Join(", ",States.ActiveStates.Values) : "";
    private void OnTimelinePreview(object? sender,EventArgs args)
    {
        if(_changingStatePreview)return;
        States.StopTransitions(false);Designer.SetPreview(Timeline.PreviewStoryboard,Timeline.PreviewTime,States.TargetState);Changed?.Invoke(this,EventArgs.Empty);
    }
    private void OnStateFrame(object? sender,EventArgs args)
    {
        Designer.SetPreview(null,0,States.PreviewState);Changed?.Invoke(this,EventArgs.Empty);
    }
    private void OnStateSelection(object? sender,DesignState? state)
    {
        _changingStatePreview=true;try{Timeline.Stop();}finally{_changingStatePreview=false;}
        Designer.SetPreview(null,0,States.PreviewState);
        Properties.ValueOverride=(node,key)=>States.TargetState?.Setters.FirstOrDefault(s=>s.TargetId==node.Id&&s.Property==key)?.Value;
        Properties.EditingContext=StateEditingContext;
        Properties.AllowInlineBrushEditing=!States.IsRecording;Properties.Refresh();
        SetStatus(States.ActiveStates.Count==0 ? "Base values" : (States.IsRecording ? "Recording properties in state: " : "Previewing visual state: ")+(state?.Name??string.Join(", ",States.ActiveStates.Values)));
    }
}
