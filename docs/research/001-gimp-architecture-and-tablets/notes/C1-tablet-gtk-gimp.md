# C1 — Drawing-tablet input on Windows in GIMP 3.2.8 + GTK 3.24.52

Sources: `gimp:` = GIMP tag GIMP_3_2_8 (7addd2b7). `gtk3:` = GTK 3.24.52 (6a0b360d). Paths below are relative to each repo root.
Labels: **CONFIRMED** = read in source. **INFERRED** = deduced from code or from Windows/driver behaviour that the code assumes but does not show. Layer tags: (a) GIMP, (b) GTK/GDK, (c) Windows API, (d) tablet driver.

---

## 1. Which APIs, how one is selected, GIMP's default

**(b) GTK 3.24 supports two tablet back-ends, plus "none". CONFIRMED.**
- The enum is `GDK_WIN32_TABLET_INPUT_API_NONE / _WINTAB / _WINPOINTER` (gtk3:gdk/win32/gdkprivate-win32.h:167-169). The global is `_gdk_win32_tablet_input_api` (gtk3:gdk/win32/gdkglobals-win32.c:39).
- The only selector is the environment variable `GDK_WIN32_TABLET_INPUT_API`, which takes `none|wintab|winpointer`. It is read in `gdk_device_manager_win32_constructed` (gtk3:gdk/win32/gdkdevicemanager-win32.c:1486-1506). GTK3 has no public `gdk_win32_display_set_*` call for this; `gdkwin32display.h` only exposes `set_cursor_theme` (gtk3:gdk/win32/gdkwin32display.h:54).
- **Default is WINPOINTER (Windows Ink).** If the variable is unset, the API is set to WINPOINTER (line 1505). If `winpointer_initialize()` then fails, GTK falls back to WINTAB, but only when no explicit preference was given (lines 1508-1513). `winpointer_initialize` fails only when user32 lacks the pointer functions (Windows < 8) or the notification window cannot be created (lines 676-704, `winpointer_ensure_procedures` 621-674).
- WinTab is initialised lazily, after the default display exists (`wintab_default_display_notify_cb`, lines 1406-1433). The reason given is that `WTOpenA()` can send messages early. The comment cites bugzilla 774379. GTK NEWS 3.22.5 gives that bug's title as *"gdk: mingw64 builds segfault during initialization of Huion H610PRO wintab"* (gtk3:NEWS:2639). This is an explicit Huion link.
- In Windows Ink mode, wintab32.dll is never loaded. In WinTab mode, `WM_POINTER*` handling is skipped (gtk3:gdk/win32/gdkevents-win32.c:2875, 4029).

**(a) How GIMP exposes it. CONFIRMED.**
- The enum is `GimpWin32PointerInputAPI { WINTAB, WINDOWS_INK }` (gimp:app/core/core-enums.h:460-464). The gimprc key is `win32-pointer-input-api`. The default is `GIMP_WIN32_POINTER_INPUT_API_WINDOWS_INK`, and changing it needs a restart (gimp:app/config/gimpearlyrc.c:105-111; gimp:app/config/gimpcoreconfig.c:822-829, `GIMP_CONFIG_PARAM_RESTART`).
- At startup, `gimp:app/main.c:620-649` reads the early rc. When the runtime GTK is ≥ 3.24.30, it always sets `GDK_WIN32_TABLET_INPUT_API` to `wintab` or `winpointer`. There is a `#warning` to switch to a "backend-specific API" for GTK4 (main.c:626).
- **Consequence (CONFIRMED by combining both codes):** GIMP always sets the variable, so GTK's "silent fallback to WinTab" path is disabled (`have_tablet_input_api_preference = TRUE`). GIMP does its own fallback in the rc setter (gimpearlyrc.c:213-233, gimpcoreconfig.c:1218-1238):
  - INK is requested but the OS is older than Windows 8 (`gimp_win32_have_windows_ink`, gimp:app/core/gimp-utils.c:1466-1470) and Wintab exists → WINTAB.
  - WINTAB is requested but `System32\Wintab32.dll` is missing (gimp-utils.c:1439-1463) → INK.
- UI: Preferences → Input Devices → "Extended Input Devices" → "Pointer Input API:" combo. It is shown only on GTK ≥ 3.24.30. Each entry is greyed out by availability (gimp:app/dialogs/preferences-dialog.c:722-746, 3464-3491). The blurb is "Sets the preferred pen and touch input API." (gimp:app/config/gimprc-blurbs.h:278).
- **Default in GIMP 3.2.8 on Windows 8+ is Windows Ink (WM_POINTER).** WinTab is opt-in.
- GTK added the Pointer stack in 3.24.30 ("Add support for Windows Pointer Input stack", gtk3:NEWS:713). GIMP added the preference in 2.99.8 ("select the input device API on Windows (Wintab or Windows Ink)", gimp:NEWS.pre-3-0:2715-2717). The appdata also says "Windows Ink support has been backported" to 2.10.x (gimp:desktop/org.gimp.GIMP.appdata.xml.in.in.in:927).

