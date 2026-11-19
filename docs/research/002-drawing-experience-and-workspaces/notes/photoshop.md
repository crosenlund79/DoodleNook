# Adobe Photoshop (desktop, Windows): UX research for DoodleNook

Research date: 2026-11-19. Current release: **Photoshop 28.0 (November 2026)** [D: G14]. Adobe rewrote most User Guide pages in 2026 (new URLs under `helpx.adobe.com/photoshop/desktop/…`). Each source below gives its "Last updated" date. A few older pages survive only on a regional mirror (`/si/`, English text). Sources are listed by code at the end.

**Labels:** [D] Adobe documentation · [T] third-party tutorial or review ("Kost" is the personal blog of Julieanne Kost, an Adobe evangelist; I treat it as [T]) · [S] community post or opinion, with attribution · [I] my inference.

**Method caveat:** helpx pages were fetched with curl into `raw/photoshop/`. web.archive.org was unreachable. Adobe's old "Default keyboard shortcuts" page now redirects to a stub, so most single-letter tool shortcuts come from third-party lists.

---

## 1. Main interface organization
- **Elements.** The screen has an Application bar (workspace switcher and menus), the Tool options bar, a notifications icon, panels, the Toolbar and the document window [D: A1]. A search icon at the upper right opens Discover (Ctrl+F) [D: A4]. On Windows Photoshop supports Snap Layouts (Win+Z) [D: A1]. The Application Frame toggle exists only on Mac [T: K3].
- **Home screen at launch.** It shows feature highlights, tutorials, Recents and keyword filters, plus tabs for New file, Open, Home, Learn and Your files. The Home button in the Options bar returns to it; Esc leaves it [D: A2]. To skip it, go to Preferences > General, clear "Auto-Show the Home Screen" and select "Use Legacy New Document Dialog" [D: G20, May 2023]. Version 27.8 added a "suggestions" modal that users disliked; 27.10 added a "Hide suggestions" toggle [S: Adobe community manager, C10].
- **28.0 AI Assisted Editor.** This is a prompt-first mode with a toggle at the top-left of the canvas for switching to "Pro Editor" (full Photoshop). Eligible new users get a welcome flow into it. Turn it off at Preferences > Interface > Show AI Assisted Editor [D: G18, G19]. [I] A first launch can therefore steer a newcomer away from drawing.
- **Toolbar (left).**
  - Layout: single column by default; the chevrons at the top toggle a double column [T: K2, K3].
  - Tool groups: a slot holding several tools opens a flyout on click-and-hold or right-click/long-press. Since Nov 2026 the flyout can also open on hover (Preferences > Tools > Show Tool Groups on Hover in the Toolbar) [D: G2]. Alt-click on a slot cycles the nested tools [T: K2].
  - Customization: Edit > Toolbar lets you drag tools and groups, move rarely used tools to Extra Tools (shown in a "…" slot), Save/Load Preset, Restore Defaults and Clear Tools [D: G1]. The same dialog assigns tool letters [T: K3].
  - Missing tools: after updates or workspace switches tools can go missing; the fix is Restore Defaults or Reset Essentials [D: G16].
- **Options bar (top).** It holds the active tool's settings [D: A1]. At its far left, right-clicking the tool-preset icon offers Reset Tool / Reset All Tools [D: G17]. Preferences > Workspace > "Enable Narrow Options Bar" (more icons, less text) and "Large Tabs" help on small screens [T: K3].
- **Panels (right dock).** Panels group and stack, and collapse to icons via the double arrow; narrowing the dock hides labels [D: A5]. You dock a panel by dragging its tab until a blue highlight appears [D: A14]. "Auto-Collapse Iconic Panels" folds a panel back after use [T: K3]. Panels can also dock beside the toolbar on the left [T: K3].
- **Contextual Task Bar (CTB).**
  - What it is: a floating on-canvas bar of "next step" actions for selections, layers and part-of-image edits [D: A3, Apr 2026]. It was updated in 2026 and the Remove tool got its own CTB [D: G14].
  - When it shows: Adobe staff's beta announcement (May 2023) listed bars for opening an image, a new document, the Type tool, selections and Free Transform [S: Adobe staff, C3]. A third-party guide says it ships in 24.5+ [T: T11].
  - How to hide it: the "⋯" menu offers Pin bar position, Reset bar position and Hide bar; Window > Contextual Task Bar brings it back [S: Adobe staff, C3].
  - Complaints: it is "always in my way" and jumps back to its default spot on the next image [S: user, C3].
- **Document tabs.** Documents open as tabs. Drag a tab out to float it, or use Window > Arrange > Float in Window / Float All in Windows [D: A15]. "Open Documents as Tabs" is a preference, and Ctrl+Tab cycles documents [T: K3].
- **Status bar.** A zoom % field sits at the lower-left of the document window [D: A9]. The status menu can show document size, profile, dimensions, GPU Mode or Efficiency [D: G11, G12, G10]. Clicking and holding the status area shows width, height, channels and resolution [D: G11].

## 2. Drawing/painting workflow
- **Brush vs Pencil vs Mixer Brush.**
  - The Brush paints soft, anti-aliased strokes; the Pencil paints hard-edged, non-anti-aliased lines. Shift-click between two points draws a straight line. Airbrush mode builds up paint while you hold still. The Pencil has Auto Erase [D: B1].
  - Brush, Pencil, Color Replacement and Mixer Brush share the B slot; Shift+B cycles them [T: T5].
  - The Mixer Brush has a reservoir well and a pickup well. Alt-click loads paint. Its settings are Wet/Load/Mix plus Load/Clean and auto-load/clean, and Sample All Layers [D: B9].
