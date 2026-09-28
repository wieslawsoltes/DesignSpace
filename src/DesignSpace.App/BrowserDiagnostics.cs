using System.Text.Json;
using DesignSpace.Workbench.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
namespace DesignSpace.App;

/// <summary>Opt-in read-only state and actual visual bounds. Tests send real pointer and keyboard input.</summary>
internal sealed class BrowserDiagnostics : IDisposable
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private readonly WorkbenchView _view;
    public BrowserDiagnostics(WorkbenchView view)
    {
        _view=view;
#if __WASM__
        if(Uno.Foundation.WebAssemblyRuntime.InvokeJS("new URLSearchParams(location.search).has('diagnostics') ? 'yes' : 'no'")!="yes") return;
        _timer.Tick+=(_,_)=>Publish(); _timer.Start();
#endif
    }
    private sealed record ControlBounds(string Name,string Type,double X,double Y,double Width,double Height);
    private void Publish()
    {
#if __WASM__
        if(_view.ActualWidth<=0) return;
        try
        {
            var designer=_view.Designer; var position=designer.TransformToVisual(_view).TransformPoint(new Point(0,0));
            var controls=new List<ControlBounds>();
            void Walk(DependencyObject obj)
            {
                if(obj is UIElement ui && ui.Visibility!=Visibility.Visible) return;
                if(obj is FrameworkElement element && element.ActualWidth>0 && element.ActualHeight>0)
                {
                    var name=AutomationProperties.GetName(element);
                    if(!string.IsNullOrEmpty(name))
                    {
                        var p=element.TransformToVisual(_view).TransformPoint(new Point(0,0));
                        controls.Add(new(name,element.GetType().Name,p.X,p.Y,element.ActualWidth,element.ActualHeight));
                    }
                }
                for(var i=0;i<VisualTreeHelper.GetChildrenCount(obj);i++) Walk(VisualTreeHelper.GetChild(obj,i));
            }
            Walk(_view);
            var snapshot=new
            {
                ready=_view.IsReady,revision=_view.Session.Revision,dirty=_view.Session.IsDirty,mode=_view.Mode,status=_view.Status,
                sourceDirty=_view.Source.IsDirty,sourceStatus=_view.Source.Status,width=_view.ActualWidth,height=_view.ActualHeight,controls,
                surface=new { x=position.X,y=position.Y,width=designer.ActualWidth,height=designer.ActualHeight,zoom=designer.Viewport.Zoom,panX=designer.Viewport.PanX,panY=designer.Viewport.PanY },
                nodes=_view.Session.Document.Root.DescendantsAndSelf().Select(n=>new { id=n.Id,name=n.Name,type=n.Type,properties=n.Properties.ToDictionary(p=>p.Key,p=>p.Value),rotation=n.Rotation,locked=n.IsLocked }).ToArray(),
                selection=_view.Session.Selection.Select(id=>_view.Session.Document.Root.Find(id)?.Name).ToArray(),canUndo=_view.Session.CanUndo,canRedo=_view.Session.CanRedo,
                drawCount=designer.Renderer.DrawCount,drawMilliseconds=designer.Renderer.LastDrawMilliseconds,
                timeline=new { time=_view.Timeline.Time,playing=_view.Timeline.IsPlaying,recording=_view.Timeline.IsRecording,tracks=_view.Timeline.ActiveStoryboard?.Tracks.Length ?? 0 },xaml=_view.Source.Text
            };
            Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.designSpaceDiagnostics="+JsonSerializer.Serialize(snapshot)+"; 'ready'");
        }
        catch(Exception e) { Console.Error.WriteLine("[DesignSpace diagnostics] "+e.Message); }
#endif
    }
    public void Dispose()=>_timer.Stop();
}