## 2. Windows Ink path (WM_POINTER)

**Messages handled (b). CONFIRMED** (gtk3:gdk/win32/gdkevents-win32.c:2874-3099):
- `WM_POINTERDOWN`, `WM_POINTERUP`, `WM_POINTERUPDATE`, `WM_POINTERENTER`, `WM_POINTERLEAVE`: consumed (`*ret_valp = 0; return_val = TRUE`).
- `WM_NCPOINTERUPDATE`: produces a crossing event, then is forwarded to DefWindowProc (line 3022).
- `WM_POINTERACTIVATE`: returns PA_NOACTIVATE for temp/modal windows (line 3220).
- `WM_POINTERDEVICECHANGE`: arrives on a message-only window and triggers re-enumeration (gtk3:gdk/win32/gdkdevicemanager-win32.c:568-584).
- `WM_POINTERCAPTURECHANGED` is only `#define`d (gtk3:gdk/win32/winpointer.h:62) and is not handled.
- Only PT_PEN and PT_TOUCH are taken. PT_MOUSE and PT_TOUCHPAD are forwarded to DefWindowProc (`gdk_winpointer_should_forward_message`, gdkdevicemanager-win32.c:2121-2133), so mice still arrive as `WM_MOUSE*`. GTK does not call `EnableMouseInPointer`.
- `RegisterPointerDeviceNotifications(hwnd, FALSE)` means notifyRange is FALSE, so there are no device in-range or out-of-range messages (line 695).

**Reading samples (b)+(c). CONFIRMED.**
- `gdk_winpointer_input_events` (gdkdevicemanager-win32.c:1895-2040) calls `GetPointerPenInfoHistory` in a size-query loop (1940-1950).
- It replays **every history (coalesced) entry, oldest first** (`h = history_count-1 … while (h-- > 0)`, 1966-1983). Each entry becomes its own GdkEvent with its own `dwTime` (`winpointer_get_time`, 1613-1617), which falls back to `msg->time`.
- Windows returns the most recent entry first. That ordering is Windows-documented behaviour (c), INFERRED, not visible in code.
- Touch uses `GetPointerTouchInfoHistory` the same way (1988-2037).
- The event type comes from `msg->message`, not from each entry (`winpointer_allocate_event`, 1663-1729). This relies on Windows coalescing only UPDATE messages (INFERRED).
- `PerformanceCount` (QPC) is not used, so timestamps have millisecond resolution (winpointer.h:204 is declared, grep shows no use).

**Coordinates (b)+(c). CONFIRMED.**
- `x_root = origin_x + ptHimetricLocation.x * scale_x` (1759-1760). The scale comes from `GetPointerDeviceRects`: display rect width divided by device rect width (`winpointer_device_update_scale_factors`, 198-218). It is refreshed on every device change (556).
- The result is sub-pixel, and the mapping to monitors comes from Windows, i.e. the driver or OS mapping (d).
- Then: client offset via `ClientToScreen`, `_gdk_offset_x/y` for negative virtual-desktop coordinates, and division by integer `impl->window_scale` for HiDPI (1762-1779).
- Raw (`ptHimetricLocationRaw`) and prediction-free fields are not used.

**Axes (b). CONFIRMED** (axes declared at 373-380, filled at 1803-1816):
- Pressure: `pressure / 1024.0` if `PEN_MASK_PRESSURE` is set. Otherwise the binary value 1.0 if `POINTER_FLAG_INCONTACT`, else 0.0. The 0..1024 range is the Windows API contract (c). Devices with higher native resolution are therefore quantised by Windows (INFERRED).
- Tilt X/Y: `tiltX/90.0`, `tiltY/90.0`, giving -1..1. These are linear degrees per axis, and 0 if the mask bit is absent.
- Rotation/twist: `rotation/360.0` → `GDK_AXIS_ROTATION` 0..1.
- No distance/hover-height, tangent pressure or wheel axis.
- `gdk_device_get_axis` returns the stored value as-is (gtk3:gdk/gdkdevice.c:1339-1370).

**Buttons and flags (b). CONFIRMED.**
- `button = (FIRSTBUTTON || ButtonChangeType==FIRSTBUTTON_UP) ? 1 : 3` (1834-1836).
- The state mask uses only FIRSTBUTTON and SECONDBUTTON (1792-1801).
- `PEN_FLAG_BARREL` and `POINTER_FLAG_THIRDBUTTON` are defined (winpointer.h:77, 102) but never read.
- Eraser: `winpointer_is_eraser` = `penFlags & (PEN_FLAG_INVERTED|PEN_FLAG_ERASER)` (1619-1623). It is evaluated on the newest history entry and selects which GdkDevice receives the whole batch (1955-1960).
- Modifiers come from `GetKeyState` because "info->dwKeyStates is not reliable" (1780-1790).

