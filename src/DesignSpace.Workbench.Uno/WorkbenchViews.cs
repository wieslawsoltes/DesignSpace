using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
namespace DesignSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private string _splitOrientation="Vertical",_workspaceProfile="Design";
    private double _splitRatio=.5;
    private DockSplitter? _sourceSplitter;
    private readonly Dictionary<string,(DockLayout Dock,double Timeline)> _profiles=[];
    public string SplitOrientation=>_splitOrientation;
    public string WorkspaceProfile=>_workspaceProfile;
    public double TimelineHeight=>_timelineHeight.Height.Value;
    private void ApplySourceLayout()
    {
        if(_designSplit.Children.Count==0)return;
        Designer.Visibility=_mode=="XAML"?Visibility.Collapsed:Visibility.Visible;
        Source.Visibility=_mode=="Design"?Visibility.Collapsed:Visibility.Visible;
        _sourceSplitter!.Visibility=_mode=="Split"?Visibility.Visible:Visibility.Collapsed;
        _designSplit.RowDefinitions.Clear();_designSplit.ColumnDefinitions.Clear();
        var horizontal=_splitOrientation=="Horizontal";
        var first=_mode=="XAML"?0:_mode=="Design"?1:_splitRatio;
        var last=_mode=="Design"?0:_mode=="XAML"?1:1-_splitRatio;
        if(horizontal)
        {
            _designSplit.RowDefinitions.Add(new(){Height=new GridLength(first,GridUnitType.Star)});
            _designSplit.RowDefinitions.Add(new(){Height=new GridLength(_mode=="Split"?4:0)});
            _designSplit.RowDefinitions.Add(new(){Height=new GridLength(last,GridUnitType.Star)});
        }
        else
        {
            _designSplit.ColumnDefinitions.Add(new(){Width=new GridLength(first,GridUnitType.Star)});
            _designSplit.ColumnDefinitions.Add(new(){Width=new GridLength(_mode=="Split"?4:0)});
            _designSplit.ColumnDefinitions.Add(new(){Width=new GridLength(last,GridUnitType.Star)});
        }
        SetRow(Designer,0);SetColumn(Designer,0);
        SetRow(_sourceSplitter,horizontal?1:0);SetColumn(_sourceSplitter,horizontal?0:1);
        SetRow(Source,horizontal?2:0);SetColumn(Source,horizontal?0:2);
    }
    private void CreateSourceSplitter()
    {
        if(_sourceSplitter is not null)_designSplit.Children.Remove(_sourceSplitter);
        _sourceSplitter=new DockSplitter(_splitOrientation!="Horizontal");
        _sourceSplitter.Delta+=(_,delta)=>
        {
            var total=(_splitOrientation=="Horizontal"?_designSplit.ActualHeight:_designSplit.ActualWidth)-4;
            if(_mode!="Split"||total<100)return;_splitRatio=Math.Clamp(_splitRatio+delta/total,.1,.9);ApplySourceLayout();QueueWorkspaceRecovery();
        };
        _designSplit.Children.Add(_sourceSplitter);
    }
    public void SetSplitOrientation(string orientation)
    {
        if(orientation is not ("Horizontal" or "Vertical"))throw new ArgumentException("Invalid split orientation.");
        _splitOrientation=orientation;CreateSourceSplitter();ApplySourceLayout();QueueWorkspaceRecovery();Changed?.Invoke(this,EventArgs.Empty);
    }
    private void SetTimelineHeight(double height)
    {
        _timelineHeight.Height=new GridLength(height);_timelineHost.Visibility=height>0?Visibility.Visible:Visibility.Collapsed;
    }
    public void SetWorkspaceProfile(string profile)
    {
        if(profile is not ("Design" or "Animation"))throw new ArgumentException("Unknown workspace profile.");
        if(profile==_workspaceProfile)return;
        _profiles[_workspaceProfile]=(_dock.Capture(),_timelineHeight.Height.Value);
        _workspaceProfile=profile;
        var next=_profiles.GetValueOrDefault(profile,profile=="Animation" ? (new DockLayout(250,286,220),300d) : (new DockLayout(),170d));
        _dock.Apply(next.Item1);SetTimelineHeight(Math.Min(next.Item2,Math.Max(100,ActualHeight*.5)));Timeline.Fit();
        SetStatus(profile+" workspace · F6 switches workspaces");QueueWorkspaceRecovery();
    }
}
