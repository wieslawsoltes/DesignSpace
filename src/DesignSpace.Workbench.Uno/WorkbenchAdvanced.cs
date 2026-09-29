using DesignSpace.Core;
using DesignSpace.Xaml;
using DesignSpace.Rendering.Skia;
using DesignSpace.Controls.Uno;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Workbench.Uno;

public sealed record OpenDesignAsset(string Name,byte[] Bytes,string MimeType);
public interface IWorkbenchAssetPlatform
{
    Task<OpenDesignAsset?> OpenImageAsync();
}
public sealed partial class WorkbenchView
{
    private TemplateEditorControl? _templateEditor;
    private StrokeEditorControl? _strokeEditor;
    private BrushEditorControl? _brushEditor;
    private AnimationTrackInspectorControl? _animationTrackEditor;
    private StateTransitionEditorControl? _stateTransitions;
    private StoryboardInspectorControl? _storyboardInspector;
    private LayoutAuthoringControl? _layoutAuthoring;
    /// <summary>Installs the reusable template/layout panels and host-dependent image import command.</summary>
    public void EnableAdvancedTools()
    {
        if(_templateEditor is not null)return;
        Designer.PreviewResolver=DesignPreview.Resolve;Designer.EditingStarted+=StopAnimationForEditing;
        _templateEditor=new(Session);_templateEditor.Error+=(_,error)=>SetStatus(error,true);_rightTabs.Add("Templates",_templateEditor);
        _storyboardInspector=new(Session,Timeline);_storyboardInspector.Error+=(_,error)=>SetStatus(error,true);_rightTabs.Add("Timing",_storyboardInspector);
        Timeline.SettingsRequested+=OpenTiming;
        _animationTrackEditor=new(Session,Timeline);_animationTrackEditor.Error+=(_,message)=>SetStatus(message,true);_rightTabs.Add("Animation",_animationTrackEditor);Timeline.TrackSettingsRequested+=OpenAnimationTrack;
        _stateTransitions=new(Session,States);_rightTabs.Add("Transitions",_stateTransitions);States.TransitionEditorRequested+=OpenTransitions;
        _rightTabs.Add("Paths",new PathToolsControl(Designer));
        _strokeEditor=new(Session){CanEditBase=()=>!States.IsRecording&&!Timeline.IsRecording};
        _strokeEditor.Error+=(_,error)=>SetStatus(error,true);
        _strokeEditor.OutlineRequested+=(_,_)=>StrokeCommands.Outline(Session,Designer.Layout);
        _rightTabs.Add("Stroke",_strokeEditor);
        _brushEditor=new(Session){CanEditBase=()=>!States.IsRecording&&!Timeline.IsRecording};
        _brushEditor.Error+=(_,error)=>SetStatus(error,true);_rightTabs.Add("Brush",_brushEditor);
        _layoutAuthoring=new(Session);_layoutAuthoring.Error+=(_,error)=>SetStatus(error,true);_leftTabs.Add("Layout",_layoutAuthoring);
        var toolbar=_mainToolbar;
        if(toolbar is not null)
        {
            toolbar.Children.Add(new StudioButton("Animation",()=>_rightTabs.Select("Animation"),"Open Animation panel"));
            toolbar.Children.Add(new StudioButton("Brush",()=>_rightTabs.Select("Brush"),"Open Brush panel"));
            toolbar.Children.Add(new StudioButton("Stroke",()=>_rightTabs.Select("Stroke"),"Open Stroke panel"));
            toolbar.Children.Add(new StudioButton("Transitions",()=>_rightTabs.Select("Transitions"),"Open Transitions panel"));
            toolbar.Children.Add(new StudioButton("Paths",()=>_rightTabs.Select("Paths"),"Open Paths panel"));
            toolbar.Children.Add(new StudioButton("Import image",()=>_=GuardAsync(ImportImageAsync),"Import image"));
            toolbar.Children.Add(new StudioButton("Templates",()=>_rightTabs.Select("Templates"),"Open Templates panel"));
            toolbar.Children.Add(new StudioButton("Layout",()=>_leftTabs.Select("Layout"),"Open Layout panel"));
        }
        Designer.InvalidateLayout();
    }
    private void OpenAnimationTrack(object? sender,EventArgs args)=>_rightTabs.Select("Animation");
    private void OpenTiming(object? sender,EventArgs args)=>_rightTabs.Select("Timing");
    private void OpenTransitions(object? sender,EventArgs args)=>_rightTabs.Select("Transitions");
    private void StopAnimationForEditing(object? sender,EventArgs args){States.StopTransitions(false);Timeline.Stop();}
    public void DisposeAdvancedTools()
    {
        Timeline.TrackSettingsRequested-=OpenAnimationTrack;_animationTrackEditor?.Dispose();_brushEditor?.Dispose();_strokeEditor?.Dispose();States.TransitionEditorRequested-=OpenTransitions;_stateTransitions?.Dispose();Timeline.SettingsRequested-=OpenTiming;_storyboardInspector?.Dispose();Designer.EditingStarted-=StopAnimationForEditing;_templateEditor?.Dispose();_layoutAuthoring?.Dispose();
    }
    public async Task ImportImageAsync()
    {
        if(_platform is not IWorkbenchAssetPlatform assets)throw new InvalidOperationException("The host has not supplied an image picker.");
        var context=Workspace.CaptureOperation();
        var asset=await assets.OpenImageAsync();if(asset is null)return;
        Workspace.RequireCurrent(context);
        var source=EmbeddedImages.CreateSource(asset.Bytes,asset.MimeType);
        using var cache=new EmbeddedImages();var image=cache.Get(source,out var error);if(image is null)throw new InvalidOperationException(error);
        var scale=Math.Min(1,480d/Math.Max(image.Width,image.Height));
        var host=Session.Selection.Select(Session.Index.Find).FirstOrDefault(n=>n?.Type=="Canvas") ?? Session.Document.Root.DescendantsAndSelf().FirstOrDefault(n=>n.Type=="Canvas");
        if(host is null||Session.Index.IsLocked(host.Id))throw new InvalidOperationException("Choose an unlocked Canvas to import into.");
        var node=DesignNode.Create("Image",Session.UniqueName("Image"),64,80,image.Width*scale,image.Height*scale).Set("Source",source).Set("Stretch","Uniform");
        Session.Execute("Import "+asset.Name,d=>d with { Root=d.Root.Update(host.Id,n=>n with { Children=n.Children.Add(node) }) },[node.Id]);
        SetTool("Selection");SetStatus("Imported "+asset.Name+" as an embedded raster asset");
    }
}
