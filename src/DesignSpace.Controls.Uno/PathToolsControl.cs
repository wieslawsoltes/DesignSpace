using DesignSpace.Docking.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace DesignSpace.Controls.Uno;

/// <summary>Composable path-authoring panel; all commands edit the shared session, not a parallel canvas model.</summary>
public sealed class PathToolsControl : ScrollViewer
{
    public PathToolsControl(DesignerSurface designer)
    {
        var body=new StackPanel{Spacing=7,Padding=new Thickness(8)};Content=body;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=StudioTheme.Brush("#FFD09B")};
        void Text(string text){var label=StudioTheme.Text(text,11);label.TextWrapping=TextWrapping.Wrap;body.Children.Add(label);}
        void Row(params (string Label,string Command)[] items)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=3};
            foreach(var item in items)row.Children.Add(new StudioButton(item.Label,()=>
            {
                try{designer.PathCommand(item.Command);designer.FocusDesigner();}
                catch(Exception e){error.Text=e.Message;}
            },"Path "+item.Command));
            body.Children.Add(row);
        }
        Text("VECTOR PATHS");Text("Pen (P): click points, drag tangents, click the first point to close. Enter finishes; Escape cancels. Pencil (Y) draws a freehand outline.");
        Row(("Finish","Finish"),("Cancel","Cancel"));Row(("Convert shape to path","Convert"));
        Text("Direct Selection (A): Shift-click anchors or Shift-drag a point marquee. Drag selected anchors together. Ctrl+A selects all points; Ctrl+Shift+A clears them. Arrows nudge; Delete removes selected points. Alt breaks linked tangents. With Pen, click an existing point to remove it or an edge to insert one.");
        Row(("All points","Select points"),("Clear points","Clear points"));
        Row(("Insert midpoint","Insert"),("Delete point","Delete point"));Row(("Delete segment","Delete segment"));Row(("Curve segment","Curve"),("Straight segment","Straight"));Row(("Close figure","Close"),("Open figure","Open"));
        Text("Fill rule");Row(("Even / odd","EvenOdd"),("Nonzero","Nonzero"));
        Text("Combine selected Canvas siblings. The bottom shape supplies appearance. Subtract removes the top shapes from the bottom; Divide takes two shapes. Animated/state-linked operands are protected.");
        Row(("Unite","Unite"),("Intersect","Intersect"),("Subtract","Subtract"));Row(("Exclude overlap","Exclude"),("Divide","Divide"));Row(("Make compound","Compound"),("Break apart","Break apart"));
        Row(("Make clipping path","Make clip"),("Release clip","Release clip"));
        Text("Align/distribute selected anchors in artboard coordinates. Each edit is one undoable transaction.");
        Row(("Left","Align Left"),("Center","Align Center"),("Right","Align Right"));
        Row(("Top","Align Top"),("Middle","Align Middle"),("Bottom","Align Bottom"));
        Row(("Distribute X","Distribute X"),("Distribute Y","Distribute Y"));
        body.Children.Add(error);
    }
}