- **Automatic new layer.** Since 25.0, painting on a smart object, text, shape or adjustment layer creates a new transparent layer instead of asking to rasterize [D: B1]. A General preference, "Create a new layer when brushing", controls this [D: B13].
- **Brush Preset picker.**
  - Opens from the options-bar brush thumbnail, or by right-clicking the canvas with any painting tool [T: K1].
  - Size and hardness changes made there are temporary until saved as a preset [D: B7, B14].
  - Comma and period (< >) step to the previous/next brush [T: K1].
- **Brushes panel.** Window > Brushes; hovering a brush shows a live stroke preview [D: B6].
- **Brush Settings panel.** Window > Brush Settings, or the toggle button in the options bar. It has checkbox groups (Shape Dynamics, Scattering, …) with per-group settings [D: B5]. Lock icons keep attributes when switching presets [T: K1].
- **Tool presets.** They store options-bar settings (opacity, flow, mode) on top of the brush. Window > Tool Presets > Create New Tool Preset [D: B7, G6].
- **Options-bar controls.** Adobe documents a Brush Preset picker (size/hardness), Brush Settings toggle, Mode, Opacity, Flow, Airbrush, Smoothing % with a gear, separate pressure buttons for opacity and for size, and a Symmetry "butterfly" [D: B1, B10, B12]. Exact left-to-right order is [I].
  - Opacity caps paint within one stroke; Flow is the build-up rate [D: B1].
  - Keys 1…0 set opacity (two quick digits give an exact value); Shift+digits set flow [D: B1]. With airbrush on, the two swap [T: K1]. Alt+digits set Smoothing [T: K1, T4].
  - The pressure buttons override the Brush Settings panel's opacity/size control [D: B14, 2023]. Both can get custom shortcuts ("Toggle Brush Pressure Controls Size/Opacity") [T: K1].
  - Brush angle: Photoshop 2020 (21.0, Nov 2019) added an options-bar angle widget. Left/Right arrows rotate the tip; with Shift it steps 15° [S: Adobe status reply, C8].
  - Mode: Shift+ +/− cycles blend modes; Alt+Shift+letter jumps to one [T: K1].
- **Painting-erasing toggle.** Holding the tilde (~) key makes the current brush erase, using the Clear mode [T: K1].
- **Symmetry.** Available on Brush, Pencil and Eraser. Types run from Vertical to Mandala, or use a custom path. "Toggle Symmetry Off/Last" can be bound to a key [D: B12].
- **Performance.** Smoothing at 0% is most responsive (default 10%). Default spacing is 25%. Complex brushes reduce responsiveness [D: B2].

## 3. Size/hardness gestures and HUD
- **Alt+right-click drag (Windows).** Left/right changes size; up/down changes hardness [D: B4, Feb 2026]. Kost's 2017 post gives Ctrl+Alt+right-click on Windows [T: K1]. [I] Both have worked across versions; Adobe now documents Alt+right-click.
- **Vertical drag → opacity.** Unchecking Preferences > Tools > "Vary Round Brush Hardness based on HUD vertical movement" makes vertical drag change opacity [T: K1; S: C1]. With it unchecked, the red overlay shows opacity but no longer previews hardness [S: user, C1]. Users report the behavior flipping after updates [S: C2].
- **On-canvas HUD.** A red brush-tip overlay plus a numeric readout box [S: C1]. Smooth Brush Resizing needs a working GPU [D: G12, G27].
- **Bracket keys.** [ and ] decrease/increase size; holding repeats. Both can be reassigned under Edit > Keyboard Shortcuts > Tools for keyboards without brackets [T: K1]. Shift+[ / ] ({ }) change hardness [T: T5].

## 4. Stroke smoothing
- **Basics.** Smoothing works on the Brush, Pencil, Mixer Brush and Eraser. The value runs 0–100; higher values smooth more "but may slightly delay the stroke preview" [D: B10]. Default is 10% [D: B2].
- **Modes (gear icon).** All four can be combined, except that Pulled String greys out the others [T: T4].
  - *Pulled String Mode*: "the stroke follows a trailing anchor, helping with tight curves" [D: B10]. In practice a circle appears, and paint starts only once the cursor leaves it, so you can pause or turn inside the circle without marking [T: T4].
  - *Stroke Catch-Up*: "finishes the stroke if you pause mid-draw" [D: B10]. The line closes on the cursor the slower you move, which suits sharp corners [T: T4].
  - *Catch-Up on Stroke End*: "smooths the stroke when you release" [D: B10]. When the pen lifts, the line jumps to the lift point [T: T4].
  - *Adjust for Zoom*: "keeps smoothing consistent at any zoom level" [D: B10]. The string lengthens when zoomed out [T: T4].
- **Brush leash.** A magenta "brush leash" shows the lag; Preferences > Cursors > "Show Brush Leash While Smoothing" turns it off [T: T4].
- **History.** Smoothing was introduced in CC 2018 (19.0); before that artists used Lazy Nezumi [T: T4]. An Adobe community expert still called Lazy Nezumi "the best add on for tablet users" [S: C12].
- **Locking.** Locking Smoothing in Brush Settings keeps the value across brushes [T: K1].
- **Complaint.** With catch-up, the stroke stops short of the cursor, so "you have to guess when you should stop" [S: user, C7].

## 5. Cursors and brush preview
- **Painting cursor options.** Edit > Preferences > Cursors:
  - Standard, Precise (crosshair), Normal Brush Tip (the area painted at ≥50% opacity) or Full Size Brush Tip.
  - Outline weight Thin/Normal/Bold/Extra Bold (24.1+).
  - Show Crosshair in Brush Tip; Show Only Crosshair While Painting (helps performance with large brushes).
  - "Other Cursors" set to Standard or Precise.
  - Sources: [D: B3, Oct 2026; B4].
