using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
namespace DesignSpace.Docking.Uno;

public sealed class ToolPane : Grid
{
    public Border Header { get; }
    private readonly TextBlock _title;
    private readonly Grid _content=new();
    public string PaneId { get; }
    public event EventHandler? FloatRequested;
    public event EventHandler? CloseRequested;
    public ToolPane(string id,string title,UIElement content)
    {
        PaneId=id; Background=StudioTheme.Panel;
        RowDefinitions.Add(new(){Height=new GridLength(23)}); RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var header=new Grid(); header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)}); header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        _title=StudioTheme.Text(title,11); _title.Margin=new Thickness(7,0,0,0); header.Children.Add(_title);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal };
        buttons.Children.Add(new StudioButton("↗",()=>FloatRequested?.Invoke(this,EventArgs.Empty),"Float or dock "+title));
        buttons.Children.Add(new StudioButton("×",()=>CloseRequested?.Invoke(this,EventArgs.Empty),"Hide "+title)); SetColumn(buttons,1); header.Children.Add(buttons);
        Header=new Border { Background=StudioTheme.Background,Child=header,BorderBrush=StudioTheme.Border,BorderThickness=new Thickness(0,0,0,1) };
        Header.DoubleTapped+=(_,e)=> { FloatRequested?.Invoke(this,EventArgs.Empty); e.Handled=true; };
        Children.Add(Header); SetRow(_content,1); Children.Add(_content); _content.Children.Add(content);
    }
    public void SetTitle(string title)=>_title.Text=title;
}
public sealed record DockLayout(double LeftWidth=286,double RightWidth=286,double TopHeight=250,string[]? Hidden=null);
/// <summary>Three-column dock workspace with resizable stacked panes and in-window floating. No native-window assumptions.</summary>
public sealed class DockWorkspace : Grid
{
    private readonly Grid _layout=new();
    private readonly Grid _left=new();
    private readonly Canvas _floating=new() { IsHitTestVisible=true };
    private readonly Dictionary<string,(ToolPane Pane,Grid Host,int Row)> _panes=[];
    private readonly Dictionary<string,Border> _windows=[];
    private readonly HashSet<string> _hidden=[];
    private readonly ColumnDefinition _leftWidth=new(){Width=new GridLength(286)};
    private readonly ColumnDefinition _rightWidth=new(){Width=new GridLength(286)};
    private readonly RowDefinition _topHeight=new(){Height=new GridLength(250)};
    public event EventHandler? LayoutChanged;
    public DockWorkspace(UIElement center,ToolPane topLeft,ToolPane bottomLeft,ToolPane right)
    {
        Background=StudioTheme.Background;
        _layout.ColumnDefinitions.Add(_leftWidth); _layout.ColumnDefinitions.Add(new(){Width=new GridLength(4)}); _layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)}); _layout.ColumnDefinitions.Add(new(){Width=new GridLength(4)}); _layout.ColumnDefinitions.Add(_rightWidth);
        _left.RowDefinitions.Add(_topHeight); _left.RowDefinitions.Add(new(){Height=new GridLength(4)}); _left.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        _left.Children.Add(topLeft); SetRow(bottomLeft,2); _left.Children.Add(bottomLeft);
        var vertical=new DockSplitter(false); vertical.Delta+=(_,delta)=> { _topHeight.Height=new GridLength(Math.Clamp(_topHeight.Height.Value+delta,100,Math.Max(100,ActualHeight-140))); Notify(); }; SetRow(vertical,1); _left.Children.Add(vertical);
        _layout.Children.Add(_left); SetColumn(center,2); _layout.Children.Add(center);
        var rightHost=new Grid(); rightHost.Children.Add(right); SetColumn(rightHost,4); _layout.Children.Add(rightHost);
        var leftSplit=new DockSplitter(); leftSplit.Delta+=(_,d)=> { _leftWidth.Width=new GridLength(Math.Clamp(_leftWidth.Width.Value+d,180,520)); Notify(); }; SetColumn(leftSplit,1); _layout.Children.Add(leftSplit);
        var rightSplit=new DockSplitter(); rightSplit.Delta+=(_,d)=> { _rightWidth.Width=new GridLength(Math.Clamp(_rightWidth.Width.Value-d,210,520)); Notify(); }; SetColumn(rightSplit,3); _layout.Children.Add(rightSplit);
        Register(topLeft,_left,0); Register(bottomLeft,_left,2); Register(right,rightHost,0);
        Children.Add(_layout); Children.Add(_floating);
    }
    private void Register(ToolPane pane,Grid host,int row)
    {
        _panes[pane.PaneId]=(pane,host,row); pane.FloatRequested+=(_,_)=>ToggleFloat(pane.PaneId); pane.CloseRequested+=(_,_)=>Hide(pane.PaneId);
    }
    public void ToggleFloat(string id)
    {
        if(!_panes.TryGetValue(id,out var p)) return;
        if(_windows.Remove(id,out var existing)) { existing.Child=null; _floating.Children.Remove(existing); SetRow(p.Pane,p.Row); p.Host.Children.Add(p.Pane); }
        else
        {
            p.Host.Children.Remove(p.Pane); p.Pane.Visibility=Visibility.Visible; _hidden.Remove(id);
            var window=new Border { Width=Math.Max(280,p.Pane.ActualWidth),Height=Math.Max(300,p.Pane.ActualHeight),Child=p.Pane,Background=StudioTheme.Panel,BorderBrush=StudioTheme.Accent,BorderThickness=new Thickness(1) };
            Canvas.SetLeft(window,Math.Max(40,ActualWidth/3)); Canvas.SetTop(window,40); _floating.Children.Add(window); _windows[id]=window;
            bool dragging=false; Point previous=default;
            p.Pane.Header.PointerPressed+=Start; p.Pane.Header.PointerMoved+=Move; p.Pane.Header.PointerReleased+=End;
            void Start(object sender,Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { if(!_windows.TryGetValue(id,out var active) || active!=window || e.OriginalSource is Button) return; dragging=true; previous=e.GetCurrentPoint(this).Position; p.Pane.Header.CapturePointer(e.Pointer); }
            void Move(object sender,Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { if(!dragging || !_windows.ContainsKey(id)) return; var now=e.GetCurrentPoint(this).Position; Canvas.SetLeft(window,Math.Clamp(Canvas.GetLeft(window)+now.X-previous.X,0,Math.Max(0,ActualWidth-window.Width))); Canvas.SetTop(window,Math.Clamp(Canvas.GetTop(window)+now.Y-previous.Y,0,Math.Max(0,ActualHeight-50))); previous=now; }
            void End(object sender,Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { dragging=false; p.Pane.Header.ReleasePointerCapture(e.Pointer); }
            window.Unloaded+=(_,_)=> { p.Pane.Header.PointerPressed-=Start; p.Pane.Header.PointerMoved-=Move; p.Pane.Header.PointerReleased-=End; };
        }
        Notify();
    }
    public void Hide(string id) { if(!_panes.TryGetValue(id,out var p)) return; if(_windows.ContainsKey(id)) ToggleFloat(id); p.Pane.Visibility=Visibility.Collapsed; _hidden.Add(id); Notify(); }
    public void Show(string id) { if(!_panes.TryGetValue(id,out var p)) return; p.Pane.Visibility=Visibility.Visible; _hidden.Remove(id); Notify(); }
    public void Reset() { foreach(var id in _windows.Keys.ToArray()) ToggleFloat(id); foreach(var id in _panes.Keys) Show(id); Apply(new()); }
    public DockLayout Capture()=>new(_leftWidth.Width.Value,_rightWidth.Width.Value,_topHeight.Height.Value,_hidden.ToArray());
    public void Apply(DockLayout layout)
    {
        _leftWidth.Width=new GridLength(Math.Clamp(layout.LeftWidth,180,520)); _rightWidth.Width=new GridLength(Math.Clamp(layout.RightWidth,210,520)); _topHeight.Height=new GridLength(Math.Clamp(layout.TopHeight,100,600));
        foreach(var id in layout.Hidden ?? []) Hide(id); Notify();
    }
    private void Notify()=>LayoutChanged?.Invoke(this,EventArgs.Empty);
}
