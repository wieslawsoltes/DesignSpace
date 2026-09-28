# DesignSpace

**A modular XAML design environment for desktop and the browser.**

DesignSpace is an independent Microsoft Blend-style workspace built with .NET 10 and Uno Platform. The project targets a familiar dense dark designer, shared Skia rendering, reusable controls, editable XAML, visual states and animation authoring.

> Development preview (0.1.0). This is not Microsoft Blend and does not claim complete Blend, WPF, WinUI or Visual Studio compatibility. See the compatibility documentation as implementation progresses.

## Libraries

Portable document, geometry, editing, layout and animation APIs have no dependency on the application. Uno controls and the workbench are separate packages. All original source is MIT licensed.

## Development

Install .NET 10 and run `dotnet run --project tests/DesignSpace.Tests -c Release` for the portable regression suite. Browser and desktop build instructions, package documentation and the deployed demo are being added alongside the application.

## Attribution

DesignSpace is not affiliated with or endorsed by Microsoft. Microsoft Blend, Visual Studio, WinUI and related names belong to their respective owners. No Microsoft product icons, logos, proprietary source or bundled fonts are redistributed.