- **Brush preview colour.** The Brush Preview colour (default red) is set in the same dialog [D: B4; T: K1].
- **Caps Lock.** It toggles a precise crosshair [D: B3]. Adobe's first troubleshooting item for "cursor is a crosshair" is Caps Lock being on [D: G17].

## 6. Eraser
- **Modes and options.** E selects the Eraser; Shift+E cycles Eraser, Background Eraser and Magic Eraser [T: T5]. Modes are Brush, Pencil and Block, with Size/Hardness/Opacity/Flow in Brush and Pencil modes. On the Background layer, or with transparency locked, erasing paints the background colour [D: B11]. Block mode is a square of fixed screen size at any zoom [T: K2].
- **Erase to History.** It is an options-bar checkbox, and holding Alt switches to it temporarily [D: B11]. The source is the opening snapshot, or any state set in the History panel [T: K2].
- **Shared features.** The Eraser supports smoothing and symmetry [D: B10, B12].
- **Switching back and forth.** Press B/E, or hold E for a spring-loaded switch [D: G3], or hold ~ to erase with the current brush [T: K1].
- **Pen eraser end.** This depends on the driver. Users report flaky pen↔eraser switching on Wacom and Huion, often traced to Windows Ink or app mapping [S: C14].
- **Non-destructive alternative: layer masks.** White reveals, black conceals, X swaps colours [D: L5].

## 7. Colour
- **Swatches.** Foreground/background swatches sit at the bottom of the toolbar, with swap and default (black/white) icons [D: C3]. X swaps them [D: L5]; D resets defaults [T: T5].
- **Color Picker dialog.** HSB, RGB, Lab, CMYK and hex. Choosing a component (H, S, B…) decides what the vertical bar controls [D: C5; S: C9].
- **Color panel.** Window > Color. It offers RGB/CMYK/HSB/Lab/Web sliders, a spectrum ramp, and Alt-click on the ramp to set the inactive colour [D: C2, Sep 2026]. Its panel menu also has a Color Wheel view [S: community expert, C9].
- **Swatches panel.** Supports groups and legacy swatches. Dropping a swatch on a pixel layer creates a fill layer [D: C4].
- **HUD colour picker.** Shift+Alt+right-click on the canvas (Windows). Choose Hue Strip or Hue Wheel at Preferences > General > HUD Color Picker. Holding Space locks shade while you change hue. It needs OpenGL [D: C1].
- **Eyedropper.**
  - Alt while painting gives a temporary eyedropper [D: C1, C2].
  - Sample Size ranges from Point to 101×101 average. Sample options: All Layers, Current Layer, Current & Below, and "no Adjustments" variants. Alt-click sets the background colour, and you can drag to preview [D: C3].
  - "Show Sampling Ring" (new vs current colour) is an options-bar checkbox; a bug in 22.0.1 hid it [S/T: C15].

## 8. Layers (everyday)
- **Layers panel.** Window > Layers: eye icon, drag to reorder, double-click to rename, "Create a new layer" button [D: L1]. Alt-clicking the new-layer button opens a dialog (name, colour label, mode, opacity, "Use previous layer to create clipping mask") [D: L1].
- **Shortcuts.** Ctrl+Shift+N new layer (with dialog), Ctrl+J layer via copy, Ctrl+G group, Ctrl+Alt+G clipping mask [T: T5].
- **Opacity, Fill and blend modes.** Opacity and Fill fields sit at the upper right with the blend-mode dropdown. Fill excludes layer effects. Groups default to Pass Through [D: L6]. Number keys change layer opacity when the active tool has no opacity setting [T: K1].
- **Locks.** A row of lock buttons: transparent pixels, image pixels, position, artboard auto-nest, all [D: L2].
- **Clipping masks.** Alt-click the line between two layers [D: L3].
- **Layer masks.** Click the mask button to reveal all; Alt-click to hide all; paint black/white [D: L4, L5].
- **Groups.** Layer > Group Layers [D: L7].

## 9. Selections and transforms
- **Marquee (M).** Shift for square/circle, Alt to draw from the centre, hold Space to move the marquee while drawing [D: S2].
- **Lasso (L).** The group holds Lasso, Polygonal, Magnetic and a new Selection Brush (coloured overlay instead of marching ants). Alt adds straight segments [D: S3].
- **W group.** Object Selection (hover highlights objects; "Object finder"), Quick Selection and Magic Wand [D: S1, S4]. Select Subject sits in the options bar [D: S5]. Ctrl+D deselects [D: S1].
- **Quick Mask (Q).** Toolbar button; red 50% overlay; paint white/black [D: S7; T: T5 for Q].
- **Select and Mask.** A separate taskspace with the Properties panel: seven view modes (F cycles, X toggles), Color Aware vs Object Aware refinement, edge tools [D: S6]. [I] Powerful but photo-oriented and heavy for a hobbyist.
- **Free Transform (Ctrl+T).**
  - Corner drag is proportional by default. A "Maintain Aspect Ratio" link button in the options bar sets that default, and Shift toggles it temporarily [D: T1].
  - Shift-rotate snaps to 15°; Ctrl distort; Ctrl+Shift skew; Ctrl+Alt+Shift perspective; Enter commits [D: T1].
  - Legacy behavior: Preferences > General > Legacy Free Transform [D: T1].
  - **Version conflict:** Adobe says "since 21.0 (Nov 2019)" [D: T1], while Fstoppers (Dec 2018) describes the change landing in CC 2019 (20.0) [T: T7]. [I] The default changed in 20.0 and the link button came later.
- **Warp.** Edit > Transform > Warp: presets, 3×3–5×5/custom grids, split warp [D: T2].

