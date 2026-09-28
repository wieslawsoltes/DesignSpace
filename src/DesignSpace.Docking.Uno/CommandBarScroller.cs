using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Docking.Uno;

/// <summary>Compact command strip that keeps every command reachable without displaying a scrollbar.</summary>
public sealed class CommandBarScroller : Grid
{
    private readonly ScrollViewer _scroll;
    private readonly StudioButton _left,_right;
    private readonly FrameworkElement _content;
    public CommandBarScroller(FrameworkElement content)
    {
        _content=content;ColumnDefinitions.Add(new(){Width=GridLength.Auto});ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        _scroll=new(){Content=content,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Enabled};
        _left=new StudioButton("‹",()=>Scroll(-180),"Scroll commands left");_right=new StudioButton("›",()=>Scroll(180),"Scroll commands right");
        Children.Add(_left);SetColumn(_scroll,1);Children.Add(_scroll);SetColumn(_right,2);Children.Add(_right);
        SizeChanged+=(_,_)=>Refresh();content.SizeChanged+=(_,_)=>Refresh();_scroll.ViewChanged+=(_,_)=>Refresh();Refresh();
    }
    private void Scroll(double delta)=>_scroll.ChangeView(Math.Clamp(_scroll.HorizontalOffset+delta,0,_scroll.ScrollableWidth),null,null,true);
    private void Refresh()
    {
        var over=_content.ActualWidth>ActualWidth+1;_left.Visibility=_right.Visibility=over?Visibility.Visible:Visibility.Collapsed;
        _left.IsEnabled=_scroll.HorizontalOffset>1;_right.IsEnabled=_scroll.HorizontalOffset<_scroll.ScrollableWidth-1;
    }
}
