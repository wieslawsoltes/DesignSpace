# Blur, drop shadows and effect authoring

The shared renderer supports `BlurEffect` (Gaussian or Box) and `DropShadowEffect` on an object and its subtree. The Effects panel edits local overrides with undo, while Artboard preferences can suppress effect preview without changing the document or PNG export. This is a defined compatibility increment, not full or pixel-exact Microsoft Blend parity.

## Authoring and inheritance

Select an object, then open **Effects** from the toolbar or View menu. Choose None, Blur or DropShadow. Blur exposes Radius and Kernel; a shadow exposes Radius, Direction, Depth, Opacity and Color. Soft shadow, Hard shadow and Blur presets initialize a draft. The small sample previews valid settings without modifying the design. Invalid text remains editable and the last valid sample is retained.

**Apply effect** validates and replaces the selected objects' local effects as one transaction. **Reload** discards the draft. Mixed selections require an explicit effect choice; applying replaces the entire effect on each target, not just one field. Locked targets or ancestors, stale document revisions and changed selections reject the complete edit. State/keyframe recording must be left before authoring base effects.

The panel displays resolved style/resource values, but Apply writes a **local copy**. It does not modify a shared resource definition. **None** writes an explicit `{x:Null}` override, suppressing a style-provided effect. **Reset to style** removes the local override and lets the supported style/resource lookup apply again. These are different operations and both are undoable.

Draft text belongs to the document where it was created. Switching tabs, Save workspace and recovery preserve unapplied values separately from the applied effect. Save design writes the committed document; it does not apply a panel draft. Missing or unsupported future draft metadata stays inert until explicitly discarded.

## Rendering behavior

Filters operate on the drawn subtree, not an opaque rectangle matching the parent's bounds. A transparent source does not cast a filled rectangle. An element's opacity affects its source and shadow together. Its opacity mask is included in the filtered input; ancestor clipping still limits the result. Nested effects compose through nested layers.

A shadow's direction is counterclockwise in degrees: zero points right, 90 up, 180 left and 270 down. Depth is measured in design units. Shadow Color supplies RGB; its alpha channel is ignored, with shadow strength controlled by the separate Opacity value. A zero-opacity shadow or zero-radius blur takes the no-filter path. These conventions follow the [WPF effect properties](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.effects.dropshadoweffect) and [WPF effect implementation](https://github.com/dotnet/wpf/tree/main/src/Microsoft.DotNet.Wpf/src/WpfGfx/core/resources).

Effects do not change layout size, selection handles or the underlying geometric hit target. Clicking only a shadow is not the same as clicking its source object. A source outside the visible artboard can still contribute an on-screen shadow through the filter's required input bounds.

## Artboard preview versus export

**View → Render effects** enables or suppresses all artboard effects. In **Artboard Options**, Render effects and the Effects zoom limit are document-local preferences. The zoom limit accepts 10–800 percent and suppresses effects only when the viewport exceeds it; the default is 800 percent. Disabling preview avoids filter resolution and creation in that draw.

Preference changes do not alter XAML, geometry or undo history. PNG export always renders the committed effects using the renderer's selected numerical mode, even when the artboard suppresses them. Workspace export and recovery retain the preferences. Older version-1 workspaces missing the new fields enable effects with the 800-percent limit; explicitly invalid or null values are rejected instead of silently migrated.

## Native and software-reference modes

`DesignRenderer.EffectMode` defaults to `EffectRenderingMode.Native`, also used by the workbench and its small previews. Native mode uses Skia's Gaussian/drop-shadow filters; Gaussian sigma is Radius / 3. Box blur uses two separable convolution filters. Because Skia applies convolution taps in device pixels, the renderer and preview sample include the current canvas zoom and DPI in the radius on each axis. Device radii are rounded upwards and quantized radii share cached filters. `RenderingBias` is retained in XAML but does not currently select different Performance/Quality implementations.

A host can explicitly select `EffectRenderingMode.WpfSoftwareCompatible`. This mode uses WPF-derived finite Gaussian taps and a truncated local blur radius, then applies the minimum device-axis scale and truncates again for the convolution radius. For shadows it reproduces the fully covered, non-overlapping software-opacity convention. It is **not** a complete WPF software rasterizer: arbitrary partial-coverage overlap, shadow-edge resampling, transforms, scaled-radius rules, intermediate precision and all blur pixels are not guaranteed equivalent.

Native WPF uses fixed-point arithmetic in its software shadow implementation, including division by 65536. A fully covered shadow at Opacity 1 can consequently have alpha 253 rather than 255. This difference is not imposed on the normal native Skia path. The implementations and upstream MIT attribution are recorded in `WpfEffectMath.cs` and `THIRD-PARTY-NOTICES.md`.

