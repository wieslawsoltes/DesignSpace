using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

public sealed class ResourcesControl : Grid,IDisposable
{
    private readonly DesignSession _session;
    private readonly StackPanel _items=new(){Spacing=3,Padding=new Thickness(6)};
    private readonly TextBox _key=StudioTheme.Input("AccentBrush","Resource key");
    private readonly ColorEditorControl _color=new(){Value="#FF0078D4"};
    private string? _selected;
    public event EventHandler<string>? Error;
    public ResourcesControl(DesignSession session)
    {
        _session=session; RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)}); RowDefinitions.Add(new(){Height=new GridLength(226)});
        var scroll=new ScrollViewer { Content=_items,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; Children.Add(scroll);
        var editor=new StackPanel { Spacing=4,Padding=new Thickness(7) }; editor.Children.Add(StudioTheme.Text("Solid color brush resource")); editor.Children.Add(_key); editor.Children.Add(_color);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,Spacing=3 }; buttons.Children.Add(new StudioButton("Save resource",Save,"Save brush resource")); buttons.Children.Add(new StudioButton("Apply",Apply,"Apply selected resource")); editor.Children.Add(buttons); SetRow(editor,1); Children.Add(editor);
        _session.DocumentChanged+=Changed; Refresh();
    }
    private void Changed(object? sender,EventArgs e)=>Refresh();
    private IEnumerable<XElement> Entries()
    {
        foreach(var raw in _session.Document.Root.PropertyElements)
        {
            XElement e; try { e=XElement.Parse(raw); } catch(System.Xml.XmlException) { continue; }
            if(!e.Name.LocalName.EndsWith(".Resources",StringComparison.Ordinal)) continue;
            foreach(var child in e.Descendants().Where(c=>c.Attribute(XName.Get("Key",DesignNode.XamlNamespace)) is not null)) yield return child;
        }
    }
    private void Refresh()
    {
        _items.Children.Clear(); _items.Children.Add(StudioTheme.Text("▾  Document resources",11,"#B8B8BE"));
        foreach(var e in Entries())
        {
            var key=(string)e.Attribute(XName.Get("Key",DesignNode.XamlNamespace))!;
            var b=new StudioButton("▧  "+key+"  ("+e.Name.LocalName+")",()=> { _selected=key; _key.Text=key; if(e.Name.LocalName=="SolidColorBrush") _color.Value=(string?)e.Attribute("Color") ?? "#FF0078D4"; },"Resource "+key); _items.Children.Add(b);
        }
        if(!Entries().Any()) _items.Children.Add(StudioTheme.Text("No resources. Create a brush below.",11,"#929299"));
    }
    private void Save()
    {
        try
        {
            var key=_key.Text.Trim(); if(key.Length==0 || key.Length>128) throw new InvalidOperationException("Enter a resource key of 1–128 characters.");
            _session.Execute("Save brush resource",d=>
            {
                var root=d.Root; var ns=(XNamespace)root.Namespace; var x=(XNamespace)DesignNode.XamlNamespace;
                var other=new List<string>(); XElement? resources=null;
                foreach(var raw in root.PropertyElements)
                {
                    var element=XElement.Parse(raw);
                    if(element.Name.LocalName==root.Type+".Resources" && resources is null) resources=element; else other.Add(raw);
                }
                resources ??= new XElement(ns+(root.Type+".Resources"));
                var existing=resources.Descendants().FirstOrDefault(e=>(string?)e.Attribute(x+"Key")==key);
                if(existing is not null && existing.Name.LocalName!="SolidColorBrush") throw new InvalidOperationException("That key belongs to a different resource type.");
                existing?.Remove();
                var container=resources.Elements().FirstOrDefault(e=>e.Name.LocalName=="ResourceDictionary") ?? resources;
                container.Add(new XElement(ns+"SolidColorBrush",new XAttribute(x+"Key",key),new XAttribute("Color",_color.Value)));
                other.Insert(0,resources.ToString(SaveOptions.DisableFormatting));
                return d with { Root=root with { PropertyElements=other.ToImmutableArray() } };
            });
            _selected=key;
        }
        catch(Exception e) { Error?.Invoke(this,e.Message); }
    }
    private void Apply()
    {
        try
        {
            var key=_selected ?? _key.Text.Trim(); if(!Entries().Any(e=>(string?)e.Attribute(XName.Get("Key",DesignNode.XamlNamespace))==key)) throw new InvalidOperationException("Save or select an existing resource first.");
            var ids=_session.Selection.ToArray();
            _session.Execute("Apply brush resource",d=>
            {
                var root=d.Root;
                foreach(var id in ids) root=root.Update(id,n=>
                {
                    if(n.IsLocked) return n;
                    var property=n.Type is "Rectangle" or "Ellipse" or "Path" ? "Fill" : "Background";
                    return n.Set(property,"{StaticResource "+key+"}") with { PropertyElements=n.PropertyElements.Where(p=>!p.Contains("."+property,StringComparison.Ordinal)).ToImmutableArray() };
                });
                return d with { Root=root };
            });
        }
        catch(Exception e) { Error?.Invoke(this,e.Message); }
    }
    public void Dispose()=>_session.DocumentChanged-=Changed;
}
