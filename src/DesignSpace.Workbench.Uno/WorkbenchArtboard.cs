using DesignSpace.Core;
using DesignSpace.Controls.Uno;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private ArtboardSettingsControl? _artboardSettings;
    private StudioButton? _gridButton,_gridSnapButton,_snaplinesButton;
    private void AddArtboardCommands(StackPanel bar)
    {
        _gridButton=new("Grid",()=>ChangeArtboardSettings(s=>s with{ShowGrid=!s.ShowGrid},"Snap grid visibility changed"),"Toggle design grid");
        _gridSnapButton=new("Snap",()=>ChangeArtboardSettings(s=>s with{SnapToGrid=!s.SnapToGrid},"Grid snapping changed · Alt bypasses snapping"),"Toggle grid snapping");
        _snaplinesButton=new("Guides",()=>ChangeArtboardSettings(s=>s with{SnapToSnaplines=!s.SnapToSnaplines},"Snaplines changed · edges, centers, margin and padding"),"Toggle snapline snapping");
        bar.Children.Add(_gridButton);bar.Children.Add(_gridSnapButton);bar.Children.Add(_snaplinesButton);
        bar.Children.Add(new StudioButton("Options",()=>_rightTabs.Select("Artboard"),"Open Artboard settings"));
        _artboardSettings=new(()=>Designer.Viewport.CaptureArtboardSettings(),settings=>
        {
            Designer.ApplyArtboardSettings(settings);RefreshArtboardSettings();QueueWorkspaceRecovery();
        });
        _artboardSettings.Error+=(_,message)=>SetStatus(message,true);_rightTabs.Add("Artboard",_artboardSettings);
        RefreshArtboardSettings();
    }
    private void ChangeArtboardSettings(Func<ArtboardSettings,ArtboardSettings> update,string message)
    {
        Designer.ApplyArtboardSettings(update(Designer.Viewport.CaptureArtboardSettings()));
        RefreshArtboardSettings();QueueWorkspaceRecovery();SetStatus(message);
    }
    private void RefreshArtboardSettings()
    {
        if(_gridButton is not null)_gridButton.IsSelected=Designer.Viewport.ShowGrid;
        if(_gridSnapButton is not null)_gridSnapButton.IsSelected=Designer.Viewport.SnapToGrid;
        if(_snaplinesButton is not null)_snaplinesButton.IsSelected=Designer.Viewport.SnapToSnaplines;
        _artboardSettings?.Refresh();
    }
}
