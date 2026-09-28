using DesignSpace.Engine;
using DesignSpace.Xaml;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace DesignSpace.Controls.Uno;

public sealed partial class SampleDataControl : Grid,IDisposable
{
    private readonly DesignSession _session;
    private readonly TextBox _json=StudioTheme.Input("","Sample data JSON");
    private readonly TextBox _path=StudioTheme.Input("Title","Binding path");
    private bool _dirty; private bool _refreshing;
    private string _canonical="";private long _draftRevision;
    private static string Normalize(string value)=>value.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
    public event EventHandler<string>? Error;
    public SampleDataControl(DesignSession session)
    {
        _session=session; RowDefinitions.Add(new(){Height=new GridLength(28)}); RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)}); RowDefinitions.Add(new(){Height=new GridLength(58)});
        var toolbar=new StackPanel { Orientation=Orientation.Horizontal };
        toolbar.Children.Add(new StudioButton("Apply JSON",Apply,"Apply sample data")); toolbar.Children.Add(new StudioButton("Reload",()=> { _dirty=false; Refresh(); },"Reload sample data")); Children.Add(toolbar);
        _json.AcceptsReturn=true; _json.TextWrapping=TextWrapping.NoWrap; _json.FontFamily=new FontFamily("Consolas"); _json.FontSize=11; _json.VerticalAlignment=VerticalAlignment.Stretch; _json.Margin=new Thickness(4); SetRow(_json,1); Children.Add(_json);
        var binding=new StackPanel { Spacing=2,Padding=new Thickness(4,0,4,2) }; binding.Children.Add(_path); binding.Children.Add(new StudioButton("Bind selected text",Bind,"Bind selected text to sample data")); SetRow(binding,2); Children.Add(binding);
        _json.TextChanged+=(_,_)=> { if(!_refreshing) _dirty=Normalize(_json.Text)!=_canonical; }; _session.DocumentChanged+=Changed; Refresh();
    }
    private void Changed(object? sender,EventArgs e)=>Refresh();
    private void Refresh() { if(_dirty) return; _refreshing=true; _json.Text=DesignData.Read(_session.Document.Root); _canonical=Normalize(_json.Text);_draftRevision=_session.Revision; _refreshing=false; }
    private void Apply()
    {
        try { RequireDraftCurrent();var root=DesignData.Set(_session.Document.Root,_json.Text); _session.Execute("Apply sample data",d=>d with { Root=root }); _dirty=false;Refresh(); }
        catch(Exception e) { Error?.Invoke(this,e.Message); }
    }
    private void Bind()
    {
        try
        {
            RequireDraftCurrent();var path=_path.Text.Trim(); if(!System.Text.RegularExpressions.Regex.IsMatch(path,"^[A-Za-z_][A-Za-z0-9_.]*$")) throw new InvalidOperationException("Use a simple JSON property path, for example Customer.Name.");
            var ids=_session.Selection.ToArray();
            _session.Execute("Bind text to sample data",d=>
            {
                var root=DesignData.Set(d.Root,_json.Text);
                foreach(var id in ids) root=root.Update(id,n=>n.IsLocked ? n : n.Set(n.Type is "Button" or "CheckBox" ? "Content" : "Text","{Binding "+path+"}"));
                return d with { Root=root };
            });
            _dirty=false;Refresh();
        }
        catch(Exception e) { Error?.Invoke(this,e.Message); }
    }
    public void Dispose()=>_session.DocumentChanged-=Changed;
}
