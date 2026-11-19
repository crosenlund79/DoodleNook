# D — Rendering, UI framework, libraries & licensing for DoodleNook

Research date: 2026-11-19. Scope: rendering, UI framework, reusable libraries, licensing. Pen-input APIs are covered by another agent; pen remarks here are incidental.

Legend: **[C]** = CONFIRMED from a primary source I read (URL, NuGet metadata, package contents, or local file). **[I]** = INFERRED / my judgement. Treat all legal statements as engineering guidance, not legal advice; get a legal review before shipping.

Evidence notes: NuGet facts come from `api.nuget.org` registration/flat-container JSON and from unpacking the `.nupkg` (nuspec, LICENSE, XML docs, and assembly metadata read with `dnfile`). Local license facts come from `RES/gimp` (GIMP 3.2.8), `RES/gegl` (0.4.72), `RES/babl` (0.1.128) and `RES/gtk3/gdk`. github.com was not used.

---

## 0. Key findings (summary)

1. **SkiaSharp is current and permissive.** Version **4.157.0** (stable) was published 2026-11-18, and 4.159.0-rc.1 is out. License is MIT. The native library is built from **Skia m157** (the string "Skia/PDF m157" is in libSkiaSharp.dll), and Skia is BSD-3 [C]. The Windows native build contains the OpenGL, Vulkan and **Direct3D 12** Ganesh backends [C].
2. **The WPF integration is CPU-bound by default.** `SKElement` rasterizes on the CPU into a `WriteableBitmap` (Lock / BackBuffer / AddDirtyRect / Unlock, then DrawImage) [C]. The only GPU view, `SKGLElement`, wraps OpenTK.GLWpfControl. That control uses **WGL_NV_DX_interop + Direct3D9Ex + D3DImage** [C]. WPF falls back to *software* rendering for surfaces larger than the GPU's max texture size ("2048x2048 or 4096x4096" on most hardware) [C]. So the app must render only the viewport, never the whole document, into WPF [I].
3. **.NET timing.** .NET 8 (LTS) and .NET 9 reached end of support on **2026-11-10**. **.NET 10 LTS is supported to 2028-11-14**, and .NET 11 (STS) shipped 2026-11-10 [C]. Target .NET 10.
4. **Avalonia 12.1.6 (2026-11-09, MIT)** gives a GPU path for direct Skia drawing on Windows. It renders Skia over ANGLE→D3D11 by default, with WGL, Vulkan and Software modes [C]. `ICustomDrawOperation` + `ISkiaSharpApiLeaseFeature` expose `SKCanvas`, `SKSurface` **and `GRContext`** [C]. Caveat: Avalonia 12.1.6 pins **SkiaSharp 3.119.4**, not 4.x [C].
5. **Docking.** WPF has **Dirkster.AvalonDock 5.0.2 (Ms-PL)**, with XML/JSON layout serializers [C]. Xceed's Extended.Wpf.Toolkit 5.1.2 is under a **non-commercial, fewer-than-10-users community license**, so avoid it [C]. Avalonia has **Dock 12.1.5 (MIT)** with JSON serializers [C]. Qt has QDockWidget/QMainWindow saveState [C]. Qt add-ons: **KDDockWidgets (GPL-2.0/3.0 or commercial)** and **Qt-ADS (LGPL-2.1+)** [C].
6. **Qt 6.12** is current. Open-source use is under LGPLv3, and some modules are GPL-only, including the new "Qt Canvas Painter" [C]. LTS patch releases go to commercial customers first ("Qt 6.8.8 (LTS, commercial only)") [C]. Qt 6.12 is the last version to support Windows 10 [C].
7. **Krita is proof that Qt plus a custom engine works:** tiled engine in `libs/image/tiles3`, OpenGL canvas in `libs/ui/opengl` [C]. However, Krita master still defaults to **Qt 5.15**, and its build says Qt 6 is "not yet production-ready" [C].
8. **WinUI 3 / Windows App SDK 2.x** has short servicing windows. 2.0 shipped 2026-04-29 and is serviced until 2027-04-29, and 1.8 ended 2026-09-24 [C]. Win2D is Direct2D-based and supports virtual (tiled) bitmaps larger than the max texture size [C]. Its license could **not** be verified, because the NuGet license URL is a dead Microsoft EULA link.
9. **Libraries.** libmypaint (ISC) is a viable brush engine via P/Invoke, with Windows builds in MSYS2 and vcpkg [C]/[I]. GEGL/babl (LGPLv3+, plus 51 GPLv3+ GEGL ops) are a poor fit for .NET [I]. libvips/NetVips is good for import/export of huge images, not for painting [I]. Magick.NET (Apache-2.0) reads and writes PSD and reads ORA and XCF [C]. ImageSharp needs a paid license above US$1M revenue [C]. LittleCMS is MIT [C].
10. **GIMP licensing.** The GIMP app is GPLv3+, and libgimp* is LGPLv3+ [C]. Copying GIMP *code* into DoodleNook makes the distributed whole GPLv3+ [C]. Studying its architecture and implementing independently is a different situation: ideas are not copyrightable [C principle], but a legal review is advised [I]. The XCF spec text is licensed GPLv2+ as documentation [C].

---

## 1. SkiaSharp

**Version and license** [C]
- SkiaSharp **4.157.0**: stable, published 2026-11-18, `licenseExpression: MIT`, authors "Microsoft". 4.159.0-rc.1.26567.13 was published the same day. Source: https://api.nuget.org/v3/registration5-gz-semver2/skiasharp/index.json.
- Release history: 2.88.9 (2024-11) → 3.116 (2024-12) → 3.119.0 (2025-04) … 3.119.4 (2026-05-25) → **4.148.0 (2026-06-23)** → 4.157.0. There have been frequent 4.x releases since mid-2026.
- The nuspec sets `requireLicenseAcceptance: true` even though the license is MIT.
- LICENSE.txt in the nupkg is MIT (© Xamarin 2015-16, Microsoft 2017-18).
- Skia is under the "BSD Free Software License": https://skia.org/about/. SkiaSharp.NativeAssets.Win32's THIRD-PARTY-NOTICES.txt includes the Skia BSD-3 text, plus notices for ANGLE, HarfBuzz, libpng, libjpeg-turbo, libwebp, the gif decoder (wuffs), DNG SDK, piex, expat, freetype, ICU, zlib and others. vcpkg lists `skia` as BSD-3-Clause, port m148: https://vcpkg.io/en/package/skia.
- The native DLL contains the string "Skia/PDF m157". This suggests the 4.x minor number tracks the Skia milestone [C for 4.157; general rule I].

