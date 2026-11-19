# C2: Windows pen and tablet input APIs (Huion support for a C#/.NET drawing app)

Researched 2026-11-19. Evidence labels: **[C]** = CONFIRMED (I read it in official docs or source), **[I]** = INFERRED (my reasoning or indirect evidence; check on hardware).
Raw page dumps: `RES/raw/c2/*.txt`. Source clones: `RES/src/qtbase` (qtbase commit 5ff08d37, sparse `src/plugins/platforms/windows`, `src/gui/kernel`), `RES/src/krita` (commit 93c5da5e) and extracted files in `RES/src/krita_files/`.
MS = https://learn.microsoft.com/en-us

---

## 0. Summary

- **Use Windows Ink (WM_POINTER) as the main path.** Add WinTab only as an optional fallback. Microsoft's API, Huion's driver, WPF, Qt 6 and Avalonia all centre on Windows Ink. Every API path needs Huion's driver checkbox "Enable Windows Ink" turned on. Driver 20.1.1.106 (2026-06) can turn it on or off per application [C].
- **Pressure resolution depends on the API.** `GetPointerPenInfo` normalises pressure to 0–1024 [C]. Huion pens report 8192 or 16384 levels [C]. For full resolution, read raw HID values with `GetRawPointerDeviceData` [C]. WPF's pointer stack already does this [C]. Qt and Avalonia go through `GetPointerPenInfo` ([C] for Qt, [I] for Avalonia).
- **WPF has two stylus stacks.**
  - The default is WISP/PenIMC. It gives you StylusPlugIns on a pen thread and `DynamicRenderer` on a separate thread.
  - The opt-in WM_POINTER stack (`Switch.System.Windows.Input.Stylus.EnablePointerSupport`) has no real-time inking. Its plug-ins run on the UI thread [C].
  - WPF is supported on .NET 10 LTS (until Nov 2028) and .NET 11 [C].
- **Side buttons and the eraser are unreliable.**
  - Huion's driver lets users remap pen keys to mouse buttons, keystrokes or "Toggle Pen/Eraser" [C].
  - Krita automatically turns on a "use mouse events for right/middle clicks" workaround when it falls back to Windows Ink [C].
  - Most Huion pens have no eraser end. PW600/PW600S (Kamvas Pro 19, Kamvas Pro 24 Gen 3, Kamvas Pro 27) have a "tail eraser" [C].
- **Latency is the biggest risk with WPF plus SkiaSharp.** Plan an early spike that measures latency with a high-speed camera on a Kamvas pen display (§9, §10).

---

## 1. Windows Ink / WM_POINTER (Win32, Windows 8+)

