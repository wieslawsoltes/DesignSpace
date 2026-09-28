# Brush authoring and rendering

## Edit a brush

Select an object, then open **Brush** from the toolbar. Choose Fill, Stroke, Background, Foreground, BorderBrush or OpacityMask. The editor supports no local brush, Solid, Linear and Radial brushes. A referenced brush is read in its resource scope; Apply creates a local copy rather than overwriting the shared resource.

Edit stop offsets and colors directly, add a stop sampled from the current ramp, reverse the stop order, remove a stop, or drag a marker in the gradient strip. Coincident offsets retain their ordering to represent a hard edge. Offsets outside zero to one are retained in source; their contribution at the visible gradient endpoints is interpolated, not blindly clamped to the first/last colors.

Linear brushes expose start/end points and direction presets. Radial brushes expose center, focal origin and separate horizontal/vertical radii. Mapping can be relative to the element's layout box or absolute. Pad, Reflect and Repeat control behavior beyond the nominal gradient interval. Brush opacity multiplies color alpha.

Transform and Relative fields accept six matrix values in the order `m11,m12,m21,m22,dx,dy`. RelativeTransform operates in the unit bounding box before the absolute Transform. The renderer transforms linear endpoints into the box's coordinate metric before projection so a diagonal gradient on a wide element is not distorted into a square-box calculation.

Changes remain a draft until **Apply brush**. Apply validates before creating one undoable document transaction. Invalid values stay editable. A document or selection change makes a pending draft stale; Reload is required instead of silently applying it to a different revision or target. Drafts are isolated by document tab and included in workspace export/recovery. Native document Save contains applied brushes, not unapplied draft errors. The editor blocks base-value brush edits during state or keyframe recording.

## Opacity masks

OpacityMask controls the alpha of the element and its subtree. Mask color brightness does not affect opacity. A black-to-white ramp whose stops are both opaque does not fade content; use transparent-to-opaque stops for a fade. Nested masks and element opacity multiply once at each layer. Masks do not become geometry clips for picking.

The renderer retains the mask shader in its native paint before drawing children, so child brush-cache eviction cannot invalidate the mask before it is composited. Mask coordinates currently use the element's layout bounds; overflow-content bounds and all WPF visual bounds rules are not runtime-qualified.

## Portable and native contracts

`DesignBrush`, `DesignGradientStop`, `BrushCodec`, `BrushColor` and `BrushResolver` live in Core. `BrushEditing` lives in Engine. These contracts never execute CLR markup extensions or application code. Unknown brush types, interpolation modes, metadata and expressions are rejected by the brush editor without changing the saved source.

```csharp
var brush = new DesignBrush
{
    Kind = DesignBrushKind.Radial,
    Center = new DPoint(0.5, 0.5),
    Origin = new DPoint(0.3, 0.45),
    RadiusX = 0.5,
    RadiusY = 0.25,
    Stops = [new(0, "#FF56D9F0"), new(1, "#FF1738AD")]
};

session.Execute("Set radial fill", document =>
    BrushEditing.Apply(document, session.Selection, "Fill", brush));
```

`BrushResolver` performs lexical resource lookup, including shadowed keys and literal Color/Double/Point/String resources used inside a brush. An external ResourceDictionary Source is not fetched. ThemeResource currently uses the same static lexical lookup, not a dynamically switching theme runtime.

`SkiaBrushShader.Create` returns a caller-owned shader. `SkiaBrushCache.Get` returns a borrowed immutable shader: do not dispose or mutate it. A borrowed shader is valid only until eviction/disposal unless retained by a native paint. These are UI-thread caches, not concurrent services. Native drawing uses gradient shaders rather than generating and uploading gradient bitmaps. Text, shape fills/strokes and borders share the same brush pipeline.

`BrushEditorControl` implements the reusable workspace-draft interface, and `GradientPreviewControl` can be used as a preview or stop strip outside the workbench. The host remains responsible for document selection, storage and UI-thread ownership.

## Verification and limits

Regression tests cover strict XML parsing, transform order, stop interpolation, hard edges, alpha, elliptical/focal radial pixels, scoped resources, masks, native lifetimes, undo, persistence and cache reuse. Published-browser tests use actual pointer/keyboard input and check rendered pixels, not just model metadata.

Brush XML is limited to 64 KiB, 128 stops, depth 32, transform depth 16 and finite coordinate magnitude of one billion. Parsed-brush retention is bounded to 128 entries with an estimated 2 MiB cost budget; native shaders to 256 entries with an estimated 4 MiB budget. Solid shaders share across placements/sizes. Retained native shaders are checked before the smaller parsed-data cache, avoiding repeated XML parsing after metadata eviction in warm scenes. Budgets estimate retained resources, not complete managed/native process memory.

This implementation supports sRGB channel interpolation. `sc#` endpoints are converted into sRGB; `ScRgbLinearInterpolation` is preserved but not approximated as supported. Image/visual/tile brushes, arbitrary brush animations, dynamic themes, every namescope/style edge case and complete WPF/WinUI runtime equivalence remain unfinished. Shader tests in software-backed Chromium do not establish physical-GPU throughput or pixel-exact Microsoft Blend parity.