## 10. Canvas navigation
- **Pan.** Space gives a temporary Hand; H selects it [D: A8]. "Enable Flick Panning" (General) adds pan momentum [D: A8].
- **Zoom.**
  - Z selects Zoom; Alt zooms out. Click-and-hold zooms continuously (needs Animated Zoom). Scrubby Zoom: drag right to zoom in, left to zoom out. Range runs from 1 px to 3200% [D: A9]. Scrubby Zoom needs the GPU [D: G12].
  - Preferences include Zoom with Scroll Wheel and Zoom Clicked Point to Center [D: A9].
  - Ctrl+Space (zoom in) and Ctrl+Alt+Space (zoom out) are temporary; dragging with them scrubs [T: T10].
  - Ctrl+0 fits on screen; Ctrl+1 shows 100% [T: T10]. Double-clicking the Hand gives Fit; double-clicking Zoom gives 100% [D: A9].
- **Bird's-eye view.** Hold H and press on the canvas: the view zooms out to fit. Drag to the target and release to return at the original zoom [D: A9]. It needs the GPU [D: G12].
- **Rotate View.**
  - Nested under the Hand tool. Drag to rotate, or type a Rotation Angle; a compass shows north; Reset View restores. Requires OpenGL [D: A8].
  - R selects it, holding R spring-loads it, Shift snaps to 15°, Esc or double-clicking the tool resets [T: K2, T9]. "Rotate All Windows" applies the rotation to all open documents [T: T9].
- **Flip Canvas view.** View > Flip Horizontal is a non-destructive view flip with no default shortcut; assign one in Edit > Keyboard Shortcuts [S: C5].
  - Since about 22.5 the flip mirrors around the window centre, so an off-centre canvas "jumps across the screen". Concept artists have stayed on old versions because of it [S: C4, threads 2021–Oct 2026].
  - Workaround: Preferences > Tools, untick Overscroll [S: community expert, C5].
  - The destructive Image > Image Rotation > Flip Canvas Horizontal is "very laggy" with many layers [S: C5].
- **Touch.** Two-finger pan/pinch/rotate; a two-finger double-tap resets the view [D: G25, 2023]. [I] Not relevant to a Kamvas 13, which has no touch.

## 11. Keyboard shortcuts
- **Single-letter tools.** V, M, L, W, C, I, B, E, G, H, R, Z, Q, D, X and others [T: T5]. Shift+letter cycles a group while Preferences > Tools > "Use Shift Key for Tool Switch" is on; with it off, the bare letter cycles [T: K2, K3].
- **Spring-loaded tools.** Hold a tool key to switch temporarily; releasing returns to the previous tool. The example is holding V while using the Brush. Preferences > Tools has an on/off switch and a delay slider (default 200 ms) [D: G3].
- **Customization.**
  - Edit > Keyboard Shortcuts (Alt+Shift+Ctrl+K). Shortcuts For: Application Menus / Panel Menus / Tools / Taskspaces [D: G7, G8].
  - Conflicts raise an alert with Accept / Accept and Go To Conflict. Named sets, Use Default, Delete Shortcut, and "Summarize" exports HTML [D: G8].
  - Tool-option commands sit at the bottom of the Tools list (pressure toggles, symmetry, airbrush, brush size keys, Mixer Brush load/clean) [T: K1; D: B12].
  - Edit > Menus hides or colours menu items [D: G26].
- **Shortcuts stop working.** Tablet drivers and other apps are known causes [D: G28].

## 12. Tablet interactions
- **Pen controls in Brush Settings.**
  - Shape Dynamics, Color Dynamics and Transfer each have a Control menu: Off, Fade, Pen Pressure, Pen Tilt, Stylus Wheel, Rotation, plus Initial Direction and Direction for angle.
  - Settings include Minimum Diameter, Tilt Scale, Minimum Roundness and Brush Projection (tilt/rotation reshape the tip). Transfer covers Opacity and Flow.
  - A warning icon appears if no compatible tablet is present.
  - Source: [D: B8, Oct 2026].
