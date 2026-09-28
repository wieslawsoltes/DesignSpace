# Contributing

Keep platform-independent behavior in the portable libraries. A new workbench feature should not make Core or Engine depend on Uno, browser globals or the application host. Public controls should remain composable and disposable outside the full workspace.

Run both portable test executables and build the desktop application before opening a change. Browser UI changes must pass the published-WebAssembly interaction suite; include a screenshot from the verification artifacts when layout changes. Add a regression for fixes affecting persistence, history, source synchronization or identity mapping.

Use immutable document transformations through `DesignSession.Execute`. Validate before committing, preserve unapplied drafts, and do not introduce silent fallback that discards unsupported XAML semantics. Avoid fabricated progress metrics: distinguish CPU drawing time, GPU completion, presentation timing and elapsed startup time.

Document newly supported behavior in `docs/compatibility.md` and keep limitations visible. Do not add copied Microsoft icons, logos, proprietary source or fonts. Third-party packages must have suitable licenses and remain compatible with the pinned Uno/Skia dependency family.

Releases are development previews until compatibility and runtime qualification justify a stronger designation. CI creates packages and artifacts; package publication to a public feed requires an explicit separate decision.
