using DesignSpace.Core;
using DesignSpace.Xaml;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace DesignSpace.Controls.Uno;

/// <summary>Accessible source editing with isolated drafts, explicit apply and optimistic revision checks.</summary>
public sealed class XamlEditorControl : Grid
{
    private readonly TextBox _editor;
    private readonly TextBlock _status=StudioTheme.Text("XAML",11,"#9E9EA6");
    private long _baseRevision;
    private bool _syncing;
    private string _canonical="";
    public bool IsDirty=>_editor.Text!=_canonical;
    public string Text=>_editor.Text;
    public string Status=>_status.Text;
    public long BaseRevision=>_baseRevision;
    public event EventHandler<(DesignDocument Document,long BaseRevision)>? ApplyRequested;
    public event EventHandler? ReloadRequested;
    public event EventHandler? DraftChanged;
    public event EventHandler<string>? Error;
    public XamlEditorControl()
    {
        RowDefinitions.Add(new(){Height=new GridLength(28)}); RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)}); RowDefinitions.Add(new(){Height=new GridLength(23)});
        Background=StudioTheme.Brush("#1E1E1E");
        var toolbar=new StackPanel { Orientation=Orientation.Horizontal,Spacing=3,Background=StudioTheme.Background };
        toolbar.Children.Add(new StudioButton("Apply XAML",Apply,"Apply XAML"));
        toolbar.Children.Add(new StudioButton("Reload from design",()=>ReloadRequested?.Invoke(this,EventArgs.Empty),"Reload XAML from design"));
        toolbar.Children.Add(new StudioButton("Format",Format,"Format XAML")); Children.Add(toolbar);
        _editor=StudioTheme.Input("","XAML source editor"); _editor.AcceptsReturn=true; _editor.TextWrapping=TextWrapping.NoWrap; _editor.FontFamily=new FontFamily("Consolas"); _editor.FontSize=12; _editor.VerticalAlignment=VerticalAlignment.Stretch; _editor.HorizontalAlignment=HorizontalAlignment.Stretch; _editor.Padding=new Thickness(12,8,12,8); _editor.Background=StudioTheme.Brush("#1E1E1E"); _editor.Foreground=StudioTheme.Brush("#B8D7A3");
        ScrollViewer.SetHorizontalScrollBarVisibility(_editor,ScrollBarVisibility.Auto); ScrollViewer.SetVerticalScrollBarVisibility(_editor,ScrollBarVisibility.Auto);
        SetRow(_editor,1); Children.Add(_editor); _status.Margin=new Thickness(8,0,8,0); SetRow(_status,2); Children.Add(_status);
        _editor.TextChanged+=(_,_)=> { if(!_syncing) { UpdateStatus(); DraftChanged?.Invoke(this,EventArgs.Empty); } };
        _editor.KeyDown+=(_,e)=> { if(DesignerKeys.Control && e.Key==Windows.System.VirtualKey.Enter) { Apply(); e.Handled=true; } };
        AutomationProperties.SetHelpText(_editor,"Edit XAML, then press Control Enter or Apply XAML. Invalid source is retained without changing the design.");
    }
    public void Synchronize(DesignDocument document,long revision,bool discardDraft=false)
    {
        if(IsDirty && !discardDraft) { if(revision!=_baseRevision) _status.Text="Design changed. Reload or resolve this draft before applying."; return; }
        _syncing=true;
        try { _canonical=XamlCodec.Write(document); _editor.Text=_canonical; _baseRevision=revision; _status.Text="XAML synchronized with design"; }
        finally { _syncing=false; }
    }
    public void RestoreDraft(string text,long baseRevision)
    {
        if(text.Length>XamlCodec.MaxCharacters) throw new InvalidDataException("The recovered draft exceeds the XAML size limit.");
        _syncing=true; _editor.Text=text; _baseRevision=baseRevision; _syncing=false;
        _status.Text=baseRevision<0 ? "Recovered a conflicting source draft. Reload or resolve before applying." : "Recovered unapplied source changes";
    }
    private void UpdateStatus()=>_status.Text=IsDirty ? "Unapplied source changes   ·   Ctrl+Enter to apply" : "XAML synchronized with design";
    public void Apply()
    {
        try
        {
            var result=XamlCodec.Parse(_editor.Text); ApplyRequested?.Invoke(this,(result.Document,_baseRevision));
            _status.Text=result.Diagnostics.Count==0 ? "XAML applied" : "XAML applied with "+result.Diagnostics.Count+" compatibility warning(s)";
        }
        catch(Exception e) { _status.Text=e.Message; Error?.Invoke(this,e.Message); }
    }
    private void Format()
    {
        try { var result=XamlCodec.Parse(_editor.Text); _editor.Text=XamlCodec.Write(result.Document); UpdateStatus(); }
        catch(Exception e) { _status.Text=e.Message; Error?.Invoke(this,e.Message); }
    }
    public void FocusSource()=>_editor.Focus(FocusState.Programmatic);
}