- **Options-bar pressure toggles.** [D: B1]
- **Windows Ink vs WinTab.**
  - Windows Ink is the default on Windows 10+, and "Adobe plans on shifting resources to improve the Windows Ink experience". Adobe "does not test or officially support non-Wacom third-party tablets" and links to Huion support [D: G21, Feb 2024].
  - To force WinTab, create `PSUserConfig.txt` with `# Use WinTab` / `UseSystemStylus 0` in `%AppData%\Roaming\Adobe\Adobe Photoshop <ver>\Adobe Photoshop <ver> Settings\` and restart [D: G22, Jun 2025; G23, Oct 2026]. Tablet vendors add: also untick Windows Ink in the driver [T: T6].
  - Windows 11 shows modifier-key "toasts" while you use a pen; turn them off at Settings > Bluetooth & devices > Pen & Windows Ink > untick "Display additional keys pressed when using my pen". Also disable press-and-hold right-click [D: G21].
- **Global pressure curve: none.** Adobe documents only per-brush dynamics and points to driver settings (Wacom Tablet Properties) [D: B8, G21]. [I] There is no app-wide pressure curve; curves live in the tablet driver or tools like Lazy Nezumi [S: C12].
- **Huion reports.**
  - A Huion Kamvas 13 owner lost pressure in 25.0 with lag, and neither the Windows Ink toggle nor the WinTab file fixed it [S: C6].
  - A Nov 2026 report describes Huion + Windows Ink getting the Zoom tool "stuck" in 27.11 [S: C6].
- **Pen side buttons.** Commonly mapped to right-click, which opens the Brush Preset picker or context menu at the cursor [T: K1]. [I] That makes the picker a one-button pen gesture.

## 13. Workspace customization
- **Built-in workspaces.** Window > Workspace or the Application-bar switcher. Presets include Essentials (default), Photography and Painting [D: A11]. Reset <name> restores one; Preferences > Workspace > Restore Default Workspaces resets them all [D: A12].
- **New Workspace.** It captures Panel Locations, Keyboard Shortcuts and Menus [D: A10], and toolbar settings too [T: K3]. Workspaces can get their own shortcut [T: K3]. Essentials' default panel arrangement differs by monitor resolution [T: K3].
- **Lock Workspace.** Window > Workspace > Lock Workspace stops docked panels from moving but not resizing, and doesn't hold floating panels [S: community expert, C13].
- **Panel groups.** Drag tabs to group; collapse to icons [D: A5, A16].

## 14. Distraction-free
- **Tab and Shift+Tab.** Tab hides everything, including the toolbar and options bar; Shift+Tab hides panels but keeps the toolbar and options bar [D: A6]. With Preferences > Workspace > Auto-Show Hidden Panels on, hovering the window edge temporarily reveals them [D: A6].
- **Screen modes (F).** F cycles Standard / Full Screen With Menu Bar (grey) / Full Screen (black, no UI) [D: A8]. In Full Screen, Tab still toggles panels; right-click the pasteboard to change its colour [T: K3].
- **Hiding extras.** Hiding Rulers (Ctrl+R) and Extras (Ctrl+H) improves painting responsiveness [D: B2].

## 15. Undo/redo
- **Commands.** Ctrl+Z steps back through multiple states; Ctrl+Shift+Z redoes. The Edit menu names the action [D: G4]. Edit > Keyboard Shortcuts > "Use Legacy Undo Shortcuts" (restart) restores Ctrl+Z as a toggle [D: G4]. The change shipped with CC 2019 (20.0, Oct 2018); the old step-back key was Ctrl+Alt+Z [T: T7].
- **History states.** Preferences > Performance: **default 50, maximum 1,000** [D: G10]. The History panel page says "lists 20 states" by default [D: G5], which conflicts; I trust G10. Every edit (including each stroke) is a separate state. Snapshots, Allow Non-Linear History and "Make Layer Visibility Changes Undoable" are available [D: G5].
- [I] In continuous sketching, the 50-state default covers only the last ~50 strokes. Snapshots or the History Brush cover deeper recovery.
- **Fade.** Edit > Fade (Shift+Ctrl+F), right after a stroke, scales that stroke's opacity or blend mode, like a partial undo [T: T13].

## 16. Discoverability
- **Discover panel.** Help > Photoshop Help, Ctrl+F, or the search icon. It finds tools, tutorials, quick actions and help (online only), and can activate or highlight a tool [D: A4, G17].
- **Tooltips.** Standard tooltips show name and shortcut; Rich Tooltips add a description and short video. Both are switched at Preferences > Tools [D: G2].
- **Other routes.** Hover flyouts [D: G2], the Home "Learn" tab [D: A2], and the CTB "next steps" [D: A3]. Extra Tools and the missing-tools article cover tools users can't find [D: G1, G16].

## 17. How quickly one can start drawing
- **New document.** Launch → Home → New file (Ctrl+N) opens the New Document dialog.
  - Categories: Photo, Print, **Art & Illustration**, Web, Mobile, Film & Video, plus Recent and Saved.
  - The Preset Details pane has W/H/units, orientation, artboards, colour mode, resolution and Background Contents (White / Background Color / Transparent / custom).
  - Custom presets can be saved.
  - Source: [D: G15, Sep 2026]. The legacy dialog is an option [D: G20].
- **Then B and paint.** Default smoothing is 10% [D: B2].
- [I] The minimum path is about Ctrl+N → Enter → B → draw. Friction comes from the Home screen, possible AI-mode onboarding for new users [D: G19], the CTB appearing on new documents [S: C3], and whether the chosen preset tip uses pressure.

## 18. UI scaling on Windows
- **Per-monitor scaling.** On Windows 10 Creators Update and later, Photoshop follows per-monitor Windows scaling from 100% to 400% in 25% steps. The Preferences > Interface > UI Scaling (Auto/100%/200%) setting only affects certain dialogs on Win10+ [D: A7].
- **Font size.** UI Font Size (Tiny/Small/Medium/Large) plus "Scale UI To Font", then restart [D: A13].
- **Minimum resolution.** Photoshop 28.x's Windows minimum is "1280x720 @ 1x OR 1920x1080 @ 150%" [D: G13]. **A Kamvas 13 at 150% is exactly the minimum spec.**
- **History.** Before per-monitor support, Adobe noted that at 1080p@200% "the Tools panel extends off the bottom of the screen" and recommended double-column [S: Adobe staff, C11]. Users asked for 150% for years [S: C16].
- [I] At 150% the logical workspace is about 1280×720. The options bar, a right dock and the toolbar leave little canvas, so Tab/Shift+Tab, Narrow Options Bar and a slim custom workspace matter.

## 19. Frustrations relevant to a hobbyist drawer
- **Tablet reliability on non-Wacom hardware.** Adobe officially supports only Wacom [D: G21]. Huion users report pressure loss and bugs [S: C6]. The Windows Ink vs WinTab choice exposes driver plumbing to users [D: G21, G22].
- **Flip View jumps the canvas.** Long-running complaints [S: C4, C5].
- **CTB obstructs the canvas** and forgets its position [S: C3].
- **Smoothing catch-up feels unpredictable** [S: C7]. Smoothing adds latency by design [D: B10].
- **HUD vertical drag** silently switches between hardness and opacity after updates [S: C2].
- **Hidden tools and resets.** Tools hide in flyouts or vanish after updates [D: G16]. Ctrl+Z semantics changed in 2018 and transform defaults changed in 2018–19, breaking muscle memory [T: T7].
- **Complexity.** Brush Settings has many groups and jitter controls [D: B8]. Adobe itself says complex brushes slow painting [D: B2]. Select and Mask is heavy [D: S6].
- **GPU dependence.** Rotate View needs OpenGL [D: A8]. Bird's-eye, Scrubby Zoom and Smooth Brush Resizing fail without a GPU [D: G12].
- **Home screen changes.** The 27.8 Home "suggestions" modal [S: C10] and AI-first onboarding [D: G19] add steps before drawing.
- **Default brushes and cost.** One reviewer finds Photoshop's default brushes unappealing and Clip Studio Paint better value for drawing [S: Parka Blogs, T12].

## 20. What Photoshop does especially well for drawing, and why
- **Single-letter tools with Shift-cycling.** One stable letter per tool family builds motor memory. Groups keep the alphabet small, so related tools share a key [T: K2]. [I]
- **Spring-loaded tools.** Hold Space, R, H, Z or E for a moment, then release to return. The "mode" lasts only as long as the key is held, so the user is never left in the wrong tool. Tap vs hold is separated by a 200 ms threshold [D: G3]. Bird's-eye on H [D: A9] and R for rotate [T: K2] make navigation one-handed while the pen stays on the canvas. [I]
- **On-canvas size/hardness drag.** Alt+right-click [D: B4] keeps the eyes on the stroke area and shows the actual tip footprint (red overlay) rather than an abstract number [S: C1]. A two-axis drag maps two properties to one gesture. [I]
- **Alt for eyedropper; numbers for opacity.** These are modifier-held quasi-modes with zero travel [D: C1, B1]. [I]
- **Pressure toggles in the options bar** let you flip a brush between pressure-sized and pressure-opaque without opening Brush Settings [D: B1]. [I]
- **Progressive disclosure for brushes.** Picker (size/hardness), then the Brushes panel, then Brush Settings, then tool presets. Casual users never need the deep layers [D: B5–B7]. [I]
- **Tab hide and auto-show edges** give instant focus with zero-cost recovery [D: A6]. [I]
- **Stable conventions across decades.** Bracket sizing, X/D colours and Ctrl+T are what other apps (Krita, CSP) emulate. [I]

---

## Takeaways for DoodleNook [I]
1. Copy the muscle memory: B/E/I/H/R/Z/V/M/L, Space/H/R/Z spring-loading with a ~200 ms hold threshold, [ ] / Shift+[ ], Alt eyedropper, X/D, number-key opacity, Ctrl+T, Tab/Shift+Tab, F.
2. Provide a built-in pen-friendly Alt+right-drag HUD, but expose "vertical = hardness/opacity" visibly in the HUD itself and keep it stable.
3. Make flip-view **in place** (around the canvas centre), with a default shortcut; this is a known Photoshop pain point.
4. Offer a global pressure curve and an in-app pen test; Photoshop lacks both.
5. Use one clear smoothing model (a string with a visible radius) and avoid catch-up surprises.
6. Design the default layout for 1920×1080 @150% (about 1280×720 logical) on day one; Photoshop treats that as its bare minimum.
7. Contextual bars should never overlap the drawing area by default and should remember their position.

---

## Sources (codes → URL, page date)
**Adobe [D]** (prefix `https://helpx.adobe.com/photoshop/desktop/` unless full URL given)
- A1 get-started/learn-the-basics/workspace-overview.html (Nov 10 2026)
- A2 get-started/learn-the-basics/homescreen-overview.html (Feb 23 2026)
- A3 get-started/learn-the-basics/boost-workflows-with-the-contextual-task-bar.html (Apr 28 2026)
- A4 get-started/learn-the-basics/access-discover-panel.html (Feb 23 2026)
- A5 get-started/learn-the-basics/collapse-expand-icons.html (Feb 23 2026)
- A6 get-started/learn-the-basics/hide-show-panels.html (Feb 23 2026)
- A7 get-started/learn-the-basics/high-density-monitor-support-and-per-monitor-scaling.html (Feb 23 2026)
- A8 get-started/learn-the-basics/navigate-and-pan-images.html (Oct 14 2026)
- A9 get-started/learn-the-basics/zoom-images.html (Oct 6 2026)
- A10 get-started/learn-the-basics/save-custom-workspaces.html (Feb 23 2026)
- A11 get-started/learn-the-basics/switch-workspaces.html (Feb 23 2026)
- A12 get-started/learn-the-basics/restore-workspaces.html (Feb 23 2026)
- A13 get-started/learn-the-basics/change-text-size.html (Feb 23 2026)
- A14 get-started/learn-the-basics/dock-undock-panels.html (Feb 23 2026)
- A15 get-started/learn-the-basics/rearrange-document-windows.html (Feb 23 2026)
- A16 get-started/learn-the-basics/manipulate-panel-groups.html (Feb 23 2026)
- B1 apply-painting-techniques/brushes-presets/paint-with-brush-or-pencil-tool.html (Oct 4 2026)
- B2 apply-painting-techniques/brushes-presets/optimize-painting-and-cloning-using-brush-settings.html (Oct 4 2026)
- B3 apply-painting-techniques/brushes-presets/set-cursor-preferences-for-painting-tools.html (Oct 7 2026)
- B4 get-started/settings-and-preferences/change-tool-pointers.html (Feb 23 2026)
- B5 apply-painting-techniques/brushes-presets/display-brush-panel-brush-options.html (Feb 23 2026)
- B6 apply-painting-techniques/brushes-presets/select-a-preset-brush.html (Feb 23 2026)
- B7 apply-painting-techniques/brushes-presets/get-started-with-brush-presets.html (Feb 23 2026)
- B8 apply-painting-techniques/fill-objects-selections-layers/brush-dynamics-settings.html (Oct 15 2026)
- B9 apply-painting-techniques/create-fill-with-patterns/paint-with-mixer-brush.html (Oct 4 2026)
- B10 repair-retouch/clean-restore-images/create-smoother-more-polished-brush-strokes-with-stroke-smoothing.html (Feb 23 2026)
- B11 repair-retouch/clean-restore-images/erase-parts-of-an-image-with-the-eraser-tool.html (Feb 23 2026); also get-started/set-up-toolbars-panels/restore-image-parts.html
- B12 apply-painting-techniques/create-fill-with-patterns/paint-symmetrical-patterns.html (Oct 4 2026)
- B13 apply-painting-techniques/fill-objects-selections-layers/create-a-new-layer-when-brushing.html (Feb 23 2026)
- B14 https://helpx.adobe.com/si/photoshop/using/painting-tools.html (old English page on regional mirror, Oct 27 2023)
- C1 adjust-color/choose-colors/choose-a-color-while-painting.html (Feb 23 2026)
- C2 adjust-color/choose-colors/choose-colors-in-the-color-panel.html (Sep 30 2026)
- C3 adjust-color/choose-colors/set-foreground-and-background-colors.html (Feb 23 2026)
- C4 adjust-color/choose-colors/work-with-the-swatches-panel.html (Oct 6 2026)
- C5 adjust-color/choose-colors/choose-colors-with-the-adobe-color-picker.html (Feb 23 2026)
- L1 create-manage-layers/create-layer-compositions/create-layers-and-layer-groups.html (Sep 28 2026); create-manage-layers/get-started-layers/work-with-the-layers-panel.html
- L2 create-manage-layers/transform-manipulate-layers/lock-layers.html (Oct 6 2026)
- L3 create-masks/layer-masks/create-and-manage-clipping-masks.html (Sep 29 2026)
- L4 create-masks/layer-masks/add-layer-masks.html (Feb 23 2026)
- L5 create-masks/layer-masks/edit-and-refine-layer-masks.html (Sep 29 2026)
- L6 create-manage-layers/apply-layer-effects/set-layer-opacity-and-blending-modes.html (Sep 28 2026)
- L7 create-manage-layers/transform-manipulate-layers/group-and-ungroup-layers.html (Feb 23 2026)
- S1 make-selections/get-started-selections/selection-tools-overview.html (Sep 28 2026)
- S2 make-selections/get-started-selections/make-selections-with-marquee-tools.html (Sep 28 2026)
- S3 make-selections/get-started-selections/isolate-specific-areas-in-images-with-lasso-tool.html (Sep 28 2026)
- S4 make-selections/get-started-selections/select-objects-with-object-selection-tool.html (Feb 23 2026)
- S5 make-selections/automatic-color-based-selections/detect-subject-using-select-subject.html (Feb 23 2026)
- S6 make-selections/refine-modify-selections/refine-your-selection-and-mask.html (Feb 23 2026)
- S7 create-masks/layer-masks/create-a-temporary-quick-mask.html (Oct 6 2026)
- T1 create-manage-layers/transform-manipulate-layers/scale-layers-in-photoshop.html (Oct 6 2026)
- T2 effects-filters/artistic-stylize-filters/reshape-and-distort-images-with-transform-warp.html (Feb 23 2026)
- G1 get-started/set-up-toolbars-panels/customize-the-toolbar.html (Nov 10 2026)
- G2 get-started/set-up-toolbars-panels/show-or-hide-tool-tips.html (Nov 10 2026)
- G3 get-started/set-up-toolbars-panels/spring-loaded-shortcuts.html (Feb 23 2026)
- G4 get-started/set-up-toolbars-panels/use-undo-redo-commands.html (Feb 23 2026)
- G5 get-started/set-up-toolbars-panels/history-panel-overview.html (Feb 23 2026)
- G6 get-started/set-up-toolbars-panels/create-tool-preset.html (Feb 23 2026)
- G7 get-started/settings-and-preferences/view-keyboard-shortcuts.html (Sep 8 2026)
- G8 https://helpx.adobe.com/photoshop/using/customizing-keyboard-shortcuts.html (May 24 2023)
- G10 get-started/settings-and-preferences/configure-photoshop-performance-preferences.html (Oct 4 2026)
- G11 get-started/settings-and-preferences/use-the-info-panel-and-document-status-bar.html (Oct 5 2026)
- G12 get-started/technical-requirements-installation/photoshop-and-graphics-processor-gpu-card-usage.html (Feb 23 2026)
- G13 get-started/technical-requirements-installation/adobe-photoshop-on-desktop-technical-requirements.html (Nov 10 2026)
- G14 whats-new/whats-new-in-adobe-photoshop-on-desktop.html (Nov 10 2026)
- G15 create-open-import-images/create-images/create-documents-using-presets.html (Sep 28 2026)
- G16 troubleshoot/tool-feature-issues/missing-tools.html (Oct 4 2026)
- G17 troubleshoot/tool-feature-issues/fix-unexpected-tool-behavior-in-photoshop.html (Oct 6 2026)
- G18 generative-ai/ai-assisted-editor-faq.html (Nov 16 2026)
- G19 generative-ai/use-ai-assisted-mode.html (Nov 10 2026)
- G20 https://helpx.adobe.com/photoshop/kb/troubleshoot-home-screen-issues.html (May 24 2023)
- G21 https://helpx.adobe.com/photoshop/kb/tablet-support-faq-photoshop.html (Feb 8 2024)
- G22 https://helpx.adobe.com/si/photoshop/kb/enable-optional-extensions-photoshop-cc.html (English content, Jun 19 2025)
- G23 app-integrations/work-with-optional-extensions.html (Oct 4 2026)
- G25 https://helpx.adobe.com/photoshop/using/touch-gestures.html (May 24 2023)
- G26 https://helpx.adobe.com/photoshop/using/panels-menus.html (May 24 2023)
- G27 troubleshoot/performance-stability-issues/resolve-graphics-processor-gpu-and-graphics-driver-issues.html
- G28 troubleshoot/tool-feature-issues/keyboard-shortcuts-not-working.html (Feb 23 2026)

