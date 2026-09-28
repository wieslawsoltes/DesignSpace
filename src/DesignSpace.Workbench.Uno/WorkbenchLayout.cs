using DesignSpace.Controls.Uno;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private void BuildWorkspace()
    {
        Background=StudioTheme.Background;
        foreach(var height in new[]{30d,25,29}) RowDefinitions.Add(new(){Height=new GridLength(height)});
        RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)}); RowDefinitions.Add(new(){Height=new GridLength(22)});
        BuildHeader(); var menu=BuildMenus(); SetRow(menu,1); Children.Add(menu); var toolbar=BuildToolbar(); SetRow(toolbar,2); Children.Add(toolbar);
        var assets=new AssetsControl(); assets.AssetInvoked+=(_,type)=>Guard(()=>AddAsset(type));
        _leftTabs.Add("Assets",assets); _leftTabs.Add("Project",BuildProject()); _leftTabs.Add("States",States); _leftTabs.Add("Data",Data);
        _rightTabs.Add("Properties",Properties); _rightTabs.Add("Resources",Resources);
        var center=new Grid();
        center.RowDefinitions.Add(new(){Height=new GridLength(27)}); center.RowDefinitions.Add(new(){Height=new GridLength(28)}); center.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)}); center.RowDefinitions.Add(new(){Height=new GridLength(4)}); center.RowDefinitions.Add(_timelineHeight); center.RowDefinitions.Add(new(){Height=new GridLength(25)});
        var tab=new Border { Background=StudioTheme.Accent,HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(11,4,18,4),MinWidth=160,Child=_documentTitle }; center.Children.Add(tab);
        var designerBar=new StackPanel { Orientation=Orientation.Horizontal,Background=StudioTheme.Brush("#333337"),Spacing=4,Padding=new Thickness(5,1,5,1) };
        designerBar.Children.Add(StudioTheme.Text("Design surface",11,"#AEAEBA")); designerBar.Children.Add(new StudioButton("Fit",Designer.Fit,"Fit artboard")); designerBar.Children.Add(new StudioButton("−",()=>Designer.Zoom(.8),"Zoom out")); _zoom.Width=44; designerBar.Children.Add(_zoom); designerBar.Children.Add(new StudioButton("+",()=>Designer.Zoom(1.25),"Zoom in"));
        designerBar.Children.Add(new StudioButton("Grid",()=> { Designer.Viewport.ShowGrid=!Designer.Viewport.ShowGrid; Designer.Invalidate(); },"Toggle design grid"));
        designerBar.Children.Add(new StudioButton("Snap",()=> { Designer.Viewport.SnapToGrid=!Designer.Viewport.SnapToGrid; SetStatus(Designer.Viewport.SnapToGrid ? "Grid snapping enabled · Alt temporarily disables snapping" : "Grid snapping disabled"); },"Toggle grid snapping"));
        designerBar.Children.Add(new StudioButton("Base",()=> { Timeline.Stop(); States.Select(null); Designer.ClearPreview(); },"Return to base values")); SetRow(designerBar,1); center.Children.Add(designerBar);
        _designSplit.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)}); _designSplit.ColumnDefinitions.Add(new(){Width=new GridLength(0)}); _designSplit.ColumnDefinitions.Add(new(){Width=new GridLength(0)});
        _designSplit.Children.Add(Designer);
        var sourceSplit=new DockSplitter(); sourceSplit.Delta+=(_,delta)=> { if(_mode!="Split") return; var width=Math.Clamp(Designer.ActualWidth+delta,100,Math.Max(100,_designSplit.ActualWidth-150)); _designSplit.ColumnDefinitions[0].Width=new GridLength(width); };
        SetColumn(sourceSplit,1); _designSplit.Children.Add(sourceSplit); Source.Visibility=Visibility.Collapsed; SetColumn(Source,2); _designSplit.Children.Add(Source); SetRow(_designSplit,2); center.Children.Add(_designSplit);
        var timelineSplit=new DockSplitter(false); timelineSplit.Delta+=(_,delta)=> { _timelineHeight.Height=new GridLength(Math.Clamp(_timelineHeight.Height.Value-delta,90,Math.Max(90,center.ActualHeight-170))); }; SetRow(timelineSplit,3); center.Children.Add(timelineSplit);
        _timelineHost=new Border { Child=Timeline,BorderBrush=StudioTheme.Border,BorderThickness=new Thickness(0,1,0,0) }; SetRow(_timelineHost,4); center.Children.Add(_timelineHost);
        var modebar=new StackPanel { Orientation=Orientation.Horizontal,Background=StudioTheme.Panel };
        foreach(var mode in new[]{"Design","Split","XAML"}) { var button=new StudioButton(mode,()=>SetMode(mode),mode+" view"); _modes[mode]=button; modebar.Children.Add(button); }
        SetRow(modebar,5); center.Children.Add(modebar);
        var leftPane=new ToolPane("assets","Assets",_leftTabs); var rightPane=new ToolPane("properties","Properties",_rightTabs);
        _leftTabs.SelectionChanged+=(_,name)=>leftPane.SetTitle(name); _rightTabs.SelectionChanged+=(_,name)=>rightPane.SetTitle(name);
        _dock=new DockWorkspace(center,leftPane,new ToolPane("objects","Objects and Timeline",Outline),rightPane);
        var body=new Grid(); body.ColumnDefinitions.Add(new(){Width=new GridLength(36)}); body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)}); body.Children.Add(BuildTools()); SetColumn(_dock,1); body.Children.Add(_dock); SetRow(body,3); Children.Add(body);
        var statusbar=new Grid { Background=StudioTheme.Accent,Padding=new Thickness(8,0,8,0) }; statusbar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)}); statusbar.ColumnDefinitions.Add(new(){Width=GridLength.Auto}); statusbar.Children.Add(_status); SetColumn(_metrics,1); statusbar.Children.Add(_metrics); SetRow(statusbar,4); Children.Add(statusbar);
    }
    private void BuildHeader()
    {
        var header=new Grid { Background=StudioTheme.Panel,Padding=new Thickness(10,0,10,0) }; header.ColumnDefinitions.Add(new(){Width=GridLength.Auto}); header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)}); header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var mark=StudioTheme.Text("◇",21,"#64B5F6"); mark.Margin=new Thickness(0,0,9,0); header.Children.Add(mark); SetColumn(_title,1); header.Children.Add(_title);
        var right=StudioTheme.Text("XAML DESIGNER   /   0.1 PREVIEW",10,"#9999A3"); SetColumn(right,2); header.Children.Add(right); Children.Add(header);
    }
    private UIElement BuildTools()
    {
        var tools=new StackPanel { Spacing=2,Padding=new Thickness(2,5,2,5),Background=StudioTheme.Panel };
        foreach(var (name,key) in new[]{("Selection","V"),("Direct Selection","A"),("Hand","H"),("Zoom","Z"),("Rectangle","R"),("Ellipse","E"),("Line","L"),("Pen","P"),("Pencil","Y"),("TextBlock","T"),("Grid","G"),("Button","B")})
        {
            var button=new StudioButton("",()=>SetTool(name),"Tool "+name+" ("+key+")") { Content=new DesignerToolIcon { Kind=name,Width=22,Height=22 },Width=31,Height=29,HorizontalContentAlignment=HorizontalAlignment.Center,Padding=new Thickness(0) }; _tools[name]=button; tools.Children.Add(button);
        }
        tools.Children.Add(new Border { Height=1,Background=StudioTheme.Border,Margin=new Thickness(4,4,4,4) }); tools.Children.Add(new Border { Width=20,Height=20,Background=StudioTheme.Accent,BorderBrush=StudioTheme.Foreground,BorderThickness=new Thickness(1),Margin=new Thickness(0,4,0,0) });
        return new ScrollViewer { Content=tools,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
    }
}
