# DesignSpace

### A modular XAML design environment for desktop and the browser.

[![Build and test](https://github.com/wieslawsoltes/DesignSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/DesignSpace/actions/workflows/build.yml)
[![Browser and Pages](https://github.com/wieslawsoltes/DesignSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/DesignSpace/actions/workflows/pages.yml)

**[User guide](https://wieslawsoltes.github.io/DesignSpace/docs/) · [Browser designer](https://wieslawsoltes.github.io/DesignSpace/) · [Architecture](docs/architecture.md) · [Compatibility](docs/compatibility.md) · [Build artifacts](https://github.com/wieslawsoltes/DesignSpace/actions)**

DesignSpace is an independent Blend-style designer built with .NET 10 and Uno Platform. The same C# workbench runs on desktop and WebAssembly: compact dark tools, Assets, States, sample data, Objects and Timeline, a Skia artboard, editable XAML, brush resources and Properties.

> **0.1 development preview.** Not Microsoft Blend, not full or pixel-exact Blend parity. The application implements real editing workflows rather than a static browser mockup. Read the compatibility matrix before using production XAML.

## Design, inspect, animate

Create controls from Assets or draw shapes on the artboard. Select, move, resize, duplicate, align, reorder and group Canvas siblings. Edit properties with bounded undo/redo. Pan, zoom around the pointer, fit the artboard and use grid snapping. Resize panels, hide/restore them, or float them within the application.

Draw editable vector paths with **Pen (P)** and **Pencil (Y)**, then use **Direct Selection (A)** to move anchors and tangent handles. Insert and delete points, split contours by deleting segments, switch line/curve segments, or convert basic shapes to paths without losing object identity. **Paths** exposes Unite, Intersect, Subtract, Exclude, two-shape Divide, compound paths and geometric clipping. These commands produce editable vector data, not raster snapshots.

XAML drafts are isolated from the current document until explicitly applied. Invalid drafts remain editable, stale revisions are rejected, and named elements retain their identity during source reconciliation. Imported XAML is data: no assemblies or arbitrary markup extensions execute.

Create numeric keyframes for position, size, opacity and rotation. Scrub the original keyframe interval or configure delay, speed, auto-reverse, repeat count/duration/forever and HoldEnd/Stop playback in Timing. Duration changes can proportionally retime keys; shortening without scaling rejects excluded keys instead of dropping them.

In States, enable **Record state** to edit overrides without modifying base properties. Inspect effective values, reset an override and undo the change. Import embedded raster images, preview scoped styles and editable ControlTemplates, author brush resources, and resolve simple JSON sample-data bindings. Layout uses intrinsic Auto and constrained weighted Star Grid tracks; text shares HarfBuzz shaping and styled measurement across layout and drawing.

Save native `.designspace` files, export XAML or a 2× PNG, and recover the last local workspace. Recovery is local to the browser origin or desktop profile and is **not a backup**. Use Save for durable copies.

## Eight reusable libraries

| Package | Responsibility |
| --- | --- |
| `DesignSpace.Core` | Immutable documents, geometry, animation contracts and validation |
| `DesignSpace.Engine` | Editing transactions, selection, history and design-time layout |
| `DesignSpace.Animation` | Deterministic keyframes, clocks, easing and state overlays |
| `DesignSpace.Xaml` | Inert XAML codec, trim-safe native persistence and JSON sample data |
| `DesignSpace.Rendering.Skia` | Host-owned canvas rendering, viewport, adorners and PNG export |
| `DesignSpace.Docking.Uno` | Compact theme, tabs, splitters and floating panes |
| `DesignSpace.Controls.Uno` | Artboard, outline, properties, source, timeline/timing, states, templates, layout, assets, resources and data controls |
| `DesignSpace.Workbench.Uno` | Embeddable workbench and platform-service boundary |

Portable packages target .NET 10. Uno libraries target desktop and WebAssembly. The application is a separate thin host. CI generates packages; nothing is automatically published to NuGet.org.

```csharp
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Xaml;

var session = new DesignSession(DesignDocument.Empty());
session.Add("Button", new DRect(80, 120, 160, 44));
session.SetProperty("Content", "Get started");
session.Move(16, 0);
session.Undo();
string xaml = XamlCodec.Write(session.Document);
```

Embed the complete UI using `new WorkbenchView(yourPlatformServices)` or compose individual controls around a shared `DesignSession`. Call `EnableAdvancedTools()` to install Templates, Layout, Timing and Paths and the host-dependent image command; the app host supplies `IWorkbenchAssetPlatform` for image import.

## Build and run

Pinned dependency family: **Uno.Sdk 6.7.30**, Uno 6.7.135, **SkiaSharp 3.119.2**, .NET 10. Skia is kept compatible with Uno rather than mixing a newer incompatible major. `global.json` permits current .NET 10 feature bands.

```sh
dotnet run --project tests/DesignSpace.Tests -c Release
dotnet run --project tests/DesignSpace.Compatibility.Tests -c Release
dotnet run --project tests/DesignSpace.Rendering.Tests -c Release
dotnet run --project tests/DesignSpace.Vector.Tests -c Release

# Desktop (install the native prerequisites for the selected Uno host)
dotnet run --project src/DesignSpace.App -f net10.0-desktop \
  -p:DesignSpaceTargetFrameworks=net10.0-desktop

# Browser at http://localhost:8080/
dotnet workload install wasm-tools
dotnet publish src/DesignSpace.App -c Release -f net10.0-browserwasm \
  -p:DesignSpaceTargetFrameworks=net10.0-browserwasm -o artifacts/browser
python3 tools/prepare-pages.py artifacts/browser artifacts/site
python3 -m http.server 8080 --directory artifacts/site
```

## Rendering and verification

The renderer draws directly into the **host-owned Skia canvas**, sharing Uno's GPU-capable backend/fallback rather than uploading a fresh bitmap every frame. It caches fonts, paths, layout snapshots and scene indexes. Native vector paths use a bounded LRU cache; unchanged geometry reuses parsed commands and native paths. Exact bounds are computed without forcing hit-test tessellation. Path rendering and geometric clipping share data with picking, and Stretch does not scale stroke thickness. Weighted least-recently-used caches retain hot glyphs and wrapped lines rather than clearing every run on capacity overflow. Repainting a warm paragraph reuses line layout. Outline property updates retain unchanged item containers and selection. Grid work is bounded by the visible viewport. Edits invalidate rendering; animation uses the composition callback. It is not a separate WebGPU engine.

The status bar reports **CPU draw-submission duration**, not GPU completion or end-to-end presentation latency. Browser CI uses Chromium with software-backed WebGL/SwiftShader, not physical GPU benchmarks.

`build.yml` runs portable, compatibility and rendering suites, reruns persistence tests in a trimmed executable with reflection serialization disabled, packs portable libraries and compiles desktop targets on Windows, macOS and Linux. `pages.yml` requires the portable regressions and published Uno browser interactions before deployment, preserves screenshots/diagnostics and checks the public commit identity. `release.yml` tests and packs all eight dual-target/portable packages, verifies framework coverage and emits source/checksum artifacts. Changes to that workflow are exercised in PRs; only a separate tag-gated publication job can create a preview release. No automatic public NuGet publication occurs.

## License and attribution

Original source is [MIT licensed](LICENSE). Uno Platform, SkiaSharp, Skia, .NET and their transitive dependencies retain their own permissive licenses and notices. Browser typography reuses a framework-supplied font; Microsoft product fonts are not redistributed.

DesignSpace is not affiliated with or endorsed by Microsoft. No Microsoft proprietary source, product logos or icon assets are included. See [security boundaries](SECURITY.md) and [compatibility](docs/compatibility.md).