**Proximity and hover (b). CONFIRMED.**
- For PT_PEN, `WM_POINTERENTER` with `IS_POINTER_NEW` → `GDK_PROXIMITY_IN`. `WM_POINTERLEAVE` with `!IS_POINTER_INRANGE` → `GDK_PROXIMITY_OUT` (1668-1679; gdkevents-win32.c:3025-3099). A leave while still in range only produces a crossing (leave) event.
- Hover produces `WM_POINTERUPDATE` → `GDK_MOTION_NOTIFY` (1685-1687). Pressure is whatever Windows reports (0 when not in contact, INFERRED).

**Mouse emulation and OS feedback suppression (b)+(c). CONFIRMED.**
1. Pen messages are consumed and not passed to DefWindowProc. The claim that legacy `WM_*BUTTON*` messages are then not synthesised is INFERRED from Windows semantics.
2. `WM_MOUSEMOVE` is dropped within 200 ms of the last digitizer message (gdkevents-win32.c:2742-2745).
3. `_gdk_win32_get_cursor_pos` returns the pen position while `pen_touch_input` is set (gdkevents-win32.c:197-204).
4. Per window, `gdk_winpointer_initialize_window` (gdkdevicemanager-win32.c:709-766, called from gtk3:gdk/win32/gdkwindow-win32.c:978-979):
   - sets the `MICROSOFT_TABLETPENSERVICE_PROPERTY` window property to disable press-and-hold, tap feedback, barrel feedback, flicks and flick fallback keys;
   - calls `SetGestureConfig` with GC_ALLGESTURES blocked;
   - calls `SetWindowFeedbackSetting(FALSE)` for 11 feedback types;
   - `WM_TABLET_QUERYSYSTEMGESTURESTATUS` returns the same disable flags (gdkevents-win32.c:4043-4052).

**Devices and tools (b). CONFIRMED.**
- Each `POINTER_DEVICE_TYPE_INTEGRATED_PEN`/`EXTERNAL_PEN` gets **two** slave `GdkDeviceWinpointer` objects: "<name> Pen stylus" (`GDK_SOURCE_PEN`) and "<name> Eraser" (`GDK_SOURCE_ERASER`) (442-456, naming 324-354).
- The name comes from the HID manufacturer and product strings (`HidD_Get*String`), else VID/PID in hex, else `productString`, else "Unnamed" (227-337).
- vendor-id and product-id are set (367-368).
- Each device gets one `GdkDeviceTool` with serial 0 and hardware id 0 (396-416). Two pens on one digitizer are therefore indistinguishable.
- Events: `device` = Virtual Core Pointer, `source_device` = the pen/eraser device. The tool is set and the core pointer's active slave is switched (1880-1891).
- `get_history` returns FALSE (gtk3:gdk/win32/gdkdevice-winpointer.c:50-58).

## 3. WinTab path

**Loading (b). CONFIRMED.**
- `LoadLibrary(GetSystemDirectory()+"\\Wintab32.dll")`, so only the System32 copy installed by the driver is used (gdkdevicemanager-win32.c:1029-1047).
- Resolves `WTInfoA/W`, `WTEnable`, `WTOpenA`, `WTGetA/SetA`, `WTOverlap`, `WTPacket`, `WTQueueSizeSet` (1050-1067).
- Packets go to a hidden 2×2 GDK toplevel at (-100,-100) (1081-1094).

**Context (b). CONFIRMED** (1095-1193):
- Starts from the device-specific default context `WTI_DSCTXS` if spec ≥ 1.1 ("Some drivers, e.g. Aiptek, don't provide this info"), else `WTI_DEFSYSCTX`.
- `lcOptions |= CXO_MESSAGES|CXO_CSRMESSAGES`, `lcMsgBase = WT_DEFBASE`, `lcPktRate = 0`.
- `PACKETDATA = PK_CONTEXT|PK_CURSOR|PK_BUTTONS|PK_X|PK_Y|PK_NORMAL_PRESSURE|PK_ORIENTATION|PK_TANGENT_PRESSURE`. `PACKETMODE 0` means all fields are absolute (lines 44-46).
- **Not requested:** PK_TIME, PK_SERIAL_NUMBER, PK_STATUS, PK_Z, PK_ROTATION.
- Output extents = device X/Y axis range, with Y negated so it grows downward.
- After `WTOpenA`, `CXO_SYSTEM` is added via `WTSetA`, so the pen also drives the system cursor. Then `WTOverlap(TRUE)`.
- Queue size: tries 128, then 64, down to 1.
- Contexts are re-raised with `WTOverlap` on `WM_ACTIVATE` (gdkevents-win32.c:3980-3989; `_gdk_wintab_set_tablet_active` 2142-2163).

