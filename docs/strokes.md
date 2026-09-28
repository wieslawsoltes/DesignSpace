# Stroke authoring and integration

## Authoring workflow

Select an unlocked Path, Line, Rectangle, Ellipse, Polygon or Polyline and open **Stroke** from the toolbar. Edit the brush, thickness, independent start/end caps, dash cap, join, miter limit, dash array and offset. The sample previews the settings without editing the document. **Apply stroke** commits changed fields to the selected shapes in one undoable transaction. **Reload** discards the draft.

Mixed selections display `<multiple>` where values differ; leaving those fields unchanged preserves each object's value. Invalid settings cannot partially apply. A draft retains its original selection and document revision, so an intervening edit or selection change requires Reload rather than silently overwriting newer work. This panel edits base values and blocks Apply/outline while state or keyframe recording is enabled.

An empty Brush removes the stroke. A literal `Transparent` brush is invisible but still has hit-test geometry; an absent brush, standard `{x:Null}`, or an `x:Null` brush property element has no stroke hit geometry. Width zero is absent, not a one-pixel hairline.

## Dash and cap model

Dash-array entries alternate between painted lengths and gaps, in multiples of stroke thickness. Offset is measured in the same units. An odd-length list repeats once to form an even-length pattern, and negative lengths use their magnitudes. Empty means solid; an all-zero pattern is rejected because it has no finite period.

The presets are Solid (empty), Dash (`2 2`), Dot (`0 2`, Round dash cap), and Dash dot (`2 2 0 2`, Round dash cap). A zero-length painted entry creates a dot through its caps. A positive-length dash clipped to zero at a contour boundary is not a dot and cannot create a phantom end cap.

Start and end caps can differ. Flat adds no extension; Square extends by half the thickness; Round adds a semicircle; Triangle adds a pointed half-thickness extension. Dash caps apply at interior painted interval boundaries. Closed contours do not have line-end caps, and a painted interval crossing the closing seam keeps a continuous join. Miter, Bevel and Round joins are supported; the miter limit controls whether sharp corners retain their spike.

These conventions follow the documented [WPF dash rules](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.dashstyle.dashes) and [line cap definitions](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.penlinecap). They are not a claim of bitwise equivalence to Microsoft's rasterizer or every WPF layout rule.

## Convert a stroke to editable geometry

**Convert stroke to path** creates filled vector contours using the same outline geometry as the renderer and picker. The original object identity and placement remain intact, and undo restores the original stroke settings. The result's fill uses the original solid stroke color. This is not a raster export.

Conversion deliberately rejects existing visible fills, state/animation-linked objects, inherited locks, unresolved styles/templates and nonliteral brush resources. It also rejects an empty or zero-width stroke. Resolve those dependencies first instead of relying on an operation that silently drops them. Very complex outlines can exceed the portable path budget and are rejected atomically.

## Reusable APIs

`StrokeStyle` in Core describes shape stroke settings and returns bounded dash intervals without Skia or Uno dependencies. `StrokeEditing` in Engine performs validated multi-shape document changes without requiring a renderer.

```csharp
using DesignSpace.Core;
using DesignSpace.Engine;

var stroke = new StrokeStyle(
    Thickness: 8,
    StartCap: DesignLineCap.Triangle,
    EndCap: DesignLineCap.Round,
    DashCap: DesignLineCap.Round,
    LineJoin: DesignLineJoin.Bevel,
    DashArray: "2 2");
var paintedIntervals = stroke.GetDashes(length: 200);

session.Execute("Change stroke", document => StrokeEditing.Apply(
    document, session.Selection,
    new Dictionary<string, string>
    {
        ["StrokeThickness"] = "8",
        ["StrokeDashArray"] = "2 2",
        ["StrokeEndLineCap"] = "Round"
    }));
```

`SkiaStrokeGeometry.Create(centerline, style)` returns a **caller-owned** `SKPath`. Use it with fill painting; the stroke has already been expanded into geometry. `StrokeGeometryCache.Get` returns a **borrowed** path that must not be mutated or disposed and remains valid only until eviction or cache disposal. The cache is UI-thread owned, not thread-safe.

```csharp
using DesignSpace.Rendering.Skia;
using SkiaSharp;

using var outline = SkiaStrokeGeometry.Create(yourCenterline, stroke);
using var paint = new SKPaint
{
    IsAntialias = true,
    Style = SKPaintStyle.Fill,
    Color = SKColors.CornflowerBlue
};
yourCanvas.DrawPath(outline, paint);
```

`DesignRenderer` implements Engine's optional `IShapeHitTest`. `new LayoutEngine(renderer)` produces snapshots that use the same filled geometry for drawing and picking, including real dash gaps and caps outside the layout box. A snapshot using this service requires its renderer to remain alive. A portable layout with no native provider retains the approximate fallback picker; it does not gain native stroke fidelity implicitly.

`StrokeEditorControl` and `StrokePreviewControl` in Controls.Uno can be embedded independently. Subscribe to `OutlineRequested` to supply the host's conversion action; the full workbench uses `StrokeCommands.Outline(session, layout)`. Their document mutation and platform-independent algorithms remain outside the application host.

## Performance and limits

The main renderer first rejects fully clipped shapes using conservative geometry bounds expanded for caps and miter joins; this avoids native outlining and brush allocation for off-screen shapes without relying on the smaller layout box. The main renderer caches native stroke outlines separately from brushes. Warm draws and picks reuse the same outline; scene translation, viewport zoom and solid recoloring do not belong in its geometry key. A uniform solid cap uses the native stroke-expansion fast path; mixed caps and dashes use native curve segments and cap geometry. Curve length measurement is numerical, not exact arc-length algebra.

Basic-shape geometry is shared by shape descriptors rather than object identity, so identically sized instances and opacity/recolor edits reuse their outlines. This portable geometry cache is limited to 256 entries and an estimated 4 MiB. The native outline cache retains at most 128 entries under an estimated 16 MiB resource-cost budget. This estimate excludes some allocator/native bookkeeping and is not a total process-memory guarantee. Dash patterns allow 128 input values and 4,096 characters; evaluation caps work at 8,192 fragments, and native outlines at 262,144 points. Existing portable path/coordinate limits still apply to conversion. Extreme inputs are diagnosed or rejected rather than allowed to run unbounded.

First-time outlining, cap unions and dashed-curve measurement still consume CPU time. Cache tests verify work avoidance and bounded allocation; software-backed browser tests do not certify hardware-GPU throughput or end-to-end frame latency.

## Remaining boundaries

This increment does not implement arbitrary pressure brushes, inside/outside stroke alignment, every brush transform/mapping rule, geometry-group/per-segment metadata, full stroke-aware intrinsic sizing, or every degenerate zero-length contour's cap behavior. Unresolved stroke settings remain preserved source and are diagnosed instead of executed. Native path length/outline operations use Skia floating-point semantics. WPF/WinUI runtime differential and physical-GPU qualification remain separate tasks.