**Messages** [C]
- **WM_POINTERUPDATE (0x0245).** Sent while the pen hovers or is in contact. While in contact, the pointer is implicitly captured to the window it touched down on. ([MS/windows/win32/inputmsg/wm-pointerupdate](https://learn.microsoft.com/en-us/windows/win32/inputmsg/wm-pointerupdate))
- **WM_POINTERENTER (0x0249).** Sent when a pointer comes into detection range (hover) or crosses into a window.
- **WM_POINTERLEAVE (0x024A).** Sent when the pointer leaves range or the window. ([wm-pointerenter](https://learn.microsoft.com/en-us/windows/win32/inputmsg/wm-pointerenter), [wm-pointerleave](https://learn.microsoft.com/en-us/windows/win32/inputmsg/wm-pointerleave))
- **Pairing is not guaranteed.** Microsoft says not to assume DOWN/UP or ENTER/LEAVE arrive in pairs: after WM_POINTERCAPTURECHANGED you may get nothing more.
- **wParam macros:**
  - `IS_POINTER_INRANGE`, `IS_POINTER_INCONTACT`, `IS_POINTER_NEW`.
  - `IS_POINTER_FIRSTBUTTON`: the pen is in contact with no buttons pressed.
  - `IS_POINTER_SECONDBUTTON`: the pen is in contact with the barrel button pressed.
  - A hovering pointer has no button flags. To read hover button state, call `GetPointerPenInfo`.
- **Unhandled messages.** If you don't handle a pointer message, `DefWindowProc` may generate WM_GESTURE or mouse messages. If you consume some pointer input and pass the rest to `DefWindowProc`, the behaviour is "undefined".
- **Coalescing.** Moves are coalesced when the app can't keep up.
- **DPI.** Coordinates are physical; the app must be DPI-aware.

**POINTER_INFO** [C] ([ns-winuser-pointer_info](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-pointer_info))
- `pointerId` lives only while the pointer is in range. Leaving range and coming back gives a new id.
- `sourceDevice` is the device handle.
- `ptPixelLocation` and `ptHimetricLocation` are predicted for touch only. For pen they equal the raw values.
- `historyCount` is the number of coalesced inputs.
- `PerformanceCount` is a QPC timestamp (useful for measuring latency).
- `ButtonChangeType` reports button changes.

**POINTER_PEN_INFO** [C] ([ns-winuser-pointer_pen_info](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-pointer_pen_info))

| Field | Meaning |
|---|---|
| `pressure` | "Normalized to a range between 0 and 1024". Default 0 if the device has none. |
| `rotation` | Clockwise twist, 0–359. |
| `tiltX` | −90..+90; positive = tilted right. |
| `tiltY` | −90..+90; positive = tilted toward the user. |
| `penFlags` | BARREL 0x1, INVERTED 0x2, ERASER 0x4. |
| `penMask` | Which optional fields are valid: PRESSURE 0x1, ROTATION 0x2, TILT_X 0x4, TILT_Y 0x8. |

Sources: [pen-flags-constants](https://learn.microsoft.com/en-us/windows/win32/inputmsg/pen-flags-constants), [pen-mask-constants](https://learn.microsoft.com/en-us/windows/win32/inputmsg/pen-mask-constants).

- **Implication [I]:** A 16384-level Huion pen is quantised to about 1025 steps through this struct.

**Coalesced samples** [C]
- `GetPointerPenInfoHistory(pointerId, &count, POINTER_PEN_INFO*)` returns entries newest first. The newest entry equals `GetPointerPenInfo`.
- The data is only valid until the thread retrieves its next message. After that the call fails with ERROR_NO_DATA.
- It fails with ERROR_ACCESS_DENIED if the calling thread doesn't own the target window.
- `GetPointerFramePenInfoHistory` returns the 2-D (frame × pointer) version. `SkipPointerFrameMessages` exists but needs care.
- Sources: [getpointerpeninfohistory](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getpointerpeninfohistory), [getpointerframepeninfohistory](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getpointerframepeninfohistory).

**EnableMouseInPointer** [C]
- Can be called only once per process; later calls that try to change the state fail.
- Desktop apps default to *off*, unless they consume mshtml. ([enablemouseinpointer](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enablemouseinpointer))

**Device enumeration and raw data** [C]
- `GetPointerDevices` returns `POINTER_DEVICE_INFO`: device handle, `pointerDeviceType`, mapped `HMONITOR`, `productString`.
  - `INTEGRATED_PEN` = pen display. `EXTERNAL_PEN` = opaque tablet.
  - Sources: [getpointerdevices](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getpointerdevices), [pointer_device_type](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ne-winuser-pointer_device_type).
- `GetPointerDeviceProperties` returns `POINTER_DEVICE_PROPERTY`: logicalMin/Max, physicalMin/Max, unit, and the HID usage page/usage. This is where you read pressure resolution. ([getpointerdeviceproperties](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getpointerdeviceproperties), [pointer_device_property](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-pointer_device_property))
- `GetRawPointerDeviceData(pointerId, historyCount, propertiesCount, props, values)` returns raw HID values, including history. ([getrawpointerdevicedata](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getrawpointerdevicedata))
- WM_POINTERDEVICECHANGE also exists.
- **HID usages for pen** [C] ([required-hid-top-level-collections](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/required-hid-top-level-collections)):

| Usage (page 0x0D) | ID | Status |
|---|---|---|
| Tip Pressure | 0x30 | Optional, but required for HLK |
| X-Tilt | 0x3D | Optional |
| Y-Tilt | 0x3E | Optional |
| Twist | 0x41 | Optional |
| Barrel | 0x44 | Mandatory |
| Invert | 0x3C | Optional, but required for HLK |
| Eraser | 0x45 | Optional, but required for HLK |
| In-range | 0x32 | Mandatory |

- **Eraser state transitions** [C] ([windows-pen-states](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/windows-pen-states)):
  - Switching between "in range" and "in range with intent to erase" must go through out-of-range.
  - Tail erasers set Invert, then Eraser when in contact.
  - **App impact [I]:** Flipping the pen gives a leave and then a new pointer id.

**Press-and-hold, flicks and feedback** [C]
- **Opt out per region.** Handle `WM_TABLET_QUERYSYSTEMGESTURESTATUS` (0x02CC) and return flags such as `TABLET_DISABLE_PRESSANDHOLD` 0x1, `TABLET_DISABLE_PENTAPFEEDBACK` 0x8, `TABLET_DISABLE_PENBARRELFEEDBACK` 0x10, `TABLET_DISABLE_FLICKS` 0x10000.
- **Opt out per window.** `SetProp(hwnd, MICROSOFT_TABLETPENSERVICE_PROPERTY, flags)` (the "MicrosoftTabletPenServiceProperty" atom).
- Microsoft notes that disabling press-and-hold "will improve responsiveness". ([wm-tablet-querysystemgesturestatus-message](https://learn.microsoft.com/en-us/windows/win32/tablet/wm-tablet-querysystemgesturestatus-message))
- **Visual feedback.** `SetWindowFeedbackSetting(hwnd, FEEDBACK_PEN_PRESSANDHOLD / FEEDBACK_PEN_BARRELVISUALIZATION / FEEDBACK_PEN_TAP …, 0, sizeof(BOOL), &FALSE)`. ([setwindowfeedbacksetting](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowfeedbacksetting), [feedback_type](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ne-winuser-feedback_type))
- **Touch press-and-hold** (Windows 7+): use `SetGestureConfig` (Raymond Chen, [Old New Thing 2017-02-27](https://devblogs.microsoft.com/oldnewthing/20170227-00/?p=95585)).

**Low-latency ink facilities**
- **InkPresenter / InkCanvas (UWP).** Ink is collected on a "low-latency background thread" and drawn as "wet" ink, then "dried" on the UI thread. `ActivateCustomDrying` lets you dry it yourself through `IInkD2DRenderer` [C]. ([windows.ui.input.inking.inkpresenter](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.inking.inkpresenter))
- **Win32 hosting (Windows 10+).** `InkDesktopHost` / `IInkPresenterDesktop` inserts an InkPresenter into the app's **DirectComposition** visual tree [C]. ([input_ink/ink-presenter](https://learn.microsoft.com/en-us/windows/win32/input_ink/ink-presenter), [inkdesktophost](https://learn.microsoft.com/en-us/windows/win32/input_ink/inkdesktophost))
  - [I] From WPF this would need an HwndHost child window, with airspace limits.
  - [I] Wet ink uses the system ink renderer, not your Skia brush engine.
- **Delegated ink trail.** `IDCompositionDelegatedInkTrail` (dcomp.h; AddTrailPoints, AddTrailPointsWithPrediction, StartNewTrail) and WinRT `DelegatedInkTrailVisual` (Windows 10 build 20348+). The OS compositor draws the newest "wet" segment while the app renders "dry". Both pages are marked prerelease [C]. ([idcompositiondelegatedinktrail](https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nn-dcomp-idcompositiondelegatedinktrail), [delegatedinktrailvisual](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.delegatedinktrailvisual))
  - [I] Usable from Win32 with a DXGI swap chain plus DComp. Not usable inside WPF's own composition.

---

## 2. WinTab

**What it is** [C]
- An open industry API defined in the early 1990s by LCS/Telegraphics. Wacom maintains its own implementation.
- The DLLs ship with the tablet driver. Apps load `Wintab32.dll` dynamically.
- **Contexts.** Each app opens its own context: digitizer (WTI_DEFCONTEXT) or system (WTI_DEFSYSCTX). A context sets packet contents, tablet-to-screen mapping and messaging or polling.
- **Focus.** In Wacom's implementation an app gets data only while it has keyboard focus.
- Sources: [Wacom Wintab overview](https://developer-docs.wacom.com/docs/icbt/windows/wintab/wintab-overview/), [basics](https://developer-docs.wacom.com/docs/icbt/windows/wintab/wintab-basics/)

**Packet data** [C] ([Wacom reference](https://developer-docs.wacom.com/docs/icbt/windows/wintab/wintab-reference/))
- `PACKETDATA` bits: PK_X/Y/Z, PK_NORMAL_PRESSURE, PK_TANGENT_PRESSURE, PK_ORIENTATION, PK_ROTATION, PK_CURSOR, PK_BUTTONS, PK_TIME, PK_SERIAL_NUMBER, PK_STATUS (TPS_INVERT, TPS_PROXIMITY, TPS_QUEUE_ERR).
- **ORIENTATION:** `orAzimuth` (clockwise about z), `orAltitude` (signed angle to the surface), `orTwist`.
- **Ranges:** `WTInfo(WTI_DEVICES, DVC_NPRESSURE / DVC_ORIENTATION)` returns AXIS ranges and resolutions.
- **Messages and calls:** WT_PACKET, WT_PROXIMITY, WTPacketsGet, WTQueueSizeSet.
- **Cursor type and eraser:** Qt reads cursor type from `pkCursor`: index %3 gives 0 = puck, 1 = pen, 2 = eraser (`qwindowstabletsupport.cpp:352-363`) [C].
- **Huion's copy.** Huion's driver installs its own `wintab32.dll` (version 9.00.00.01) into System32/SysWOW64, and Huion support has a page on repairing it [C]. ([Huion: Issues with WinTab32 DLL](https://support.huion.com/en/support/solutions/articles/44002332146-issues-with-wintab32-dll-file), [SAI/old Photoshop article](https://support.huion.com/en/support/solutions/articles/44002379839))

**.NET use**
- Wacom's docs say ".NET Wintab support is built into the WintabDN.dll module". It is published as sample code in GitHub repo Wacom-Developer/wacom-device-kit-windows ("Wintab .NET") [C]. ([sc-wintab-net](https://developer-docs.wacom.com/docs/icbt/windows/sample-code/sc-wintab-net/))
- **License not verified.** github.com is blocked here, so I couldn't read the license.
- NuGet has only third-party repackages [C]: `WintabDN` 1.0.0.4 (author "Mathias"), `WacomSolutionPartner.WintabDotNet` 1.2.2 (fork, MIT link), `Apos.WintabDN` 0.0.1.
- **Plan [I]:** Write a small P/Invoke layer yourself (WTInfo/WTOpen/WTPacketsGet/WTQueueSizeSet, a custom PACKET struct), or vendor WintabDN after checking its license.

**Pros and cons versus Windows Ink**

WinTab pros:
- Device-native ranges (raw pressure) [C].
- Azimuth, altitude and twist; cursor type and serial number [C].
- Per-app mapping [C].
- The mature default for many painting apps (Krita still defaults to it, §6) [C].

WinTab cons:
- Only one WinTab driver can be installed at a time (Krita docs [C]). Huion's install replaces System32 `wintab32.dll` [C].
- Tablet-to-screen mapping offsets on multi-monitor setups (Krita has an "Advanced Tablet Settings for WinTab" dialog) [C].
- Krita warns that driver timestamps may "cause severe artifacts when using WinTab" (`kis_dlg_preferences.cc:1819`) [C].
- You write and maintain the P/Invoke yourself.
- Huion tells users to *disable* Windows Ink for WinTab apps, and vice versa [C]. ([Huion pressure FAQ](https://support.huion.com/en/support/solutions/articles/44002618549))
- Not available to UWP apps [I: Wacom support statement, page blocked].

---

## 3. WPF stylus stack

**API** [C] (System.Windows.Input, view windowsdesktop-10.0)
- **Events:** StylusDown/Move/Up, StylusInAirMove, StylusInRange/OutOfRange, StylusEnter/Leave, StylusButtonDown/Up, StylusSystemGesture, plus Preview variants. ([Stylus class](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.stylus?view=windowsdesktop-10.0))
- **StylusDevice:** InAir, InRange, Inverted ("secondary tip in use"), StylusButtons, TabletDevice, GetStylusPoints(IInputElement[, StylusPointDescription]). ([stylusdevice](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.stylusdevice?view=windowsdesktop-10.0))
- **TabletDevice.SupportedStylusPointProperties** lists what the device supports.
- **StylusPointProperties:** NormalPressure, X/YTiltOrientation, TwistOrientation, AzimuthOrientation, AltitudeOrientation, BarrelButton, TipButton, SecondaryTipButton, TangentPressure, Z, and more. ([styluspointproperties](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.styluspointproperties?view=windowsdesktop-10.0))
- **StylusPoint.PressureFactor** ranges 0..1. Its default is 0.5f when the device has no pressure (`StylusPoint.DefaultPressure`, [source.dot.net](https://source.dot.net/PresentationCore/System/Windows/Input/Stylus/Common/StylusPoint.cs.html)).
- **Ranges are device-defined.** The GetStylusPoints note shows NormalPressure ranges such as 0..1023 or 0..255 ([doc](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.styluseventargs.getstyluspoints?view=windowsdesktop-10.0)).
- **Coalescing:** each StylusMove carries a `StylusPointCollection` (all packets since the last event).
- **Attached properties:** `Stylus.IsPressAndHoldEnabled`, `IsFlicksEnabled`, `IsTapFeedbackEnabled`, `IsTouchFeedbackEnabled`.

**Threading and low latency** [C] ([The Ink Threading Model](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/the-ink-threading-model))
- Pen packets arrive on a **pen thread** (a pool).
- `StylusPlugIn`s see and can modify points there before the UI thread does. Hit-testing there is inaccurate, so use `NotifyWhenProcessed`.
- `DynamicRenderer` renders on a separate **dynamic rendering thread** "so the ink appears to flow even when the UI thread is busy". For mouse input it runs on the UI thread ([DynamicRenderer](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.stylusplugins.dynamicrenderer)).
- A plug-in exception terminates the app ([StylusPlugIns](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/intercepting-input-from-the-stylus)).
- WPF also has a separate composition/render thread ([threading model](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)). Its rendering tiers are DirectX 9-based ([rendering tiers](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/graphics-rendering-tiers)).

**Underlying stacks** [C] ([StylusLogic.cs](https://source.dot.net/PresentationCore/System/Windows/Input/Stylus/Common/StylusLogic.cs.html), [CoreAppContextSwitches.cs](https://source.dot.net/PresentationCore/MS/Internal/CoreAppContextSwitches.cs.html))
- **Default: `WispLogic`** (WISP/PenIMC).
- **`PointerLogic` (WM_POINTER)** is chosen only when all of these hold:
  - `Switch.System.Windows.Input.Stylus.EnablePointerSupport=true`, or the registry value HKCU\Software\Microsoft\Avalon.Touch\EnablePointerSupport is set.
  - The OS is Windows 10 RS2 (Creators Update) or later.
- **`Switch.System.Windows.Input.Stylus.DisableStylusAndTouchSupport`** turns the whole stack off.
- **How to enable:** `AppContextSwitchOverrides` in app.config ([mitigation doc](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/mitigation-pointer-based-touch-and-stylus-support)). [I] In .NET 10 you can also use `AppContext.SetSwitch` before WPF's input initialises, or `runtimeconfig.json`.
- **Documented limits of the pointer stack** [C]:
  - "No support for real-time inking"; plug-ins run on the UI thread.
  - Touch/stylus-to-mouse promotion changes.
  - Manipulation and drag/drop differences.
- **Raw pressure in the pointer stack.** It calls `GetRawPointerDeviceData` with `historyCount` over all device properties, so it gets raw HID ranges and coalesced history [C]. ([HwndPointerInputProvider.cs](https://source.dot.net/PresentationCore/System/Windows/InterOp/HwndPointerInputProvider.cs.html), [PointerData.cs](https://source.dot.net/PresentationCore/System/Windows/Input/Stylus/Pointer/PointerData.cs.html))

**Limitations [I unless marked]**
- No WinTab support at all. WPF only sees pens that the driver exposes as Windows Ink/HID digitisers. With Huion's "Enable Windows Ink" off, expect mouse-only input.
- WISP is legacy (the Tablet PC platform), but it is still the default [C].
- Stylus events are promoted to mouse events, so de-duplicate.

**Support status** [C]
- WPF runs only on Windows ([WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)).
- .NET 10 is LTS until Nov 2028; .NET 11 (STS, Nov 2026) is also supported until Nov 2028 ([releases-and-support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)).
- Supported OS for .NET 10/11: Windows 11 (23H2 Ent/Edu, 24H2, 25H2, 26H1), and Windows 10 only in LTSC/Enterprise editions (21H2, 1809, 1607) ([install/windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows)).

**SkiaSharp in WPF** [C]: `SkiaSharp.Views.WPF` has `SKElement` and `SKGLElement` (OpenGL) ([namespace](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.views.wpf)). The current package is 4.157.0 (NuGet).
- [I] SKElement renders with the CPU into a bitmap that WPF then composes. This adds a copy and at least one composition frame.

---

## 4. Avalonia (current NuGet 12.1.6; MIT license per nuspec [C])

- **Pen properties** [C] ([docs](https://docs.avaloniaui.net/docs/input/pointer), [API](https://docs.avaloniaui.net/api/avalonia/input/pointerpointproperties)):
  - `PointerType.Pen`.
  - `PointerPointProperties`: Pressure (0..1), XTilt/YTilt (degrees: left/up negative, right/down positive), Twist (degrees clockwise), IsEraser, IsInverted, IsBarrelButtonPressed, IsRightButtonPressed.
  - "Available on Windows, macOS, and Linux with X11."
  - `PointerEventArgs.GetIntermediatePoints(Visual)` returns the coalesced points ([API](https://docs.avaloniaui.net/api/avalonia/input/pointereventargs)).
- **Windows backend.** No Avalonia doc or release note says this; I checked the shipped `Avalonia.Win32.dll` (12.1.6) metadata instead [C]. It contains:
  - WM_POINTERDOWN/UP/UPDATE/ENTER/LEAVE/DEVICEINRANGE/…
  - `GetPointerPenInfo`, `GetPointerPenInfoHistory`, `GetPointerDeviceRects`, `EnableMouseInPointer`, `_wmPointerEnabled`, `s_historyPenInfos`, `MaxPointerHistorySize`.
  - No "wintab" strings.
- **[I] What that implies:**
  - Pen data comes from WM_POINTER `POINTER_PEN_INFO`, so pressure is quantised to 1024 steps.
  - Intermediate points come from the pointer history.
  - There is no WinTab path.
  - I could not verify when WM_POINTER is enabled (the source is on GitHub, which is blocked).

---

## 5. Qt 6 (qtbase dev @5ff08d37)

**API** [C] ([QTabletEvent](https://doc.qt.io/qt-6/qtabletevent.html))
- pressure 0..1, xTilt/yTilt (documented as −60..+60), rotation (degrees), tangentialPressure (−1..1), z, plus QPointingDevice / QEventPoint.
- **Outdated doc.** The "Notes for Windows Users" section still says wintab32.dll is required. The code contradicts this (below).
- **Compression.** `Qt::AA_CompressTabletEvents` defaults to false, and Windows 8+ is supported ([Qt namespace](https://doc.qt.io/qt-6/qt.html)).

**Windows plugin** [C]
- **Default is WM_POINTER.** `qwindowspointerhandler.cpp`:
  - :115-142 `GetPointerPenInfo`, plus `GetPointerPenInfoHistory` when compression is off.
  - :600 `GetPointerDeviceRects`, used to map HIMETRIC to sub-pixel position.
  - :612 `pressure = penInfo->pressure / 1024.0` (0.5 if the pressure mask is absent).
  - :637-642 `PEN_FLAG_ERASER|PEN_FLAG_INVERTED` → Eraser pointer type; `PEN_FLAG_BARREL` → RightButton *only while in contact*.
  - :715 returns false "Allow mouse messages to be generated".
- **WinTab is opt-in through a *private* API.** `QNativeInterface::Private::QWindowsApplication::setWinTabEnabled(bool)` (`src/gui/kernel/qguiapplication_p.h:430`; implementation `qwindowsapplication.cpp:68-76` → `QWindowsContext::initTablet()`, `qwindowscontext.cpp:265`). There is **no** `-platform windows:wintab` option.
- **Documented platform options:** `nowmpointer` (since 5.12) and `tabletabsoluterange=` (legacy WinTab mouse-mode detection) ([QGuiApplication](https://doc.qt.io/qt-6/qguiapplication.html)). The current `parseOptions` (`qwindowsintegration.cpp:137-190`) parses `tabletabsoluterange` but not `nowmpointer`, so the doc entry looks stale.
- **WinTab implementation** (`qwindowstabletsupport.cpp`):
  - :26 PACKETDATA = X|Y|BUTTONS|NORMAL_PRESSURE|TANGENT_PRESSURE|ORIENTATION|CURSOR|Z|TIME.
  - :33 queue size 128; :188 loads `wintab32`.
  - :222-224 tilt support detected through DVC_ORIENTATION.
  - :575-640 a heuristic for absolute versus mouse mode.
  - :660-680 converts azimuth/altitude to tiltX/tiltY; twist → rotation.

**License** [C]: commercial, LGPLv3, or GPL (some modules GPL-only) ([licensing](https://doc.qt.io/qt-6/licensing.html)).

---

## 6. Krita (Qt-based) on Windows

**User switch** [C] ([Tablet Settings](https://docs.krita.org/en/reference_manual/preferences/tablet_settings.html))
- "Tablet Input API" has two choices: **WinTab** (the default, "recommended for most Wacom tablets") or **Windows 8+ Pointer Input (Windows Ink)**. The Windows Ink option "depends on Windows Ink support from the tablet driver", is labelled "still … experimental", and is suggested for N-Trig/Surface or when WinTab works badly.
- Also on that page:
  - "Use Mouse Events for Right and Middle clicks", because some devices "don't tell us whether it has side buttons".
  - Driver timestamps for brush speed.
  - Tilt direction offset.
  - Advanced WinTab mapping (for multi-monitor offsets).
  - A Tablet Tester.
- [Drawing tablets](https://docs.krita.org/en/user_manual/drawing_tablets.html) says:
  - Only one WinTab driver can be installed at a time.
  - Razer utilities and Sandboxie interfere.
  - Broken or cheap drivers are a common cause of problems.
- The [Krita FAQ](https://docs.krita.org/en/KritaFAQ.html) says almost all Windows tablet failures it has seen came from buggy drivers.
- I found no Huion-specific troubleshooting on the docs.krita.org pages I read.

**Source** [C] (commit 93c5da5e)
- `krita/main.cc:663-669` (Qt 6): `setWinTabEnabled(!useWin8PointerInput)`.
- `main.cc:647-659` (Qt 5, using Krita's patched-Qt attribute `AA_MSWindowsUseWinTabAPI`).
- `main.cc:835-844`: if WinTab fails, Krita logs "Switching to WinInk…" and **turns on the right/middle-button mouse workaround**.
- `main.cc:847` turns `AA_CompressHighFrequencyEvents` off, so every sample is delivered.
- `libs/ui/kis_config.cc:1663-1666`: default `useWin8PointerInput=false`, so WinTab is the default.
- UI: `libs/ui/forms/wdgtabletsettings.ui:136-180`, `libs/ui/dialogs/kis_dlg_preferences.cc:1754-1819` (shows the protocol actually in use).
- Krita's old in-tree WinTab code no longer exists; only `libs/ui/input/wintab/drawpile_tablettester/` and `kis_incremental_average.h` remain.

---

## 7. WinUI 3 / Windows App SDK (brief)

- **`Microsoft.UI.Input.PointerPointProperties`** [C]: Pressure, XTilt, YTilt, Twist, Orientation, IsEraser, IsInverted, IsBarrelButtonPressed, IsInRange, IsRightButtonPressed… ([doc](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.input.pointerpointproperties)).
- **Coalesced points:** `PointerRoutedEventArgs.GetIntermediatePoints` [C].
- **InkCanvas / InkToolbar are experimental only and not in the stable channel** [C]. InkCanvas arrived in 2.0-experimental1, and InkPresenter inking in 2.4.1-experimental. For stable apps Microsoft says to use "Win2D with pointer input handling" (page updated 2026-10-14). ([what-is-supported](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/what-is-supported))
- **Win2D** is a GPU immediate-mode 2D WinRT API over Direct2D. It works in WinUI 3 and can "rasterize ink strokes" ([Win2D](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/)).
- [I] WinUI 3 also gets its pen data from Windows Ink, so it shares the same driver dependency.

---

## 8. Huion specifics (official huion.com / support.huion.com). Capabilities vary by model, pen and driver version.

**Windows Ink checkbox** [C]
- In the driver: Digital Pen → "Enable Windows Ink", which allows "Windows Ink functionality" ([V15 Driver Settings](https://support.huion.com/en/support/solutions/articles/44002356041)).
- Manual: when it is ticked, pen features work in Office, Whiteboard, Photoshop CC, SketchBook ([Inspiroy Dial 2 manual](https://www.huion.com/manual/inspiroy-dial-2/628)).
- Pressure FAQ: "Ensure your drawing software uses the same protocol as your tablet driver. If your drawing program uses WinTab32, disable Windows Ink… Restart the driver after changing protocols." "Software Diagnosis" can enable WinTab per app ([FAQ](https://support.huion.com/en/support/solutions/articles/44002618549)).

**Driver changelog** [C] ([update log](https://www.huion.com/download/driver-update-log))
- 20.1.1.106 (2026-06-09): "Independently enable or disable the 'Windows Ink' function for individual applications"; "Rename 'Switch Brush' to 'Toggle Pen/Eraser'"; adds a prompt to turn Windows Ink on before pressure testing.
- 20.0.1.118: Windows on ARM.
- 15.7.4.195: "When you cancel WinInk, the driver is elevated to administrator privileges".
- 15.7.6.485: pen eraser can be customised as other shortcuts.
- Several fixes for pen button "Right Click" in specific apps.

**Pressure** [C]
- Typical is 8192 levels (PenTech 3.0: PW110, PW550S).
- PenTech 4.0 pens (PW600/PW600S/PW600L/PW600A) have **16384 levels** and a "2g initial activation force".
- Older pens: 2048 (PEN68D) and 4096 (PW201).
- Sources: [Kamvas 16 Gen 3](https://www.huion.com/products/kamvas-16-gen-3), [Kamvas Pro 19](https://www.huion.com/products/kamvas-pro-19), [digital pens](https://www.huion.com/accessories/digital-pen).
- The driver applies its own pressure curve, a max-force setting and pressure calibration.

**Tilt** [C]
- **±60°** on current models. "A 60-degree angle refers to the angle from the pen being vertical to tilted."
- "Tilt support is only available in partial software."
- Tilt arrived on models released from July 2018; there is a list of models with tilt ([tilt FAQ](https://support.huion.com/en/support/solutions/articles/44001151825)).

**Report rate, hover and wireless** [C]
- Report rate ">260 PPS" on current models.
- Inspiroy Frego M: "Wired >300PPS, Bluetooth >133PPS".
- Sensing height 10–12 mm ([pen types](https://support.huion.com/en/support/solutions/articles/44002389269)).

**Barrel rotation / twist** [I]: no Huion page I read mentions pen rotation or twist. Assume `penMask` ROTATION is absent and verify on hardware.

**Side buttons** [C]
- Most pens have two programmable buttons; PenTech 4.0 pens have three side keys.
- Buttons are set in the driver (Digital Pen → Press Key), per app or globally, to mouse buttons, keyboard combinations, "Toggle Pen/Eraser" and so on.
- In V14 drivers "Mouse Button" is one of the function types ([per-app article](https://support.huion.com/en/support/solutions/articles/44001909702)).
- New in v20: "Contact click", where a pen button takes effect only while touching.
- **Implication [I]:** The app may see a native barrel flag, or a synthesized mouse right/middle click, or a keystroke.

**Eraser** [C]
- Most pens list only buttons.
- **PW600 / PW600S have a "Tail Eraser"** (Kamvas Pro 19, Kamvas Pro 24 Gen 3, Kamvas Pro 27). "You can set and change the eraser function of the pen within the driver" ([Kamvas Pro 24 Gen 3](https://www.huion.com/products/kamvas-pro-24-gen-3)).
- PW600L (Kamvas 13/16 Gen 3) and PW600A have no eraser mention.

**Compatibility advice** [C]
- The Huion article on Office lag says to turn on Windows "Press and hold for right-clicking" ([article](https://support.huion.com/en/support/solutions/articles/44002264330)).
- Huion repeatedly advises: "repair Wintab32", run the driver as administrator, reinstall the driver.

---

## 9. Practical implications for a C# app

1. **Main input path: Windows Ink.**
   - In WPF, either:
     - (a) use the default WISP stylus stack: StylusPlugIn on the pen thread, `StylusPointCollection` with device-range NormalPressure/tilt, and `Stylus.IsPressAndHoldEnabled/IsFlicksEnabled/IsTapFeedbackEnabled=false` on the canvas; or
     - (b) handle WM_POINTER yourself on a dedicated canvas HWND (HwndHost) with `GetPointerPenInfoHistory` + `GetRawPointerDeviceData` (full 8192/16384 pressure, QPC timestamps) and disable press-and-hold with WM_TABLET_QUERYSYSTEMGESTURESTATUS / SetProp.
   - [I] Option (b) also lets you render through a D3D11/DXGI swap chain (Skia GPU) or DelegatedInkTrail, bypassing WPF composition for the canvas.
   - Avoid WPF's `EnablePointerSupport` switch: it is documented to have no real-time inking [C].
2. **WinTab as an optional user setting**, like Krita does:
   - For users who must keep Windows Ink off for other apps, or whose driver is better on WinTab.
   - Load wintab32.dll dynamically; tolerate it being missing or broken; reopen the context after driver restart, sleep/wake or hot-plug [C: Wacom basics].
   - [I] Put it behind an abstraction (PenSample {pos, pressure raw/normalised, tiltX/Y, twist?, buttons, eraser, timestamp, source}).
3. **Buttons and eraser:**
   - Accept barrel input from `PEN_FLAG_BARREL`, WPF `StylusButtonDown`, **and** mouse right/middle clicks that arrive while the pen is in range. Copy Krita's workaround [C].
   - Accept "eraser" from `PEN_FLAG_INVERTED/ERASER`, `StylusDevice.Inverted`, WinTab cursor type, *and* a keyboard shortcut (the driver's "Toggle Pen/Eraser").
   - Expect a pointer leave and a new id when the eraser state changes [C/I].
4. **Pressure and tilt:**
   - Normalise using the device's logical range from `GetPointerDeviceProperties` (usage 0x0D/0x30), not a hard-coded 1024.
   - Expose the app's own pressure curve, and document that the driver applies a curve too.
   - Huion tilt is ±60° physically, while the API range is ±90.
   - Plan for twist being absent.
5. **Latency:**
   - Input: pen thread or `GetPointerPenInfoHistory`.
   - Rendering: WPF's composition thread (DX9-era, vsync-paced) adds latency [I]. `DynamicRenderer` only accelerates WPF's own stroke visuals, not a custom Skia brush.
   - SKElement adds a CPU→bitmap upload [I].
   - Prototype early: (i) WPF + SKElement, (ii) WPF + HwndHost + D3D11/Skia GPU swap chain (+ optional DelegatedInkTrail), and (iii) Avalonia (Skia-native, WM_POINTER, MIT) for comparison.
6. **DPI and multi-monitor:**
   - Declare Per-Monitor-V2 awareness.
   - Use HIMETRIC plus `GetPointerDeviceRects` for sub-pixel accuracy, as Qt does at `qwindowspointerhandler.cpp:600-608` [C].
   - Note that pointer coordinates are in physical pixels [C].

---

## 10. Prioritised hardware tests (real Huion devices, at least one PenTech 3.0 tablet and one PenTech 4.0 pen display with a PW600 tail-eraser pen)

Build a "pen probe" harness that logs, side by side:
- WM_POINTER (`GetPointerPenInfo` + History + `GetRawPointerDeviceData` + `POINTER_INFO.PerformanceCount`);
- WPF Stylus events (WISP);
- WinTab packets;
- mouse messages.

Use Krita's Tablet Tester as a reference.

**P0**
1. **Driver mode matrix.** Test "Enable Windows Ink" on/off globally and per app (driver ≥ 20.1.1.106), each against WM_POINTER, WISP, WinTab and mouse-only. Record which path delivers pen data, pressure, tilt and buttons. Also test after a driver restart.
2. **Pressure:**
   - Read logicalMax from GetPointerDeviceProperties (expect 8191 or 16383) and the WinTab DVC_NPRESSURE range.
   - Check quantisation of `POINTER_PEN_INFO.pressure`.
   - Measure minimum activation force (Huion claims 2 g) and force to reach max.
   - Check linearity with calibrated weights on a scale, with the driver curve at default and at "linear".
3. **Rates and coalescing.** Compare the device report rate (expect >260 PPS; Bluetooth >133) with app events per second. Measure `historyCount` per WM_POINTERUPDATE and points per WPF StylusMove, with the UI thread idle versus loaded. Check for dropped or duplicated samples and timestamp monotonicity.
4. **End-to-end latency:**
   - Film with a 240–960 fps camera on a Kamvas pen display at constant pen speed; measure the distance from the nib to the ink head and convert it to ms.
   - Cross-check QPC input time against present time (DXGI frame statistics).
   - Compare render variants (§9.5) at 60 Hz and at high-refresh displays (e.g. Kamvas Pro 27 144 Hz).
5. **Side buttons.** For each driver mapping (default, right click, middle click, keystroke, Toggle Pen/Eraser, "Contact click"), in Windows Ink mode and in WinTab mode, while hovering and while in contact, record which of these fire: `PEN_FLAG_BARREL`, `IS_POINTER_SECONDBUTTON`, `StylusButtonDown`, WM_RBUTTON/MBUTTON, WinTab pkButtons. Include the third key on PenTech 4.0 pens.
6. **Eraser (PW600/PW600S tail).** Check INVERTED/ERASER flags, `StylusDevice.Inverted`, the WinTab cursor index and TPS_INVERT, whether the pointer id changes or a leave/enter occurs, and the effect of the driver's eraser remap. On pens without an eraser, check the "Toggle Pen/Eraser" keystroke behaviour.

**P1**
7. **Tilt.** Check sign conventions (+X right, +Y toward the user), the maximum reported value (±60 versus the API's ±90), and step resolution. Compare WinTab azimuth/altitude converted to tilt with WM_POINTER values. Confirm twist/rotation is absent.
8. **Hover and proximity.** Check enter/leave on approach (about 10 mm), hover jitter, in-range events (WPF StylusInRange/InAirMove, WM_POINTERENTER/LEAVE, WinTab WT_PROXIMITY), and leaving the window while in contact (capture).
9. **System gestures.** With press-and-hold, flicks and tap feedback disabled, confirm there is no delay or ripple at pen-down, no right-click synthesis from hold, and no promoted-mouse duplicate strokes. Check how this interacts with Windows "press and hold for right-click".
10. **Multi-monitor and DPI.** Test a pen display and a main monitor at mixed scaling (100/150/200%), the driver mapped to one or all displays, screen rotation 0/90/180/270 (driver rotation), cursor-to-nib offset (Huion "tilt auto-alignment"), and WinTab mapping offset.

**P2**
11. **Hot-plug, sleep/wake, USB versus Bluetooth, driver crash or restart.** Check WM_POINTERDEVICECHANGE, device re-enumeration, and WinTab context reopen.
12. **Coexistence.** Test with a Wacom driver also installed (the wintab32.dll conflict), Razer-type utilities, and "Software Diagnosis → Repair Wintab32".
13. **Touch-enabled Kamvas Pro models.** Check palm rejection and that touch and pen are distinguished correctly.
14. **Focus and background behaviour.** Check WinTab data when the app is unfocused, and modal dialogs during a stroke.
15. **Windows on ARM** (driver 20.0.1.118 added ARM) if it is a target.

---

## 11. Gaps / unverified
- License and maintenance status of Wacom's WintabDN: github.com is blocked here.
- When Avalonia enables WM_POINTER (only binary metadata was checked).
- How Huion's wintab32.dll behaves (focus policy, button mapping).
- What Huion pens report in Windows Ink mode for barrel and eraser at the HID level. Must be measured.
- Wacom's support-site "pros/cons of Wintab vs Ink" page was blocked (Cloudflare 403).