**Cursors and devices (b)+(d). CONFIRMED** (`_wintab_recognize_new_cursors`, 1198-1400):
- One floating `GdkDeviceWintab` is created per active Wintab cursor, named "<DVC_NAME> <CSR_NAME>", and **always `GDK_SOURCE_PEN`** (1267-1276). The eraser cursor therefore becomes a separate device but is not typed as eraser. No `GdkDeviceTool` is created; `CSR_TYPE` and `CRC_INVERT` are not consulted.
- Vendor hack: inactive cursors are skipped only when `DVC_NAME == L"WACOM Tablet"` and `physid == 0` (1240-1248).
- New cursors are discovered lazily on `WT_CSRCHANGE` (2583-2610).

**Axis scaling (b). CONFIRMED.**
- Pressure axis = `DVC_NPRESSURE` axMin..axMax (1310-1319). It is normalised linearly to 0..1 by `_gdk_device_translate_axis` (gtk3:gdk/gdkdevice.c:1838-1866), which trusts the driver's reported range.
- Tilt: `decode_tilt` (gdkdevicemanager-win32.c:2167-2220):
  - `az = 2π·orAzimuth/(res/65536) − π/2` and `el = 2π·orAltitude/(res/65536)`;
  - `xtilt = cos(az)·cos(el)·1000`, `ytilt = sin(az)·cos(el)·1000`, then normalised to ±1;
  - this is a cos(altitude) projection, not linear degrees, so the scale differs from the Ink path (INFERRED from the maths);
  - comment: "Tested with a Wacom Intuos 5 … Wintab's reference angle leads gdk's by 90 degrees".
- **Huion quirk (explicit):** "The same is true of the Huion H610PRO … it's the altitude resolution that's zero". If either azimuth or altitude resolution is 0, tilt is forced to 0 (2175-2193).
- Twist: `orTwist` is exposed as `GDK_AXIS_WHEEL` with min and max deliberately swapped (Art Pen), and is disabled if `PK_TANGENT_PRESSURE` exists (1324-1365). Tangent pressure (airbrush wheel) is also `GDK_AXIS_WHEEL` (1368-1378). GTK NEWS 3.24.27 says "Add support for wheel and rotation axes" (gtk3:NEWS:826).
- X/Y: integer tablet coordinates are mapped by `_gdk_device_translate_screen_coord` (gtk3:gdk/gdkdevice.c:1785-1835). The scale is `gdk_screen_get_width()/axis_width`, i.e. **the full tablet range maps to the full GDK screen**, independent of the driver's monitor mapping.
- The target window comes from the **system cursor position** (`gdk_device_get_window_at_position(core_pointer)` → `WindowFromPoint(_gdk_win32_get_cursor_pos)`, gdkdevicemanager-win32.c:2320; gtk3:gdk/win32/gdkdevice-win32.c:214-217). Events then propagate up the parent chain until a window selects them (2486-2508).

**Packets → events (b). CONFIRMED** (`gdk_wintab_input_events`, 2288-2632):
- **One GdkEvent per `WT_PACKET` message.** The packet is fetched with `WTPacket(hctx, serial)` (2332). There is no `WTPacketsGet` batch read.
- If the button bitmask changed, the event is a BUTTON_PRESS/RELEASE carrying that packet's coordinates; otherwise it is MOTION. Only **one** button transition per packet; others wait for the next packet (2437-2470).
- `button_map[8] = {0,1,4,5,2,3,6,7}` swaps Wintab buttons 2 and 3. Tablet bit1 → GDK 3, bit2 → GDK 2 (2309, 2435).
- Time = `_gdk_win32_get_next_tick(msg->time)`, the message time clamped to be monotonic (2515, 2550; gdkevents-win32.c:183-195). Packet time is not used.
- Packets are dropped while a window is being moved or resized (2390-2394, "bug #151090") and for disabled devices (2383-2385).
- `get_history` returns FALSE (gtk3:gdk/win32/gdkdevice-wintab.c:31-39).

**Proximity and mouse-vs-pen (b). CONFIRMED.**
- `WT_PROXIMITY` **does not emit GDK_PROXIMITY_IN/OUT**. It only adjusts counters (2612-2629).
- On cursor change or first packet after proximity, `_gdk_input_ignore_core += 1` and the core pointer switches to the pen. While that count is above 0, core mouse motion and button events are suppressed (gdkevents-win32.c:1873, 2796). Leaving proximity decrements it. `WM_ACTIVATEAPP` resets it to 0 (3992-3999).
- The comment at 2366-2381 documents a fallback for two devices in proximity at the same time.

## 4. Event delivery, coalescing, latency

