# Krita (5.3.x on Windows): UX research notes for DoodleNook

**Version context.** The current stable release is Krita 5.3.4.1 ([dl]). Krita 5.3 and 6.0 (the Qt6 build of the same features) shipped on 5 Feb 2026 ([rn53]). Manual text comes from the official Krita Manual EPUB, downloaded 2026-11-19 from docs.krita.org.

**Labels.** [D] = Krita docs or release notes. [T] = third party, such as the KDE bug tracker or tutorials. [S] = subjective or community opinion, with the person named. [I] = my inference. Keys in brackets resolve to the source list at the end.

---

## 1. Main interface

- **Default layout** [D start, screenshot of a 5.x build]:
  - A menu bar, with one toolbar row under it. From left to right the toolbar holds: New/Open/Save, Undo/Redo, gradient and pattern swatches, foreground/background (FG/BG) colour swatch, brush-editor button, preset chooser, blend-mode dropdown ("Normal"), eraser-mode toggle, preserve-alpha toggle, reload-preset button, **Opacity** slider, **Size** slider, horizontal and vertical mirror-painting toggles (each with a dropdown), one more view toggle (its icon looks like crop marks; I could not identify it), and the **workspace chooser** at the far right.
  - A two-column **toolbox on the left**.
  - On the right, from top to bottom: tabbed **Advanced Color Selector / Tool Options**, then **Layers**, then **Brush Presets**. The presets docker has a tag dropdown, a Search field and a "Filter in Tag" option.
  - A status bar showing selection display mode, current preset, colour space, image size and a zoom control [D nav].