**Platforms** [C] (nupkg README and nuspec)
- OS targets: Windows, macOS, Linux, Android, iOS, Mac Catalyst, tvOS, Tizen, WebAssembly.
- Target frameworks: net6.0, net9.0, net10.0 (plus -windows10.0.19041, -android, -ios, etc.), netstandard2.0/2.1, net462, net48.
- Windows natives: win-x86, win-x64, win-arm64.

**WPF integration** (SkiaSharp.Views.WPF 4.157.0, MIT) [C]
- TFMs: net9/net10-windows10.0.19041, net462, net48.
- Dependencies: **OpenTK 4.3.0 and OpenTK.GLWpfControl 4.2.3** (OpenTK 3.3.x on .NET Framework).
- `SKElement`:
  - XML doc: "A visual element that can be drawn on using SkiaSharp drawing commands".
  - Assembly MemberRefs: `WriteableBitmap::.ctor/Lock/get_BackBuffer/get_BackBufferStride/AddDirtyRect/Unlock` and `DrawingContext::DrawImage`. So it is a CPU raster into a WriteableBitmap back buffer.
  - `IgnorePixelScaling` trades sharpness for speed by rendering at DIP size and stretching.
- `SKGLElement`:
  - XML doc: "uses OpenGL for hardware-accelerated SkiaSharp rendering"; it exposes `GRContext`.
  - It wraps `OpenTK.Wpf.GLWpfControl`.
  - Inspecting GLWpfControl 4.2.3 shows `D3DImage`, `D3DImage.SetBackBuffer/Lock/AddDirtyRect/Unlock`, `Wgl.DXOpenDeviceNV/DXRegisterObjectNV/DXLockObjectsNV`, and the strings `WGL_NV_DX_interop` and `Direct3DCreate9Ex`. So it is GL→D3D9Ex shared surface→D3DImage.
- No Direct3D-backed WPF view ships in SkiaSharp.
- The stale Microsoft.Wpf.Interop.DirectX (D3D11Image) package is still 0.9.0-beta (2015-11-04). Vortice.Direct3D9/11 3.8.3 (MIT, 2026-03-04) are current building blocks for DIY interop [C NuGet].

**What this means for large canvases** [I]
- A 4K viewport at RGBA8 is 3840×2160×4 ≈ 33 MB per full-frame update. That is CPU-rasterized, then copied back buffer→front buffer→video memory by WPF's render thread (WriteableBitmap remarks, [C]). Repainting the full surface every frame while painting means roughly 2 GB/s of memory traffic at 60 fps.
- Mitigations:
  - Keep the WPF bitmap viewport-sized.
  - Track dirty rectangles. The app has to own this; SKElement's PaintSurface model repaints the surface.
  - Composite only changed tiles.
  - Or use your own WriteableBitmap with `AddDirtyRect` on stroke bounds, or a GPU path:
    1. SKGLElement. Depends on driver support for NV_DX_interop.
    2. Custom D3DImage interop via a shared D3D9Ex surface. D3DImage only takes Direct3D9 surfaces [C]. Skia's D3D backend is D3D12 (`GRD3DBackendContext` has Adapter/Device/Queue), so you would need cross-API shared handles, which is complex.
    3. An `HwndHost` child swap chain. This is subject to WPF "airspace": each pixel belongs to exactly one HWND, so WPF content cannot overlay or alpha-blend over it [C]: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/technology-regions-overview.

**Painting-relevant API surface** [C], from the SkiaSharp.dll metadata and XML docs in the 4.157.0 nupkg:
- Raster types: `SKBitmap`, `SKPixmap`, `SKImage`, `SKSurface.Create` (raster or GPU), `PeekPixels`, `ReadPixels`.
- `SKColorType` covers 8-bit, F16, F32, 16-bit and 10-bit formats (Rgba8888, Bgra8888, RgbaF16, RgbaF32, Rgba16161616, Gray8, Alpha8, and others). This allows 8-bit or high-bit-depth layers.
- `SKBlendMode`, documented as Porter-Duff, Separable and Non-Separable:
  - Porter-Duff: Clear, Src, Dst, SrcOver, DstOver, SrcIn, DstIn, SrcOut, DstOut, SrcATop, DstATop, Xor, Plus, Modulate.
  - Separable: Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion, Multiply.
  - Non-separable: Hue, Saturation, Color, Luminosity.
  - This covers the Photoshop core set. There is no Linear Burn, Vivid Light, Pin Light or similar; those need custom `SKRuntimeEffect`/SkSL [I].
- Shaders: `SKShader.CreateLinearGradient` and other gradient/blend shaders.
- Sampling: `SKSamplingOptions` and `SKFilterMode.Nearest/Linear`. Nearest is what pixel-art zoom needs.
- Color spaces: `SKColorSpace.CreateIcc/CreateRgb/CreateSrgb/CreateSrgbLinear/ToProfile`.
- Codecs:
  - `SKEncodedImageFormat` enum: Bmp, Gif, Ico, Jpeg, Png, Wbmp, Webp, Pkm, Ktx, Astc, Dng, Heif, Avif, Jpegxl.
  - The Windows native exports **encoders for PNG, JPEG and WebP (including animated WebP)** (`sk_pngencoder_encode`, `sk_jpegencoder_encode`, `sk_webpencoder_encode[_animated]`).
  - Decoding goes through `SKCodec` using the bundled libpng, libjpeg-turbo, libwebp, wuffs (GIF), BMP/ICO/WBMP, and DNG/RAW (piex, DNG SDK) [C via notices].
  - HEIF/AVIF/JPEG XL are in the enum, but no libheif/libavif/libjxl appears in the Windows notices, so decode support on Windows is doubtful [I].
