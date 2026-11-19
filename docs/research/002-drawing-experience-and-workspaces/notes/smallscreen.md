# DoodleNook: UI for small pen displays (Huion Kamvas 13, 2020) plus desktop monitors

Research notes, 2026-11-19. Raw downloads are in `research2/raw/smallscreen/`.

**Labels:** [D] vendor or Microsoft documentation · [D*] vendor text taken from the search-engine index because a direct fetch of helpx.adobe.com (and one retired Microsoft Learn page) returned HTTP 403 · [R] peer-reviewed research · [T] third-party review · [S] community or subjective opinion · [I] my own inference, with arithmetic shown.

**Do not confuse models:** the 2020 **Kamvas 13 is model GS1331**. The **Kamvas 13 (Gen 3) is GS1333**, a different device with 5 keys and 2 dials ([T] https://www.parkablogs.com/content/huion-kamvas-13-gen-3-pen-display-2024).

---

## 1. Kamvas 13 (GS1331): specifications and driver

| Item | Value | Label / source |
|---|---|---|
| Panel | "13.3-inch FHD display with full lamination". IPS, 1920×1080 (16:9), anti-glare matte film | [D] https://www.huion.com/products/kamvas-13 (meta description and spec table) |
| Active area | 293.76 × 165.24 mm | [D] same page; manual specs https://www.huion.com/manual/kamvas-13/173 |
| Lamination / parallax | "Full Lamination: YES"; "Full lamination, minimum parallax" | [D] product page. [T] Parkablogs: "laminated display so there's no issues with parallax" (https://www.parkablogs.com/content/review-huion-kamvas-13-pen-display-just-us-239) |
| Colour | Claimed 120% sRGB, 8-bit. Also 220 cd/m², 1000:1 contrast, 25 ms response | [D]. [T] Parkablogs measured "100% sRGB, 86% AdobeRGB, 81% NTSC and 90% P3" |
| Pen | PW517, battery-free EMR. 8192 pressure levels, ±60° tilt, 5080 LPI, 10 mm sensing (hover) height | [D] product page and manual |
| Report rate / accuracy | Product page: 266 PPS, accuracy ±0.5 mm (centre) and ±3 mm (corner). Manual: >233 PPS, ±0.3 mm. The two sources disagree. | [D] both URLs above |
| Pen side buttons | 2. "The buttons on the pen can be set to two different customizable functions… whenever the pen tip is within 10mm"; the tip does not need to touch | [D] https://www.huion.com/manual/kamvas-13/172 |
| Eraser end | None. Huion lists no eraser tip, and a reviewer "wishes it comes with an eraser on its end" | [D] absence on the spec pages; [T] https://www.techporn.ph/huion-kamvas-13-pen-display-review/ |
| Press keys | 8 programmable keys (K1 to K8). The manual image shows one column on the **left bezel** in landscape: 3 round keys, 2 elongated centre keys, 3 round keys. Holding K4 and K5 for about 3 s opens the OSD, and K2, K3, K6 and K7 become OSD controls | [D] https://www.huion.com/manual/kamvas-13/165; driver screenshot in the PDF manual p.12 (https://www.huion.com/manaul_pdf/es/Kamvas%2013.pdf) |
| Key functions | Keyboard key or combo, mouse button or wheel, "Switch Screen" or "Switch Brush", Run Program, system functions, multimedia, None. Keys can be disabled | [D] https://www.huion.com/manual/kamvas-13/169 |
| Per-app mapping | Yes. "Add a Program": "when launching a specific program, the Driver will automatically recognize it and the setting features will change". Changes apply to all software unless a program is added | [D] https://www.huion.com/manual/kamvas-13/168; https://support.huion.com/en/support/solutions/articles/44001909702 |
| Default key functions | "The default functions of all shortcut keys used in our drivers are Photoshop functions." Example given: zoom is Ctrl+- / Ctrl+= in Photoshop but - / = in Krita | [D] https://support.huion.com/en/support/solutions/articles/44002318166 |
| Mapping and handedness | The full active area maps to one monitor. "If more than one monitor is in use and you are in extended mode, you need to choose the related monitor". The working area can be rotated 0/90/180/270° "to adapt to the left and right hand operation". Cursor calibration is available | [D] /manual/kamvas-13/169 |
| Connection | 3-in-1 cable (USB-A data, HDMI, USB-A power, USB-C to the device) or one full-featured USB-C cable (host needs USB 3.1 Gen1 data and DP 1.2). Can be powered by a laptop at 5 V; ≤7 W | [D] https://www.huion.com/manual/kamvas-13/175 |
| Other | Works as a plain pen tablet when the screen is off. ST300 stand is optional. 366.5 × 217.4 × 11.8 mm, 980 g. Windows 7+, macOS 10.12+, Android, Linux | [D] product page |
| Use as a second monitor | Huion's troubleshooting page treats extended mode as the normal setup: "Displays wallpaper & task bar of computer monitor only… normal… choose pen display as Work Area in Driver. It is usually 'Display 2'" | [D] https://www.huion.com/manual/kamvas-13/174; [T] Parkablogs describes the Display Switch toggle. [I] So the device ships expecting to sit beside a main monitor. |

## 2. Physical-size arithmetic

**Panel geometry [I]**
- Pixel pitch is 293.76 / 1920 = 0.1530 mm, and 165.24 / 1080 = 0.1530 mm, so pixels are square. That is 6.536 px/mm, or 25.4 / 0.153 = **166.0 PPI**. This matches the "166 PPI" quoted by [T] techporn.
- Diagonal: √(293.76² + 165.24²) = 337.0 mm = **13.27 in**, sold as 13.3 in.

**Size of N pixels, or N effective pixels (epx) after Windows scaling [I]** (mm = N × scale × 0.153)

| Size | 100% | 125% | 150% |
|---|---|---|---|
| 16 | 2.45 mm | 3.06 | 3.67 |
| 24 | 3.67 | 4.59 | 5.51 |
| 32 | 4.90 | 6.12 | 7.34 |
| 40 | 6.12 | 7.65 | 9.18 |
| 48 | 7.34 | 9.18 | 11.02 |
| Effective desktop | 1920×1080 epx | 1536×864 | 1280×720 |
| mm per epx (effective PPI) | 0.153 (166) | 0.191 (132.8) | 0.230 (110.7) |

**Which scale will Windows pick?**
- [D] Microsoft's scaling table (https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dpi-related-apis-and-registry-settings) lists these neighbours:
  - 13.3" WUXGA at 170 DPI: **150%**
  - 15.4" FHD at 143 DPI: **125%**
  - 17" FHD at 130 DPI: 125%
- [D] Same page: "applications expect to have at least 720 effective lines". With 1080 vertical lines, Windows allows only 100%, 125% and 150%; custom values from 100% to 500% are possible.
- [D] Windows 10 and later also take viewing distance and screen density into account (https://learn.microsoft.com/en-us/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design).
- [I] At 166 PPI the Kamvas sits between those table rows, so expect a recommendation of 150%, possibly 125%. 150% is the largest step that keeps 720 effective lines (1080 / 1.5 = 720).
- [S] Users report 150% as the usual recommendation for 13" 1080p laptops (https://forum.literatureandlatte.com/t/windows-setting-display-size-problem/49421).

**Comparison with Microsoft's reference size [I]**
- Microsoft's 40 px at 135 PPI is 40 / 135 × 25.4 = 7.53 mm, or 0.188 mm per epx.
- At 125% the Kamvas gives 0.191 mm per epx (132.8 effective PPI), which is almost exactly that reference.
- At 100% the same 40 px is only 6.1 mm.
- Reaching 7.5 mm takes 49 physical px, which is 39 epx at 125% or 33 epx at 150%.

**Desktop monitors, for contrast [I]**

| Monitor | PPI | 40 px is | 24 px is |
|---|---|---|---|
| 24" 1080p at 100% | 91.8 | 11.1 mm | 6.6 mm |
| 27" 1440p at 100% | 108.8 | 9.3 mm | 5.6 mm |
| 27" 4K at 150% | same physical size as 27" 1440p at 100% | 9.3 mm | 5.6 mm |

- Pixel for pixel, a layout on the Kamvas at 100% is 0.153 / 0.277 = 55% of its physical size on a 24" 1080p monitor.

## 3. Microsoft targeting and pen guidance

- [D] "Set your touch target size to 7.5mm square range (40x40 pixels on a 135 PPI display at a 1.0x scaling plateau)". Targets used often, or with "severe consequences if touched in error", should be larger and "placed further from the edge" (https://learn.microsoft.com/en-us/windows/apps/develop/input/guidelines-for-targeting, updated 2026-11-16).
- [D] "We define touchable as a minimum of 40 x 40 epx, even if the visual is smaller, or 32 epx tall if the width is at least 120 epx." For touch-optimized UI, "consider increasing the target size to 44 x 44 epx with at least 4 epx of visible space between targets". Microsoft suggests two defaults: "Always touch optimized or transition based on device signals" (https://learn.microsoft.com/en-us/windows/apps/design/input/touch-interactions).
- [D] Recommended spacing (https://learn.microsoft.com/en-us/windows/apps/design/basics/content-basics):
  - 8 epx between buttons
  - 12 epx between a control and its label
  - 16 epx between a surface and edge text
- [D*] WinUI "compact" density is a higher-density option, mainly for pointer input (WinUI 2.1+), aligned to a **32×32 epx** target. Standard density targets 40×40. Source: the indexed "Control size and density" page, https://learn.microsoft.com/en-us/windows/apps/design/style/spacing, which now redirects.
- [D] Pens (https://learn.microsoft.com/en-us/windows/apps/design/input/input-primer):
  - A pen "can serve as a pixel-precise pointing device".
  - Active pens provide "hover, or proximity data" and "Palm rejection is much more robust".
  - Apps "should provide pen-specific UI commands, or affordances", for example "+ and -" buttons in place of touch gestures.
  - Input modifiers include "pen eraser and barrel buttons".
- [D] Windows Ink treats the barrel button like the right mouse button ("secondary hardware affordance… pen barrel button, right mouse button"). "If an eraser tip or button is not present", the app must offer erasing itself (https://learn.microsoft.com/en-us/windows/uwp/ui-input/pen-and-stylus-interactions).
- [D] Windows' pen press-and-hold right-click and flicks can be switched off per window by answering `WM_TABLET_QUERYSYSTEMGESTURESTATUS` with `TABLET_DISABLE_PRESSANDHOLD` (https://learn.microsoft.com/en-gb/windows/win32/tablet/wm-tablet-querysystemgesturestatus-message). Krita's manual reports flicks popping up its palette while users draw (https://docs.krita.org/en/user_manual/drawing_tablets.html).
- [I] Microsoft gives no smaller pen-only target size. In practice the Kamvas's ±0.5 mm (centre) to ±3 mm (corner) accuracy argues against going below about 5 mm for frequent targets, and against small targets in corners. ±3 mm is ±20 px at 100%.

## 4. Hand occlusion, reach, parallax and screen edges

**Vogel et al. 2009, "Hand Occlusion with Tablet-sized Direct Pen Input" (CHI '09, DOI 10.1145/1518701.1518787; PDF https://www.dgp.toronto.edu/~ravin/papers/chi2009_occlusion.pdf)** [R]
- Hand and arm occlude "typically as high as 38%, but could range up to 47%" of a 12.1" display (261×163 mm).
- More pixels are hidden *above* the pen-tip horizon than expected. Grip style matters more than body size.
- Commonly occluded pixels "emanat[e] from the lower two-thirds along the right edge" for right-handed users.
- Design implications: avoid feedback in the circle to the right of the pen, and avoid status or alert messages in the bottom-right.
- Left-handed data "mirrored the right-handed".
- [I] The Kamvas is 293.8×165.2 mm, so 14% more area than that display (48,541 vs 42,543 mm²). Expect roughly 33–41% occlusion (38% or 47% × 42,543 / 48,541).

**Hancock & Booth 2004, "Improving menu placement strategies for pen input" (Graphics Interface, pp. 221–230; abstract https://uwaterloo.ca/touchlab/references/improving-menu-placement-strategies-pen-input)** [R]
- Left- and right-handed users showed consistent but mirrored selection-time patterns.
- Vogel 2009, citing them: place pop-up menus south-west of the pen for right-handed users.
- Inkpen et al. found left-handed users preferred left-side scrollbars (also cited in Vogel 2009).

**Vogel & Balakrishnan 2010, "Direct Pen Interaction With a Conventional GUI" (HCI 25:324–388, DOI 10.1080/07370024.2010.499839; https://www.dgp.toronto.edu/public_user/ravin/papers/hcijournal2010_peninteraction.pdf)** [R]
- Main problems found: "poor precision when tapping and dragging, errors caused by hand occlusion, instability and fatigue due to ergonomics and reach".
- "20% of pen hesitation errors occurred when tooltips were popping up near a desired target" ("hover junk").
- Parallax made "selecting targets difficult at the extreme top".
- "Ergonomic issues when reaching targets near the top or at extreme edges".
- Right-handed users return to a home position "near the lower right corner".
- The mouse group "automatically reached for the keyboard" for Ctrl-Z, Ctrl-C and similar shortcuts.

**Vogel & Balakrishnan 2010, "Occlusion-Aware Interfaces" (CHI '10, DOI 10.1145/1753326.1753365)** [R]
- The app models the occluded area and shows hidden status or previews in a callout outside it. This "can successfully mitigate the effects of occlusion".

**Screen edges and Fitts's law**
- [R] Mouse menus at an impenetrable screen edge are faster (Walker & Smelcer, CHI '90, DOI 10.1145/97243.97277).
- [R] Edge targets were "always faster" than targets 1 px from the edge, with up to about 393 ms saved (Farris, Jones & Anders 2001, HFES, DOI 10.1177/154193120104501511).
- [I] A mouse cursor clamps at the edge, which gives a target there effectively infinite depth. A pen is absolute: the tip must land on the edge pixels, at the point where the panel is least accurate (±3 mm in corners, [D]) and subject to parallax and reach costs ([R] Vogel 2010).
- [I] So edge-flush toolbars suit the mouse but cost more with a pen. Lamination reduces parallax but does not remove the accuracy or reach costs.

**Tracking menus**
- [R] Fitzmaurice et al., "Tracking Menus" (UIST '03, pp. 71–79; https://research.autodesk.com/app/uploads/2023/03/tracking-menus.pdf_recneisFEkhnKU1oG.pdf) keep a tool cluster under the pen. The motivation was "preventing round trips to tool palettes with the pen".

**Desk layout**
- [D, Wacom manuals as indexed] Position "the pen display, the pen, and the keyboard for easy access without unnecessary reaching", and consider a keyboard tray (e.g. https://www.manualsdir.com/manuals/225450/wacom-12wx.html?page=26).

## 5. Radial, pie and marking menus

**Research [R]**
- **Callahan, Hopkins, Weiser & Shneiderman 1988** (CHI '88, DOI 10.1145/57167.57182): pie menus "reduce target seek time and lower error rates" and are subjectively equivalent to linear menus (abstract as indexed). A later retrospective claims about 15% faster [S] (https://www.osnews.com/story/30371/pie-menus-a-30-year-retrospective/).
- **Kurtenbach & Buxton 1993** (INTERCHI, pp. 482–487, DOI 10.1145/169059.169426; https://www.billbuxton.com/MMExpert.html):
  - Errors stay under 10% with 4 items per level up to depth 4, and with 8 items per level only up to depth 2.
  - Off-axis items cause most errors.
  - Average selection time was 1.69 s with a pen against 2.07 s with a mouse.
- **Kurtenbach & Buxton 1994**, "User learning and performance with marking menus" (CHI '94, pp. 258–264; https://research.autodesk.com/publications/user-learning-and-performance-with-marking-menus):
  - Marks were 3.5× faster than selecting from the displayed menu.
  - Marks became dominant with experience, but experts still opened the menu to refresh their memory.
- **Gross & Do 1996**, cited in Vogel 2010: users had "problems with hand occlusion when using marking menus".

**How drawing apps implement them**
- [D] **Krita pop-up palette** (https://docs.krita.org/en/reference_manual/popup-palette.html):
  - Opened with a right-click and spawns "at the position of the brush tip or cursor".
  - Contains a ring of brush presets from a tag (default "first 10 presets"), a hue ring and triangle, recent colours, and rotate and zoom sliders.
  - Also has mirror and canvas-only buttons.
- [D*] **Photoshop HUD colour picker** (https://helpx.adobe.com/photoshop/desktop/adjust-color/choose-colors/choose-a-color-while-painting.html):
  - Opened with Shift+Alt+right-click on Windows, with a painting tool active.
  - Available as a Hue Strip or a Hue Wheel. Requires OpenGL.
- [D] **Procreate QuickMenu** (https://help.procreate.com/procreate/handbook/interface-gestures/quickmenu):
  - Six-button radial menu with switchable profiles.
  - Press and drag selects immediately, which becomes "a quick flick".
- [D] **SketchBook lagoon / marking menus** (https://help.autodesk.com/cloudhelp/ENU/SKETPRO-Help/files/sb-basics/SKETPRO_Help_sb_basics_basic_ui_elements_html.html):
  - With the stylus or right button held, drag toward a tool and release. Flicking toward it also works.
  - Hotkeys 1–6 also select.

**Pros and cons for pen use [I, building on the R items above]**
- Pros:
  - Opens at the pen, with no reach, no edge targets and no trip away from the canvas.
  - Directional, so it tolerates imprecise pointing.
  - Supports a smooth move from novice to expert.
- Cons:
  - The hand occludes the lower-right sectors for right-handed users.
  - Item count is limited: about 8 per ring, with on-axis items best.
  - Needs an invocation path: a pen button, an express key or a hold. Windows press-and-hold must not hijack it.
  - Text labels need room; place them in the sectors that are not occluded.

## 6. Hiding and revealing panels

- **Photoshop** [D*] (https://helpx.adobe.com/photoshop/desktop/get-started/learn-the-basics/hide-show-panels.html):
  - Tab hides all panels, including the Tools panel and Options bar. Shift+Tab keeps those two visible.
  - "Auto-Show Hidden Panels" temporarily reveals hidden panels when you hover over the strip at the application-window edge (Windows).
  - Adobe says to use "Window > Workspace > Lock Workspace to prevent accidentally moving panels while using a stylus" (https://helpx.adobe.com/photoshop/desktop/get-started/learn-the-basics/move-panels.html).
- **Krita** [D]:
  - "Canvas Only mode is Krita's version of full screen mode. It is activated by hitting the Tab key". You choose which UI items it hides.
  - It can also be entered with a 4-finger tap (5.0+) (https://docs.krita.org/en/reference_manual/preferences/canvas_only_mode.html).
- **Clip Studio Paint (CSP)** [D]:
  - Tab hides or shows all palettes (https://help.clip-studio.com/en-us/manual_en/690_interface/Palettes.htm).
  - Palette docks can be minimised or turned into icon strips. An icon can open "as tab" (the whole dock) or "as pop-up".
  - Palette layout can be locked with "Lock palette position", "Fix the width of palette dock" and "Lock palette height".
  - "Disable palette operation with finger or mouse" stops stray touches from changing palettes (https://help.clip-studio.com/en-us/manual_en/720_preferences/Preferences.htm).
  - Simple Mode is iPad/Android only: "essential functions… with a minimal interface to maximize canvas space" (https://help.clip-studio.com/en-us/manual_en/090_tablet/Simple_Mode_and_Studio_Mode.htm).
- **Aseprite** [D]:
  - The timeline is hidden by default and toggled with Tab (https://www.aseprite.org/docs/workspace/).
  - Layout editing mode is Shift+W, with built-in "Default" and "Mirrored Default" layouts (https://www.aseprite.org/docs/workspace-layout/).
  - F toggles fullscreen mode (v1.1.8 release notes, https://www.aseprite.org/release-notes/11/).
- **Risks with a pen [I, building on R]:**
  - With 10 mm hover sensing, the cursor sweeps edge strips whenever you stroke near an edge. Hover-to-reveal panels will then pop open over the canvas, the "hover junk" problem ([R] Vogel 2010).
  - Floating palettes over the canvas get occluded and are easy to drag by accident. That is why Adobe and CSP both offer locks.
  - Prefer explicit toggles: a visible edge handle you tap, an express key, or Tab. Disable hover auto-reveal on the pen display.

## 7. Switching workspaces

- **Photoshop** [D*]: Window > Workspace > New Workspace saves panel locations, keyboard shortcuts and menus. Switch with the Application-bar dropdown (https://helpx.adobe.com/photoshop/desktop/get-started/learn-the-basics/save-custom-workspaces.html; …/switch-workspaces.html).
- **Krita** [D]:
  - Workspaces are saved docker configurations, chosen from the toolbar chooser or Window > Workspaces.
  - **Window Layouts** "store multiple windows, their positions and the monitor they were on".
  - "Primary Workspace Follows Focus" lets a sparse secondary window follow the main one (https://docs.krita.org/en/reference_manual/resource_management/resource_workspace.html).
- **CSP**:
  - Window > Workspace > Register Workspace, PRO/EX only ([T] https://tips.clip-studio.com/en-us/articles/8964).
  - Preferences > Interface > "Switch to tablet interface" swaps to a tablet-optimised palette layout. Save your layout as a workspace first ([D] Preferences page).
- **Aseprite** [D]: layout dropdown with user layouts.
- **Per-display layouts:** Krita's Window Layouts come closest, because they store which monitor each window was on. I found no surveyed app that switches layout automatically by display size or DPI [I, absence of evidence].

## 8. UI scaling at 125–150% on Windows

- **Photoshop**:
  - In 2015 it offered only 100% and 200%. Users asked for 150% because 200% was "too big to do anything productively" [S] (https://community.adobe.com/t5/photoshop-ecosystem-discussions/150-ui-scaling-option-for-photoshop/td-p/6831079/page/2).
  - Since Photoshop 19.1 (Jan 2018), on Windows 10 Creators Update and later, it follows Windows per-monitor scaling from 100% to 400% in 25% steps [D*] (https://helpx.adobe.com/photoshop/desktop/get-started/learn-the-basics/high-density-monitor-support-and-per-monitor-scaling.html); [T] CG Channel, Jan 2018.
  - On Windows 10 and later, the "UI Scaling" preference affects only some dialogs. "Scale UI To Font" resizes the interface with the UI font size. Adobe says not to override High-DPI scaling, which makes menus and panels misaligned or too small [D*] (https://helpx.adobe.com/photoshop/kb/high-dpi-scaling.html).
- **CSP** [D]:
  - Version 1.11.0 (28 Sept 2021) added "(Windows) Multi-DPI display support". Palettes on a display with different scaling resize automatically, and changes to Windows scaling apply live (https://www.clipstudio.net/en/dl/release_note/).
  - "Adjust interface for touch operations" offers Interface Scaling Small, Default or Large, after a restart (Preferences page).
- **Krita** [D]:
  - "Enable Hi-DPI support… an option because we are still experiencing bugs on Windows."
  - "Enable fractional scale factor" (5.0) "may cause some visual artifacts" (https://docs.krita.org/en/reference_manual/preferences/general_settings.html).
- **Aseprite** [D]:
  - "Screen Scaling" defaults to 200% and works in integer steps. A separate "UI Element Scaling" defaults to 100% (https://www.aseprite.org/docs/preferences/).
  - [I] On a 1920×1080 Kamvas, 200% screen scaling leaves a 960×540 logical workspace. 100% screen scaling with 200% UI elements keeps the sprite crisp but makes chrome 2× larger in physical px.
- **Developer note** [D]: per-monitor DPI awareness determines whether an app sees each monitor's real DPI. Unaware or system-aware apps get bitmap-stretched, which makes them blurry (dpi-related-apis page; https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows).

## 9. Express keys, pen buttons and keyboard-free work

- [D] Huion: defaults are Photoshop shortcuts; mappings can be per-app; "Switch Brush" and "Switch Screen" functions exist (sources in §1). Pen buttons work while hovering within 10 mm.
- PW517 default button assignments are **not documented**. On another Huion model, by default "the second button… will simulate a stylus flip to eraser" ([T] David Revoy, Kamvas Pro 19, 2024: https://www.davidrevoy.com/article1054/huion-kamvas-pro-19-review-on-gnulinux). Revoy maps a button to Ctrl for colour picking and keeps right-click [S].
- Common artist mappings [S/T]: undo; `[` and `]` for brush size; E for eraser; I for eyedropper; Ctrl+S; Ctrl+T; Space for pan.
  - One guide: start with "undo, decrease brush size ([), and increase brush size (])" (https://xp-pen.co.in/blogs/news/graphic-tablet-shortcut-keys-express-keys-setup-guide).
  - Photoshop's own defaults are `[` `]` for brush size, R for rotate view, Space for the temporary Hand tool, and I for eyedropper [D*] (https://helpx.adobe.com/content/dam/help/en/photoshop/using/default-keyboard-shortcuts/photoshop-keyboard-shortcuts.pdf).
- Why keyboard-free matters:
  - [R] Users reach for the keyboard for core shortcuts (Vogel 2010).
  - [D] Wacom tells users to place the keyboard so they avoid unnecessary reaching.
  - [I] A 13.3" display lying on the desk occupies the keyboard's usual spot. The keyboard ends up behind it (reaching over the display) or beside it (twisting).
  - [D] One-handed modifier substitutes already exist:
    - Adobe Fresco's Touch Shortcut, an on-screen circle for primary and secondary modifiers (https://helpx.adobe.com/fresco/using/getting-started-with-user-interface.html).
    - CSP's edge keyboard, where "Toggle… is recommended when drawing with a pen".

## 10. Dual-screen setups

- [D] The Huion driver maps the pen to **one** monitor in extended mode, and "Switch Screen" toggles it to another. Wacom's equivalent is "Display Toggle", which needs extended mode (e.g. https://cdn.wacom.com/u/support/wiki_migration/c/cd/dtk-1660_en.pdf).
- [D] Krita: Window Layouts remember monitors. Display priority order can cause pen offsets.
- [D*] Photoshop: undocked panels float freely and can sit on another monitor. Adobe advises connecting all monitors to the same GPU (https://helpx.adobe.com/photoshop/desktop/get-started/learn-the-basics/move-panels.html).
- [D] CSP: per-monitor DPI since 1.11. "Operate specified monitor" maps the tablet inside the app.
- [I] Implications for DoodleNook:
  - When the pen is mapped only to the Kamvas, panels on the main monitor are mouse-only territory.
  - Everything needed mid-stroke must be on the Kamvas, on the radial menu, or on keys.
  - The app must handle mixed DPI, e.g. Kamvas at 150% and monitor at 100%. A floating panel dragged between screens has to re-layout.

## 11. Vendor design statements about small or tablet screens

- [D] CSP Simple Mode, quoted in §6. CSP's "Switch to tablet interface" and touch Interface Scaling.
- [D] Krita: "not available for Android phones, only for tablets, because the user interface requires a large screen". 5.2.14 also made "popup menus appear at touch position" and added press-and-hold for the colour picker (https://krita.org/en/posts/2025/krita-5.2.14-released/, 11 Dec 2025).
- [D*] Adobe Fresco moved its toolbar and taskbar (iPad 7.3, May 2026) so that the most-used tools sit at the top and the canvas has more room (https://helpx.adobe.com/ph_en/fresco/using/whats-new.html).
- [D] Microsoft: "Always touch optimized or transition based on device signals" (touch-interactions page).
- [D*] Adobe: lock the workspace when using a stylus.

---

## SYNTHESIS

### A. Screen used by typical layouts on 1920×1080 [I]

Bar sizes are nominal assumptions in epx; they were not measured from any app.
- **Layout A, full desktop style:** menu 28 + options bar 36 + document tabs 28 + status bar 24 = 116 epx vertical. Tool column 48 + right dock 320 = 368 epx horizontal.
- **Layout B, moderate:** menu 28 + options 36 = 64 vertical. Tools 48 + dock 260 = 308 horizontal.
- **Layout C, compact:** a single 40 epx top row. 40 epx tool strip + 40 epx collapsed dock icon strip = 80 horizontal.
- **Layout D:** canvas only.

Canvas = (W − h) × (H − v), where W×H is the effective resolution.

| Layout | 100% (1920×1080 epx) | 125% (1536×864) | 150% (1280×720) |
|---|---|---|---|
| A | 1552×964 = **72.2%**; 237×147 mm | 1168×748 = **65.8%** | 912×604 = **59.8%**; 209×139 mm |
| B | 1612×1016 = **79.0%** | 1228×800 = **74.0%** | 972×656 = **69.2%** |
| C | 1840×1040 = **92.3%** | 1456×824 = **90.4%** | 1200×680 = **88.5%**; 275×156 mm |
| D | 100% | 100% | 100% |

- **Right dock width.** On the Kamvas a 320 epx dock is 49 mm (16.7% of the width) at 100%, 61 mm (20.8%) at 125%, and **73 mm (25.0%)** at 150%. On a 24" 1080p monitor at 100% the same dock is 89 mm but still only 16.7% of the width. On a 27" 1440p monitor it is 75 mm and Layout A leaves 78.7% for canvas.
- **Narrow canvas.** At 150%, Layout A leaves a 912 epx-wide canvas. That falls in Microsoft's "Medium" class (641–1007 epx).

### B. Guidelines for a 13.3" 1080p pen display

1. **Scaling.** Assume 125–150% and declare the app Per-Monitor-V2 DPI aware ([D] Microsoft; Photoshop and CSP both moved this way). Lay out in epx and test at 1280×720 effective. Offer an in-app density control (Compact 32 / Standard 40 / Pen 44–48 epx) on top of Windows scaling, so a 100% setup can still get usable targets [I].
2. **Target sizes, with Kamvas mm at 100/125/150%** [I from D]:
   - Frequent pen targets (tools, radial items, layer visibility): **≥40 epx** (6.1 / 7.7 / 9.2 mm). At 125% and above this meets Microsoft's 7.5 mm. At 100% use 48 epx (7.3 mm).
   - Dense secondary controls: ≥32 epx (4.9 / 6.1 / 7.3 mm), spaced ≥8 epx apart.
   - A 16 epx glyph can sit inside a larger hit area ("even if the visual is smaller" [D]).
   - Never make a hit area smaller than 24 epx (3.7 / 4.6 / 5.5 mm).
   - Sliders: give the thumb ≥32 epx and make the whole row scrubbable [I].
3. **Edges and corners.** On the pen display:
   - Do not rely on edge-flush "infinite" targets ([R] Farris/Walker apply to mice).
   - Keep frequent and high-consequence targets away from corners (±3 mm accuracy there [D]) and the extreme top (parallax and reach [R]).
   - Give edge targets ≥48 epx of depth.
4. **Occlusion-aware placement for right-handed users** [R Vogel 2009, Hancock & Booth]:
   - Put status, alerts, colour readouts and previews **top-left**, never bottom-right.
   - Open pop-ups and labels to the left or above the pen (south-west for menus).
   - Tools and frequently tapped controls can go in a left column. This also matches the left-bezel express keys.
   - A right dock should hold things you tap, not things you watch while drawing.
   - **Left-handed users:** offer one Handedness switch that mirrors docks, pop-up direction and scrollbar side, like Aseprite's "Mirrored Default" [D]. In the driver, rotate the Kamvas 180° so the keys sit on the right bezel [D].
5. **Hover.** Use the 10 mm hover for the brush outline and cursor only [D].
   - Show no tooltips over the canvas, and delay panel tooltips ([R] hover junk).
   - Turn off edge hover auto-reveal on the pen display by default ([I]).
   - Turn off Windows press-and-hold and flicks in the canvas window [D].
6. **Temporary palettes** [R/D]:
   - A pen-button or express-key **radial palette** at the pen, in the style of Krita's pop-up palette:
     - ≤8 on-axis items per ring.
     - Flick-ahead selection.
     - Labels drawn in the sectors the hand does not cover.
     - Optional HUD colour picker as in Photoshop.
   - A tracking or contextual mini-toolbar near the work for transform and selection.
   - Tab, or an express key, for canvas-only mode.
   - Lockable docks (Adobe's and CSP's lock pattern).
7. **Keyboard-free operation** [D/I]:
   - Ship Photoshop-compatible default shortcuts: B, E, I, `[` `]`, Ctrl+Z, Space or H, R. Huion's out-of-box press keys then work with no setup.
   - Make every command bindable to a single key combination.
   - Provide on-canvas sticky modifiers (Alt/Shift/Ctrl, as in Fresco and CSP).
   - Provide an explicit eraser toggle, because the PW517 has no eraser tip.
8. **Workspaces.** Ship presets "Pen display (compact)", "Desk (full)" and "Dual screen". Remember the layout per monitor, keyed by the monitor's identity, size and DPI (Krita's Window Layouts precedent [D]). Offer to switch automatically when the window moves to a display with a different DPI or size [I].

### C. Changes on a 24–27" desktop monitor with a mouse

- Physical size per px is 1.5–1.8× larger: 40 px is 9.3–11.1 mm at 100% [I].
- Compact 32 epx density is appropriate, since Microsoft designed it for pointer input [D*].
- Edge-flush toolbars and menus regain their Fitts's-law advantage [R].
- Full docks are cheap: Layout A keeps 72–79% of the screen for canvas.
- Hover-reveal and tooltips are fine with a mouse.
- The keyboard is available, so radial menus become optional accelerators rather than the main path.
- **Dual screen:** canvas and essential tools on the Kamvas (pen), reference, layers, history and navigator on the monitor (mouse), with per-monitor DPI handled live [I].

### Gaps

- Exact PW517 default button functions.
- Exact Windows-recommended scale for the GS1331's EDID (verify on the hardware).
- Adobe pages were read through the search index, not fetched directly.
- Pixel sizes of Photoshop's real bars were assumed, not measured.
