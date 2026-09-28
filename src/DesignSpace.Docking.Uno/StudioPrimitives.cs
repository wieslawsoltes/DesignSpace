using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
namespace DesignSpace.Docking.Uno;

public static class StudioTheme
{
    public static SolidColorBrush Brush(string color)
    {
        var s=color.TrimStart('#'); var value=Convert.ToUInt32(s,16);
        return new(Windows.UI.Color.FromArgb(s.Length==8 ? (byte)(value>>24) : (byte)255,(byte)(value>>16),(byte)(value>>8),(byte)value));
    }
    public static SolidColorBrush Background => Brush("#2D2D30");
    public static SolidColorBrush Panel => Brush("#252526");
    public static SolidColorBrush Field => Brush("#333337");
    public static SolidColorBrush Foreground => Brush("#D6D6D6");
    public static SolidColorBrush Muted => Brush("#99999F");
    public static SolidColorBrush Border => Brush("#3F3F46");
    public static SolidColorBrush Accent => Brush("#007ACC");
    public static TextBlock Text(string text,double size=11,string color="#D6D6D6") => new() { Text=text,FontSize=size,Foreground=Brush(color),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis };
    public static TextBox Input(string value,string name,double width=double.NaN)
    {
        var input=new TextBox { Text=value,Width=width,MinWidth=0,MinHeight=23,FontSize=11,Padding=new Thickness(5,2,5,2),Background=Field,Foreground=Foreground,BorderBrush=Border,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(0),IsSpellCheckEnabled=false };
        AutomationProperties.SetName(input,name); ToolTipService.SetToolTip(input,name); return input;
    }
}
public sealed class StudioButton : Button
{
    private bool _selected;
    public bool IsSelected { get=>_selected; set { _selected=value; Background=value ? StudioTheme.Accent : StudioTheme.Brush("#00000000"); } }
    public StudioButton(string text,Action? action=null,string? name=null)
    {
        Content=StudioTheme.Text(text); Background=StudioTheme.Brush("#00000000"); Foreground=StudioTheme.Foreground;
        BorderThickness=new Thickness(0); CornerRadius=new CornerRadius(0); Padding=new Thickness(7,3,7,3); MinHeight=23; MinWidth=0;
        HorizontalContentAlignment=HorizontalAlignment.Left; VerticalContentAlignment=VerticalAlignment.Center;
        AutomationProperties.SetName(this,name ?? text); ToolTipService.SetToolTip(this,name ?? text);
        if(action is not null) Click+=(_,_)=>action();
        PointerEntered+=(_,_)=>Background=_selected ? StudioTheme.Accent : StudioTheme.Brush("#444448");
        PointerExited+=(_,_)=>Background=_selected ? StudioTheme.Accent : StudioTheme.Brush("#00000000");
    }
}
public sealed class DockSplitter : Border
{
    private bool _dragging; private Point _previous;
    public event EventHandler<double>? Delta;
    public DockSplitter(bool vertical=true)
    {
        Background=StudioTheme.Background; if(vertical) Width=4; else Height=4;
        AutomationProperties.SetName(this,vertical ? "Resize side pane" : "Resize stacked pane");
        PointerEntered+=(_,_)=>Background=StudioTheme.Accent;
        PointerExited+=(_,_)=> { if(!_dragging) Background=StudioTheme.Background; };
        PointerPressed+=(_,e)=> { _dragging=true; _previous=e.GetCurrentPoint(null).Position; CapturePointer(e.Pointer); e.Handled=true; };
        PointerMoved+=(_,e)=> { if(!_dragging) return; var p=e.GetCurrentPoint(null).Position; Delta?.Invoke(this,vertical ? p.X-_previous.X : p.Y-_previous.Y); _previous=p; e.Handled=true; };
        PointerReleased+=(_,e)=> { _dragging=false; ReleasePointerCapture(e.Pointer); Background=StudioTheme.Background; e.Handled=true; };
        PointerCaptureLost+=(_,_)=> { _dragging=false; Background=StudioTheme.Background; };
    }
}
public sealed class StudioTabs : Grid
{
    private readonly StackPanel _tabs=new() { Orientation=Orientation.Horizontal,Spacing=0,Background=StudioTheme.Background };
    private readonly Grid _body=new();
    private readonly Dictionary<string,(UIElement Content,StudioButton Button)> _items=[];
    public string Selected { get; private set; }="";
    public event EventHandler<string>? SelectionChanged;
    public StudioTabs()
    {
        RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)}); RowDefinitions.Add(new(){Height=new GridLength(24)});
        Children.Add(_body); SetRow(_tabs,1); Children.Add(_tabs);
    }
    public void Add(string name,UIElement content)
    {
        var button=new StudioButton(name,()=>Select(name)); _items[name]=(content,button); _tabs.Children.Add(button); content.Visibility=Visibility.Collapsed; _body.Children.Add(content); if(Selected.Length==0) Select(name);
    }
    public void Select(string name)
    {
        if(!_items.ContainsKey(name)) return;
        foreach(var item in _items) { item.Value.Content.Visibility=item.Key==name ? Visibility.Visible : Visibility.Collapsed; item.Value.Button.IsSelected=item.Key==name; }
        Selected=name; SelectionChanged?.Invoke(this,name);
    }
}
