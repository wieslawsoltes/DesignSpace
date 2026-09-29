# Third-party notices

Original DesignSpace code is MIT licensed. Upstream components retain their own copyright and license notices; this file is an index, not a replacement for their licenses.

| Component | Role | Upstream |
| --- | --- | --- |
| Uno Platform / Uno.Sdk | Cross-platform application, input, composition and controls; Apache-2.0 | https://github.com/unoplatform/uno |
| SkiaSharp | .NET Skia bindings; MIT | https://github.com/mono/SkiaSharp |
| Skia | Raster/vector graphics engine; BSD-style | https://skia.googlesource.com/skia/ |
| .NET runtime and libraries | Managed application runtime; upstream .NET licenses | https://github.com/dotnet/runtime |
| Uno.Fonts.OpenSans | Framework-provided browser typography; retain package font notices | https://github.com/unoplatform/uno |
| Playwright | Browser verification; Apache-2.0 | https://github.com/microsoft/playwright |
| pngjs | Test screenshot decoding; MIT | https://github.com/pngjs/pngjs |

The Uno/Skia packages may include additional native/transitive notices. Inspect the restored package graph and packaged license files when redistributing a build. DesignSpace does not replace these obligations or provide legal certification.

Microsoft Blend, Visual Studio, WPF and WinUI are referenced descriptively. DesignSpace is not affiliated with Microsoft and does not bundle Microsoft's proprietary product source, logos, icon assets or font files.


## WPF-compatible animation mathematics

`DesignSpace.Animation/WpfKeySplineSampler.cs` adapts the numerical portion of
[dotnet/wpf KeySpline.cs](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Animation/KeySpline.cs).
`DesignSpace.Core/EasingCurve.cs` also adapts the built-in Back, Bounce, Elastic,
Exponential, Power, polynomial, Circle and Sine easing mathematics from the same
[dotnet/wpf animation directory](https://github.com/dotnet/wpf/tree/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Animation).
These are portable mathematical algorithms, not a copied Microsoft Blend interface or proprietary asset.
The following upstream license is retained in source and every DesignSpace NuGet package.

The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
