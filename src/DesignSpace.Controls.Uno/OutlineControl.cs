using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
namespace DesignSpace.Controls.Uno;

[Bindable]
public sealed class OutlineRow
{
    public Guid Id { get; init; }
    public string Caption { get; init; }="";
}
/// <summary>Virtualized accessible outline backed by stable document identities.</summary>
public sealed class OutlineControl : Grid,IDisposable
{
    private readonly DesignSession _session;
    private readonly ListView _list=new() { SelectionMode=ListViewSelectionMode.Extended,Background=StudioTheme.Panel,BorderThickness=new Thickness(0) };
    private readonly TextBox _filter=StudioTheme.Input("","Search objects");
    private readonly HashSet<Guid> _collapsed=[];
    private OutlineRow[] _rows=[]; private bool _updating;
    public event EventHandler<string>? Error;
    public OutlineControl(DesignSession session)
    {
        _session=session; RowDefinitions.Add(new(){Height=new GridLength(28)}); RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)}); RowDefinitions.Add(new(){Height=new GridLength(26)});
        _filter.PlaceholderText="Search objects"; _filter.Margin=new Thickness(4,2,4,2); Children.Add(_filter);
        _list.ItemTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding Caption}' FontSize='11' Foreground='#FFD6D6D6' VerticalAlignment='Center' Margin='0,2,0,2' /></DataTemplate>");
        _list.ItemContainerStyle=(Style)XamlReader.Load("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListViewItem'><Setter Property='MinHeight' Value='24'/><Setter Property='Padding' Value='4,0'/><Setter Property='Margin' Value='0'/></Style>");
        AutomationProperties.SetName(_list,"Objects and Timeline outline"); SetRow(_list,1); Children.Add(_list);
        var tools=new StackPanel { Orientation=Orientation.Horizontal,Background=StudioTheme.Background };
        tools.Children.Add(new StudioButton("◉",()=>ForSelected(_session.ToggleVisibility),"Toggle selected object visibility"));
        tools.Children.Add(new StudioButton("♙",()=>ForSelected(_session.ToggleLock),"Toggle selected object lock"));
        tools.Children.Add(new StudioButton("↑",()=>Guard(()=>_session.Reorder(true)),"Bring selected objects to front"));
        tools.Children.Add(new StudioButton("↓",()=>Guard(()=>_session.Reorder(false)),"Send selected objects to back"));
        tools.Children.Add(new StudioButton("⊞",()=>Guard(_session.Group),"Group selected objects"));
        tools.Children.Add(new StudioButton("×",()=>Guard(_session.Delete),"Delete selected objects")); SetRow(tools,2); Children.Add(tools);
        _filter.TextChanged+=(_,_)=>Refresh();
        _list.SelectionChanged+=(_,_)=> { if(!_updating) _session.Select(_list.SelectedItems.OfType<OutlineRow>().Select(r=>r.Id)); };
        _list.DoubleTapped+=(_,e)=> { if(_list.SelectedItem is OutlineRow row) { if(!_collapsed.Add(row.Id)) _collapsed.Remove(row.Id); Refresh(); e.Handled=true; } };
        _session.DocumentChanged+=OnDocument; _session.SelectionChanged+=OnSelection; Refresh();
    }
    private void Guard(Action action) { try { action(); } catch(Exception e) { Error?.Invoke(this,e.Message); } }
    private void ForSelected(Action<Guid> action) { foreach(var id in _session.Selection.ToArray()) Guard(()=>action(id)); }
    private void OnDocument(object? sender,EventArgs e)=>Refresh();
    private void OnSelection(object? sender,EventArgs e)=>SyncSelection();
    public void Refresh()
    {
        var rows=new List<OutlineRow>(); var filter=_filter.Text;
        void Walk(DesignNode n,int depth)
        {
            if(filter.Length==0 || n.Name.Contains(filter,StringComparison.OrdinalIgnoreCase) || n.Type.Contains(filter,StringComparison.OrdinalIgnoreCase))
                rows.Add(new(){Id=n.Id,Caption=new string(' ',depth*3)+(n.Children.IsEmpty ? "  " : _collapsed.Contains(n.Id) ? "▸ " : "▾ ")+n.Name+"  ["+n.Type+"]"+(n.IsLocked ? "  L" : "")+(!n.Visible ? "  hidden" : "")});
            if(!_collapsed.Contains(n.Id) || filter.Length>0) foreach(var child in n.Children) Walk(child,depth+1);
        }
        Walk(_session.Document.Root,0); _rows=rows.ToArray(); _updating=true; _list.ItemsSource=_rows; _updating=false; SyncSelection();
    }
    private void SyncSelection()
    {
        _updating=true; _list.SelectedItems.Clear(); foreach(var row in _rows.Where(r=>_session.Selection.Contains(r.Id))) _list.SelectedItems.Add(row); _updating=false;
    }
    public void Dispose() { _session.DocumentChanged-=OnDocument; _session.SelectionChanged-=OnSelection; }
}