- GPU contexts: `GRContext.CreateGl/CreateVulkan/CreateDirect3D/CreateMetal`. Native exports `gr_direct_context_make_{gl,vulkan,direct3d,metal}` and contains `d3d12.dll`/`GrD3D*`, `vkCreate*` and `opengl32.dll` references. `GRBackend` enum: Metal, OpenGL, Vulkan, Dawn, Direct3D.
- **Not provided:** layered documents, undo, tiling or sparse storage, selection masks, brush engine, file formats with layers. The app must build all of these [C by absence; I].

**SkiaSharp.Views.WinUI 4.157.0** [C]: types `SKXamlCanvas` (CPU), `SKSwapChainPanel` and `AngleSwapChainPanel` (GPU via ANGLE). Depends on Microsoft.WindowsAppSDK 1.7.250909003.

---

## 2. WPF

- **Status** [C]:
  - Runs on .NET as an open-source implementation, but "WPF only runs on Windows": https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/.
  - Release notes: .NET 10 "What's new" (Nov 2025) covers perf work and Fluent style "still in progress": https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100. The .NET 11 RC1 page (Sep 2026) lists bug fixes only: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net110.
  - .NET lifecycle: .NET 8 LTS and .NET 9 both ended 2026-11-10. **.NET 10 (LTS) runs 2025-11-11 to 2028-11-14.** .NET 11 started 2026-11-10 as STS with 2-year support (to 2028-11-14). Sources: https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-and-net-core and https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core.
- **Rendering model** [C]:
  - "milcore is written in unmanaged code in order to enable tight integration with DirectX"; WPF is "a retained rendering system". Source: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-architecture.
  - Hardware tiers are defined against DirectX 9.0: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/graphics-rendering-tiers.
  - One render thread per application, with UI-thread/render-thread separation (WriteableBitmap remarks).
  - D3DImage hosts **Direct3D9** surfaces (IDirect3D9Ex on WDDM). Performance drops when the image moves to a monitor on another adapter: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-direct3d9-interoperation and https://learn.microsoft.com/en-us/dotnet/api/system.windows.interop.d3dimage.
- **WriteableBitmap** [C], from https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.writeablebitmap:
  - Two buffers, both in system memory. "The rendering system copies the front buffer to video memory."
  - Workflow is Lock → BackBuffer → write (other threads may write while locked) → AddDirtyRect → Unlock. "The rendering thread copies the changed rectangles from the back buffer to the front buffer."
  - So cost scales with dirty area, which favours tile-based dirty-rect painting [I].
  - Software fallback applies for "Surfaces that exceed the maximum texture size … 2048x2048 or 4096x4096" and for TileBrush tiling, RenderTargetBitmap and similar (tiers page).
- **High DPI** [C]:
  - WPF has framework-level per-monitor DPI scaling from Windows 10 1607; PMv2 is recommended (Win10 1703+): https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows.
  - Pixel-art scaling is available via `RenderOptions.BitmapScalingMode=NearestNeighbor`: https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.bitmapscalingmode.
- **Docking and theming** [C NuGet]:
  - **Dirkster.AvalonDock 5.0.2** (2026-11-12; 5.1.0-preview.149 on 2026-11-15): LICENSE file is **Ms-PL**. Packages include Themes (VS2013, Metro, Aero, Arc, and others), `Dirkster.AvalonDock.Serializer.Xml` / `.Serializer.Json` 5.0.2 for layout persistence, and Mvvm/DI packages.
  - **Extended.Wpf.Toolkit 5.1.2** (Xceed, 2026-06-12): "COMMUNITY LICENSE AGREEMENT (for non-commercial use)". Deployment is limited to fewer than 10 users, Xceed's name/logo must be visible, and competing products are barred. **Not usable** for a distributed or commercial app.
  - Theming: WPF-UI 4.3.0 (MIT, 2026-05-04), MaterialDesignThemes 5.4.0 (MIT, 2026-10-20), MahApps.Metro 2.4.11 (MIT, 2025-09; 3.0 RC), HandyControl 3.5.1 (MIT, last stable 2024-03).
  - Commercial suites (Syncfusion, Telerik, DevExpress, Actipro) also offer WPF docking [I, not verified].
- **Testing** [C NuGet]: FlaUI.Core/UIA3 5.0.0 (MIT, 2025-02-25) drives UI Automation. Appium.WebDriver 9.0.0 (2026-09-16). SkiaSharp offscreen `SKSurface` raster lets the engine's pixel tests run headless [I].

---

## 3. Avalonia 11/12

- **Versions and license** [C NuGet]: Avalonia, Avalonia.Skia and Avalonia.Win32 **12.1.6** (2026-11-09) are MIT. The 11.3.x line is still patched (11.3.23).
- **Commercial changes** [C]:
  - https://avaloniaui.net/pricing: "Avalonia — Free … MIT licensed UI framework".
  - Paid tiers: **Plus €299/yr/seat** (VS/VSCode extensions, DevTools, Parcel packaging, Hot Reload); **Pro €899/yr/seat** (Media Player, TreeDataGrid, Markdown, Rich Text, PDF viewer, charts, and more); **Enterprise from €21,000/yr**.
  - "Avalonia Accelerate has been retired, and is now part of Avalonia": https://avaloniaui.net/accelerate.
  - **Avalonia XPF** is a commercial "fork of WPF" with binary compatibility on Avalonia's renderer, sold as per-application licences: https://avaloniaui.net/xpf.
  - The core framework, Avalonia.Skia, Avalonia.Headless(.XUnit) 12.1.6 and Dock remain MIT.