**(b) GDK3 generic. CONFIRMED.**
- `event_compression` defaults to TRUE (gtk3:gdk/gdkwindow.c:1405). `gdk_window_set_event_compression` doc: "paint programs … will benefit from turning off event compression" (10666-10688).
- After each event, `_gdk_event_queue_handle_motion_compression` drops all but the last trailing motion for the same window and device (gtk3:gdk/gdkevents.c:255-311; called at gdkwindow.c:10120). Pen events all carry `device = core pointer`, so pen samples coalesce together.
- Frame-clock throttling: `_gdk_event_queue_find_first` holds the **last** unflushed MOTION event in the queue (gdkevents.c:93-122). It is released only when another event follows, or when the frame clock's FLUSH_EVENTS phase marks it flushed.
- The flush idle is a timeout at `GDK_PRIORITY_EVENTS+1` with `min_interval` derived from `min_next_frame_time` (gtk3:gdk/gdkframeclockidle.c:296-317).
- `gdk_window_flush_events` then **pauses** event delivery until RESUME_EVENTS after paint (gdkwindow.c:11700-11715).
- Input delivery is therefore aligned to the frame clock, and the newest sample can wait up to one frame cycle. That magnitude is INFERRED and was not measured.
- Motion history: every Win32 device class returns FALSE from `get_history` (gtk3:gdk/win32/gdkdevice-win32.c:32, gdkdevice-virtual.c:66, gdkdevice-winpointer.c:50, gdkdevice-wintab.c:31). On Windows, coalesced samples exist **only** as the extra events GDK expands from `GetPointerPenInfoHistory`. They survive only if compression is off.

**(a) GIMP. CONFIRMED.**
- On button-1 press with a `GIMP_MOTION_MODE_EXACT` tool, GIMP calls `gdk_window_set_event_compression(canvas, FALSE)`. On release it sets it back to TRUE (gimp:app/display/gimpdisplayshell-tool-events.c:584-592, 746-751). The comment says "enable motion compression", which is a misnomer.
- EXACT tools: paint tools (gimp:app/tools/gimppainttool.c:162), warp, foreground/paint select, polygon select, bucket fill (conditional).
- Hover always stays compressed.
- `gdk_device_get_history` is attempted only if `use-event-history` is on (default FALSE, gimp:app/config/gimpdisplayconfig.c:43). The blurb says "Bugs in event history buffer are frequent". On Windows it returns FALSE anyway, so GIMP falls back to per-event processing (tool-events.c:937-1006).
- The canvas avoids `GDK_POINTER_MOTION_HINT` (gimp:app/display/gimpdisplayshell.c:812-820). The canvas mask includes `GDK_PROXIMITY_OUT_MASK` but not PROXIMITY_IN (gimp:app/display/gimpcanvas.h:23-36). Motion implies proximity-in (tool-events.c:892-897).

**GimpMotionBuffer** (gimp:app/display/gimpmotionbuffer.c). CONFIRMED:
- Drops samples that move less than half a screen pixel: `filter = MIN(1/scale_x,1/scale_y)/2` (298-304). **Pressure-only changes at a stationary pen are discarded.**
- Velocity is computed from a smoothed `delta_time` (`SMOOTH_FACTOR 0.3`, `VELOCITY_UNIT 3.0`), with millisecond timestamps (273-333). Direction uses a 4-entry history (335-390).
- "Event fill": when consecutive samples are ≥ 6 image px apart (`EVENT_FILL_PRECISION`), one event is held. On the next sample, the segment is Catmull-Rom interpolated (393-406, 540-571).
- Held events are flushed by a 50 ms timeout (`g_timeout_add(50,…)`, 488-494). This adds latency for sparse or fast strokes.
- Strokes are emitted synchronously. Painting happens on a separate "paint" thread (`GIMP_NO_PAINT_THREAD` disables it), and the display is flushed every `DISPLAY_UPDATE_INTERVAL 10000` µs (gimp:app/tools/gimppainttool-paint.c:45, 99-121, 372-378).

## 5. GIMP device handling

- **Device → GimpDeviceInfo.** `gimp_devices_get_from_event` uses `gdk_event_get_source_device`. Slaves in DISABLED mode map to their master; floating (WinTab) devices map to themselves (gimp:app/widgets/gimpdevices.c:201-282). Infos are keyed by **device name** (gimp:app/widgets/gimpdevicemanager.c:551-568).
- Default modes: PEN/ERASER/CURSOR → SCREEN; MOUSE, TOUCHSCREEN, TOUCHPAD, TRACKPOINT slaves → DISABLED. Curves are reset when a device is added (gimpdevicemanager.c:699-774).
- Win32-only hack: if a second GdkDevice arrives with the same name, it is ignored (`return FALSE`) instead of replacing the first. This dates from regression #2495 (gimp:app/widgets/gimpdeviceinfo.c:868-893). NEWS 3.0.2 says: "Windows: temporary revert of some Input Device handling code which was breaking pressure sensitivity for some graphics tablets (though this revert may make issues with the eraser tip)" (gimp:NEWS.pre-3-2:1091-1093).
- **Per-device tool switching.** `devices-share-tool` defaults to FALSE (gimp:app/config/gimpguiconfig.c:183-188). Changing device restores that device's tool, brush and options (gimpdevicemanager.c:280-310). Default tool by source: ERASER → eraser tool, PEN → paintbrush, TOUCHSCREEN → smudge (gimpdeviceinfo.c:951-992).
  - Under Windows Ink, flipping the pen auto-selects the Eraser tool.
  - Under WinTab, the eraser cursor is `GDK_SOURCE_PEN`, so it defaults to paintbrush; only its icon is guessed from the name containing "eraser" (gimpdeviceinfo.c:576-612).
