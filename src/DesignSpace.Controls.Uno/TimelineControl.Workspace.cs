namespace DesignSpace.Controls.Uno;
public sealed partial class TimelineControl
{
    public void SelectStoryboard(Guid? id,double localTime=0)
    {
        StopPlayback();IsRecording=false;_record.IsSelected=false;_boardId=id;_key=null;_trackSelection=null;Refresh();Scrub(localTime);SelectedStoryboardChanged?.Invoke(this,EventArgs.Empty);
    }
}