- **Rendering on Windows** [C], from https://docs.avaloniaui.net/docs/platform-specific-guides/windows and Avalonia.Win32.xml:
  - Skia "on top of Direct3D 11, reached through the ANGLE translation layer". `Win32RenderingMode` = AngleEgl (default), Wgl, Vulkan, Software (fallback).
  - `Win32CompositionMode` = WinUIComposition (default), DirectComposition, LowLatencyDxgiSwapChain (FL 11_3, "reduced input latency"), RedirectionSurface.
  - PerMonitor DPI is the default.
  - Avalonia.Win32 depends on Avalonia.Angle.Windows.Natives.
  - **Avalonia.Skia 12.1.6 depends on SkiaSharp 3.119.4** (and HarfBuzzSharp 8.3.1.3). You cannot freely use SkiaSharp 4.x APIs alongside it [I risk].
- **Custom drawing** [C], from https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering and assembly metadata:
  - `ICustomDrawOperation.Render(ImmediateDrawingContext)` → `TryGetFeature<ISkiaSharpApiLeaseFeature>().Lease()` → `ISkiaSharpApiLease { SkCanvas, SkSurface, GrContext, TryLeasePlatformGraphicsApi }`.
  - `CompositionCustomVisualHandler` gives per-frame `SKCanvas` callbacks on the render thread.
  - `ICompositionGpuInterop` imports external GPU images (Windows: D3D11TextureNtHandle, VulkanOpaqueNtHandle).
  - `WriteableBitmap.Lock()` returns `ILockedFramebuffer`.
  - `BitmapInterpolationMode.None` supports pixel art. `BitmapBlendingMode` covers the same Porter-Duff and separable/non-separable set.
  - The `GrContext` lease means document tiles can stay as GPU textures in the *same* context as the UI. That avoids WPF's CPU→D3D9 copy chain [I].
- **Docking** [C NuGet]: **Dock.Avalonia 12.1.5** (Wiesław Šoltés, 2026-11-06), **MIT**, with Dock.Model.Mvvm and Dock.Serializer.SystemTextJson/Newtonsoft 12.1.5 for saved layouts.
- **Testing** [C], from https://docs.avaloniaui.net/docs/testing/:
  - Headless platform with xUnit/NUnit integration.
  - Visual regression via `.UseSkia()` + `UseHeadlessDrawing=false` + CaptureRenderedFrame.
  - Appium for end-to-end tests.
- **Pen (incidental)** [C]: `PointerPointProperties` has Pressure, XTilt, YTilt, Twist and IsEraser: https://docs.avaloniaui.net/docs/input-interaction/pointer.
- Cross-platform builds (macOS/Linux) remain an option [C docs]. Windows maturity is generally good, but there is less precedent for professional painting apps than with Qt [I].

---

## 4. Qt 6 (C++)