**Third-party [T]**
- K1 Kost, "20 Brush and Painting Tool Shortcuts" (2017): https://jkost.com/blog/2017/05/brushes-and-painting-tool-shortcuts-in-photoshop-cc.html
- K2 Kost, "10 Little Known Tips for Working with Tools" (Jan 2023): https://jkost.com/blog/2023/01/10-little-known-tips-for-working-with-tools-in-photoshop.html
- K3 Kost, "30 Tips to Customize the Photoshop Interface" (May 2021): https://jkost.com/blog/2021/05/30-ways-to-customize-the-photoshop-interface.html
- T4 digitalEPIGRAPHY, brush-stroke smoothing (CC 2018): https://www.digital-epigraphy.com/tutorials/the-most-useful-new-features-of-photoshop-cc-using-brush-stroke-smoothing-for-digital-inking
- T5 Hongkiat shortcut list (updated Jun 2020): https://www.hongkiat.com/blog/adobe-photoshop-keyboard-shortcuts/
- T6 Xencelabs WinTab article (Feb 2021): https://solutions.xencelabs.com/en/support/solutions/articles/67000524156-how-do-i-set-adobe-ps-to-use-wintab-for-my-windows-computer-
- T7 Fstoppers, legacy transform/undo in CC 2019 (Dec 7 2018): https://fstoppers.com/originals/how-bring-back-legacy-transform-tool-and-undo-shortcut-photoshop-cc-2019-314464
- T9 Photoshop Essentials, Rotate View tool (search-result summary, not fetched; site blocks bots): https://www.photoshopessentials.com/?p=19800
- T10 bwillcreative zoom shortcuts (search-result summary): https://www.bwillcreative.com/?p=71561
- T11 TeachUcomp, Contextual Task Bar (search-result summary): https://www.teachucomp.com/how-to-use-the-contextual-task-bar-in-photoshop-instructions/
- T12 Parka Blogs, CSP vs Photoshop for drawing: https://www.parkablogs.com/content/clip-studio-paint-vs-photoshop-drawing
- T13 bwillcreative, Edit > Fade (search-result summary): https://www.bwillcreative.com/?p=72252

