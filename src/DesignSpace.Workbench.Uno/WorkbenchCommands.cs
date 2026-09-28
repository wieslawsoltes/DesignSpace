using System.Collections.Immutable;
using DesignSpace.Core;
using DesignSpace.Xaml;
using DesignSpace.Docking.Uno;
using DesignSpace.Controls.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
namespace DesignSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private UIElement BuildMenus()
    {
        var bar=new StackPanel { Orientation=Orientation.Horizontal,Spacing=1,Background=StudioTheme.Background,Padding=new Thickness(6,0,0,0) };
        void Menu(string name,params (string Label,Action Action)[] items)
        {
            var button=new StudioButton(name); var flyout=new MenuFlyout();
            foreach(var item in items)
            {
                if(item.Label=="-") { flyout.Items.Add(new MenuFlyoutSeparator()); continue; }
                var entry=new MenuFlyoutItem { Text=item.Label }; entry.Click+=(_,_)=>Guard(item.Action); flyout.Items.Add(entry);
            }
            button.Flyout=flyout; bar.Children.Add(button);
        }
        Menu("File",("New design    Ctrl+N",()=>_=GuardAsync(()=>NewAsync())),("Open…    Ctrl+O",()=>_=GuardAsync(OpenAsync)),("Save design…    Ctrl+S",()=>_=GuardAsync(SaveAsync)),("-",()=>{}),("Save all    Ctrl+Shift+S",()=>_=GuardAsync(SaveAllAsync)),("Save workspace…",()=>_=GuardAsync(SaveWorkspaceAsync)),("Close document    Ctrl+F4",()=>_=GuardAsync(()=>CloseDocumentAsync(Workspace.ActiveDocumentId))),("Export XAML…",()=>_=GuardAsync(ExportXamlAsync)),("Export PNG…",()=>_=GuardAsync(ExportPngAsync)));
        Menu("Edit",("Undo    Ctrl+Z",Session.Undo),("Redo    Ctrl+Y",Session.Redo),("-",()=>{}),("Cut    Ctrl+X",()=>_=GuardAsync(CutAsync)),("Copy    Ctrl+C",()=>_=GuardAsync(CopyAsync)),("Paste    Ctrl+V",()=>_=GuardAsync(PasteAsync)),("Duplicate    Ctrl+D",Session.Duplicate),("Delete    Del",Session.Delete),("Select all    Ctrl+A",()=>Session.Select(Session.Document.Root.Children.Select(n=>n.Id))));
        Menu("View",("Design",()=>SetMode("Design")),("Split",()=>SetMode("Split")),("XAML",()=>SetMode("XAML")),("-",()=>{}),("Fit artboard    F",Designer.Fit),("Show / hide grid",()=> { Designer.Viewport.ShowGrid=!Designer.Viewport.ShowGrid; Designer.Invalidate(); }),("Show / hide rulers",()=> { Designer.Viewport.ShowRulers=!Designer.Viewport.ShowRulers; Designer.Invalidate(); }),("Show / hide timeline",ToggleTimeline),("Preview    F5",TogglePreview));
        Menu("Object",("Group into Canvas    Ctrl+G",Session.Group),("Ungroup    Ctrl+Shift+G",Session.Ungroup),("Bring to front",()=>Session.Reorder(true)),("Send to back",()=>Session.Reorder(false)),("-",()=>{}),("Align left",()=>Session.Align("Left")),("Align horizontal centers",()=>Session.Align("Center")),("Align right",()=>Session.Align("Right")),("Align top",()=>Session.Align("Top")),("Align vertical centers",()=>Session.Align("Middle")),("Align bottom",()=>Session.Align("Bottom")));
        Menu("Path",("Convert to Path",()=>Designer.PathCommand("Convert")),("Unite",()=>Designer.PathCommand("Unite")),("Intersect",()=>Designer.PathCommand("Intersect")),("Subtract",()=>Designer.PathCommand("Subtract")),("Exclude overlap",()=>Designer.PathCommand("Exclude")),("Divide",()=>Designer.PathCommand("Divide")),("Make compound path",()=>Designer.PathCommand("Compound")),("Break apart",()=>Designer.PathCommand("Break apart")));
        Menu("Project",("Open XAML document…",()=>_=GuardAsync(OpenAsync)),("Load interaction sample",()=>_=GuardAsync(()=>NewAsync(true))),("Assets",()=>_leftTabs.Select("Assets")),("Sample data",()=>_leftTabs.Select("Data")),("Resources",()=>_rightTabs.Select("Resources")));
        Menu("Tools",("New storyboard",Timeline.AddStoryboard),("Add keyframe",()=>Timeline.AddKey(Timeline.PropertyToRecord)),("Play / pause animation",Timeline.TogglePlay),("Stop animation",()=> { Timeline.Stop(); Designer.ClearPreview(); }),("Visual states",()=>_leftTabs.Select("States")),("Reset to base values",()=> { Timeline.Stop(); States.Select(null); Designer.ClearPreview(); }));
        Menu("Window",("Next document    Ctrl+Tab",()=>CycleDocument(1)),("Previous document    Ctrl+Shift+Tab",()=>CycleDocument(-1)),("Rename document",()=>_=GuardAsync(RenameActiveDocumentAsync)),("Toggle Design / Animation    F6",()=>SetWorkspaceProfile(_workspaceProfile=="Design"?"Animation":"Design")),("Horizontal split",()=>SetSplitOrientation("Horizontal")),("Vertical split",()=>SetSplitOrientation("Vertical")),("Assets / Project",()=>_dock.Show("assets")),("Objects and Timeline",()=>_dock.Show("objects")),("Properties / Resources",()=>_dock.Show("properties")),("Float Properties",()=>_dock.ToggleFloat("properties")),("Reset workspace",()=>_dock.Reset()));
        Menu("Help",("Keyboard shortcuts",()=>_=GuardAsync(()=>ShowInfoAsync("Keyboard shortcuts","V Selection · A Direct Selection · H Hand · Z Zoom\nR Rectangle · E Ellipse · L Line · P Pen · Y Pencil · T Text\nF Fit artboard · F5 Preview · Space+drag Pan\nCtrl+wheel Zoom around pointer · Alt Disable snapping\nShift+resize Preserve aspect ratio\nCtrl+Z / Ctrl+Y Undo / Redo\nCtrl+D Duplicate · Ctrl+G Group · Ctrl+Shift+G Ungroup\nCtrl+C / X / V Copy / Cut / Paste\nArrow keys Move · Shift+arrows Move by 10\nCtrl+Enter Apply the current XAML draft"))),("Compatibility and architecture",()=>_=GuardAsync(()=>ShowInfoAsync("DesignSpace 0.1 preview","Independent Uno Platform XAML designer with eight reusable libraries.\n\nThe artboard previews a documented subset of XAML; it does not execute imported code, arbitrary markup extensions, behaviors or custom assemblies. Grid Auto layout, inherited transforms, typography, controls and gradients are not full WPF/WinUI runtime equivalents.\n\nThe native .designspace format preserves editor identities, states, storyboards and locks. XAML export is a compatibility subset.\n\nNo Microsoft product source, logos or icons are included. Documentation is in the repository."))),("About DesignSpace",()=>_=GuardAsync(()=>ShowInfoAsync("DesignSpace","A modular XAML design environment for desktop and browser.\n\n.NET 10 · Uno Platform 6.7 · Skia rendering\nMIT licensed original source\n\nDesignSpace is not affiliated with or endorsed by Microsoft."))));
        return bar;
    }
    private UIElement BuildToolbar()
    {
        var bar=new StackPanel { Orientation=Orientation.Horizontal,Spacing=2,Background=StudioTheme.Brush("#333337"),Padding=new Thickness(7,1,7,1) };
        bar.Children.Add(new StudioButton("New",()=>_=GuardAsync(()=>NewAsync()),"New design")); bar.Children.Add(new StudioButton("Open",()=>_=GuardAsync(OpenAsync),"Open design")); bar.Children.Add(new StudioButton("Save",()=>_=GuardAsync(SaveAsync),"Save design"));
        bar.Children.Add(new StudioButton("Save all",()=>_=GuardAsync(SaveAllAsync),"Save all documents"));
        bar.Children.Add(new StudioButton("Workspace",()=>_=GuardAsync(SaveWorkspaceAsync),"Save workspace"));
        bar.Children.Add(Separator()); bar.Children.Add(new StudioButton("↶",()=>Guard(Session.Undo),"Undo")); bar.Children.Add(new StudioButton("↷",()=>Guard(Session.Redo),"Redo")); bar.Children.Add(Separator());
        bar.Children.Add(new StudioButton("▶  Preview",TogglePreview,"Preview design")); bar.Children.Add(new StudioButton("■",()=> { Timeline.Stop(); Designer.IsPreview=false; Designer.ClearPreview(); },"Stop preview")); bar.Children.Add(Separator());
        bar.Children.Add(StudioTheme.Text("Artboard",11,"#AEAEB5"));
        var devices=new ComboBox { ItemsSource=new[]{"960 × 560","1280 × 720","1920 × 1080","390 × 844","768 × 1024"},SelectedIndex=0,MinWidth=119,MinHeight=24,FontSize=11,Padding=new Thickness(5,1,5,1),Background=StudioTheme.Field };
        devices.SelectionChanged+=(_,_)=>
        {
            if(devices.SelectedItem is not string value) return; var dimensions=value.Split('×');
            Guard(()=>Session.Execute("Change artboard size",d=>d with { Root=d.Root.Set("Width",DesignSpace.Core.Numbers.Parse(dimensions[0])).Set("Height",DesignSpace.Core.Numbers.Parse(dimensions[1])) })); Designer.Fit();
        };
        bar.Children.Add(devices); bar.Children.Add(Separator()); bar.Children.Add(new StudioButton("Animation",()=>SetWorkspaceProfile("Animation"),"Animation workspace"));
        return bar;
    }
    private static Border Separator()=>new(){Width=1,Height=17,Background=StudioTheme.Border,Margin=new Thickness(5,3,5,3)};
    private UIElement BuildProject()
    {
        var project=new StackPanel{Padding=new Thickness(8),Spacing=5};
        project.Children.Add(StudioTheme.Text("Open documents",12));project.Children.Add(_projectDocuments);
        project.Children.Add(new StudioButton("Open another document…",()=>_=GuardAsync(OpenAsync),"Project open document"));
        project.Children.Add(new StudioButton("New blank design",()=>_=GuardAsync(()=>NewAsync()),"Project new document"));
        project.Children.Add(new StudioButton("Load interaction sample",()=>_=GuardAsync(()=>NewAsync(true)),"Project new sample"));
        project.Children.Add(new StudioButton("Save workspace…",()=>_=GuardAsync(SaveWorkspaceAsync),"Project save workspace"));
        var note=StudioTheme.Text("Each tab keeps its own undo history, selection, drafts and view. Workspace files retain documents and drafts, not a compiled Visual Studio solution.",11,"#8E8E98");
        note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(4,12,4,0);project.Children.Add(note);
        return new ScrollViewer{Content=project,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    }
    private void ToggleTimeline()
    {
        var visible=_timelineHost.Visibility!=Visibility.Visible; _timelineHost.Visibility=visible ? Visibility.Visible : Visibility.Collapsed; _timelineHeight.Height=new GridLength(visible ? 170 : 0);
    }
    private void TogglePreview()
    {
        Designer.IsPreview=!Designer.IsPreview;
        if(Designer.IsPreview) { Timeline.Stop(); Timeline.TogglePlay(); SetStatus("Preview mode · Click a button to replay the active storyboard · F5 to return to design"); }
        else { Timeline.Stop(); Designer.ClearPreview(); SetStatus("Design mode"); }
        Designer.Invalidate(); Designer.FocusDesigner();
    }
    private void OnKeyDown(object sender,KeyRoutedEventArgs e)
    {
        var control=DesignerKeys.Control; var shift=DesignerKeys.Shift;
        if(control && e.Key==VirtualKey.S) { _=GuardAsync(shift?SaveAllAsync:SaveAsync); e.Handled=true; return; }
        if(control && e.Key==VirtualKey.Tab){Guard(()=>CycleDocument(shift?-1:1));e.Handled=true;return;}
        if(control && e.Key==VirtualKey.F4){_=GuardAsync(()=>CloseDocumentAsync(Workspace.ActiveDocumentId));e.Handled=true;return;}
        if(e.Key==VirtualKey.F6){SetWorkspaceProfile(_workspaceProfile=="Design"?"Animation":"Design");e.Handled=true;return;}
        if(control && e.Key==VirtualKey.Enter && _mode!="Design") { Source.Apply(); e.Handled=true; return; }
        var focused=XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
        if(focused is TextBox or PasswordBox || e.OriginalSource is TextBox) return;
        try{if(Designer.HandlePathKey(e.Key,control,shift)){e.Handled=true;return;}}catch(Exception ex){SetStatus(ex.Message,true);e.Handled=true;return;}
        if(control)
        {
            Action? action=e.Key switch
            {
                VirtualKey.Z=>shift ? Session.Redo : Session.Undo,VirtualKey.Y=>Session.Redo,
                VirtualKey.D=>Session.Duplicate,VirtualKey.G=>shift ? Session.Ungroup : Session.Group,
                VirtualKey.A=>()=>Session.Select(Session.Document.Root.Children.Select(n=>n.Id)),
                VirtualKey.C=>()=>_=GuardAsync(CopyAsync),VirtualKey.X=>()=>_=GuardAsync(CutAsync),VirtualKey.V=>()=>_=GuardAsync(PasteAsync),
                VirtualKey.O=>()=>_=GuardAsync(OpenAsync),VirtualKey.N=>()=>_=GuardAsync(()=>NewAsync()),_=>null
            };
            if(action is not null) { Guard(action); e.Handled=true; } return;
        }
        var distance=shift ? 10 : 1;
        Action? command=e.Key switch
        {
            VirtualKey.Delete or VirtualKey.Back=>Session.Delete,VirtualKey.Left=>()=>Session.Move(-distance,0),VirtualKey.Right=>()=>Session.Move(distance,0),VirtualKey.Up=>()=>Session.Move(0,-distance),VirtualKey.Down=>()=>Session.Move(0,distance),
            VirtualKey.Escape=>()=> { Designer.CancelGesture(); Timeline.Stop(); Designer.IsPreview=false; Designer.ClearPreview(); },
            VirtualKey.F=>Designer.Fit,VirtualKey.F5=>TogglePreview,
            VirtualKey.V=>()=>SetTool("Selection"),VirtualKey.A=>()=>SetTool("Direct Selection"),VirtualKey.H=>()=>SetTool("Hand"),VirtualKey.Z=>()=>SetTool("Zoom"),VirtualKey.R=>()=>SetTool("Rectangle"),VirtualKey.E=>()=>SetTool("Ellipse"),VirtualKey.L=>()=>SetTool("Line"),VirtualKey.P=>()=>SetTool("Pen"),VirtualKey.Y=>()=>SetTool("Pencil"),VirtualKey.T=>()=>SetTool("TextBlock"),VirtualKey.G=>()=>SetTool("Grid"),VirtualKey.B=>()=>SetTool("Button"),_=>null
        };
        if(command is not null) { Guard(command); e.Handled=true; }
    }
    private async Task<bool> ConfirmAsync(string title,string message)
    {
        var dialog=new ContentDialog { XamlRoot=XamlRoot,Title=title,Content=new TextBlock { Text=message,TextWrapping=TextWrapping.Wrap },PrimaryButtonText="Continue",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close };
        return await dialog.ShowAsync()==ContentDialogResult.Primary;
    }
    private async Task ShowInfoAsync(string title,string message)
    {
        var dialog=new ContentDialog { XamlRoot=XamlRoot,Title=title,Content=new ScrollViewer { Content=new TextBlock { Text=message,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true },MaxHeight=440 },CloseButtonText="Close" }; await dialog.ShowAsync();
    }
    private const string ClipboardHeader="DesignSpace.Selection/1\n";
    private async Task CopyAsync()
    {
        var ids=Session.TopLevelSelection(); if(ids.Count==0) return;
        var root=new DesignNode { Type="Canvas",Children=ids.Select(id=>Session.Document.Root.Find(id)!).ToImmutableArray() };
        _clipboard=ClipboardHeader+NativeDocumentCodec.Write(new DesignDocument { Root=root });
        try { await _platform.WriteClipboardAsync(_clipboard); SetStatus("Copied "+ids.Count+" object(s)"); }
        catch { SetStatus("Copied "+ids.Count+" object(s) to the in-app clipboard; system clipboard access was denied."); }
    }
    private async Task CutAsync() { var context=Workspace.CaptureOperation();await CopyAsync();Workspace.RequireCurrent(context);Session.Delete(); }
    private async Task PasteAsync()
    {
        var context=Workspace.CaptureOperation();
        string? text; try { text=await _platform.ReadClipboardAsync(); } catch { text=_clipboard; }
        Workspace.RequireCurrent(context);
        if(string.IsNullOrEmpty(text)) text=_clipboard;
        if(!text.StartsWith(ClipboardHeader,StringComparison.Ordinal)) throw new InvalidOperationException("The clipboard does not contain DesignSpace objects. Use Open to import XAML.");
        var copied=NativeDocumentCodec.Read(text[ClipboardHeader.Length..]);
        var names=Session.Document.Root.DescendantsAndSelf().Select(n=>n.Name).ToHashSet(StringComparer.Ordinal);
        DesignNode Clone(DesignNode n)
        {
            var prefix=n.Name; var name=prefix; var i=1; while(!names.Add(name)) name=prefix+"Copy"+i++;
            return (n with { Id=Guid.NewGuid(),Children=n.Children.Select(Clone).ToImmutableArray() }).Set(DesignNode.NameKey,name);
        }
        var children=copied.Root.Children.Select(Clone).Select(n=>n.Set("Canvas.Left",n.Number("Canvas.Left")+16).Set("Canvas.Top",n.Number("Canvas.Top")+16)).ToImmutableArray();
        var host=Session.Selection.Select(id=>Session.Document.Root.Find(id)).FirstOrDefault(n=>n?.Type=="Canvas") ?? Session.Document.Root.DescendantsAndSelf().FirstOrDefault(n=>n.Type=="Canvas") ?? Session.Document.Root;
        if(Session.Index.IsLocked(host.Id))throw new InvalidOperationException("Unlock the paste container and its ancestors first.");
        Session.Execute("Paste objects",d=>d with { Root=d.Root.Update(host.Id,n=>n with { Children=n.Children.AddRange(children) }) },children.Select(n=>n.Id));
    }
}
