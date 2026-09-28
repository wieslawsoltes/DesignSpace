using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
namespace DesignSpace.Controls.Uno;

public sealed record DocumentTabItem(Guid Id,string Title,bool Dirty,bool Active,bool Pinned);
/// <summary>Reusable document strip with close, pin, reorder, overflow navigation and accessible commands.</summary>
public sealed class DocumentTabsControl : Grid
{
    private readonly StackPanel _tabs=new(){Orientation=Orientation.Horizontal};
    private readonly ScrollViewer _scroll=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Enabled};
    private readonly StudioButton _left,_right;
    private DocumentTabItem[] _items=[];
    private readonly Dictionary<Guid,FrameworkElement> _headers=[];
    private Guid? _drag;
    private Point _down;
    private bool _dragging;
    public event EventHandler<Guid>? Selected;
    public event EventHandler<Guid>? CloseRequested;
    public event EventHandler<Guid>? CloseOthersRequested;
    public event EventHandler<Guid>? PinRequested;
    public event EventHandler<(Guid Id,int Index)>? MoveRequested;
    public DocumentTabsControl()
    {
        Height=27;Background=StudioTheme.Panel;
        ColumnDefinitions.Add(new(){Width=GridLength.Auto});ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        _left=new StudioButton("‹",()=>_scroll.ChangeView(Math.Max(0,_scroll.HorizontalOffset-180),null,null),"Scroll document tabs left");
        _right=new StudioButton("›",()=>_scroll.ChangeView(_scroll.HorizontalOffset+180,null,null),"Scroll document tabs right");
        _scroll.Content=_tabs;Children.Add(_left);SetColumn(_scroll,1);Children.Add(_scroll);SetColumn(_right,2);Children.Add(_right);
        SizeChanged+=(_,_)=>UpdateOverflow();_scroll.SizeChanged+=(_,_)=>UpdateOverflow();_scroll.ViewChanged+=(_,_)=>UpdateOverflow();_tabs.SizeChanged+=(_,_)=>UpdateOverflow();
    }
    public void SetItems(IEnumerable<DocumentTabItem> items)
    {
        var next=items.ToArray();if(_items.SequenceEqual(next))return;
        _items=next;_tabs.Children.Clear();_headers.Clear();
        foreach(var item in next)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,Background=item.Active?StudioTheme.Accent:StudioTheme.Panel};
            var select=new StudioButton((item.Pinned ? "• " : "")+item.Title+(item.Dirty?" *":""),()=>{if(!_dragging)Selected?.Invoke(this,item.Id);},"Document tab "+item.Title)
                {MaxWidth=205,Height=27,Padding=new Thickness(9,3,7,3),IsSelected=item.Active};
            var close=new StudioButton("×",()=>CloseRequested?.Invoke(this,item.Id),"Close document "+item.Title){Height=27,Padding=new Thickness(5,3,5,3)};
            row.Children.Add(select);row.Children.Add(close);_tabs.Children.Add(row);_headers[item.Id]=row;
            AutomationProperties.SetHelpText(select,"Select document. Drag to reorder. Right-click for pin and close commands.");
            select.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,e)=>{if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed){_drag=item.Id;_down=e.GetCurrentPoint(this).Position;_dragging=false;}}),true);
            select.AddHandler(UIElement.PointerMovedEvent,new PointerEventHandler((_,e)=>
            {
                if(_drag!=item.Id||!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
                var point=e.GetCurrentPoint(this).Position;if(Math.Abs(point.X-_down.X)>8){_dragging=true;select.CapturePointer(e.Pointer);}
            }),true);
            select.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,e)=>
            {
                if(_drag!=item.Id)return;_drag=null;
                if(!_dragging)return;var point=e.GetCurrentPoint(this).Position;var index=0;
                foreach(var header in _headers.Values){var origin=header.TransformToVisual(this).TransformPoint(new());if(point.X>origin.X+header.ActualWidth/2)index++;}
                var original=Array.FindIndex(_items,i=>i.Id==item.Id);if(index>original)index--;
                select.ReleasePointerCapture(e.Pointer);MoveRequested?.Invoke(this,(item.Id,Math.Clamp(index,0,_items.Length-1)));e.Handled=true;
                DispatcherQueue.TryEnqueue(()=>_dragging=false);
            }),true);
            select.PointerCaptureLost+=(_,_)=>DispatcherQueue.TryEnqueue(()=>{if(_drag==item.Id){_drag=null;_dragging=false;}});
            var menu=new MenuFlyout();
            void Add(string text,Action action){var command=new MenuFlyoutItem{Text=text};AutomationProperties.SetName(command,text+" "+item.Title);command.Click+=(_,_)=>action();menu.Items.Add(command);}
            Add(item.Pinned?"Unpin document":"Pin document",()=>PinRequested?.Invoke(this,item.Id));
            Add("Move left",()=>MoveRequested?.Invoke(this,(item.Id,Array.FindIndex(_items,i=>i.Id==item.Id)-1)));
            Add("Move right",()=>MoveRequested?.Invoke(this,(item.Id,Array.FindIndex(_items,i=>i.Id==item.Id)+1)));
            Add("Close",()=>CloseRequested?.Invoke(this,item.Id));Add("Close other unpinned documents",()=>CloseOthersRequested?.Invoke(this,item.Id));
            select.ContextFlyout=menu;
        }
        UpdateOverflow();DispatcherQueue.TryEnqueue(RevealActive);
    }
    private void RevealActive()
    {
        var active=_items.FirstOrDefault(i=>i.Active);if(active is null||!_headers.TryGetValue(active.Id,out var header))return;
        var position=header.TransformToVisual(_tabs).TransformPoint(new Point()).X;
        if(position<_scroll.HorizontalOffset)_scroll.ChangeView(position,null,null,true);
        else if(position+header.ActualWidth>_scroll.HorizontalOffset+_scroll.ViewportWidth)_scroll.ChangeView(position+header.ActualWidth-_scroll.ViewportWidth,null,null,true);
    }
    private void UpdateOverflow()
    {
        var overflow=_tabs.ActualWidth>ActualWidth+1;
        _left.Visibility=_right.Visibility=overflow?Visibility.Visible:Visibility.Collapsed;
        _left.IsEnabled=_scroll.HorizontalOffset>1;_right.IsEnabled=_scroll.HorizontalOffset+_scroll.ViewportWidth<_tabs.ActualWidth-1;
    }
}