**Community [S]**
- C1 HUD vertical-drag pref (Jan 2019): https://community.adobe.com/questions-712/photoshop-2019-show-brush-opacity-and-hardness-at-the-same-time-not-possible-anymore-1074370
- C2 Hardness→opacity flip (Nov 2024): https://community.adobe.com/questions-712/brush-alt-setting-changed-from-hardness-to-opacity-1174765
- C3 Adobe staff CTB beta announcement and replies (May 2023): https://community.adobe.com/questions-700/contextual-task-bars-now-in-photoshop-beta-668168
- C4 "View > Flip Horizontal is broken" (2021–Oct 2026): https://community.adobe.com/bug-reports-711/p-view-flip-horizontal-is-broken-in-newer-versions-of-photoshop-658818
- C5 Flip view and Overscroll workaround (May 2024): https://community.adobe.com/questions-712/please-fix-view-flip-horizontal-to-flip-canvas-on-it-s-vertical-axis-to-maintain-it-s-location-1167997
- C6 Huion pen pressure in 25.0+ (Kamvas 13 user; Nov 2026 zoom bug): https://community.adobe.com/bug-reports-711/p-pen-pressure-not-working-with-huion-devices-with-photoshop-v-25-0-above-656831
- C7 Smoothing catch-up complaint: https://community.adobe.com/bug-reports-711/p-smoothing-catch-up-behaves-awkward-659486
- C8 Brush-tip rotation released in 21.0: https://community.adobe.com/feature-requests-713/p-rotate-brush-tip-on-the-fly-keyboard-shortcut-mouse-wheel-or-click-drag-653321
- C9 Color Wheel in Color panel (Feb 2026): https://community.adobe.com/questions-712/how-do-i-get-the-color-wheel-back-for-color-selection-1549539
- C10 27.8 Home screen / 27.10 "Hide suggestions": https://community.adobe.com/bug-reports-711/p-ps-v27-8-new-home-screen-1628247
- C11 Adobe staff, 200% UI scaling on Windows (historic): https://community.adobe.com/bug-reports-711/p-introducing-scale-the-ui-200-for-high-density-displays-for-windows-659749
- C12 Pen pressure too sensitive (Huion user; Lazy Nezumi; WinTab): https://community.adobe.com/questions-712/pen-pressure-too-sensitive-1068964
- C13 Lock Workspace limits (Dec 2024): https://community.adobe.com/questions-712/lock-workspace-doesn-t-do-what-it-says-1175066
- C14 Pen/eraser-end switching trouble: https://community.adobe.com/questions-712/wacom-cintiq-13hd-pen-has-trouble-switching-between-pen-and-eraser-ends-1073916
- C15 Sampling ring missing in 22.0.1: https://community.adobe.com/t5/photoshop-ecosystem-discussions/color-picker-does-not-show-sampling-ring-preview-ps-22-0-1/td-p/11633307
- C16 150% UI scaling request: https://community.adobe.com/t5/photoshop-ecosystem-discussions/150-ui-scaling-option-for-photoshop/m-p/6831225