- **Version and licensing** [C], from https://doc.qt.io/qt-6/licensing.html (Qt 6.12):
  - Open-source terms are **LGPLv3** (or GPLv3), or a commercial license.
  - GPL-only modules include **Qt Canvas Painter**, Qt Graphs, Qt Quick 3D, Qt Virtual Keyboard, Qt Quick Timeline, Qt HTTP Server and others.
  - Tools are GPLv3 with Qt GPL exception 1.0, or commercial. Examples are BSD-3.
  - Release policy (https://doc.qt.io/qt-6/qt-releases.html): minor releases twice a year, and every 4th minor is LTS since 6.8. "Immediate access to LTS releases is limited to commercial customers." Supported: 6.12 (to 2031-09-30), 6.11 (to 2027-03-17), "6.8.8 (LTS, commercial only)".
  - Windows: Win10 1809+/Win11 with MSVC 2022/2026 or MinGW; **"Qt 6.12 will be the last version to support Windows 10"**: https://doc.qt.io/qt-6/windows.html.
- **LGPL obligations** as Qt states them [C], from https://www.qt.io/licensing/open-source-lgpl-obligations:
  - Deliver the complete corresponding source of Qt, even if unmodified, or a written offer.
  - Dynamic linking is recommended; static linking may make the app subject to the LGPL.
  - The user must be able to re-link and run the result, with installation information (no "tivoization").
  - Prominent notice and a copy of the LGPL.
  - Some app stores' terms may conflict.
- **Rendering** [C]:
  - QPainter paints on QImage, a "hardware-independent image … optimized for … direct pixel access". This can run "in another thread than the current GUI thread": https://doc.qt.io/qt-6/paintsystem-devices.html.
  - **QRhi** backends: Null, Vulkan, OpenGL(ES)2, **D3D11 (11.2+), D3D12 (Win10 1703+)**, Metal. Warning: QRhi has "limited compatibility guarantees … no source or binary compatibility guarantees": https://doc.qt.io/qt-6/qrhi.html.
  - `QRhiWidget` embeds Vulkan/Metal/D3D11/D3D12/GL rendering in a Widgets UI: https://doc.qt.io/qt-6/qrhiwidget.html.
  - Widgets vs Quick: "Qt Widgets are for creating complex desktop applications"; Qt Quick suits fluid/touch UIs: https://doc.qt.io/qt-6/topics-ui.html.
- **Docking** [C]:
  - `QDockWidget` (closable, movable, floatable) plus QMainWindow `AllowNestedDocks/AllowTabbedDocks/GroupedDragging` and `saveState()/restoreState()` (versioned blob; don't feed it untrusted data): https://doc.qt.io/qt-6/qmainwindow.html.
  - **KDDockWidgets 2.5.1**: vcpkg license `GPL-2.0-only OR GPL-3.0-only`. KDAB docs say commercial licensing is available on request: https://vcpkg.io/en/package/kddockwidgets, https://docs.kdab.com/kddockwidgets/2.0.0/index.html.
  - **Qt-Advanced-Docking-System 5.1.1**: `LGPL-2.1-or-later`: https://conan.io/center/recipes/qt-advanced-docking-system.
- **Testing** [C]: Qt Test supports data-driven tests, "mouse and keyboard simulation" and benchmarking: https://doc.qt.io/qt-6/qtest-overview.html. QImage painting enables headless pixel tests [I].
- **Krita as existence proof** [C], from invent.kde.org/graphics/krita:
  - The README describes it as a "free and open source digital painting application".
  - `libs/` contains brush, image, pigment (color), psd, ui, and other modules.
  - `libs/image/tiles3/` holds the tiled data manager: `kis_tiled_data_manager`, `kis_tile_data_store`, `kis_memento_manager` (undo mementos), and a `swap/` dir.
  - `libs/ui/opengl/` holds the OpenGL canvas with texture tiles (`kis_opengl_canvas2`, `kis_texture_tile`).
  - Source headers are `SPDX: GPL-2.0-or-later` (kis_tile.h).
  - **CMakeLists.txt: "Qt5 is the default"; Qt6 build "is not yet production-ready"** (MIN_QT_VERSION 5.15.0).
  - So Krita shows that Qt plus your own tiled engine and GPU canvas works at scale, but not yet on Qt 6 [C].
- **Pen (incidental)** [C]: QTabletEvent carries pressure, tilt, rotation and tangentialPressure. The doc page notes a Wacom `wintab32.dll` requirement in its tablet context: https://doc.qt.io/qt-6/qtabletevent.html. Defer to the pen agent.

---

## 5. WinUI 3 / Windows App SDK + Win2D (brief)

- **Status** [C]:
  - WinUI 3 is "the recommended native UI framework for building new Windows desktop applications" and supports Win10 1809+: https://learn.microsoft.com/en-us/windows/apps/winui/winui3/.
  - Windows App SDK **2.7.0** is Stable as of 2026-11-17.
  - Servicing: **2.0 released 2026-04-29, end of servicing 2027-04-29**. 1.8 went to maintenance and ended 2026-09-24. 1.7 is out of support: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels.
  - Short servicing means a forced upgrade treadmill [I].
- **License** [C]: Windows App SDK uses Microsoft Software License Terms, "solely for use on Windows". Redistribution requires adding significant functionality, passing protective terms on to users, and indemnifying Microsoft (license.txt in the Microsoft.WindowsAppSDK 2.7.0 nupkg).
- **Deployment** [C]: framework-dependent (default) or self-contained. Single-file exe is supported only for unpackaged self-contained apps (1.5+). Packaged (MSIX) and unpackaged options exist: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview.
- **Win2D 1.4.0** (NuGet 2026-03-16) [C]:
  - "immediate-mode 2D graphics rendering with GPU acceleration … utilizes the power of Direct2D".
  - Features include "virtual bitmaps, which can be larger than the maximum GPU texture size and are automatically split into tiles", "Rasterize ink strokes", blends and custom pixel shaders: https://learn.microsoft.com/en-us/windows/apps/develop/win2d/.
  - **License: unverified.** The nuspec licenseUrl `http://www.microsoft.com/web/webpi/eula/eula_win2d_10012014.htm` returns 403, the package contains no license file, and a Microsoft Q&A thread shows developers asking the same question.
- **Docking**: no first-party control; third-party or commercial only [I]. Testing: UI Automation via Appium/WinAppDriver-style tools [I].
- **Maturity concerns** [I]: rapid major-version churn, a packaging/runtime dependency, and a smaller ecosystem of complex desktop controls than WPF or Qt.

---

## 6. Other approaches (brief)

- **Aseprite**:
  - Proprietary EULA since v1.1.8 (2016-09-01). Source is available, but "you cannot redistribute compiled versions". It was previously GPLv2: https://dev.aseprite.org/2016/09/01/new-source-code-license/ and https://www.aseprite.org/faq/ [C].
  - Its devblog discusses linking Skia statically into its program: https://dev.aseprite.org/2018/06/27/skia-rtti-and-cpp-exceptions/ [C].
  - Its own UI toolkit "laf" (MIT, Skia backend) is described only on GitHub mirrors, which I did not use, so it is **unverified** [I].
  - It shows that a custom-drawn UI on Skia is a credible route for a pixel-art tool [I].
- **Dear ImGui** 1.93.0 is MIT (https://vcpkg.io/en/package/imgui) [C]. SDL3 3.4.20 is "Zlib AND MIT AND Apache-2.0 AND BSD-3-Clause" (https://vcpkg.io/en/package/sdl3) [C]. Good for tools and dev UIs. Accessibility, IME, native dialogs and docking-layout persistence are all build-it-yourself [I].
- **Electron / web canvas** (Photopea-style): not researched in depth. It adds browser memory limits and pen-latency layers; low priority for a Windows-native pen app [I].

---

## 7. Image-processing and painting libraries

| Library | License (verified where) | Version / date | Fit for DoodleNook |
|---|---|---|---|
| **libmypaint** | **ISC** [C] (MSYS2 https://packages.msys2.org/base/mingw-w64-libmypaint, Arch, vcpkg) | 1.6.1 (vcpkg 1.6.1#3; MSYS2 1.6.1-5) | C brush engine. Headers: `mypaint-brush.h`, `mypaint-surface.h`, `mypaint-tiled-surface.h`, `mypaint-fixed-tiled-surface.h` [C Arch file list]. vcpkg dependencies are **glib and json-c** [C]; optional GEGL surface lib `libmypaint-gegl` [C]. Used by GIMP 3.2.8 (`RES/gimp/meson.build` lines 539-541: libmypaint ≥1.5.0, mypaint-brushes-2.0) [C]. P/Invoke is feasible: a flat C API, a surface interface with callbacks, and tiles you can map onto your own tile store. You must ship glib, which is LGPL, via DLLs [I]. |
| **GEGL** | Library **LGPLv3+**; `gegl` binary/GUI **GPLv3+**; poly2tri-c BSD (`RES/gegl/docs/copyright.adoc`, `gegl/gegl.h`, `bin/gegl.c`) [C]. **51 operations in `operations/common-gpl3+` are GPLv3+** and are always built (`operations/meson.build` `subdir('common-gpl3+')`) [C] | 0.4.72 (MSYS2 0.4.72-2 "GPL-3.0-or-later AND LGPL-3.0-or-later") | GObject/C graph engine that needs glib/gobject/gio/json-glib (`RES/gegl/meson.build` 375-384) [C]. Heavy to bind from .NET; MSYS2/MinGW toolchain. You must *not* ship the GPL ops module with a non-GPL app [I]. **Poor fit** [I]. |
| **babl** | **LGPLv3+** (`RES/babl/COPYING`, `babl/babl.h`) [C] | 0.1.128 | Pixel-format conversion; only useful with GEGL [I]. |
| **libvips** / **NetVips** | libvips **LGPL-2.1-or-later** (https://www.libvips.org/) [C]; NetVips **MIT**; NetVips.Native **LGPL-3.0-or-later** (bundles glib, pango, libheif, librsvg and others under LGPLv3 "via any later version", all in one `libvips-42.dll`) [C nupkg notices] | NetVips 3.2.0 (2026-01-01), Native 8.18.7 (2026-09-28) | "Demand-driven, horizontally threaded", region-based [C]. Great for opening, exporting and thumbnailing very large images and for format breadth. **Not a painting engine** [I]. A replaceable DLL suits LGPL "suitable shared library mechanism" [I]. |
| **Magick.NET** / ImageMagick | Magick.NET **Apache-2.0** [C NuGet]. ImageMagick license allows commercial use and requires the license copy plus attribution (https://imagemagick.org/license/) [C] | 15.1.0 (2026-11-05) | Formats: **PSD RW** ("alpha blending, adjustment layers, opacity masks"), **ORA R**, **XCF R**, WebP RW (https://imagemagick.org/formats/) [C]. Large native footprint. Useful for import/export breadth [I]. |
| **ImageSharp** | **Six Labors Split License 1.0**: Apache-2.0 if the consuming software is open source, if it is a transitive dependency, or if you are a for-profit with **< US$1M annual gross revenue**, or a non-profit. Otherwise a commercial license (US$799–4,999/yr tiers) [C nupkg LICENSE; https://sixlabors.com/pricing/] | 4.1.3 (2026-10-09) | Fully managed; redundant with SkiaSharp. License risk if the business grows [I]. |
| **OpenCV / OpenCvSharp4** | OpenCvSharp4 **Apache-2.0** [C NuGet]. OpenCV 4.5.0+ Apache 2, ≤4.4 BSD-3 [C per opencv.org license page snippet; page blocked by bot-check] | OpenCvSharp4 4.13.0.20261107; vcpkg opencv4 4.14.0 | Filters such as blur, morphology, inpainting, and selection helpers. Large binaries [I]. |
| **LittleCMS** | **MIT** (https://www.littlecms.com/, vcpkg lcms 2.19.1) [C] | 2.19.1 | Full ICC transforms and soft-proofing. Skia covers ICC tagging and conversion for display [I]. |
| **OpenRaster (.ora)** | Spec v0.0.6 at https://www.openraster.org/ [C]. Spec license **not stated** on the pages I read | — | Zip with `mimetype` (stored, first) + `stack.xml` + `data/*.png` + required `Thumbnails/thumbnail.png` (≤256px) + required `mergedimage.png` [C]. Supported by Krita, GIMP and MyPaint [C]. Easy to implement natively [I]. NuGet: Olve.OpenRaster 0.63.2 (MIT, 2026-11-13) [C]. |
| **PSD for .NET** | Aspose.PSD 26.11.0 (commercial EULA, 2026-11-13) [C NuGet]; Magick.NET (above); Ntreev.Library.Psd 1.1.18134 (2018-05, stale; license only via a GitHub link, not verified) | — | No maintained permissive layered-PSD *writer* surfaced in a NuGet search, other than Magick.NET [I]. |
| **Aseprite .ase/.aseprite** | The spec lives at `docs/ase-file-specs.md` in the aseprite repo (GitHub; not read). **Spec text license not verified.** The program itself is under its EULA [C] | — | Independent readers exist: AsepriteDotNet 1.9.1 (MIT, 2026-04-03) [C NuGet]. Implementing the format from the spec is implementing a file format, not copying code. Write your own code [I + legal review]. |
| **Dirkster.AvalonDock** / **Dock** | Ms-PL / MIT [C] | 5.0.2 / 12.1.5 | Workspace layouts (see §2, §3). |

---

## 8. Licensing

### 8.1 What the local trees say [C]
- **GIMP 3.2.8** (`RES/gimp/LICENSE`, `COPYING`):
  - "The GIMP application core … [is] licensed under the GNU GENERAL PUBLIC LICENSE". `COPYING` is GPLv3. App headers say "version 3 … or (at your option) any later version" (`app/core/gimpimage.c`).
  - "'libgimp' and the other GIMP libraries are licensed under the GNU LESSER GENERAL PUBLIC LICENSE". `libgimp/COPYING` is LGPLv3; `libgimp/gimp.h` says LGPL v3 or later.
  - The LICENSE file also states GIMP's position that programs talking to GIMP through libgimp/PDB are "mere aggregation".
  - Icon themes: CC BY-SA 3.0/4.0.
  - Data (brushes, patterns, `data/`): new files "will be expected to be CC0". Older data has no clear licensing "for historical reasons".
  - In this clone, `data/` holds dynamics, gradients, palettes, tags, tips and tool-presets. Raster brushes/patterns live in the `gimp-data/` submodule, which is empty here, so I could not check per-file notices.
- **GIMP's dependencies** (`RES/gimp/meson.build`): babl, gegl-0.4, **gtk+-3.0 ≥3.24.0**, libmypaint ≥1.5.0, mypaint-brushes-2.0.
- **GEGL / babl**: see §7. GEGL's `COPYING` is GPLv3 and `COPYING.LESSER` is LGPLv3; babl's `COPYING` is LGPLv3.
- **GTK**: `RES/gtk3/gdk/gdkdevice.c` header is "GNU Lesser General Public License … version 2 … or (at your option) any later version", i.e. LGPL-2.1+.
- **XCF documentation**:
  - `devel-docs/xcf.txt` is not in 3.2.8. `devel-docs/README.md` says all developer docs moved to developer.gimp.org.
  - https://developer.gimp.org/core/standards/xcf/ has a License section: "free documentation; you can modify and/or redistribute it according to the terms of the GNU General Public License … version 2 … or … any later version".
  - The doc itself says XCF is a "living format", "the ultimate reference … is therefore its code". It recommends third-party use of XCF "only as a way to get data into and out of GIMP".

### 8.2 GNU license mechanics [C]
- **LGPLv3 §4 "Combined Works"** (https://www.gnu.org/licenses/lgpl-3.0.html; identical text in `RES/gegl/COPYING.LESSER`). You may convey a combined work "under terms of your choice that … do not restrict modification of the portions of the Library … and **reverse engineering for debugging such modifications**", provided you also:
  - (a) give prominent notice that the Library is used;
  - (b) include copies of the GPL and LGPL;
  - (c) include the library's copyright notice where the app shows notices;
  - (d) either (0) provide the Minimal Corresponding Source and the app in relinkable form, or (1) "use a suitable shared library mechanism" that works with a modified, interface-compatible library;
  - (e) provide **Installation Information** where GPL §6 requires it.
- **LGPL-2.1 §6** (https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html): terms must "permit modification of the work for the customer's own use and reverse engineering for debugging such modifications", along with a notice and a copy of the license.
- **GPL FAQ** (https://www.gnu.org/licenses/gpl-faq.html):
  - *LGPLStaticVsDynamic*: static linking means providing the app in object form for relinking. If you ship the LGPL library yourself, statically or dynamically, you must also convey its source.
  - *GPLStaticVsDynamic*: no difference; linking makes a combined work, and "the GPL cover[s] the whole combination".
  - *IfLibraryIsGPL*: the "work as a whole must be licensed under the GPL".
  - *GPLInProprietarySystem*: "You cannot incorporate GPL-covered software in a proprietary system". Separate programs must communicate "at arms length".
  - *MereAggregation*: modules "in the same executable" or "linked together in a shared address space" are almost surely one program; pipes, sockets and command lines are normally separate.
  - *WhatCaseIsOutputGPL*: program output is generally not covered. That matters for users' artwork, but bundled art assets carry their own license.
- **GPLv3 §0**: obligations attach to *conveying*, "any kind of propagation that enables other parties to make or receive copies": https://www.gnu.org/licenses/gpl-3.0.html.
- **Idea/expression principle**:
  - 17 U.S.C. §102(b): "In no case does copyright protection … extend to any idea, procedure, process, system, method of operation, concept, principle, or discovery": https://www.copyright.gov/title17/92chap1.html.
  - US Copyright Office Circular 33: protection "will extend only to the original expression … and not to the underlying idea, methods, or systems": https://www.copyright.gov/circs/circ33.pdf.
  - WIPO Copyright Treaty Art. 2: "Copyright protection extends to expressions and not to ideas, procedures, methods of operation or mathematical concepts as such": https://www.wipo.int/wipolex/en/text/295166.
  - The EU Software Directive 2009/24/EC Art. 1(2) is the relevant EU rule, but I could not fetch it, so it is **unverified**.

### 8.3 How this applies to DoodleNook [I — legal review recommended]
1. **Studying GIMP's or Krita's architecture** (tiling, undo design, docking concepts, menu conventions) and writing your own code is fine.
   - Takeaways such as "64×64 tiles", "memento undo" or "layer stack compositing" are ideas or methods.
   - Do not transcribe code, comments or distinctive structure. Keep notes at concept level.
2. **Clean-room practice** is good hygiene, but no official source mandates it.
   - Have people who read GPL source write specs, and people who have not read it implement them. Or at minimum, never paste or port GPL code, and keep provenance notes in the repo.
   - The GPL FAQ only tells you what triggers the GPL; it does not prescribe a clean-room process.
3. **Linking LGPL libraries** (GTK, glib, babl, GEGL lib, libvips, Qt under LGPL) from a proprietary or MIT app is allowed, if you:
   - keep them as replaceable DLLs;
   - ship notices and license texts;
   - provide or offer the library's corresponding source (including unmodified);
   - don't forbid reverse engineering for debugging;
   - provide installation information where relevant.
   - For .NET, P/Invoke to a separate DLL matches "suitable shared library mechanism". Avoid static or single-file native bundling of LGPL code.
4. **Incorporating GPL source** (GIMP app code, GEGL `common-gpl3+` ops, GEGL's GUI, KDDockWidgets under GPL, Krita code) into DoodleNook means the distributed whole must be GPL-compatible and licensed GPLv3+. That includes Krita's GPL-2.0-or-later code, which is usable only under GPLv3 in combination [C via FAQ; application I].
   - Loading GPL plugins into the same process counts as linking per *MereAggregation*.
5. **Forking GIMP** means DoodleNook is GPLv3+ and C/GTK3/GEGL-based.
   - You would inherit GIMP's architecture and UI toolkit, not a C#/WPF stack.
   - It is commercially allowed (sell + provide source), but it conflicts with the C# stack plan.
6. **Reading the XCF or ORA specs to write your own reader/writer** implements a format (a system or method) [principle C; application I].
   - Do not copy the GPLv2+ XCF document text into your code or docs.
   - Do not port `app/xcf/` code.
   - XCF is a moving target; prefer ORA or PSD for interchange.
7. **GIMP brushes, patterns and data**: new data is intended CC0, but the historical data has no clear licence (GIMP's own LICENSE says so). Don't bundle GIMP data unless each file's license is clear [I].
8. **Avoid** the Xceed Extended.Wpf.Toolkit, because of its non-commercial terms.
   - ImageSharp is free only below US$1M revenue.
   - Win2D's license is unverified.
   - Qt LGPL is workable but carries real obligations, and some modules are GPL-only.

---

## 9. Comparison matrix

Judgement cells are [I] unless marked [C].

| Criterion | (1) C#/.NET 10 + WPF + SkiaSharp | (2) C# + Avalonia 12 (+ its Skia) | (3) C++ + Qt 6 | (4) C# + WinUI 3 + Win2D | (5) C++ + custom (SDL3/ImGui/Skia) |
|---|---|---|---|---|---|
| **Windows support** | Windows-only [C]; .NET 10 LTS to 2028-11 [C]; WPF in maintenance mode, small yearly changes [C what's-new] | Win32-based, Win10+ composition modes [C]; also macOS/Linux | Win10 1809+/11, MSVC/MinGW [C]; **6.12 is the last with Win10** [C] | Win10 1809+ [C]; WinAppSDK 2.x serviced ~1 yr [C] | Whatever you build; SDL3/Skia support Win10/11 [C skia.org] |
| **Pen input** | *Placeholder — see pen agent.* (WPF stylus stack not reviewed here) | Pressure, tilt, twist, eraser in PointerPointProperties [C] | QTabletEvent pressure/tilt/rotation [C] | Pointer API (pen agent) | SDL3 pen API (pen agent) |
| **Drawing performance** | Skia CPU raster fast; bottleneck is the WriteableBitmap→D3D9 copy and software fallback over max texture size [C mechanism] | Skia on GPU via ANGLE/D3D11 by default [C]; GRContext lease allows GPU tiles | QPainter raster on QImage (multi-thread) [C]; GPU via QRhi/GL (Krita uses GL) [C] | Direct2D GPU [C]; effects pipeline | Best ceiling, most work |
| **Canvas rendering (large images, zoom/pan, GPU)** | Viewport-sized bitmap + own tile cache + dirty rects. GPU only via SKGLElement (NV_DX_interop) or custom D3DImage/HwndHost (airspace) [C] | ICustomDrawOperation / CompositionCustomVisual with Skia GPU surface; nearest sampling [C] | QRhiWidget/QOpenGLWidget + own tile textures; Krita proves it [C] | CanvasVirtualControl/virtual bitmaps auto-tile beyond max texture [C] | Own swap chain; full control |
| **Raster layers (given vs build)** | Given: SKSurface/SKBitmap, 29 blend modes, F16/F32 colour types, ICC tagging [C]. Build: document model, tiles, undo, selections, brush engine, file I/O | Same as (1) (Skia 3.119), plus Avalonia BitmapBlendingMode [C] | Given: QImage, QPainter composition modes. Build the rest (as Krita did, ~tiles3) | Given: D2D blend/effects. Build the rest | Everything |
| **UI flexibility (docking/custom workspaces)** | AvalonDock (Ms-PL) + XML/JSON layout serializers [C]; rich WPF control ecosystem | Dock (MIT) + JSON serializers [C]; younger ecosystem | QDockWidget + saveState [C]; KDDW (GPL/commercial), ADS (LGPL-2.1+) [C] | No first-party docking; weakest | Build your own (ImGui docking ≠ Photoshop-grade) |
| **Dev complexity** | Low–medium; mature tooling; GPU path is the hard part | Medium; XAML is WPF-like; good GPU path | High (C++, CMake, Qt deploy, LGPL compliance) | Medium; packaging and runtime friction | Very high |
| **Testing** | FlaUI 5 (UIA3, MIT) / Appium [C]; headless Skia surface pixel tests | Headless platform + xUnit/NUnit + Skia frame capture + Appium [C] | Qt Test (mouse/keyboard sim, data-driven, benchmarks) [C]; QImage pixel tests | UIA-based tools; offscreen D2D targets | Self-built |
| **Dependencies & long-term risk** | WPF Windows-only with modest investment; SkiaSharp fast-moving 4.x; OpenTK dep for GL view [C] | Single vendor plus paid add-on business (core MIT) [C]; pinned to older SkiaSharp [C] | Qt Company policy shifts (LTS commercial-first) [C]; Win10 drop after 6.12 [C] | Microsoft churn (short servicing) [C]; Win2D license unclear | Bus factor = you |
| **Licensing** | MIT (SkiaSharp), BSD-3 (Skia), WPF part of .NET; AvalonDock Ms-PL. Clean | MIT core + Dock MIT. Clean | LGPLv3 obligations or commercial; GPL-only modules; KDDW GPL | WinAppSDK MS terms (Windows-only, indemnity) [C]; Win2D unverified | MIT/Zlib/BSD (ImGui, SDL3, Skia) |

---

## 10. Implications and recommendations [I]

- **WPF + SkiaSharp is viable** if the architecture assumes WPF is only the chrome and the canvas is a thin presenter:
  - Keep the document in your own tiled store (e.g. 256² tiles, sparse, copy-on-write for undo).
  - Composite only visible tiles at the current zoom into a viewport-sized buffer.
  - Upload only dirty rects.
  - Prototype early: (a) a WriteableBitmap with stroke-bounded `AddDirtyRect` at 4K/120 Hz, and (b) the SKGLElement path on Intel, AMD, NVIDIA and RDP. If both are inadequate, switch to Avalonia (GPU lease) or a D3D interop layer.
- **Avalonia** is the strongest C# alternative for the canvas: a real GPU Skia path, MIT docking, headless visual tests, and optional macOS/Linux. The main costs are a smaller ecosystem and dependence on Avalonia's SkiaSharp version.
- **Qt** is the proven route for professional painting apps (Krita), but it means C++, LGPL compliance or a commercial license, and Krita itself has not finished moving to Qt 6.
- **WinUI 3** is not recommended as the primary framework: servicing churn, no docking, and Win2D licensing unclear.
- **Library picks**:
  - SkiaSharp for compositing and codecs.
  - Your own ORA reader/writer (simple, Krita/GIMP-compatible).
  - Magick.NET for PSD/XCF import and PSD export breadth.
  - Optionally libmypaint via P/Invoke for natural-media brushes, shipping glib/json-c as separate DLLs with LGPL notices.
  - Optionally NetVips for huge-image import/export.
  - LittleCMS only if a print/CMYK workflow is needed.
- **Licensing guardrail**: no GIMP, Krita or GEGL-GPL code in the repo; LGPL components only as replaceable DLLs; a THIRD-PARTY-NOTICES file from day one. Get a lawyer to check the clean-room process and the LGPL compliance (source offer, relinking).

### Unverified or open items
- Win2D license terms (EULA URL dead).
- Aseprite spec-text license and laf licensing (GitHub only).
- OpenRaster spec license.
- EU Directive 2009/24/EC text (not fetched).
- Whether SkiaSharp's Windows native build decodes HEIF/AVIF/JXL.
- How SKElement chooses its dirty rect (it calls AddDirtyRect; extent not inspected).
- WPF stylus/WinUI pointer details (pen agent).
