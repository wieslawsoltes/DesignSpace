using System.Collections.Immutable;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

/// <summary>Reusable, non-mutating editor for numeric keyframe composition options.</summary>
public sealed class AnimationCompositionControl : StackPanel
{
    private readonly CheckBox _additive=new(){Content="Add to base value",MinHeight=25};
    private readonly CheckBox _cumulative=new(){Content="Accumulate each repeat",MinHeight=25};
    private bool _restoring;
    public bool IsAdditive=>_additive.IsChecked==true;
    public bool IsCumulative=>_cumulative.IsChecked==true;
    public event EventHandler? DraftChanged;
    public AnimationCompositionControl()
    {
        Spacing=2;
        AutomationProperties.SetName(_additive,"Animation additive");
        AutomationProperties.SetName(_cumulative,"Animation cumulative");
        ToolTipService.SetToolTip(_additive,"Treat numeric key values as offsets from the underlying base or selected state value.");
        ToolTipService.SetToolTip(_cumulative,"Add the last key value for every completed child-clock cycle, including a complete auto-reverse cycle.");
        Children.Add(_additive);Children.Add(_cumulative);
        foreach(var box in new[]{_additive,_cumulative}){box.Checked+=Changed;box.Unchecked+=Changed;}
    }
    private void Changed(object sender,RoutedEventArgs e){if(!_restoring)DraftChanged?.Invoke(this,EventArgs.Empty);}
    public ImmutableDictionary<string,string> CaptureDraft()=>ImmutableDictionary<string,string>.Empty
        .Add("Additive",IsAdditive.ToString()).Add("Cumulative",IsCumulative.ToString());
    public void RestoreDraft(IReadOnlyDictionary<string,string> values)
    {
        _restoring=true;
        try{_additive.IsChecked=values.GetValueOrDefault("Additive")=="True";_cumulative.IsChecked=values.GetValueOrDefault("Cumulative")=="True";}
        finally{_restoring=false;}
    }
}
