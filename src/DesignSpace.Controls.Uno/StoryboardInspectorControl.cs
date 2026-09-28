using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

/// <summary>Transactional storyboard timing inspector, independently composable with a timeline.</summary>
public sealed partial class StoryboardInspectorControl : ScrollViewer,IDisposable
{
    private readonly DesignSession _session;
    private readonly TimelineControl _timeline;
    private readonly StackPanel _body=new(){Spacing=9,Padding=new Thickness(8)};
    private DesignStoryboard? _displayed;
    private StoryboardSettingsControl? _workspaceEditor;
    private ImmutableDictionary<string,string> _workspaceInitialFields=ImmutableDictionary<string,string>.Empty;
    private bool _workspaceStale;
    public event EventHandler<string>? Error;
    public StoryboardInspectorControl(DesignSession session,TimelineControl timeline)
    {
        _session=session;_timeline=timeline;Content=_body;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        _session.DocumentChanged+=Changed;_timeline.SelectedStoryboardChanged+=Changed;Refresh();
    }
    private void Changed(object? sender,EventArgs e)=>Refresh();
    public void Refresh()
    {
        var board=_timeline.ActiveStoryboard;
        if(ReferenceEquals(board,_displayed) && _body.Children.Count>0) return;
        _displayed=board;_workspaceStale=false;_body.Children.Clear();
        if(board is null) { _body.Children.Add(StudioTheme.Text("Create a storyboard in the timeline first."));return; }
        _body.Children.Add(StudioTheme.Text("Timing · "+board.Name,13));
        var editor=_workspaceEditor=new StoryboardSettingsControl(board);_body.Children.Add(editor);_workspaceInitialFields=editor.CaptureFields();
        _body.Children.Add(new StudioButton("Apply timing",()=>
        {
            try
            {
                if(_workspaceStale||!ReferenceEquals(board,_timeline.ActiveStoryboard)) throw new InvalidOperationException("The storyboard changed. Review its current settings.");
                var updated=editor.CreateUpdated(); if(updated==board) return;
                _session.Execute("Edit storyboard timing",d=>d with { Storyboards=d.Storyboards.Select(b=>b.Id==board.Id ? updated : b).ToImmutableArray() });
                _timeline.Scrub(Math.Min(_timeline.Time,updated.Duration));
            }
            catch(Exception e) { editor.ShowError(e.Message);Error?.Invoke(this,e.Message); }
        },"Apply storyboard timing"));
    }
    public void Dispose() { _session.DocumentChanged-=Changed;_timeline.SelectedStoryboardChanged-=Changed; }
}
