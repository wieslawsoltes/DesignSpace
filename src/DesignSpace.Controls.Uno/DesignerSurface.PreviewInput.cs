using DesignSpace.Core;
using DesignSpace.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
namespace DesignSpace.Controls.Uno;

public sealed partial class DesignerSurface
{
    public DesignPreviewContext PreviewContext { get; }=new();
    private Guid? _previewPress;
    private VirtualKey? _previewKey;
    private bool _previewCaptured;
    private static bool IsButton(DesignNode n)=>n.Type is "Button" or "ToggleButton" or "CheckBox" or "RadioButton" or "RepeatButton";
    private void InitializePreviewInput()
    {
        _surface.PointerExited+=(_,_)=>{if(IsPreview)UpdatePreviewInput(PreviewContext.State with{Hovered=null,Pressed=null});};
        _surface.PointerCaptureLost+=(_,_)=>{if(_previewCaptured){_previewCaptured=false;_previewPress=null;UpdatePreviewInput(PreviewContext.State with{Pressed=null});}};
        _focus.LostFocus+=(_,_)=>{if(IsPreview){_previewKey=null;UpdatePreviewInput(PreviewContext.State with{Focused=null,Pressed=null});}};
        _focus.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler((_,e)=>
        {if(IsPreview&&!DesignerKeys.Control&&e.Key is VirtualKey.Tab or VirtualKey.Space or VirtualKey.Enter or VirtualKey.Escape&&HandlePreviewKey(e.Key,false,DesignerKeys.Shift))e.Handled=true;}),true);
        _focus.AddHandler(UIElement.KeyUpEvent,new KeyEventHandler((_,e)=>
        {
            if(!IsPreview||_previewKey!=e.Key)return;
            var id=PreviewContext.State.Pressed;_previewKey=null;UpdatePreviewInput(PreviewContext.State with{Pressed=null});
            if(id is { } target)ActivatePreview(target);e.Handled=true;
        }),true);
    }
    private void ResetPreviewInput()
    {
        _previewCaptured=false;_previewPress=null;_previewKey=null;PreviewContext.Clear();
        _surface.ReleasePointerCaptures();_layout=null;
    }
    private void UpdatePreviewInput(PreviewInteractionState state)
    {if(PreviewContext.Update(state))InvalidateLayout();}
    private bool PreviewEnabled(Guid id)=>Layout.ById.TryGetValue(id,out var entry)&&entry.IsEffectivelyVisible&&entry.Node.Get("IsEnabled","True") is not ("False" or "false");
    private Guid? PreviewHit(DPoint world)
    {
        foreach(var hit in Layout.HitStack(world))
        {
            var id=Guid.TryParse(hit.Node.Get(DesignPreview.OwnerKey),out var owner)?owner:hit.Node.Id;
            if(Session.Index.Find(id) is not null)return PreviewEnabled(id)?id:null;
        }
        return null;
    }
    private void PreviewPressed(PointerRoutedEventArgs e,DPoint world)
    {
        var hit=PreviewHit(world);_previewPress=hit is { } id&&Session.Index.Find(id) is { } n&&IsButton(n)?hit:null;
        _previewCaptured=_previewPress is not null;
        if(_previewCaptured)_surface.CapturePointer(e.Pointer);
        UpdatePreviewInput(PreviewContext.State with{Hovered=hit,Pressed=_previewPress,Focused=_previewPress});e.Handled=true;
    }
    private void PreviewMoved(DPoint world)
    {
        var hit=PreviewHit(world);
        UpdatePreviewInput(PreviewContext.State with{Hovered=hit,Pressed=_previewCaptured&&hit==_previewPress?_previewPress:_previewKey is not null?PreviewContext.State.Pressed:null});
    }
    private void PreviewReleased(PointerRoutedEventArgs e)
    {
        var point=e.GetCurrentPoint(_surface).Position;var hit=PreviewHit(Viewport.ScreenToWorld(new(point.X,point.Y)));
        var activate=_previewCaptured&&_previewPress==hit?_previewPress:null;
        _previewCaptured=false;_previewPress=null;UpdatePreviewInput(PreviewContext.State with{Hovered=hit,Pressed=null});
        _surface.ReleasePointerCapture(e.Pointer);if(activate is { } target)ActivatePreview(target);e.Handled=true;
    }
    private void ActivatePreview(Guid id)
    {
        if(!PreviewEnabled(id)||Session.Index.Find(id) is not { } node)return;
        if(node.Type is "CheckBox" or "ToggleButton" or "RadioButton")
        {
            var state=PreviewContext.State;var current=Layout.ById[id].Node.Get("IsChecked","False");
            bool? value=current is "True" or "true"?node.Get("IsThreeState")=="True"?null:false:true;
            if(current=="{x:Null}")value=false;
            var checks=state.Checked;
            if(node.Type=="RadioButton")
            {
                value=true;var group=node.Get("GroupName");var parent=Session.Index.ParentOf(id)?.Id;
                foreach(var sibling in Session.Index.Nodes.Values)
                    if(sibling.Id!=id&&sibling.Type=="RadioButton"&&sibling.Get("GroupName")==group&&(group.Length>0||Session.Index.ParentOf(sibling.Id)?.Id==parent))checks=checks.SetItem(sibling.Id,false);
            }
            UpdatePreviewInput(state with{Checked=checks.SetItem(id,value)});
        }
        PreviewClicked?.Invoke(this,id);
    }
    public bool HandlePreviewKey(VirtualKey key,bool control,bool shift)
    {
        if(!IsPreview)return false;
        if(key==VirtualKey.Escape){IsPreview=false;return true;}
        if(key==VirtualKey.F5)return false; // Keep the workbench preview toggle available.
        if(control)return true; // File/tab commands are routed by the workbench before this handler.
        if(key==VirtualKey.Tab)
        {
            var targets=Layout.Entries.Where(e=>Session.Index.Find(e.Node.Id) is not null&&IsButton(e.Node)&&PreviewEnabled(e.Node.Id)).Select(e=>e.Node.Id).ToArray();
            if(targets.Length>0)
            {
                var index=Array.IndexOf(targets,PreviewContext.State.Focused??Guid.Empty);var next=index<0?(shift?targets.Length-1:0):(index+(shift?-1:1)+targets.Length)%targets.Length;
                _previewKey=null;UpdatePreviewInput(PreviewContext.State with{Focused=targets[next],Pressed=null});
            }
            return true;
        }
        if(key is VirtualKey.Space or VirtualKey.Enter)
        {
            if(_previewKey is null&&PreviewContext.State.Focused is { } id&&PreviewEnabled(id)){_previewKey=key;UpdatePreviewInput(PreviewContext.State with{Pressed=id});}
            return true;
        }
        // Preview must never route Delete, arrow nudges or tool shortcuts into the base document.
        return true;
    }
}