The Windows reference suite compares 96 hard-shadow cases across eight directions, three shadow opacities, two source opacities and two output scales. It samples source, shadow and untouched background channels with a fixed one-channel-level tolerance in the explicit software-reference mode. Native-mode differences are measured separately, not silently counted as equivalent.

Six soft-edge profiles compare Gaussian and Box at radii 3, 9 and 18. They retain actual native/portable PNGs and per-pixel values as **measurements**, not complete pixel-equivalence passes. `wpf-effect-results.json` states each mode's maximum differences and qualification scope. Consult the artifact for the exact commit; a test's existence does not establish its result.

## Reusable libraries

```csharp
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Rendering.Skia;

var session = new DesignSession(DesignDocument.Empty());
var card = session.Add("Rectangle", new DRect(80, 100, 160, 80));
var effect = new DesignEffect
{
    Kind = DesignEffectKind.DropShadow,
    Radius = 12,
    Direction = 315,
    ShadowDepth = 8,
    Opacity = 0.45,
    Color = "Black"
};
session.Execute("Apply shadow", document =>
    EffectEditing.Apply(document, [card], effect));

using var renderer = new DesignRenderer();
var layout = new LayoutEngine(renderer).Arrange(session.Document.Root);
byte[] png = renderer.ExportPng(layout, scale: 2);
```

`DesignEffect` and strict `EffectCodec` are portable Core APIs. Engine's `EffectEditing.Apply(document, targets, null)` explicitly disables effects; `EffectEditing.Reset` removes local overrides. Neither operation executes imported markup.

`EffectFilterCache.Create(effect, mode, scaleX, scaleY)` returns a caller-owned `SKImageFilter`, possibly null for a defined no-op. Scale defaults to one for standalone callers. Pass local-to-device scale including DPI; `GetForCanvas` derives it from a caller-owned canvas without modifying the matrix. Unexpected native factory failures throw rather than silently removing the effect. Dispose non-null results. `EffectFilterCache.Get` returns a borrowed filter: do not mutate or dispose it, and do not retain it beyond eviction. The renderer attaches it to a paint before visiting children, keeping a native reference alive even when a child causes cache eviction. Filters and the renderer are UI-thread-owned, not shared concurrent services.

`EffectEditorControl` accepts a `DesignSession`, an optional base-edit guard and error notifications. It implements `IWorkspaceDraftEditor`; a host owns document switching and draft persistence. `EffectPreviewControl` can be embedded independently. Both own disposable resources; the host must dispose them when permanently removed.

## Performance, limits and remaining work

Effect XML is parsed and filters are built on cache miss rather than every draw. The cache retains up to 64 entries under an estimated 256-KiB descriptor/filter-cost budget. Native and software-reference modes have distinct cache keys. The separate descriptor cache retains at most 128 entries under an estimated 512-KiB budget; warmed scaled queries reuse parsed settings. Box/software-convolution entries also key the quantized device radii. Native Gaussian and shadow filters already handle transforms and therefore remain shared across viewport scales. This estimate does not include intermediate render targets or total GPU/process memory. Recoloring unrelated content reuses retained filters; zoom changes reuse native Gaussian/shadow filters and any matching quantized convolution kernels.

The test suite checks 10,000 warmed filter lookups for per-call managed allocation, bounded retention, parent-filter lifetime during child eviction, pixel placement, style precedence, clipping, export, transactions and persistence. Browser workflows use real pointer/keyboard/file input. Blur halo checks sample away from white selection handles rather than confusing the adorner with the effect. Independent 2× export, nonuniform device scale and combined DPI/zoom tests check the radius outside the original source edge; cache tests verify that scale-dependent convolution and transform-independent native filters retain their separate contracts. Software-backed Chromium results are not physical-GPU measurements.

Effect XML is limited to 16 KiB, Radius to 128 design units, Depth to 10000, absolute Direction to one billion degrees and Opacity to [0,1]. Separable convolution additionally limits each projected radius to 1023 device pixels (2047 taps), within the pinned Skia factory's 2048-sample axis limit. Exceeding this budget is explicitly diagnosed; reduce radius/zoom or suppress artboard effects rather than relying on a silently missing filter. This limit includes export density and high-DPI scaling. Large radius, deep nesting and large filtered subtrees still consume rendering resources. Unknown/custom/named effects, unresolved expressions, extra attributes and unsupported property-element metadata remain preserved and diagnosed; they are not executed or silently flattened.

Remaining work includes animated effect properties, arbitrary custom shaders, filter chains beyond nested objects, exact WPF transform/blur/coverage semantics, true RenderingBias implementations and matched Blend UI/theme/DPI qualification. No imported shader bytecode or control assembly is loaded.