- The 5.2 default dockers are Layers, Tool Options, Brush Presets and Advanced Color Selector. The toolbar "is static: it never changes, no matter which tool is selected" [T vc].
- **Tool Options** sit in a docker by default. They can be moved into the toolbar, where they open with the `\` key [D general].
- **Toolbars.** There are two toolbars, "Main" and "Brushes and Stuff", edited via Settings ▸ Configure Toolbars…. You cannot create new toolbars. Toolbars can be moved by their left handle (4.2+). Items that don't fit collapse behind a chevron [D settings]. Since 5.3 a "Docker Box" item can pop dockers out of the toolbar [D rn53].
- **Dockers** [D nav]:
  - Shown via Settings ▸ Dockers. There are about 35 dockers [D dockers].
  - Dock one by dragging it to any canvas edge. Hold Ctrl before dragging to keep it floating.
  - The "x" closes a docker. Settings ▸ Show Dockers hides all of them at once [D settings].
  - Right-clicking any docker title bar or empty toolbar area lists the dockers, including Toolbox [D faq].
  - **Docker locks** returned in 5.0, "helpful for those with sensitive tablet stylii, as sometimes a subtle stroke can undock a docker" [D rn50]. The padlock shows at the left of each docker title bar in the manual screenshot [D start].
- **Welcome page** (when no document is open) [D welcome]:
  - Start: New File (Ctrl+N), Open File (Ctrl+O).
  - Recent Documents.
  - Community links.
  - News (off by default).
  - The whole page is a drag-and-drop target for opening files.
- **Workspaces** [D ws]:
  - Reached from the toolbar chooser or Window ▸ Workspace. Each workspace is a saved docker arrangement: grouping and placement only.
  - You can save the current layout under a name, import `.kws` files, or delete one. There is no workspace docker.
  - "Window Layouts" save multi-window, multi-monitor setups. They include the options "Primary Workspace Follows Focus" and "Show Active Image In All Windows".
  - [S AhabGreybeard] Workspaces do not store toolbar contents [T ka-tablet].
- **Shipped workspaces:**
  - The docs mention **Default** and **Animation** (Window ▸ Workspace ▸ Animation adds the Timeline docker) [D anim].
  - A 2014 krita.org post describes "Big_Paint" as the "classic view for painting on big screen resolution (1920x1080)". It also lists Big_Paint_2 (wide dockers on both sides), Big_Vector, Small_Paint (a minimal set for very small screens, then the first-start default), Small_Vector and VFX_Paint [D ws2014, old].
  - Big_Paint_2 still existed in 5.1.5 and 5.2.9 [T b472814]. A forum user counts "nine default workspace files" [S Xue555, T ka-restore].
  - The exact 5.3 list is unverified [I].
- **Themes:** Dark (default), Darker, Blender, Bright, Neutral [D settings].

## 2. Pop-up Palette

- **Opening it.** Right-click on the canvas opens a circular palette **at the cursor or brush-tip position** [D popup, nav]. Krita calls it "unique to Krita" [D popup]. The SAI guide says it is "by default on the right click" [D sai].
- **Contents** [D popup + screenshot]:
  - FG/BG indicators at top-left.
  - A canvas-rotation ring.
  - A ring of brush presets from the selected **tag**. By default this is the "My Favorite" tag, showing the first 10 presets.
  - A central colour selector (hue ring plus S/V triangle).
  - A **colour-history ring**.
  - A tag-selector button.
  - A ">" button that opens **dockers on canvas**. By default this is the On-Canvas Brush Editor, but any docker can be added (5.3).
  - A zoom slider and a **100%** button.
  - A **canvas-only** button and a **mirror-view** button.
  - A "Clear colors" button, visible in the screenshot.
- **Settings** (Settings ▸ Configure Krita ▸ Pop-up Palette, 5.0+) [D popset]:
  - Number of Brush Presets. The maximum rose from 30 to 45 in 5.0 [D rn50].
  - Palette Size (diameter).
  - Color Selector type: the sRGB Triangle, or a Wide Gamut selector that adapts to the layer's colour space.
  - Selector Size.
  - Dynamically Adjust Slot Count.
  - Show Color History Ring.
  - Show Rotation Ring.
- **Brush HUD (On-Canvas Brush Editor).** It shows a few sliders the user picks per brush engine ("for when you think flow is more important than size"). Since 5.3 it is a docker that can live inside the pop-up [D hud, rn53].
- **Windows pitfall.** Windows "flicks" can trigger the palette by accident; the docs advise turning flicks off [D drawtab].
- **Why it saves hand movement.**
  - The docs say it puts "brushes, color history and a color selector within arm's reach" [D ps], and it is meant to "save time" [D popup].
  - [I] On a 13.3″ pen display (about 29 cm wide), the right-hand dockers are an arm move away, and the hand covers the screen while reaching. The palette comes to the pen tip. One gesture gives brush, colour, recent colours and view controls. Switching tags swaps whole "kits" of brushes.
  - [S LizStaley, a CSP user] "Honestly I wish this was a thing in Clip Studio Paint" [T csp].

## 3. Canvas-only mode and full screen

- **Toggling.** Press **Tab** (or View ▸ Show Canvas Only), "Krita's version of full screen mode" [D canvasonly, view]. A **4-finger tap** also enters it (5.0) [D canvasonly]. The separate View ▸ Full Screen Mode (**Ctrl+Shift+F**) hides the system bar [D view].
- **Configuration.** Settings ▸ Configure Krita ▸ Canvas-only settings: "Selected items will be hidden" [D canvasonly].
  - Bug reports mention options for the title bar, menu, toolbars, and toolbox/dockers [T bug-search].
  - [I, unverified] My recollection is that the dialog also lists scrollbars and the status bar.
  - Document **tab bars cannot be hidden**; this is an open wishlist bug [T b475381].
- **Reaching functions while hidden** [D popup, keys]:
  - The pop-up palette, which includes its own canvas-only and mirror buttons and the on-canvas docker area.
  - All keyboard shortcuts.
  - **Ctrl+Enter** action search.
  - Tab again to leave.
- **Windows quirk.** With OpenGL, Windows full screen deliberately leaves a thin gap at the bottom; switching to Direct3D 11 avoids it [D faq].

## 4. Brush selection and adjustment

- **Choosing brushes:**
  - Brush Presets docker: left-click to switch, right-click to add or remove tags [D presetdock].
  - **F6** opens the preset chooser from the toolbar [D start].
  - The Brush Preset History docker remembers the last 10 presets. Its behaviours are Static, Move to Top and Bubble Up [D history].
  - **/** switches between the current and previous preset [D ps].
  - Tags are created with the "+" next to the tag dropdown [D tags].
- **Brush Editor** (F5 or the toolbar button) has six areas [D brushes]:
  - A: icon, live S-curve preview (pressure rising from left to right), name, engine, rename, **reload**, Save New Brush Preset, Overwrite Brush (which always makes a timestamped backup).
  - B: an engine-filtered preset chooser (collapsed by default).
  - C: a scratch pad (collapsed by default).
  - D: a per-engine option list with enable checkboxes and padlocks to lock settings.
  - E: option widgets, mostly **sensor curves**. Quick-curve buttons cover soft and hard pressers. There is a "Share curve across all settings" option and curve-combination modes (multiply, add, max, min, difference).
  - F: **Eraser switch size**, **Eraser switch opacity**, "Temporarily save tweaks to preset" (*dirty presets*, marked with an icon until restart), and an Instant Preview toggle with a hidden threshold slider.
  - The editor can be detached into its own window (5.0) [D rn50].
- **Depth and complexity.** There are 18 brush engines [D engines]. Krita separates brush *tips* from *presets*, unlike Photoshop [D brushes]. The docs' own basic inking-brush recipe takes about 10 steps [D brushes, sai]. The SAI guide calls it a "misconception" that the engine is simple "because you can only change Size, Flow and Opacity from the top bar" [D sai].
- **Size:**
  - **Shift+drag** on canvas: drag outward to grow, inward to shrink [D freehand].
  - **[ / ]** keys [D ps].
  - The toolbar Size slider.
  - Slider behaviour: click or drag; right-click, double-click or a number key to type a value (maths allowed); Shift for fine steps and "relative mode" (5.1); Ctrl to snap; a toggle between "soft range" and full range [D widgets].
  - The toolbar sliders' small arrow chooses what each slider controls (opacity, size, flow, etc.) [T ka-slider]. There are no built-in size presets; a third-party "Brush Size Docker" plugin adds Small/Medium/Large [T ka-sizedock].
  - 5.1 added shortcut actions for flow, fade and scatter [D rn51].
- **Opacity:** **I / O** decrease and increase it [D ps].
- **Eraser:**
  - **E** toggles the current brush into the Erase blend mode, "individually for each of your brushes" [D ps]. It also works in the line, rectangle and gradient tools [D start].
  - Separate eraser size and opacity are stored per preset [D brushes].
  - Separate eraser cursor settings (5.1) [D general, rn51]. 5.3 adds an eraser icon on non-brush tool cursors [D rn53].
  - "Toggle Eraser Preset" (5.2) switches to the stylus-eraser-end preset for pens without an eraser end [D rn52].
  - Pattern "Auto Invert For Eraser" (5.3) [D rn53].
  - [S Takiro] "Eraser mode is not the same as an eraser preset". [S Voidsted] The preset's pressure-driven opacity still applies when erasing [T ka-newcomer].
- **Reload preset:** the circular-arrow toolbar button, or the icon after the engine name in the editor [D brushes].

## 5. Stabilization (Freehand Brush tool options)

Smoothing is set **per tool, not per preset**. The SAI guide lists "No Preset-tied stabilizer" as a gap [D sai].

- **None:** "The input from the tablet translates directly to the screen… fastest option, and good for fine details" [D freehand].
- **Basic:** "a very little bit of smoothing" for slightly jagged lines from older tablets [D freehand].
- **Weighted:** adjustable [D freehand].
  - **Distance**: events received before the first dab is drawn.
  - **Stroke Ending**: pulls toward the last cursor position after lift, "always… a straight line, so use with caution".
  - **Smooth Pressure**.
  - **Scalable Distance**: scales with zoom.
- **Stabilizer:** averages all input. "Draw[s] a circle around your cursor and the line will be a bit behind your cursor", and "allows for always completing the line" [D freehand].
  - 5.3 replaced Distance with **Sample Count at Max Speed / at Min Speed**, giving less smoothing on fast strokes and more on slow, jittery ones [D freehand, rn53].
  - **Delay**: a dead zone, giving "sharp corners with more control".
  - **Finish Line**.
  - **Stabilize Sensors**: smooths pressure, speed and tilt.
  - **Scalable Distance**.
  - Stabilizer with Delay off "is much like Paint Tool SAI's" [D sai].
- **Pixel** (5.3): keeps one-pixel lines free of doubled pixels and L-shaped corners [D freehand].
- **Related settings:**
  - Assistant snapping lives in the same panel (toggle Ctrl+Shift+L, optionally excluding erasers) [D freehand].
  - "Smoothing: basic/stabilizer" can be added as toolbar buttons [D sai].
  - The tablet setting "Use Tablet Driver Stamps For Brush Speed" changes how smoothing reads timing [D tablet, rn50].
  - The FAQ's first check for "slow brushes" is whether the stabilizer was left on by accident [D faq].

## 6. Pressure and tablet

- **Settings ▸ Configure Krita ▸ Tablet settings** [D tablet]:
  - **Input Pressure Global Curve**: "make your tablet feel soft or hard globally".
  - **Use Mouse Events for Right and Middle clicks**: for styluses that don't report side buttons; needs a restart.
  - Driver timestamps for speed.
  - **Maximum Brush Speed**: calibrate it with the Tablet Tester.
  - **Brush Speed Smoothing**.
  - **Pen Tilt Direction Offset** (5.3, for handedness).
  - 5.3 also adds a default tilt for non-tilt tablets [D rn53].
  - **Tablet Input API**: **WinTab** or **Windows 8+ Pointer Input (Windows Ink)**. Changing it requires a restart [D tablet screenshot].
  - Advanced WinTab mapping fixes offsets on multi-monitor and pen-display setups [D tablet, faq].
- **Tablet Tester.** Red line = mouse events; **blue line = real tablet events**, with pressure and coordinates. It shows raw events and ignores the global curve [D tablet]. Tilt data was added in 5.2 [D rn52].
- **Per-brush sensors** [D sensors]:
  - Pressure, PressureIn, X- and Y-tilt, Tilt-direction, Tilt-elevation.
  - Speed, Drawing Angle, Rotation, Distance, Time, Fade.
  - Fuzzy Dab, Fuzzy Stroke, Perspective, Tangential Pressure.
  - 5.3 added sharp "corner" curve points with Ctrl+click [D rn53, widgets].
- **Huion specifics:**
  - Huion's guidance is WinTab with Windows Ink off, or the reverse [T huion].
  - A Kamvas 13 user had "Windows 8+ pointer input" enabled with no pressure. The advice was that Krita and the driver must match, and Krita must be restarted after each change [S sooz, T ka-kamvas13].
  - Krita's view is that tablet problems on Windows are almost always buggy or broken drivers [D faq].

## 7. Cursor and brush preview (General ▸ Cursor)

- **Cursor Shape:** Tool Icon, Arrow, Crosshair, Small circle, **No Cursor** ("useful for tablet-monitors"), Triangle right- or left-handed, Black Pixel, White Pixel [D general].
- **Outline Shape:** No Outline, Circle Outline, **Preview Outline** (the real brush shape), Tilt Outline [D general].
- **While painting:** Show Outline, and **Use effective outline size**, which shows the maximum diameter rather than the pressure-varying one ("less noisy") [D general].
- **Cursor Color** is mixed for contrast and can be overridden [D general].
- **Use separate settings for eraser** [D general].
- 5.1 added a shortcut to toggle the brush outline [D rn51].
- [S Voidsted] The crosshair "doesn't scale" with the brush circle [T ka-newcomer].

## 8. Colour

- **Advanced Color Selector** (in the default layout):
  - A settings icon at top-left.
  - HSV, HSL, HSI or HSY′ models with selectable shapes.
  - Optional shade selectors (MyPaint style) and behaviour on resize.
  - It is sRGB-limited ("This is a bug") [D acs].
- **Wide Gamut Color Selector** (5.2) is "intended to eventually replace" it [D wgcs].
- **On-canvas pop-ups:** colour history **H**, common colours **U**, shade selectors **Shift+N / Shift+M**, big selector **Shift+I** [D sai]. The docs admit Shift+I/N/M are hard for right-handed painters to reach [D ps].
- **Other dockers:**
  - **Artistic Color Selector**: traditional wheel with discrete or continuous steps, gamut masks [D artistic].
  - **Digital Color Mixer**: SAI-like slider mixing [D mixer, sai].
  - **Palette docker**: grid swatches. Drag a swatch to the canvas to fill, or to Layers to create a fill layer (5.0) [D palette, rn50].
  - Small and Specific selectors, and Color Sliders [D dockers].
- **Sampling:**
  - Holding **Ctrl** with the brush turns it into the colour sampler. Left-click samples FG, right-click samples BG; releasing Ctrl returns to the brush [D freehand].
  - The default binding is "Sample Foreground Color from **Merged Image**" [D sai].
  - The Ctrl sampler ignores the Color Sampler tool's layer option **by design**. Current-layer sampling is a separate Canvas Input action [T b412635], which users report as Ctrl+Alt+click [S MagnusBullseye, T ka-sample].
  - The tool itself offers all-layers or active-layer sampling, Radius, and Blend ("soaks up" colour) [D sampler].
  - "Sample Screen Color" (5.2) works outside Krita [D rn52].
- **Colour shortcuts:** **D** resets to black/white, **X** swaps, **K/L** darker/lighter [D ps].
- **Colour history** can be saved globally or per document (5.3) [D rn53].

## 9. Layers

- **Layers docker** [D layers]:
  - Top: blend mode, opacity, filter (by name or colour label), display settings (thumbnail size, indent, opacity/blend info badges in 5.2, checkboxes for multi-select on tablets).
  - Rows: eye, name, then property icons: **Alpha Inheritance** ("a" icon), **Alpha Lock** (paint layers), Lock, Pass-through (groups), Layer Style.
  - Bottom bar: Add (with a type dropdown), Duplicate, Up/Down, Properties (its arrow opens the right-click menu when right-click is unavailable), Delete.
- **Keys** [D layers, ps]:
  - **Ins** new paint layer; Shift+Ins vector layer.
  - **Ctrl+G** group; **Ctrl+Shift+G** clipping group (selected layers plus a new inherit-alpha layer on top); Ctrl+Alt+G ungroup.
  - Ctrl+J duplicate; Ctrl+E merge down; Ctrl+Shift+E flatten.
  - PgUp/PgDn switch layers; Ctrl+PgUp/PgDn move layers.
  - Alt+click the eye to isolate a layer.
  - **R+click** on canvas picks the top layer under the cursor. "Select Layers From Menu" (5.2) works like Photoshop's Ctrl+right-click.
  - F3 edits properties of multiple layers.
- **Clipping.** "Krita has no clipping mask, but it has a clipping feature called inherit alpha" [D faq].
- **Group blend modes** are isolated by default, unlike Photoshop; pass-through is a group toggle [D ps].
- [D faq] Erasing on the first layer shows a checkerboard because every layer has alpha. Users are told to add a layer first.
- 5.3 adds a **Fast Color Overlay** mask button for tinting sketches [D rn53].

## 10. Selections and transforms

- **Selection tools** (bottom of the toolbox): rectangle, ellipse, polygon, freehand, similar colour, contiguous ("magic wand"), path, magnetic [D sel].
- **Modifiers:** Shift add, Alt subtract, Shift+Alt intersect, Ctrl replace. The Ctrl and Alt roles can be swapped for GIMP habits [D sel, general].
- **Common shortcuts:** Deselect is **Ctrl+Shift+A** (not Ctrl+D); Ctrl+H hides the selection display [D ps].
- **Selection types and display.** Selections are **vector** by default [D sel]. The status-bar button switches between marching ants and mask display [D sel].
- **Global Selection Mask** (Select ▸ Show Global Selection Mask), or right-click ▸ Edit Selection, lets you paint, transform or move the selection like a greyscale layer [D sel].
- **Selection Action Bar** (5.3 GSoC):
  - A floating bar that appears when a selection is active: Select All, Deselect, Invert, Crop to Selection, Fill with FG, Copy to New Layer, plus a drag handle [D sel, rn53].
  - Toggle it in Settings ▸ Configure Krita ▸ General ▸ Tools or the selection tool options [D sel, p532].
  - 5.3.2 added a context menu; 5.3.3 lets you pin it to canvas sides [D p532, p533].
- **Transform (Ctrl+T)** [D transform, ps]:
  - Modes are **Free, Perspective, Warp, Cage, Liquify, Mesh**, chosen in the Tool Options tabs. The ps guide says: "do not right-click on the on-canvas widget".
  - **Enter** applies. Esc cancels a "continuous transform".
  - Ctrl+Alt rotates the bounding box (5.3).
  - The tool can transform several selected layers.
  - **T** is the Move tool. There is no Ctrl-drag move.
  - Option: "Activate transform tool after pasting" [D general].
- **Assistant tool:** perspective, concentric, ruler and similar assistants, with snapping via brush tool options. 5.3 adds limit-to-area, a widget redesign and a curvilinear assistant [D assistant, rn53].

## 11. Canvas navigation

- **Pan:** Space+drag, middle-drag, or arrow keys [D nav].
- **Zoom:**
  - Ctrl+Space+drag or Ctrl+middle-drag for continuous zoom; Ctrl+Alt+Space for discrete steps; **+/−** keys [D nav, ps].
  - **1 / 2 / 3**: 100%, fit page, fit width [D sai].
  - A wheel-zoom step setting [D general].
  - 5.3 splits zoom-to-cursor from zoom-to-canvas-centre; the default is now centre [D rn53].
- **Rotate:** Shift+Space+drag or Shift+middle-drag; Ctrl+[ / Ctrl+] or **4 / 6** for 15° steps; **5** resets [D nav, sai].
- **Mirror:** **M** mirrors the view; **Alt+M** mirrors around the cursor (5.1) [D nav].
- **Overview docker:** drag the view rectangle; zoom, rotation and mirror controls that auto-hide unless pinned (5.0) [D overview].
- **Wrap Around Mode** (View) tiles the image for textures [D view].
- **Multiple views:** Detach Canvas, and Window ▸ New View (independent zoom and rotation) [D view, window].
- **Canvas Input Settings** (mouse, pen and touch bindings) [D cis]:
  - Profiles can be saved.
  - Shipped profiles include **Krita Default, Photoshop Compatible, Paint Tool SAI Compatible, Clip Studio Paint Compatible** [T b437241, b488478; D faq names Photoshop Compatible].
  - The FAQ warns that keyboard and canvas schemes **must match** to avoid conflicts [D faq]. 5.2 detects conflicts [D rn52].
  - In the Photoshop profile, Shift+drag starts the line tool, which conflicts with Shift-add-to-selection [T b437241].
  - Touch gestures became configurable in 5.1 [D rn51]. 5.3 adds sampler, deselect, layer and tool actions [D rn53].
- **Pen buttons.** [I] Pen barrel buttons arrive as right/middle clicks. That gives right = pop-up palette and middle = pan by default; the "mouse events" workaround in §6 covers pens that don't report buttons.

## 12. Keyboard shortcuts and discoverability

- **Where:** Settings ▸ Configure Krita ▸ Keyboard Shortcuts [D keys]:
  - A search box.
  - Primary and alternate keys for each action.
  - Key sequences (Shift+S then B).
  - Conflict prompts.
  - Load, save and export of schemes.
- **Schemes:** Krita ships **Photoshop** and **Paint Tool SAI** schemes [D keys], plus **Clip Studio Paint** since 5.2 [D rn52]. There are Tablet Pro profiles for the Surface [D faq].
- **Search Actions (Ctrl+Enter, 5.0)** is a command palette over every action, "reducing the need to assign a shortcut to every single action" [D keys, rn50].
- **Differences from Photoshop** [D ps]:
  - Ctrl+Shift+A deselect.
  - Ctrl+Shift+E flatten.
  - No dedicated eraser, dodge or burn tools; these are blend modes and the filter brush.
  - Group compositing differs.
- [S Voidsted] Could not rebind the sampler from Ctrl to Alt. [S kaichi1342] Wants a first-run shortcut-preset picker, because "the Ctrl/Alt color picker change has caused many users to give up" [T ka-newcomer].

## 13. Tablet-oriented features and reduced hand movement

- **Built-in aids:**
  - The pop-up palette (§2).
  - **Hold-to-switch tools:** hold **V** for the line tool [D freehand]. 5.0 made Ellipse, Rectangle, Move, Fill, Gradient, Measure and selection tools configurable the same way [D rn50].
  - R+click to pick a layer [D layers].
  - The **Touch Docker**: big buttons for open/save, undo/redo, opacity ±, lighter/darker, rotate, reset [D touch].
  - Configurable toolbars [D settings].
  - **On-canvas popup messages** (tabbed mode, rotation, mirroring), which can be toggled [D general].
  - Touch Painting: Auto, the default since 5.2.13 [D general].
  - Long-press for context menus (5.2.14) [D general].
  - Layer checkboxes and a context-menu button for pen-only use [D layers, rn51].
  - In 5.3: Workflow Buttons docker, Docker Box, and the Brush Mutator (Z) [D rn53].
- [S Hannes_Radke] "incredibly customizable… Much more so than Clip Studio Simple Mode and Procreate", but setup takes time [T ka-tablet].
- [S halla] Older tablet UIs (Krita Sketch and Gemini) broke when Qt changed [T ka-tablet].

## 14. Undo and redo

- **Ctrl+Z** undo and **Ctrl+Shift+Z** redo [D edit].
- The **Undo History docker** jumps between states [D undo].
- **Undo Stack Size** is under General ▸ Miscellaneous; **0 = unlimited** [D general]. The docs do not state the default.
- **Cumulative Undo** merges old similar strokes. Options: wait time, exclude last N, max interval, max group duration. Strokes with a different colour, preset or blend mode are never merged [D undo]. It was overhauled in 5.2 to be "more intuitive" [D rn52].

## 15. Customisation depth and sharing

- **What can be saved:**
  - Workspaces (`.kws`), window layouts and **Sessions** (File ▸ Sessions). The startup choice is default window, last session, or session manager, with "Save session when Krita closes" [D ws, general, file].
  - Shortcut schemes and Canvas Input profiles [D keys, cis].
  - Toolbars [D settings].
- **Resources** live in an SQLite-indexed folder (%APPDATA%\krita on Windows) [D res].
- **Bundles** are the sharing format. Settings ▸ Manage Resource Libraries imports or deactivates them. The bundle creator wizard, which can include tags, was rebuilt in 5.3 [D res, rn53].
- Krita can load ABR brush libraries and ASL layer-style files, with limits [D res, faq].
- **Multiple windows:** New Window and New View [D window].

## 16. Time to first stroke

- The first launch shows the Welcome page with **no canvas**; the user clicks New File or presses Ctrl+N [D start].
- **New Document dialog** [D newdoc + screenshot]:
  - Left list: Custom Document, Create from Clipboard, and template groups (Animation, Comic, Design, DSLR, Texture).
  - Dimensions tab: Predefined dropdown, W/H, resolution, "Save Image Size as", and colour model, depth and profile.
  - Content tab: name, background, number of layers ("Krita remembers the amount of layers you picked last time").
  - A live readout shows size and RAM per layer.
  - Templates are `.kra` files made via File ▸ Create Template from Image [D newdoc].
- After creation, the **Freehand Brush is already active** [D freehand, start].

## 17. HiDPI and scaling on Windows

- **Hi-DPI is on by default** and follows the Windows scale [D faq, scaling].
- **Window tab options** [D general]:
  - **Enable Hi-DPI support** ("still experiencing bugs on Windows").
  - **Enable fractional scale factor** (5.0) for 125% or 150%, which "may cause some visual artifacts".
  - Custom interface font and size.
- **Other controls:**
  - `QT_SCALE_FACTOR` forces a scale; values below 100% are unsupported [D scaling].
  - Toolbox icon size is set by right-clicking the toolbox [D faq].
  - If the dialog is too big, **Alt+O** presses OK [D faq].
  - Fractional-scaling artefacts were fixed in 5.0 and 5.1.4 [T search, krita.org 5.1.4].
- **[I] On a 13.3″ 1080p panel:**
  - Windows typically suggests 125% to 150%, which needs the fractional option.
  - The single toolbar row overflows into a chevron [D settings, sai].
  - Three stacked right-hand dockers get cramped. [T bug-search] reports default workspaces stacking three dockers is "hard to use at Full HD".

## 18. Frustrations and complexity ([S]/[T])

- **Docker inconsistency.** [S abhifx] "every docker is designed in different way and behaves differently" [T ka-newcomer].
- **Overwhelm on first open.** [S LizStaley] "far less intuitive than Clip Studio Paint… this is about where I got overwhelmed" [T csp].
- **Default UI.** A KDE task called the default UI "pretty unfriendly towards newbies" [T phab-T4480].
- **Brushes and erasing:**
  - Duplicate brushes across tags [S Voidsted].
  - Eraser mode vs eraser preset confusion, and wanting a real eraser tool and a separate eraser history [S Takiro, YRH] [T ka-newcomer].
- **Shortcut rebinding** fails or confuses (Ctrl sampler, Shift line conflict) [S Voidsted] [T ka-newcomer, b437241].
- **Toolbar and tool feedback.** The toolbar doesn't fit at 1080p, and Tool Options doesn't name the active tool [S Voidsted] [T ka-newcomer]. The toolbar is static [T vc].
- **Resources.** The 5.0 SQLite resource system needs `resourcecache.sqlite` deleted when workspaces break [S Michelist, T ka-restore]. Many bundles slow startup [D faq].
- **Tablet setup.** WinTab vs Ink must match the driver, with restarts each time [S sooz, T ka-kamvas13].
- **Documentation gaps.** The Photoshop guide was written for 2.9 and CS2/3 [D ps], and the Canvas Input page is a stub [D cis].

## 19. What Krita does especially well for drawing, and why

1. **Pop-up palette.** Brush, colour, history and view controls come to the pen [D popup]. [I] It removes most trips to dockers, which matters most on pen displays.
2. **Canvas-only (Tab).** One key gives a distraction-free canvas, while the palette, shortcuts and Ctrl+Enter keep everything reachable [D canvasonly, rn50]. [I] The UI turns off but capability stays.
3. **Stabilizer family.** The modes are graded from None to Basic to Weighted to Stabilizer, with Delay for corners and speed-adaptive sampling in 5.3. [I] Users can tune line "feel" without changing brushes [D freehand].
4. **Per-preset eraser toggle (E).**
   - The eraser keeps the *same* texture and dynamics as the brush, with its own stored size and opacity [D brushes, ps].
   - [I] Erasing feels like "the same pencil, reversed", and there is no tool switch.
5. **Configurable Canvas Input plus compatibility profiles.** Photoshop and CSP muscle memory ports over [D keys, rn52]. Hold-to-switch tools avoid round-trips [D rn50].
6. **Global plus per-brush pressure curves and Tablet Tester.** [I] Pressure problems can be diagnosed and the feel tuned, separately for the device and for each brush [D tablet, brushes].
7. **Mirror view (M), rotation keys and wrap-around.** These are traditional painter checks on single keys [D nav].

## 20. Small-screen and pen-display features

- **Docs:** canvas-only (Tab), the 4-finger tap, the "No Cursor" option for tablet-monitors [D general], the Touch Docker [D touch], and docker locks against accidental pen undocking [D rn50].
- **Workspaces:** "Small_Paint" and "Small_Vector" were designed "for very small screen". "Big_Paint" targets 1920×1080 [D ws2014, may be renamed or retired].
- **Touch:** touch painting Auto, long-press menus, layer checkboxes, configurable touch gestures [D general, layers, rn51].
- **Missing:** no dedicated "tablet mode" UI exists today; Sketch and Gemini are gone [S halla, T ka-tablet].

---

## Sources

**Krita Manual (docs.krita.org/en/…)**

- nav: user_manual/getting_started/navigation.html
- start: user_manual/getting_started/starting_krita.html
- welcome: reference_manual/welcome_screen.html
- newdoc: reference_manual/create_new_document.html
- popup: reference_manual/popup-palette.html
- popset: reference_manual/preferences/popup_palette_settings.html
- canvasonly: reference_manual/preferences/canvas_only_mode.html
- view: reference_manual/main_menu/view_menu.html
- window: reference_manual/main_menu/window_menu.html
- settings: reference_manual/main_menu/settings_menu.html
- file: reference_manual/main_menu/file_menu.html
- edit: reference_manual/main_menu/edit_menu.html
- general: reference_manual/preferences/general_settings.html
- tablet: reference_manual/preferences/tablet_settings.html
- cis: reference_manual/preferences/canvas_input_settings.html
- keys: reference_manual/preferences/shortcut_settings.html
- scaling: reference_manual/interface_scaling.html
- freehand: reference_manual/tools/freehand_brush.html
- brushes: user_manual/loading_saving_brushes.html
- presetdock: reference_manual/dockers/brush_preset_docker.html
- history: reference_manual/dockers/brush_preset_history.html
- hud: reference_manual/dockers/oncanvas_brush_editor.html
- sensors: reference_manual/brushes/brush_settings/tablet_sensors.html
- engines: reference_manual/brushes/brush_engines.html
- widgets: reference_manual/special_widgets.html
- drawtab: user_manual/drawing_tablets.html
- acs: reference_manual/dockers/advanced_color_selector.html
- wgcs: reference_manual/dockers/wide_gamut_color_selector.html
- artistic: reference_manual/dockers/artistic_color_selector.html
- mixer: reference_manual/dockers/digital_color_mixer.html
- palette: reference_manual/dockers/palette_docker.html
- sampler: reference_manual/tools/color_sampler.html
- layers: reference_manual/dockers/layers.html
- sel: user_manual/selections.html
- transform: reference_manual/tools/transform.html
- assistant: reference_manual/tools/assistant.html
- overview: reference_manual/dockers/overview.html
- touch: reference_manual/dockers/touch_docker.html
- undo: reference_manual/dockers/undo_history.html
- ws: reference_manual/resource_management/resource_workspace.html
- res: reference_manual/resource_management.html
- tags: user_manual/tag_management.html
- anim: user_manual/animation.html
- dockers: reference_manual/dockers.html
- ps: user_manual/introduction_from_other_software/introduction_from_photoshop.html
- sai: user_manual/introduction_from_other_software/introduction_from_sai.html
- faq: KritaFAQ.html

**Krita release notes and posts (krita.org/en/…)**

- rn50: release-notes/krita-5-0-release-notes/
- rn51: release-notes/krita-5-1-release-notes/
- rn52: release-notes/krita-5-2-release-notes/
- rn53: release-notes/krita-5-3-release-notes/
- p532: posts/2026/krita-5.3.2-released/
- p533: posts/2026/krita-5.3.3-released/
- dl: download/
- ws2014: posts/2014/last-week-in-krita-week-45-3/

**Third party**

- b437241: https://bugs.kde.org/show_bug.cgi?id=437241
- b488478: https://bugs.kde.org/show_bug.cgi?id=488478
- b475381: https://bugs.kde.org/show_bug.cgi?id=475381
- b412635: https://bugs.kde.org/show_bug.cgi?id=412635
- b472814: https://bugs.kde.org/show_bug.cgi?id=472814
- bug-search: bugs.kde.org canvas-only and "window too tall" reports, found via search; individual reports not opened.
- phab-T4480: https://phabricator.kde.org/T4480 (search-summarised)
- vc: https://virtualcuriosities.com/?p=1571
- csp: https://tips.clip-studio.com/en-us/articles/10480
- ka-newcomer: https://krita-artists.org/t/lets-see-krita-from-a-newcomers-eyes/93317
- ka-tablet: https://krita-artists.org/t/is-there-intuitive-or-simple-user-interface-mode-for-krita-tablet-pad-user-question/72193
- ka-restore: https://krita-artists.org/t/how-to-restore-the-default-workspace/130232
- ka-kamvas13: https://krita-artists.org/t/pen-pressure-not-working-on-krita-huion-kamvas-13/95202
- ka-sample: https://krita-artists.org/t/help-with-sample-current-layer-option/79131
- ka-slider: https://krita-artists.org/t/a-way-to-toggle-size-pressure-and-opacity-with-shortcut-as-if-checking-the-box-for-those-options-in-f5/106869 (search-summarised)
- ka-sizedock: https://krita-artists.org/t/brush-size-docker/98260 (search-summarised)
- huion: https://support.huion.com/en/support/solutions/articles/44002618549 (search-summarised)
- krita.org 5.1.4: https://krita.org/ca/posts/2022/krita-5-1-4-released/ (search-summarised)
