using DesignSpace.Engine;
using DesignSpace.Xaml;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace DesignSpace.Controls.Uno;

/// <summary>Reusable revision-guarded ControlTemplate source authoring with inert preview.</summary>
public sealed partial class TemplateEditorControl : Grid,IDisposable
{
    private readonly DesignSession _session;
    private readonly ComboBox _templates=new(){MinHeight=26,FontSize=11,Padding=new Thickness(5,2,5,2),Background=StudioTheme.Field};
    private readonly TextBox _source=StudioTheme.Input("","ControlTemplate source");
    private readonly TextBlock _status=StudioTheme.Text("ControlTemplate resources",11,"#AAAAAF");
    private string? _key;
    private bool _syncing,_saving;
    private long _revision;
    private string _canonical="",_initialDraft="";
    private bool Dirty=>Normalize(_source.Text)!=_canonical;
    private bool UserDraft=>Dirty&&Normalize(_source.Text)!=_initialDraft;
    private static string Normalize(string text)=>text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
    public event EventHandler<string>? Error;
    public TemplateEditorControl(DesignSession session)
    {
        _session=session;
        RowDefinitions.Add(new(){Height=new GridLength(30)});RowDefinitions.Add(new(){Height=new GridLength(30)});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});RowDefinitions.Add(new(){Height=new GridLength(60)});
        _templates.Margin=new Thickness(4,2,4,2);Children.Add(_templates);
        var tools=new StackPanel{Orientation=Orientation.Horizontal};
        tools.Children.Add(new StudioButton("New",()=>New(),"New control template"));
        tools.Children.Add(new StudioButton("Sample",()=>New(true),"New interactive control template"));
        tools.Children.Add(new StudioButton("Save",Save,"Save control template"));
        tools.Children.Add(new StudioButton("Apply",Apply,"Apply control template"));
        tools.Children.Add(new StudioButton("Reload",Reload,"Reload control template"));SetRow(tools,1);Children.Add(tools);
        _source.AcceptsReturn=true;_source.TextWrapping=TextWrapping.NoWrap;_source.FontFamily=new FontFamily("Consolas");_source.FontSize=11;_source.VerticalAlignment=VerticalAlignment.Stretch;_source.Margin=new Thickness(4);
        ScrollViewer.SetHorizontalScrollBarVisibility(_source,ScrollBarVisibility.Auto);ScrollViewer.SetVerticalScrollBarVisibility(_source,ScrollBarVisibility.Auto);SetRow(_source,2);Children.Add(_source);
        _status.TextWrapping=TextWrapping.Wrap;_status.Margin=new Thickness(6);SetRow(_status,3);Children.Add(_status);
        _templates.SelectionChanged+=(_,_)=>
        {
            if(_syncing||_templates.SelectedItem is not string key||key==_key)return;
            if(UserDraft){_status.Text="Save or Reload this draft before switching templates.";_syncing=true;_templates.SelectedItem=_key;_syncing=false;return;}
            _key=key;LoadSelected();
        };
        _source.TextChanged+=(_,_)=>{if(!_syncing)_status.Text=Dirty?"Unapplied template source":"Template synchronized";};
        _session.DocumentChanged+=Changed;Reload();
    }
    private void Changed(object? sender,EventArgs e){if(!_saving)Refresh();}
    private void Refresh()
    {
        var entries=TemplateLibrary.Read(_session.Document);_syncing=true;_templates.ItemsSource=entries.Keys.ToArray();
        if(UserDraft){_templates.SelectedItem=_key;_syncing=false;_status.Text="Template draft retained. Reload before saving against a changed document.";return;}
        if(_key is null||!entries.ContainsKey(_key))_key=entries.Keys.FirstOrDefault();
        _templates.SelectedItem=_key;_syncing=false;
        if(_key is not null)LoadSelected();else New();
    }
    private void LoadSelected()
    {
        if(_key is null||!TemplateLibrary.Read(_session.Document).TryGetValue(_key,out var source))return;
        _syncing=true;_source.Text=source;_canonical=Normalize(source);_initialDraft="";_revision=_session.Revision;_syncing=false;_status.Text="Template synchronized";
    }
    private void Reload()
    {
        _syncing=true;_source.Text="";_canonical="";_initialDraft="";_revision=_session.Revision;_syncing=false;Refresh();
    }
    private void New(bool interactive=false)
    {
        if(UserDraft){_status.Text="Save or Reload this draft before creating another template.";return;}
        var entries=TemplateLibrary.Read(_session.Document);var prefix=interactive?"InteractiveButtonTemplate":"ButtonTemplate";var key=prefix;var i=1;while(entries.ContainsKey(key))key=prefix+i++;
        _syncing=true;_key=key;_canonical="";_source.Text=interactive?TemplateLibrary.CreateInteractiveDefault(key):TemplateLibrary.CreateDefault(key);_initialDraft=Normalize(_source.Text);_revision=_session.Revision;_templates.SelectedItem=null;_syncing=false;
        _status.Text="New template. Save and Apply, then Test controls to try hover, press and keyboard focus.";
    }
    private void Save()
    {
        try
        {
            if(Dirty&&_revision!=_session.Revision)throw new InvalidOperationException("Template draft is stale. Reload before saving.");
            var source=_source.Text;_saving=true;
            try{_session.Execute("Save control template",d=>TemplateLibrary.Save(d,source));}finally{_saving=false;}
            _key=(string?)System.Xml.Linq.XElement.Parse(source).Attribute(System.Xml.Linq.XName.Get("Key",DesignSpace.Core.DesignNode.XamlNamespace));
            _canonical=Normalize(source);_initialDraft="";_revision=_session.Revision;Refresh();_status.Text="Template resource saved";
        }
        catch(Exception e){_status.Text=e.Message;Error?.Invoke(this,e.Message);}
    }
    private void Apply()
    {
        try
        {
            if(Dirty)Save();if(Dirty)return;
            if(_key is null||_session.Selection.Count==0)throw new InvalidOperationException("Select an object and a saved template.");
            _session.Execute("Apply control template",d=>TemplateLibrary.Apply(d,_session.Selection,_key));_status.Text="Template applied. Test controls previews input without changing the document.";
        }
        catch(Exception e){_status.Text=e.Message;Error?.Invoke(this,e.Message);}
    }
    public void Dispose()=>_session.DocumentChanged-=Changed;
}
