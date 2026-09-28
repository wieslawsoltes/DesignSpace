using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

public sealed class AssetsControl : Grid
{
    private readonly TextBox _filter=StudioTheme.Input("","Search Assets");
    private readonly StackPanel _items=new(){Spacing=1};
    private string _category="All";
    private static readonly (string Kind,string Category,string Icon)[] Assets=[("Button","Controls","▣"),("TextBox","Controls","▤"),("CheckBox","Controls","☑"),("RadioButton","Controls","⊙"),("ToggleSwitch","Controls","⇆"),("Slider","Controls","⊸"),("ProgressBar","Controls","━"),("TextBlock","Text","T"),("Rectangle","Shapes","□"),("Ellipse","Shapes","○"),("Line","Shapes","╱"),("Path","Shapes","◇"),("Grid","Panels","⊞"),("Canvas","Panels","▧"),("StackPanel","Panels","☷"),("Border","Panels","▢")];
    public event EventHandler<string>? AssetInvoked;
    public AssetsControl()
    {
        RowDefinitions.Add(new(){Height=new GridLength(29)}); RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        _filter.PlaceholderText="Search Assets"; _filter.Margin=new Thickness(5,3,5,3); _filter.TextChanged+=(_,_)=>Refresh(); Children.Add(_filter);
        var body=new Grid(); body.ColumnDefinitions.Add(new(){Width=new GridLength(85)}); body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var categories=new StackPanel { Background=StudioTheme.Brush("#202021"),Padding=new Thickness(0,3,0,0) };
        foreach(var name in new[]{"All","Controls","Panels","Shapes","Text"}) categories.Children.Add(new StudioButton("▸ "+name,()=> { _category=name; Refresh(); },"Asset category "+name));
        body.Children.Add(categories); var scroll=new ScrollViewer { Content=_items,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; SetColumn(scroll,1); body.Children.Add(scroll); SetRow(body,1); Children.Add(body); Refresh();
    }
    private void Refresh()
    {
        _items.Children.Clear();
        foreach(var asset in Assets.Where(a=>(_category=="All" || a.Category==_category) && a.Kind.Contains(_filter.Text,StringComparison.OrdinalIgnoreCase)))
        {
            var button=new StudioButton(asset.Icon+"   "+asset.Kind,()=>AssetInvoked?.Invoke(this,asset.Kind),"Add "+asset.Kind); button.HorizontalAlignment=HorizontalAlignment.Stretch; button.MinHeight=25; _items.Children.Add(button);
        }
    }
}