- **Coords.** `gimp_device_info_get_event_coords` reads X/Y from event x/y, and pressure, xtilt, ytilt, wheel, distance, rotation and slider from `gdk_event_get_axis` (gimp:app/widgets/gimpdeviceinfo-coords.c:35-131). `gimp_device_info_map_axis` (gimpdeviceinfo.c:1396-1423) works as follows:
  - pressure → per-device `GimpCurve` (`pressure-curve` property, 252-257);
  - tilt clamped to ±1;
  - wheel clamped to 0..1;
  - rotation and distance passed through.
  - Same physical twist ends up in **different fields** per API: WinTab → `wheel`, Ink → `rotation`. The "Rotation" dynamics input was added in 3.2.6 ("support of the Wacom Art Pen 2", gimp:NEWS:147-148).
- **Per-device pressure curve: yes.** It is editable in Input Devices (gimp:app/widgets/gimpdeviceinfoeditor.c:274-362) and persisted in devicerc. It is shown **only for `GDK_SOURCE_PEN`** (274), so the Ink "Eraser" device has a curve but no editor UI (INFERRED consequence). NEWS 3.2.0 explains the move to "always show the Pressure Curve for any device of type Pen" (gimp:NEWS.pre-3-2:122-129).
- Pad actions are only for `GDK_SOURCE_TABLET_PAD`. On Windows, "there is likely no GDK_SOURCE_TABLET_PAD device at all" (gimpdeviceinfoeditor.c:484-501). Express keys are therefore not handled.
- Input Controllers (gimpcontroller*, `modules/controller-dx-dinput.c` DirectInput, MIDI, linux-input) map generic controller events to actions. They are not used for the stylus.
- Tool events: button 1 → tool; buttons 2/3 → `gimp_modifiers_manager_get_action` (tool-events.c:569-640). Other buttons are ignored while one is held (536-544). `GDK_SOURCE_ERASER` and `GdkDeviceTool` type are not read in tool-events. Eraser behaviour comes only through device switching.

## 6. Pen side buttons

- **Ink (b)+(c), CONFIRMED.** Only FIRSTBUTTON and SECONDBUTTON are mapped, giving GDK 1 or 3 (gdkdevicemanager-win32.c:1834-1836). Per Windows docs, SECONDBUTTON is set when the pen is in contact *with* the barrel pressed (INFERRED, (c)). Barrel in hover (`PEN_FLAG_BARREL`) and a second barrel button (THIRDBUTTON) are ignored, so GDK button 2 cannot come from a pen via Ink.
- **WinTab (b), CONFIRMED.** `pkButtons` is absolute. Bit1 → GDK 3, bit2 → GDK 2, bit3+ → 4+ (2309, 2435). It works in hover, because packets flow in proximity. Logical button numbering after the driver's `CSR_BUTTONMAP` is (d), INFERRED.
- **GIMP defaults (a), CONFIRMED** (gimp:app/display/gimpmodifiersmanager.c:342-400, 560-623):
  - button 2: none → pan, Shift → rotate, Shift+Ctrl → step-rotate, Ctrl → zoom, Alt → layer pick;
  - button 3: none → context menu, Alt → brush pixel size.
  - The mapping is configurable per device and keyed by vendor-id:product-id (510-539). WinTab devices have no IDs, so they share the "(no-vendor-id)" bucket.
- If the driver emulates a mouse right or middle click instead (d):
  - in WinTab mode, core events are suppressed while the pen is in proximity;
  - in Ink mode, mouse *moves* are suppressed for 200 ms, but button messages are not.

  Outcome depends on the driver (INFERRED).

## 7. NEWS / release notes

- GTK 3.24.30: "Add support for Windows Pointer Input stack" (gtk3:NEWS:713).
- GTK 3.24.27: "Fix unresponsive windows after tablet input"; "Add support for wheel and rotation axes" (825-826).
- GTK 3.22.5: bugs 774265 "No tilt for wintab devices" and 774379 Huion H610PRO WinTab init segfault (2638-2639).
- GIMP 2.99.8: Wintab/Windows Ink preference (NEWS.pre-3-0:2715-2717).
- GIMP 2.99.x: default tools per source, Pen → paintbrush, Eraser → eraser (NEWS.pre-3-0:3087-3092).
- GIMP 3.0.2: Windows input-device revert, "may make issues with the eraser tip" (NEWS.pre-3-2:1091-1093).
- GIMP 3.2.0 RC3: MyPaint barrel rotation (NEWS.pre-3-2:355-356).
- GIMP 3.2.0: axis list dropped, pressure curve always shown for Pen (NEWS.pre-3-2:122-129).
- GIMP 3.2.6: "Rotation" dynamics input (NEWS:147-148).
- GIMP 2.10 (pre-2-10:406-409): "Wacom Art Pen and Airbrush pen use the same axis for barrel rotation and wheel".
- GIMP 2.8 (pre-2-8:120-122): "Fix graphics tablet canvas interaction by moving from gdk_pointer_grab() to gtk_grab_add()… Stop using motion hints".
- devel-docs: nothing tablet-specific found.

