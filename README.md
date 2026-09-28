# DesignSpace

### A modular XAML design environment for desktop and the browser.

[![Build and test](https://github.com/wieslawsoltes/DesignSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/DesignSpace/actions/workflows/build.yml)
[![Browser and Pages](https://github.com/wieslawsoltes/DesignSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/DesignSpace/actions/workflows/pages.yml)

**[Browser designer](https://wieslawsoltes.github.io/DesignSpace/) · [Architecture](docs/architecture.md) · [Compatibility](docs/compatibility.md) · [Build artifacts](https://github.com/wieslawsoltes/DesignSpace/actions)**

DesignSpace is an independent Blend-style designer built with .NET 10 and Uno Platform. The same C# workbench runs on desktop and WebAssembly: compact dark tools, Assets, States, sample data, Objects and Timeline, a Skia artboard, editable XAML, brush resources and Properties.

> **0.1 development preview.** Not Microsoft Blend, not full or pixel-exact Blend parity. The application implements real editing workflows rather than a static browser mockup. Read the compatibility matrix before using production XAML.

## Design, inspect, animate

Create controls from Assets or draw shapes on the artboard. Select, move, resize, duplicate, align, reorder and group Canvas siblings. Edit properties with bounded undo/redo. Pan, zoom around the pointer, fit the artboard and use grid snapping. Resize panels, hide/restore them, or float them within the application.

XAML drafts are isolated from the current document until explicitly applied. Invalid drafts remain editable, stale revisions are rejected, and named elements retain their identity during source reconciliation. Imported XAML is data: no assemblies or arbitrary markup extensions execute.

Create numeric keyframes for position, size, opacity and rotation. Scrub, play, move keys and choose linear, discrete or cubic easing. Create and preview states, capture selected property values, author brush resources and preview simple bindings against JSON sample data.

Save native `.designspace` files, export XAML or a 2× PNG, and recover the last local workspace. Recovery is local to the browser origin or desktop profile and is **not a backup**. Use Save for durable copies.

## Eight reusable libraries

| Package | Responsibility |
| --- | --- |
| `DesignSpace.Core` | Immutable documents, geometry, animation contracts and validation |
| `DesignSpace.Engine` | Editing transactions, selection, history and design-time layout |
| `DesignSpace.Animation` | Deterministic keyframes, easing and state overlays |
| `DesignSpace.Xaml` | Inert XAML codec, trim-safe native persistence and JSON sample data |
| `DesignSpace.Rendering.Skia` | Host-owned canvas rendering, viewport, adorners and PNG export |
| `DesignSpace.Docking.Uno` | Compact theme, tabs, splitters and floating panes |
| `DesignSpace.Controls.Uno` | Artboard, outline, assets, properties, source, timeline, states, resources and data controls |
| `DesignSpace.Workbench.Uno` | Embeddable workbench and platform-service boundary |

Portable packages target .NET 10. Uno libraries target desktop and WebAssembly. The application is a separate thin host. CI generates packages; nothing is automatically published to NuGet.org.

```csharp
var session = new DesignSession(DesignDocument.Empty());
session.Add("Button", new DRect(80, 120, 160, 44));
session.SetProperty("Content", "Get started");
session.Move(16, 0);
session.Undo();
string xaml = XamlCodec.Write(session.Document);
```

Embed the complete UI using `new WorkbenchView(yourPlatformServices)` or compose individual controls around a shared `DesignSession`.

## Build and run

Pinned dependency family: **Uno.Sdk 6.7.30**, Uno 6.7.135, **SkiaSharp 3.119.2**, .NET 10. Skia is kept compatible with Uno rather than mixing a newer incompatible major. `global.json` permits current .NET 10 feature bands.

```sh
dotnet run --project tests/DesignSpace.Tests -c Release
dotnet run --project tests/DesignSpace.Compatibility.Tests -c Release

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

The renderer draws directly into the **host-owned Skia canvas**, sharing Uno's GPU-capable backend/fallback rather than uploading a fresh bitmap every frame. It caches fonts, paths, layout snapshots and scene indexes. Grid work is bounded by the visible viewport. Edits invalidate rendering; animation uses the composition callback. It is not a separate WebGPU engine.

The status bar reports **CPU draw-submission duration**, not GPU completion or end-to-end presentation latency. Browser CI uses Chromium with software-backed WebGL/SwiftShader, not physical GPU benchmarks.

`build.yml` runs portable and compatibility suites, reruns persistence tests in a trimmed executable with reflection serialization disabled, packs portable libraries and compiles desktop targets on Windows, macOS and Linux. `pages.yml` publishes and tests the actual Uno application before deployment, preserving screenshots and diagnostics. `release.yml` produces all eight dual-target/portable packages and source archives; tagged builds create preview releases. No automatic public NuGet publication occurs.

## License and attribution

Original source is [MIT licensed](LICENSE). Uno Platform, SkiaSharp, Skia, .NET and their transitive dependencies retain their own permissive licenses and notices. Browser typography reuses a framework-supplied font; Microsoft product fonts are not redistributed.

DesignSpace is not affiliated with or endorsed by Microsoft. No Microsoft proprietary source, product logos or icon assets are included. See [security boundaries](SECURITY.md) and [compatibility](docs/compatibility.md).
