using DesignSpace.Engine;
using DesignSpace.Core;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

/// <summary>Panel layout, typography and hierarchy authoring independent of the workbench.</summary>
public sealed class LayoutAuthoringControl : ScrollViewer,IDisposable
{
    private readonly DesignSession _session;
    private readonly StackPanel _body=new(){Padding=new Thickness(7),Spacing=4};
    private bool _syncing;
    public event EventHandler<string>? Error;
    public LayoutAuthoringControl(DesignSession session)
    {
        _session=session;Content=_body;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        _session.SelectionChanged+=Changed;_session.DocumentChanged+=Changed;Refresh();
    }
    private void Changed(object? sender,EventArgs e)=>Refresh();
    private void Guard(Action action){try{action();}catch(Exception e){Error?.Invoke(this,e.Message);}}
    private void Refresh()
    {
        _syncing=true;
        try
        {
            _body.Children.Clear();var nodes=_session.Selection.Select(_session.Index.Find).OfType<DesignNode>().ToArray();
            if(nodes.Length==0){_body.Children.Add(StudioTheme.Text("Select an object to author its layout."));return;}
            var n=nodes[0];
            if(n.Type=="Grid")
            {
                Header("Grid tracks · Auto, pixels, weighted *");Field("RowDefinitions",n.Get("RowDefinitions","*"));Field("ColumnDefinitions",n.Get("ColumnDefinitions","*"));Field("RowSpacing",n.Get("RowSpacing","0"));Field("ColumnSpacing",n.Get("ColumnSpacing","0"));
            }
            Header("Placement and constraints");
            foreach(var property in new[]{"MinWidth","MinHeight","MaxWidth","MaxHeight","Grid.RowSpan","Grid.ColumnSpan","Canvas.ZIndex"}) Field(property,n.Get(property,property.Contains("Span",StringComparison.Ordinal) ? "1" : property.StartsWith("Max",StringComparison.Ordinal) ? "100000" : "0"));
            if(n.Type=="StackPanel") Field("Spacing",n.Get("Spacing","0"));
            if(n.Type=="Image") Field("Stretch",n.Get("Stretch","Uniform"));
            if(n.Type is "TextBlock" or "Button" or "TextBox")
            {
                Header("Typography");Field("TextWrapping",n.Get("TextWrapping","NoWrap"));Field("TextAlignment",n.Get("TextAlignment","Left"));Field("FontWeight",n.Get("FontWeight","Normal"));Field("FontStyle",n.Get("FontStyle","Normal"));Field("TextDecorations",n.Get("TextDecorations"));Field("LineHeight",n.Get("LineHeight","0"));
            }
            Header("Arrange siblings");var buttons=new StackPanel{Orientation=Orientation.Horizontal};
            buttons.Children.Add(new StudioButton("Distribute X",()=>Guard(()=>_session.Distribute(true)),"Distribute horizontally"));buttons.Children.Add(new StudioButton("Distribute Y",()=>Guard(()=>_session.Distribute(false)),"Distribute vertically"));_body.Children.Add(buttons);
            if(nodes.Length==1 && n.Id!=_session.Document.Root.Id)
            {
                Header("Move to container");
                var targets=_session.Index.Nodes.Values.Where(t=>t.Type is "Canvas" or "Grid" or "StackPanel" && t.Id!=n.Id && !_session.Index.Ancestors(t.Id).Contains(n.Id)).ToArray();
                var combo=new ComboBox{ItemsSource=targets.Select(t=>t.Name).ToArray(),MinHeight=26,FontSize=11,Background=StudioTheme.Field};_body.Children.Add(combo);
                _body.Children.Add(new StudioButton("Move selected object",()=>Guard(()=>{if(combo.SelectedItem is string name)_session.Reparent(n.Id,targets.First(t=>t.Name==name).Id);}),"Reparent selected object"));
            }
        }
        finally{_syncing=false;}
    }
    private void Header(string title){var t=StudioTheme.Text(title,11,"#B3C5D9");t.Margin=new Thickness(0,7,0,2);_body.Children.Add(t);}
    private void Field(string property,string value)
    {
        var label=StudioTheme.Text(property);var input=StudioTheme.Input(value,"Layout "+property);_body.Children.Add(label);_body.Children.Add(input);
        var revision=_session.Revision;var selection=_session.Selection;var accepted=value;
        void Commit()
        {
            if(_syncing||revision!=_session.Revision||!selection.SetEquals(_session.Selection)||input.Text==accepted)return;
            accepted=input.Text;
            Guard(()=>
            {
                if(property is "RowDefinitions" or "ColumnDefinitions")
                {
                    var text=input.Text;var ids=selection;
                    _session.Execute("Set "+property,d=>d with { Root=DesignIndex.For(d.Root).Transform(ids,n=>n.Set(property,text) with { PropertyElements=n.PropertyElements.Where(raw=>!raw.Contains("."+property,StringComparison.Ordinal)).ToImmutableArray() },respectLocks:true) });
                }
                else _session.SetProperty(property,input.Text);
            });
        }
        input.LostFocus+=(_,_)=>Commit();input.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Enter){Commit();e.Handled=true;}};
    }
    public void Dispose(){_session.SelectionChanged-=Changed;_session.DocumentChanged-=Changed;}
}