## 8. Limitations and risk points in code

All CONFIRMED in code unless marked; consequences INFERRED.
- WinTab maps the full tablet range to the full virtual screen (gdkdevice.c:1785-1835), but chooses the window by system-cursor position. If the driver maps the tablet to one monitor (display tablets) or uses relative "mouse mode", the computed x/y can disagree with the cursor. INFERRED offset risk.
- WinTab: no PK_TIME/PK_STATUS/PK_SERIAL_NUMBER, no proximity GDK events, no eraser source, no GdkDeviceTool, one button transition per packet, packets dropped during move/resize.
- WinTab: `lcPktRate = 0`; what the driver does with it is unknown (d).
- Ink: pressure limited to the 0–1024 API range. No hover distance. No barrel-in-hover. No second barrel button. Tool serial is 0. Capture-change is not handled, so a stroke could stay open if capture is lost (INFERRED).
- Ink: per-batch eraser detection uses the newest entry. Key state is read at processing time, not per sample.
- HiDPI: GTK3 sets `PROCESS_SYSTEM_DPI_AWARE`, not per-monitor (gtk3:gdk/win32/gdkdisplay-win32.c:907-935). Integer `window_scale` only. Mixed-DPI multi-monitor coordinates rely on Windows virtualisation (INFERRED risk).
- GDK motion compression is on by default. The trailing motion waits for the frame clock. Event delivery pauses during paint.
- GIMP: devices keyed by name (duplicate-name hack); sub-pixel filter drops pressure-only updates; 50 ms event-fill hold; history unsupported on Windows.
- `gimpdeviceinfo.c:623-630` notes axis-count changes at runtime ("the first time when a stylus approached then moved away").

---

## Summary table

| Capability | WinTab path (GTK 3.24.52) | Windows Ink path (GTK 3.24.52) |
|---|---|---|
| Pressure | Yes. `PK_NORMAL_PRESSURE` normalised by driver-reported axMin..axMax (gdkdevicemanager-win32.c:1310-1319; gdkdevice.c:1838) | Yes. `pressure/1024`, binary fallback (gdkdevicemanager-win32.c:1810-1811) |
| Tilt | Yes, if both orientation resolutions are non-zero. Azimuth/altitude → cos projection ±1. Forced 0 for Huion H610PRO-style drivers (2167-2214) | Yes. `tiltX/90`, `tiltY/90` linear (1812-1813) |
| Rotation/twist | `orTwist` → `GDK_AXIS_WHEEL` (inverted), off if tangent pressure present (1324-1365). GIMP sees `coords.wheel` | `rotation/360` → `GDK_AXIS_ROTATION` (1814). GIMP sees `coords.rotation` |
| Barrel buttons | `pkButtons` bitmask, 2↔3 swapped, works in hover (2309-2470) | Only SECONDBUTTON → GDK 3 (in contact). Hover barrel and 2nd barrel ignored (1834-1836) |
| Eraser end | Separate GdkDevice per Wintab cursor but `GDK_SOURCE_PEN`. No auto eraser tool in GIMP (1267-1276) | `PEN_FLAG_INVERTED`/`ERASER` → separate `GDK_SOURCE_ERASER` device + ERASER tool. GIMP auto-selects Eraser (1619-1623; gimpdeviceinfo.c:967-971) |
| Proximity/hover | Hover motion yes. No GDK proximity events; only core-pointer switching (2612-2629) | `PROXIMITY_IN/OUT` from POINTERENTER/LEAVE. Hover motion via POINTERUPDATE (1668-1687) |
| History/coalesced | No. One event per WT_PACKET, no batch read, queue ≤128 (1181, 2332) | Yes. `GetPointerPenInfoHistory` expanded oldest-first into separate events (1940-1983). `gdk_device_get_history` is FALSE in both paths |
| Mouse vs pen | `_gdk_input_ignore_core` suppresses core mouse while pen in proximity; pen drives system cursor (CXO_SYSTEM) | Pen WM_POINTER consumed. WM_MOUSEMOVE dropped within 200 ms. Ink gestures and feedback disabled per window (gdkevents-win32.c:2742; gdkdevicemanager-win32.c:709-766) |
| Timestamps | `msg->time`, made monotonic | per-sample `POINTER_INFO.dwTime` (ms) |
| Coordinate mapping | tablet range → whole GDK screen (risk on multi-monitor) | `GetPointerDeviceRects` (OS/driver mapping), sub-pixel |

## Implications for a non-GTK app (C#/.NET + WPF + SkiaSharp)

