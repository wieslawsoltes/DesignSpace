using DesignSpace.Engine;
using DesignSpace.Xaml;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace DesignSpace.Controls.Uno;

/// <summary>Reusable ControlTemplate resource source editor with inert preview integration.</summary>
public sealed class TemplateEditorControl : Grid,IDisposable
{
    private readonly DesignSession _session;
    private readonly ComboBox _templates=new(){MinHeight=26,FontSize=11,Padding=new Thickness(5,2,5,2),Background=StudioTheme.Field};
    private readonly TextBox _source=StudioTheme.Input("","ControlTemplate source");
    private readonly TextBlock _status=StudioTheme.Text("ControlTemplate resources",11,"#AAAAAF");
    private string? _key;
    private bool _syncing;
    private string _canonical="";
    private bool Dirty=>Normalize(_source.Text)!=_canonical;
    private static string Normalize(string text)=>text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
    public event EventHandler<string>? Error;
    public TemplateEditorControl(DesignSession session)
    {
        _session=session;
        RowDefinitions.Add(new(){Height=new GridLength(30)});RowDefinitions.Add(new(){Height=new GridLength(28)});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});RowDefinitions.Add(new(){Height=new GridLength(42)});
        _templates.Margin=new Thickness(4,2,4,2);Children.Add(_templates);
        var tools=new StackPanel{Orientation=Orientation.Horizontal};
        tools.Children.Add(new StudioButton("New",New,"New control template"));tools.Children.Add(new StudioButton("Save",Save,"Save control template"));tools.Children.Add(new StudioButton("Apply to selection",Apply,"Apply control template"));SetRow(tools,1);Children.Add(tools);
        _source.AcceptsReturn=true;_source.TextWrapping=TextWrapping.NoWrap;_source.FontFamily=new FontFamily("Consolas");_source.FontSize=11;_source.VerticalAlignment=VerticalAlignment.Stretch;_source.Margin=new Thickness(4);
        ScrollViewer.SetHorizontalScrollBarVisibility(_source,ScrollBarVisibility.Auto);ScrollViewer.SetVerticalScrollBarVisibility(_source,ScrollBarVisibility.Auto);SetRow(_source,2);Children.Add(_source);
        _status.TextWrapping=TextWrapping.Wrap;_status.Margin=new Thickness(6);SetRow(_status,3);Children.Add(_status);
        _templates.SelectionChanged+=(_,_)=>
        {
            if(_syncing || _templates.SelectedItem is not string key || key==_key) return;
            if(Dirty) { _status.Text="Save the current template before switching.";_syncing=true;_templates.SelectedItem=_key;_syncing=false;return; }
            _key=key;LoadSelected();
        };
        _source.TextChanged+=(_,_)=>{if(!_syncing) _status.Text=Dirty ? "Unapplied template source" : "Template synchronized";};
        _session.DocumentChanged+=Changed;Refresh();
        if(_key is null) New();
    }
    private void Changed(object? sender,EventArgs e)=>Refresh();
    private void Refresh()
    {
        var entries=TemplateLibrary.Read(_session.Document);_syncing=true;_templates.ItemsSource=entries.Keys.ToArray();
        if(_key is null || !entries.ContainsKey(_key)) _key=entries.Keys.FirstOrDefault();
        _templates.SelectedItem=_key;_syncing=false;
        if(!Dirty) LoadSelected();
    }
    private void LoadSelected()
    {
        if(_key is null || !TemplateLibrary.Read(_session.Document).TryGetValue(_key,out var source)) return;
        _syncing=true;_source.Text=source;_canonical=Normalize(_source.Text);_syncing=false;_status.Text="Template synchronized";
    }
    private void New()
    {
        if(Dirty) { _status.Text="Save the current draft before creating another template.";return; }
        var entries=TemplateLibrary.Read(_session.Document);var key="ButtonTemplate";var i=1;while(entries.ContainsKey(key)) key="ButtonTemplate"+i++;
        _key=key;_source.Text=TemplateLibrary.CreateDefault(key);_status.Text="New template: save, then apply to a selection.";
    }
    private void Save()
    {
        try
        {
            var source=_source.Text;_session.Execute("Save control template",d=>TemplateLibrary.Save(d,source));
            _canonical=Normalize(_source.Text);Refresh();_status.Text="Template resource saved";
        }
        catch(Exception e){_status.Text=e.Message;Error?.Invoke(this,e.Message);}
    }
    private void Apply()
    {
        try
        {
            if(Dirty) Save();if(Dirty) return;
            if(_key is null || _session.Selection.Count==0) throw new InvalidOperationException("Select an object and a saved template.");
            _session.Execute("Apply control template",d=>TemplateLibrary.Apply(d,_session.Selection,_key));_status.Text="Template applied; edit the resource to update its preview.";
        }
        catch(Exception e){_status.Text=e.Message;Error?.Invoke(this,e.Message);}
    }
    public void Dispose()=>_session.DocumentChanged-=Changed;
}
