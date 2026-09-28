using DesignSpace.Animation;
using DesignSpace.Core;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

/// <summary>Reusable timing editor. Reading the result does not mutate the supplied storyboard.</summary>
public sealed class StoryboardSettingsControl : StackPanel
{
    private readonly DesignStoryboard _original;
    private readonly TextBox _duration,_begin,_speed,_repeat;
    private readonly ComboBox _repeatMode,_fill;
    private readonly CheckBox _reverse,_scale;
    private readonly TextBlock _error=StudioTheme.Text("",11,"#FFD09B");
    public StoryboardSettingsControl(DesignStoryboard storyboard)
    {
        _original=storyboard; Spacing=7;
        TextBox Field(string label,string name,double value)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(137)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            row.Children.Add(StudioTheme.Text(label));var box=StudioTheme.Input(Numbers.Format(value),name);Grid.SetColumn(box,1);row.Children.Add(box);Children.Add(row);return box;
        }
        _duration=Field("Duration (seconds)","Storyboard duration",storyboard.Duration);
        _begin=Field("Start delay (seconds)","Storyboard start delay",storyboard.BeginTime);
        _speed=Field("Playback speed", "Storyboard speed",storyboard.SpeedRatio);
        _repeatMode=new ComboBox{ItemsSource=new[]{"Count","Duration","Forever"},SelectedIndex=storyboard.Loop ? 2 : storyboard.RepeatDuration is null ? 0 : 1,MinHeight=28,HorizontalAlignment=HorizontalAlignment.Stretch};
        AutomationProperties.SetName(_repeatMode,"Storyboard repeat mode");Children.Add(StudioTheme.Text("Repeat mode"));Children.Add(_repeatMode);
        _repeat=Field("Repeat count / seconds","Storyboard repeat amount",storyboard.RepeatDuration ?? storyboard.RepeatCount);
        _reverse=new CheckBox{Content="Auto reverse",IsChecked=storyboard.AutoReverse,MinHeight=28};AutomationProperties.SetName(_reverse,"Storyboard auto reverse");Children.Add(_reverse);
        _fill=new ComboBox{ItemsSource=new[]{"HoldEnd","Stop"},SelectedIndex=storyboard.FillBehavior=="Stop" ? 1 : 0,MinHeight=28,HorizontalAlignment=HorizontalAlignment.Stretch};
        AutomationProperties.SetName(_fill,"Storyboard fill behavior");Children.Add(StudioTheme.Text("After completion"));Children.Add(_fill);
        _scale=new CheckBox{Content="Scale existing keyframes",IsChecked=false,MinHeight=28};AutomationProperties.SetName(_scale,"Scale storyboard keyframes");Children.Add(_scale);
        var note=StudioTheme.Text("The timeline ruler stays in keyframe time. Playback applies delay, speed and repeats; scrubbing edits the original interval.",11,"#AAAAB3");note.TextWrapping=TextWrapping.Wrap;Children.Add(note);
        _error.TextWrapping=TextWrapping.Wrap;Children.Add(_error);
    }
    public DesignStoryboard CreateUpdated()
    {
        double Read(TextBox field)=>Numbers.Parse(field.Text,double.NaN);
        var duration=Read(_duration);var board=AnimationEngine.ChangeDuration(_original,duration,_scale.IsChecked==true);
        var mode=_repeatMode.SelectedItem as string;
        return board with
        {
            BeginTime=Read(_begin),SpeedRatio=Read(_speed),AutoReverse=_reverse.IsChecked==true,
            Loop=mode=="Forever",RepeatCount=mode=="Count" ? Read(_repeat) : 1,
            RepeatDuration=mode=="Duration" ? Read(_repeat) : null,FillBehavior=_fill.SelectedItem as string ?? "HoldEnd"
        };
    }
    public void ShowError(string message)=>_error.Text=message;
}
