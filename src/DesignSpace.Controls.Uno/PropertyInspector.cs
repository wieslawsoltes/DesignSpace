using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

/// <summary>Selection-aware property editing with revision-guarded, exactly-once commits.</summary>
public sealed class PropertyInspector : Grid,IDisposable
{
    private readonly DesignSession _session;
    private readonly StackPanel _body=new(){Spacing=2,Padding=new Thickness(7,4,7,12)};
    private readonly TextBox _search=StudioTheme.Input("","Search properties");
    private bool _refreshing;
    private DesignDocument? _displayed;
    private ImmutableHashSet<Guid> _selection=[];
    public string ActiveProperty { get; private set; }="Opacity";
    public event EventHandler<(string Property,string Value)>? PropertyEdited;
    public event EventHandler<string>? ActivePropertyChanged;
    public event EventHandler<string>? Error;
    public PropertyInspector(DesignSession session)
    {
        _session=session; RowDefinitions.Add(new(){Height=new GridLength(27)}); RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        _search.PlaceholderText="Search properties"; _search.Margin=new Thickness(5,2,5,2); Children.Add(_search); _search.TextChanged+=(_,_)=>Refresh();
        var scroll=new ScrollViewer { Content=_body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; SetRow(scroll,1); Children.Add(scroll);
        _session.SelectionChanged+=Changed; _session.DocumentChanged+=Changed; Refresh();
    }
    private void Changed(object? sender,EventArgs e)
    {
        if(ReferenceEquals(_displayed,_session.Document) && _selection.SetEquals(_session.Selection)) return;
        Refresh();
    }
    public void Refresh()
    {
        if(_refreshing) return; _refreshing=true;
        try
        {
            _displayed=_session.Document; _selection=_session.Selection; _body.Children.Clear();
            var nodes=_selection.Select(id=>_session.Document.Root.Find(id)).OfType<DesignNode>().ToArray();
            if(nodes.Length==0) { _body.Children.Add(StudioTheme.Text("Select an object to edit its properties.",11,"#99999F")); return; }
            var n=nodes[0];
            _body.Children.Add(StudioTheme.Text(nodes.Length==1 ? "Type   "+n.Type : nodes.Length+" objects selected",11,"#AFAFB6"));
            AddField("Name",DesignNode.NameKey,n.Get(DesignNode.NameKey,n.Get("Name")),nodes.Length>1);
            var brushProperty=n.Type is "Rectangle" or "Ellipse" or "Path" ? "Fill" : "Background";
            Section("Brush"); AddField(brushProperty,brushProperty,Common(nodes,brushProperty)); AddField("Stroke","Stroke",Common(nodes,"Stroke"));
            if(_search.Text.Length==0)
            {
                var modes=new StackPanel { Orientation=Orientation.Horizontal,Spacing=2,Margin=new Thickness(0,2,0,3) };
                foreach(var mode in new[]{"None","Solid","Linear","Radial"}) modes.Children.Add(new StudioButton(mode,()=>SetBrush(brushProperty,mode),mode+" brush"));
                _body.Children.Add(modes); var color=new ColorEditorControl { Value=n.Get(brushProperty,"#FF0078D4"),Margin=new Thickness(0,2,0,5) };
                color.ColorChanged+=(_,value)=>Edit(brushProperty,value); _body.Children.Add(color);
            }
            Section("Appearance"); AddField("Opacity","Opacity",Common(nodes,"Opacity","1")); AddField("Visibility","Visibility",Common(nodes,"Visibility","Visible")); AddField("Stroke thickness","StrokeThickness",Common(nodes,"StrokeThickness","1")); AddField("Corner radius","CornerRadius",Common(nodes,"CornerRadius","0"));
            Section("Layout"); AddField("Width","Width",Common(nodes,"Width","Auto")); AddField("Height","Height",Common(nodes,"Height","Auto")); AddField("X","Canvas.Left",Common(nodes,"Canvas.Left","0")); AddField("Y","Canvas.Top",Common(nodes,"Canvas.Top","0")); AddField("Margin","Margin",Common(nodes,"Margin","0")); AddField("Padding","Padding",Common(nodes,"Padding","0")); AddField("Horizontal","HorizontalAlignment",Common(nodes,"HorizontalAlignment","Stretch")); AddField("Vertical","VerticalAlignment",Common(nodes,"VerticalAlignment","Stretch"));
            AddField("Grid row","Grid.Row",Common(nodes,"Grid.Row","0")); AddField("Grid column","Grid.Column",Common(nodes,"Grid.Column","0"));
            Section("Transform"); AddField("Rotation","Rotation",Numbers.Format(n.Rotation));
            if(n.Type is "TextBlock" or "TextBox" or "Button" or "CheckBox" or "RadioButton")
            {
                Section("Text"); var property=n.Type is "TextBlock" or "TextBox" ? "Text" : "Content"; AddField(property,property,Common(nodes,property)); AddField("Font size","FontSize",Common(nodes,"FontSize","14")); AddField("Foreground","Foreground",Common(nodes,"Foreground","#FF202838")); AddField("Font family","FontFamily",Common(nodes,"FontFamily","Segoe UI"));
            }
            if(n.Type=="Path") { Section("Geometry"); AddField("Data","Data",n.Get("Data")); }
            if(n.Type=="StackPanel") AddField("Orientation","Orientation",n.Get("Orientation","Vertical"));
        }
        finally { _refreshing=false; }
    }
    private static string Common(DesignNode[] nodes,string key,string fallback="")=>nodes.Select(n=>n.Get(key,fallback)).Distinct().Count()==1 ? nodes[0].Get(key,fallback) : "<multiple>";
    private void Section(string title)=>_body.Children.Add(new Border { Background=StudioTheme.Brush("#2C2C2F"),Margin=new Thickness(-3,6,-3,2),Padding=new Thickness(3,4,3,4),Child=StudioTheme.Text("▾  "+title,11) });
    private void AddField(string label,string key,string value,bool readOnly=false)
    {
        if(_search.Text.Length>0 && !label.Contains(_search.Text,StringComparison.OrdinalIgnoreCase) && !key.Contains(_search.Text,StringComparison.OrdinalIgnoreCase)) return;
        var row=new Grid { MinHeight=24 }; row.ColumnDefinitions.Add(new(){Width=new GridLength(104)}); row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        row.Children.Add(StudioTheme.Text(label,11,"#BABAC0")); var input=StudioTheme.Input(value,"Property "+label); input.IsReadOnly=readOnly; SetColumn(input,1); row.Children.Add(input); _body.Children.Add(row);
        var revision=_session.Revision; var targets=_session.Selection; var committed=value;
        input.GotFocus+=(_,_)=> { ActiveProperty=key; ActivePropertyChanged?.Invoke(this,key); };
        void Commit()
        {
            if(_refreshing || readOnly || _session.Revision!=revision || !targets.SetEquals(_session.Selection) || input.Text==committed || input.Text=="<multiple>") return;
            committed=input.Text; Edit(key,committed);
        }
        input.LostFocus+=(_,_)=>Commit(); input.KeyDown+=(_,e)=> { if(e.Key==Windows.System.VirtualKey.Enter) { Commit(); e.Handled=true; } };
    }
    private void Edit(string key,string value)
    {
        try { if(PropertyEdited is not null) PropertyEdited.Invoke(this,(key,value)); else _session.SetProperty(key,value); }
        catch(Exception e) { Error?.Invoke(this,e.Message); Refresh(); }
    }
    private void SetBrush(string property,string mode)
    {
        if(mode is "None" or "Solid") { Edit(property,mode=="None" ? "Transparent" : "#FF0078D4"); return; }
        try
        {
            var ns=(XNamespace)DesignNode.PresentationNamespace; var ids=_session.Selection.ToArray();
            _session.Execute("Set "+mode+" brush",d=>
            {
                var root=d.Root;
                foreach(var id in ids) root=root.Update(id,n=>
                {
                    if(n.IsLocked) return n;
                    var color=n.Get(property,"#FF0078D4"); if(color.StartsWith('{')) color="#FF0078D4";
                    var brush=new XElement(ns+(mode+"GradientBrush"),new XElement(ns+"GradientStop",new XAttribute("Color",color),new XAttribute("Offset","0")),new XElement(ns+"GradientStop",new XAttribute("Color","#FFFFFFFF"),new XAttribute("Offset","1")));
                    var element=new XElement(ns+(n.Type+"."+property),brush);
                    return n with { Properties=n.Properties.Remove(property),PropertyElements=n.PropertyElements.Where(p=>!p.Contains("."+property,StringComparison.Ordinal)).Append(element.ToString(SaveOptions.DisableFormatting)).ToImmutableArray() };
                });
                return d with { Root=root };
            });
        }
        catch(Exception e) { Error?.Invoke(this,e.Message); }
    }
    public void Dispose() { _session.SelectionChanged-=Changed; _session.DocumentChanged-=Changed; }
}
