namespace DesignSpace.Controls.Uno;
public sealed partial class TimelineControl
{
    public void SelectStoryboard(Guid? id,double localTime=0)
    {
        StopPlayback();_boardId=id;_key=null;Refresh();Scrub(localTime);SelectedStoryboardChanged?.Invoke(this,EventArgs.Empty);
    }
}