GTK/GDK carried these responsibilities. The new app or its framework must cover each one.
1. **API selection and fallback.** Choosing Ink vs WinTab, probing System32\Wintab32.dll and Windows ≥ 8, persisting the choice, and requiring a restart. GIMP keeps WinTab as a user-switchable escape hatch because some drivers behave better there (INFERRED from the preference's existence and the 3.0.2 revert).
2. **Device enumeration and hot-plug.** Ink: `GetPointerDevices`/`GetPointerDeviceCursors`/`GetPointerDeviceRects` plus `WM_POINTERDEVICECHANGE`. WinTab: lazy cursor discovery on `WT_CSRCHANGE`. Also stable device naming (HID strings, VID/PID) for per-device settings.
3. **Coordinate transforms.** HIMETRIC→pixel via device rects; negative virtual-desktop offsets; client offset; DPI scale. For WinTab, a correct tablet→monitor mapping (GTK's whole-screen mapping is a known-weak point to *not* copy).
4. **Axis normalisation** with "mask absent" defaults: pressure 0..1, tilt (decide degrees vs projection, and make Ink and WinTab consistent), twist. Also a per-device pressure curve (GIMP feature).
5. **Coalesced samples.** Read `GetPointerPenInfoHistory` (or the framework's batched points), keep them in order with per-sample timestamps, and **do not compress** them during strokes. Compress only for hover.
6. **Suppress OS pen behaviours.** Press-and-hold right-click, flicks, tap and barrel visual feedback, legacy mouse promotion. Do not double-handle promoted mouse events (GTK's 200 ms rule).
7. **Eraser/pen identity and proximity.** Separate pen/eraser device state, proximity in/out, auto tool switching.
8. **Buttons.** Map barrel buttons consistently across APIs (GTK's two paths differ). Handle hover barrel presses if desired.
9. **WinTab specifics if supported.** Delayed init, packet queue size, `WTOverlap` on activation, ignoring core mouse while in proximity, dropping input during window move/resize.
10. **Latency pipeline.** GTK ties input to the frame clock. GIMP decouples painting onto a thread with a ~10 ms display flush. The new app needs its own input thread → stroke engine → render scheduling, and should avoid holding samples (GIMP's 50 ms event-fill hold and sub-pixel/pressure-only filter are design choices to reconsider).

WPF-specific notes (**outside this evidence base, from general knowledge, verify**):
- WPF's default stylus stack is WISP/RealTimeStylus-based; a WM_POINTER stack is opt-in via an AppContext switch.
- WPF exposes pressure, tilt, twist and buttons through StylusPoint properties when the device reports them, promotes stylus to mouse, and has no WinTab support. WinTab would need P/Invoke plus an `HwndSource` hook.
- GetStylusPoints batches are WPF's analogue of history.

## Huion-specific risks

- **CONFIRMED (code or NEWS names Huion):**
  - The Huion H610PRO WinTab driver reports `PK_ORIENTATION` with zero altitude resolution, so GTK zeroes tilt (gdkdevicemanager-win32.c:2175-2193).
  - WinTab init crashed with the Huion H610PRO, which motivated GTK's delayed init (1406-1412; gtk3:NEWS:2639).
- INFERRED:
  - WinTab tilt math was tuned on Wacom ("reference angle leads gdk's by 90°"). Huion azimuth conventions may rotate or flip tilt.
  - The non-Wacom cursor list is not filtered by physid. Huion WinTab may expose extra or duplicate cursors, and GIMP's name-keyed devices and Win32 duplicate-name hack could misattribute settings.
  - Pressure normalisation trusts `DVC_NPRESSURE.axMax`. A wrong maximum yields capped or compressed pressure.
  - Display tablets (Kamvas) mapped to a single monitor hit the WinTab whole-screen mapping, causing offsets. Ink avoids this via `GetPointerDeviceRects`.
  - Huion drivers have a "Windows Ink" toggle (d). When it is off, the Ink path sees no PT_PEN, so there is no pressure. GIMP will not auto-fall back to WinTab, because it always sets the environment variable.
  - Ink quantises pressure to 0–1024.
  - Barrel buttons are often remapped by the driver to mouse or keyboard events rather than SECONDBUTTON, so behaviour varies by driver setting.
  - Hover-barrel and second barrel button are lost in the Ink path.
  - Pens without twist or eraser yield no rotation and no eraser device events, which is fine but must be handled gracefully.
  - Coalesced history depth from Huion drivers is unknown.

## Unknowns

- Actual behaviour of Huion's wintab32.dll (cursor list, axis ranges, button bits, lcPktRate=0).
- Whether Huion's Ink path supplies tilt, rotation or history entries.
- Real latency added by the GDK frame-clock hold and GIMP's 50 ms fill. No measurements were made.
- WPF stylus-stack details (not in these sources).
- Behaviour on mixed-DPI multi-monitor setups under GTK's system-DPI awareness.
