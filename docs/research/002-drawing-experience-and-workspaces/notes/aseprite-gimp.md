# Aseprite 1.3.x (Windows) in depth + GIMP 3.x brief — UX research for DoodleNook

Labels: **[D]** official doc/release note/blog · **[C]** community.aseprite.org or other official-forum post (attributed; "dacap" = David Capello, Aseprite lead dev) · **[T]** third-party · **[S]** subjective/opinion · **[I]** my inference. Source IDs → URLs in the legend at the end. GitHub was not used.

**Version context.** The latest Aseprite stable is v1.3.18.6, released 22 Sep 2026 [D A45]. In Sep 2026 dacap said a render-engine rewrite "for v1.4" is in progress. It should allow any zoom level plus view rotation and flips, but it is still ongoing [C C3]. GIMP 3.2 is current, and 3.2.8 shipped on 2 Nov 2026 [D G25].

---

## 1. Main interface (Aseprite)

- **Default layout** (official diagram) [D A1]:
  - Menu bar on top: File, Edit, Sprite, Layer, Frame, Select, View, Help [D A8]. Alt + the underlined letter opens a menu [D A8].
  - Document tabs sit under the menu bar, with a Workspace Layout button at the top right.
  - The **Context Bar** runs across the top of the editor.
  - The **Tool Bar** is a vertical strip on the **right**.
  - The **Color Bar** is on the **left**: palette, then the colour picker, then the FG/BG colour buttons.
  - The **sprite editor** is in the centre and the **Timeline** at the bottom, with a preview toggle.
  - The **Status Bar** is at the very bottom. It shows the sprite name, a Frame field, a "+" new-frame button and a zoom field.
- **Home tab.** It offers New File…, Open File… and Recover Files… [D A43], plus recent files and folders. The number of recent items is set in Preferences > Files [D A27]. "Show Home tab when Aseprite is started" is a preference [D A27]. Edit > Paste on the Home tab creates a new image from the clipboard (1.3.4) [D A45].
- **Context Bar.** It shows only the options for the active tool. It also changes with document state; for example, while you move a selection it shows X/Y/W/H/rotation/skew fields [D A3, A35]. Per tool:
  - **Pencil:** brush type, size (px), ink, dynamics and a Pixel-perfect checkbox. Opacity only appears with certain inks.
  - **Rectangle tools:** Corner Radius (hold C while dragging to change it).
  - **Paint Bucket:** Tolerance (0–255), Contiguous, Stop at grid, Refer active/visible layers, 4- or 8-connectivity.
  - **Gradient:** linear/radial plus Bayer 2×2/4×4/8×8 dithering.
  - **Move:** auto-select layer.
  - **Selection tools:** add/subtract/intersect modes, transparent colour, pivot and rotation algorithm [D A3, A32, A33].
- **Tool Bar.**
  - Tools are grouped; hovering reveals the group (e.g. Eraser with Eyedropper) [D A17].
  - Since 1.3.11 the toolbar groups its buttons when the window is too small [D A45].
  - A Timeline-visibility button was added in 1.3-rc7 [D A45].
- **Timeline.** It toggles with View > Timeline or **Tab**. The docs say it is *not visible by default* and pops up when you add a frame or layer (a preference) [D A1, A6]. The docs diagram nevertheless shows it, so the default state is unclear [I].
- **Status Bar.** Its content depends on the mouse position [D A7]. It holds the zoom field and slider [D A36]. Since 1.3.11 it shows the grid-cell index [D A45].
- **Workspace layouts (new in 1.3.16).**
  - Open layout mode with Shift+W, View > Workspace Layout or the boxes icon at the top right.
  - Two native layouts exist, **Default** and **Mirrored Default**, and "New Layout" saves user layouts.
  - Movable parts are the Timeline, Color Bar, Context Bar, Tool Bar and Status Bar; you drag them by a three-line grip.
  - Only the timeline can go top, bottom, left or right. Vertical bars stay vertical and horizontal bars stay horizontal [D A2]. This was introduced in 1.3.16-beta1 as "all elements dockable/movable/resizable" [D A47, A45].
- **Other window behaviour.**
  - Tabs can be dragged to any side of the editor to split the view; Ctrl or Alt + drag duplicates a view [D A1].
  - The Preview window can float outside the main window (1.3-beta1) [D A45].
  - Multi-window UI is off by default since 1.3-rc1 [D A45].
  - Light/Dark theme option since 1.3 [D A27].

## 2. Tools & drawing

- **Default single-key tools** [D A44, A10, A39, A55]:

  | Group | Tools and keys |
  |---|---|
  | Painting | Pencil **B** · Spray Shift+B · Eraser **E** · Eyedropper **I** (or hold **Alt**) · Paint Bucket **G** · Gradient Shift+G |
  | Shapes | Line L · Curve Shift+L · Rectangle **U** (press again for Filled) · Ellipse Shift+U · Contour D · Polygon Shift+D |
  | Selection | Rectangular Marquee **M** · Elliptical Shift+M · Lasso Q · Polygonal Lasso Shift+Q · Magic Wand W |
  | Navigation and other | Move **V** (or hold Ctrl) · Hand H (or hold Space) · Zoom Z · Slice Shift+C · Blur R · Jumble R R · Text **T** (1.3.14+ [D A48]) |

- **How keys behave.** Pressing the same key again cycles through tools that share it. "(quick)" tools stay active only while the key is held and then return to the previous tool [D A39].
- **Clicks.** Left click paints with the foreground (FG) colour and right click with the background (BG) colour, for almost all painting tools [D A10]. The right-click action is configurable in Preferences > Editor [D A9].
- **Eraser** right-click replaces FG with BG [D A62, A44].
- **Pencil modifiers.** Shift draws a straight line from the last point, and Ctrl snaps its angle. The line preview can appear immediately, which is a preference [D A39, A27].
- **Shape modifiers.** Shift gives a square or circle, Ctrl draws from the centre, Alt rotates the shape and Space moves the origin while dragging [D A39]. Rounded rectangles arrived in 1.3.17, with the C key adjusting the radius live [D A49].
- **Pixel-perfect** is a per-tool context-bar checkbox [D A3] that "create[s] perfect strokes for pixel-art" [D A52].
- **Inks** (a context-bar dropdown; "Same in all Tools" is optional) [D A11]:
  - Simple (the default) paints opaque colour, composites semi-transparent colour, and erases with alpha=0.
  - Alpha Compositing.
  - Copy Alpha+Color writes the pixel exactly.
  - Lock Alpha keeps existing alpha.
  - Shading is covered in §3.
- **Brushes** [D A13, A44, A46]:
  - Types are **circle (default)**, square and line; square and line have an angle option.
  - Custom brushes come from a selection: Edit > New Brush (**Ctrl+B**), with pattern alignment options (to source, to destination, or as a paint brush).
  - Brushes can be saved with chosen parameters, and Alt+1/Alt+2… save or load custom brushes.
  - Space+X/Y/D/R flip or rotate a custom brush.
- **Brush size.** Use the **+/−** keys or Ctrl+wheel [D A44, A37]. Ctrl+Alt+drag left/right is the default "Drag Value" binding [D A38, A3]. The v1.3 blog says Ctrl+Shift+drag, which contradicts the docs [D A46].
- **Dynamics popup** (context bar):
  - Rows are **Size / Angle / Gradient**; columns are **Pressure** (pen only) and **Velocity** (pen or mouse).
  - You can tick one cell per row and several rows. Each has sensor thresholds and min/max values.
  - "Same in all Tools" applies the settings to every tool.
  - A **Stabilizer** checkbox and amount sit at the top [D A14 + screenshot].
- **What dynamics cannot do.** I found no pressure → opacity mapping; "Gradient" interpolates the colour [I from A14 screenshot].
- **Dynamics history.** The stabilizer arrived in 1.3-beta4. Dynamics persist between sessions, and the button highlights when a sensor is active [D A45].

## 3. Colour & palettes

- **Color Bar layout.** It sits on the left by default: palette, picker, then the FG/BG buttons [D A19].
- **Palette clicks and keys.** Left click sets FG and right click sets BG. **X swaps them** [D A5]. 9/0 or Alt+wheel step FG through the palette, and Alt+Shift+wheel steps BG [D A44]. [ and ] also step [D A19].
- **Editing the palette.**
  - Edit Color mode toggles with **A** or the lock button [D A19].
  - Ctrl+wheel resizes the swatches [D A19].
  - Drag the selection outline to move entries, and Ctrl+drag to duplicate [D A19].
  - A warning icon next to FG adds an off-palette colour [D A5].
- **Indexed vs RGB.**
  - **Indexed** stores a palette index per pixel, with up to 256 entries [D A18].
  - Editing an entry **recolours every pixel that uses it** [D A18].
  - You can only paint with colours in the palette unless you edit the palette (Edit Palette button or **F4**) [D A5].
  - One index (0 by default) is the transparent colour. Change it in Sprite > Properties or with a middle-click in the color bar [D A18, A58].
  - In **RGB**, the palette is only a swatch set and colours outside it are allowed [D A5].
- **Why Indexed matters for pixel art.** It enforces palette discipline and makes instant global recolours, palette swaps and retro hardware limits practical. The FAQ discusses the SNES 15-bit/256-colour limits [D A51]. The palette-swap and game-dev benefits are [I].
- **Colour pickers.** Options are HSV tint/shade/tone (the default), HSL spectrum, RGB wheel, RYB wheel and a Normal-map wheel. The wheels offer harmony modes and a "Discrete" mode [D A19].
- **Presets.** A searchable **Presets** button sits above the palette, and "Save Palette as Preset" adds your own [D A19]. Pin icons were added in 1.3.14 [D A45].
- **Default palette.** New sprites use DawnBringer's 32-colour palette, and "Save Palette as Default" replaces it [D A20]. Sort & Gradients menu (hue/sat/lum…) [D A19]; HSL palette gradients [D A45].
- **Picking colours.**
  - Alt+left/right click picks FG/BG from the image [D A9].
  - Eyedropper options: sample All Layers, the Current Layer or the First Reference Layer, and RGB/HSV/HSL [D A17].
  - Dragging from the FG/BG button picks colour *outside* Aseprite [D A17].
- **Shading ink.** Select a ramp of palette colours. Left click moves a pixel one step left along the ramp and right click one step right [D A12]. This is a palette-first way of shading.

## 4. Layers & frames (the timeline doubles as the layers panel)

- **Structure.** The timeline is a grid: **rows are layers** (bottom row drawn first) and **columns are frames**. Each intersection is a **cel** that can be edited on its own [D A9, A22]. There is no separate Layers panel [I].
- **Layers.**
  - Each row has a visible eye (**Shift+X**), a lock and a "continuous" toggle. Rename by double-clicking or with Shift+P [D A21].
  - New layer **Shift+N**, new group Alt+Shift+N, collapse a group Shift+E [D A21, A61].
  - Ctrl+J / Ctrl+Shift+J make a new layer via copy or cut [D A21].
  - Shift+1…4 set layer opacity to 25/50/75/100 % [D A44].
  - The **Background** layer is opaque and cannot be moved [D A21].
  - A "Solo Layer" command arrived in 1.3.16 [D A49].
- **Frames.**
  - **Alt+N** adds a frame and Alt+B an empty frame.
  - Navigate with ←/→ or , and .; **Enter** plays.
  - **P** opens Frame Properties, where duration is set in ms [D A26, A44, A63].
- **Onion skin.** **F3** toggles it. The timeline gear sets the frame count and red/blue tint [D A23].
- **Tags.** Press **F2 twice** to create one. Directions are Forward, Reverse and Ping-pong, and there is a Repeat field [D A24, A46].
- **Linked cels.** They are created in continuous layers (Alt+M duplicates a linked cel) and unlinked from the right-click menu [D A25, A44].
- **Other.** Per-cel opacity and a per-cel z-index [D A64, A46]. You can drop files onto the timeline (1.3.14) [D A48]. Group presentation was improved in 1.3.18 [D A45]. Ctrl+click a layer selects its content [D A32].

## 5. Grids & precision

- **Grid.**
  - View > Grid: Show Grid **Ctrl+'**, Show Pixel Grid **Ctrl+Shift+'**, Snap to Grid **Shift+S**, plus Grid Settings [D A44, A53].
  - There is a "Selection as Grid" command [D A53].
  - Preferences > Grid sets defaults separately for new documents and the active document: X/Y offset, colour, opacity and pixel-grid colour/opacity [D A27].
- **Snapping and grid helpers.**
  - Holding Alt while moving a selection snaps it; Shift+arrows move it by one tile [D A39, A44].
  - Paint Bucket has a "Stop at grid" option [D A3].
  - A tile can be selected with a double-click (preference) [D A27].
  - Since 1.3-rc8 the brush preview can follow snapping [D A45].
- **Symmetry.**
  - Open it from View > Symmetry Options. Context-bar buttons toggle horizontal and vertical symmetry; diagonal symmetry arrived in 1.3.14.
  - Axis handles can be dragged on canvas, including onto half-pixels, and there is "Reset to center" [D A28, A48, A53].
- **Tiled Mode.** View > Tiled Mode (both/X/Y) repeats the image in a 3×3 grid for drawing patterns [D A29, A52, A53].
- **Pixel aspect ratio.** Set it in Sprite > Properties (**Ctrl+P**) [D A30].
- **Preview window.** **F7** shows it as a small floating window. Shift+Enter plays the animation and Shift+F7 hides the other layers [D A31, A46]. **F8** is a full-screen preview [D A44].

## 6. Selections & transforms

- **Selection modes.** The default replaces the selection. **Shift** adds, **Alt+Shift** (or right-drag) subtracts and **Ctrl+Shift** intersects. These are configurable under Action Modifiers [D A32].
- **Selection shortcuts.** Ctrl+A selects all and **Ctrl+T** selects the cel bounds (Edit > Transform). Ctrl+D deselects, Ctrl+Shift+D reselects and Ctrl+Shift+I inverts [D A32]. **S** strokes the selection and **F** fills it [D A44].
- **Moving and transforming.**
  - Drag or use the arrow keys to move. The context bar shows X/Y/W/H/rotation/skew fields [D A35].
  - Corner and edge handles scale; the outer part of a handle rotates; the cursor changes to show which [D A33].
  - While rotating, Shift snaps the angle (26.6°, 45°, 90°…) [D A39].
  - While scaling, Shift keeps the aspect, Alt scales from the centre/pivot and Ctrl gives fine sub-pixel scaling [D A39].
  - The pivot is shown and set in the context bar [D A33].
- **Rotation algorithms.** **Fast Rotation** or **RotSprite** [D A33]. There is a preference to force RotSprite even for right angles [D A27]. RotSprite "avoid[s] extreme pixel distortions" on tiny sprites [D A52].
- **Flip.** Edit > Flip Horizontal **Shift+H** and Vertical **Shift+V** [D A34]. Canvas flips are under Sprite > Rotate Canvas [D A66].
- **Applying and cancelling.** Esc drops the pixels and deselects. Since 1.3.15 the apply/discard keys are configurable [D A45].
- **Multiple cels.** Transforms apply to all cels selected in the timeline [D A67].

## 7. Canvas navigation

- **Zoom keys.** **1–6** set 100/200/400/800/1600/3200 % [D A36].
- **Mouse wheel.** By default the wheel **zooms**; on Windows, wheel-up zooms in. Shift+wheel scrolls horizontally and **Ctrl+wheel changes brush size**. Unchecking "Zoom with scroll wheel" makes the wheel scroll and Ctrl+wheel zoom [D A37].
- **Zoom from where.** Zoom is centred on the mouse by default; preferences switch it to the editor centre [D A27].
- **Other zoom methods.** The Zoom tool (Z: click, or drag left/right), the status-bar zoom field/slider, and since 1.3-beta12 a hold-key-and-drag zoom [D A36].
- **Pan.** Space+drag, middle-drag or the Hand tool (H). Space+arrows moves 1 px and Shift+Space+arrows one tile [D A51, A9]. Shift+Z centres the view [D A44].
- **Opening a sprite.** It opens at **100 %**; "Auto-fit on screen" is off by default [D A27]. Below 50 % zoom, the default downsampling is bilinear mipmapping [D A27].
- **Integer zoom.** Zoom snaps to fixed steps so pixels stay equal size. A community regular explains that this is deliberate; others ask for 75/125/150 % steps [C/S C3]. The levels cannot be changed without rebuilding from source [T search summary of C3] (I did not verify this).
- **No view rotation or flip.**
  - Sprite > Rotate Canvas rotates the *pixels* (180°, 90° CW/CCW) [D A56].
  - In 2021 dacap said viewport rotation needs a rendering rewrite that had not started [C C1].
  - Users were still asking in 2025 [C C2], and a flip-view request dates from 2018 [C C11].
  - As of Sep 2026 the v1.4 render rewrite aims to add rotation and flips [C C3].
- **Touch.** Pinch zoom works on a Windows Surface [D A57].

## 8. Keyboard shortcuts

- **Editing shortcuts.** Edit > Keyboard Shortcuts (**Ctrl+Alt+Shift+K**) has sections for Menus, Commands, Tools, Action Modifiers, Mouse Wheel and Drag Value, with a search field. You can bind commands that are not in any menu, e.g. "Set Ink Type" [D A39].
- **Recent additions.** Search by shortcut (1.3.16) [D A49]. Mouse buttons, including X1/X2, can be bound to commands, tools or drag values (1.3.17) [D A49]. This matters for pen side buttons [I].
- **Command palette.** "Run Command" (**Ctrl+Space**, View > Run Command, 1.3.16) searches all commands and scripts, evaluates math with "=" and inline Lua with "@" [D A40, A47].
- **Preferences.** **Ctrl+K**, with a search field since 1.3.18 [D A27, A45].
- **Undo.** Ctrl+Z / Ctrl+Y [D A41].
- **Photoshop overlap** [I]. B, E, I, G, M, V, W, U, T, Z, H and Ctrl (temporary Move) map much as they do in Photoshop. L (Line rather than Lasso) and Q (Lasso rather than Quick Mask) differ.

## 9. Tablet

- **Pen API.** Edit > Preferences > **Tablet** offers "Windows 8/10 Pointer API (Windows Ink)", "Wintab" and "Wintab (direct packet processing)". Changes apply without a restart. The same page has "Interpret one finger as mouse movement" and "Load wintab32 library" checkboxes [D A15 + screenshot].
- **Default API.** Since 1.2.34 the default is the **Pointer API**, because buggy third-party Wintab drivers caused crashes [D A16]. The Tablet doc still calls Wintab the default; that text is stale [I].
- **Recovery options.** The `-disable-wintab` launch flag helps if Aseprite will not start [D A16]. Aseprite may switch itself to the Pointer API after a Wintab crash [C dacap, C9].
- **Pressure.** Pressure drives only size, angle and colour-gradient dynamics (see §2) [D A14].
- **Huion reports** [C/S]:
  - A Kamvas 20 drew single pixels until Wintab was disabled. dacap tested a Kamvas and confirmed that disabling Wintab fixed it (2020) [C8].
  - Huion users dislike the Windows Ink feedback circle and the modifier-key indicator. Wintab avoided them but became unreliable for one user [C9, C10].
- **Fixes in 1.3.x.** Mouse/stylus lag was fixed in 1.3.1 and a mouse-vs-stylus cursor issue in 1.3.8 [D A45].
- **FAQ tip for pen lag.** Adjust double-click speed and area in the Windows and tablet settings [D A51].

## 10. Distraction-free

- **Tab** toggles **only the timeline**, unlike Photoshop, Krita and GIMP, where Tab hides the panels [D A6, G2; comparison I].
- **Ctrl+F (Advanced Mode).** Users report it hides the tool bar and color bar/palette [C C7, C6]; the shortcut itself is official [D A44, A53]. People trigger it by accident and "lose" their palette [C C7].
- **F11** toggles full screen (added 1.3-beta1) [D A45]. **F8** is a full-screen *preview* only [D A44].
- **No borderless mode.** No true borderless drawing mode exists, and Advanced Mode is "the closest you can get" (2020) [C C6]. In 2024 dacap floated a possible "Zen mode" [C C6].
- **Other options.** Scroll bars can be hidden [D A27]. Saved workspace layouts can be made minimal [D A2].

## 11. UI scaling

- **Settings.** Preferences > General has **Screen Scaling**, documented default **200 %**, where each Aseprite pixel becomes 2×2 screen pixels. It also has **UI Element Scaling**, default 100 %, which scales the UI but not the editor [D A27].
- **Why 200 %.** The FAQ says double-sized pixels are deliberate, to reproduce the look of old sprite editors [D A51].
- **Integer steps only.** dacap (2018) said 150 % screen or UI scaling is impossible [C C5]. A third-party "150%" theme exists as a workaround [T T1].
- **Recommended combination.** dacap recommends Screen 100 % + UI 200 %, so that 100 % zoom shows one sprite pixel per screen pixel. A user replied that buttons and the cursor become too small that way [C C4]. 1.3.18.3 added an option to scale the crosshair with the UI [D A50].
- **On a Kamvas 13** [I]:
  - 1920×1080 on 13.3″ is about 166 ppi.
  - At the default 200 %, the UI behaves like a 960×540 canvas, which is chunky.
  - At 100 %/100 % it is tiny for pen targets.
  - 100 %/200 % is the usual compromise.

## 12. Undo/redo & time-to-first-stroke

- **Undo.** Ctrl+Z / Ctrl+Y. Edit > Undo History lists states. **Non-linear history** is optional. The undo limit is set in MB per sprite. Further preferences: "Go to modified frame/layer" and an undo tooltip [D A41, A27]. Undo internals got faster in 1.3.18-beta1 [D A45].
- **Starting to draw.**
  - From the Home tab choose **New File**, or press **Ctrl+N** for the New Sprite dialog. It asks for width/height (fields accept maths), colour mode (RGB/Grayscale/Indexed) and background (Transparent/White/Black) [D A42]. The doc screenshot shows **32×32, RGBA, Transparent** [D A42 image].
  - New sprites use the DB32 palette [D A20].
  - Ctrl+Alt+N creates a new sprite from the selection [D A42].
  - The sprite opens at 100 % zoom [D A27], and the pencil is ready on left click [D A9].
- **Safety net.** Recovery data is saved every 2 min by default, and closed sprites are kept in memory for a while [D A43].

## 13. What Aseprite does well, and why — and its frustrations

**Strengths**

- **One contextual strip instead of panels.** The Context Bar shows only what the current tool needs [D A3]. The canvas stays large and option discovery stays in one place [I].
- **Two-colour mouse model everywhere.** Left = FG, right = BG, X swaps, Alt picks [D A10, A5, A9]. It is fast and consistent [I].
- **Hold-to-use quick tools.** Alt for the eyedropper, Space for the hand, Ctrl for move; they return to the previous tool on release [D A39]. This keeps you in the flow of drawing [I].
- **Pixel-art-specific quality.** Pixel-perfect strokes, RotSprite, Shading ink, dithered gradients, pattern brushes, Tiled Mode and symmetry [D A52, A3, A28, A29].
- **Palette-first workflow.** Indexed mode, presets and ramp-based shading [D A18, A19, A12].
- **Exact zoom.** Integer zoom and the 1–6 keys keep pixels crisp [D A36; S C3].
- **One timeline for layers and frames** [D A22].
- **A modern command palette, searchable shortcuts and data recovery** [D A40, A43].
- **Generally praised UX** ("simple, specialized… great UX", Product Hunt user) [S T2].

**Frustrations**

- **No view rotation or flip.** It has been requested since 2018 and the fix is tied to the v1.4 render rewrite [C C1, C2, C3, C11]. This hurts pen-display drawing [I].
- **Coarse UI scaling.** Integer steps only, with a chunky 200 % default and 100 % tiny on small screens [C C4, C5].
- **Coarse zoom steps** [C C3].
- **No borderless focus mode** [C C6]. **Accidental Advanced Mode** hides the palette [C C7].
- **Windows Ink / Wintab friction with Huion tablets** [C C8, C9, C10].
- **Limited pressure dynamics.** Pressure cannot drive opacity, and the stabilizer is only a simple distance-based one [D A14; I].
- **Slow progress.** One forum user: "same major update since 2021… mostly fixes and tiny features" [S C2].
- **Narrow scope.** It is not a general drawing tool, and new users find it confusing at first [S T2].

---

## 14. GIMP 3.x — brief UI reference

- **Single-window mode** is the default on first launch. Panels are fixed but their width can be changed. Image tabs sit on top; switch with Ctrl+PgUp/PgDn or Alt+number [D G1]. Windows > Single-Window Mode toggles it [D G1, G2].
- **Tab hides all docks** (Windows > Hide Docks). The hidden state persists across restarts [D G2, G9]. **F11** is full screen [D G3].
- **Tool groups.**
  - They were added and enabled by default in 2.10.18, with a changed default tool order [D G5].
  - Hover over a group, or long-click it, to pick another tool in it.
  - Turn groups off or edit them in Preferences > Toolbox ("Use tool groups") [D G4].
  - The 3.0 Welcome dialog's *Personalize* page also toggles tool groups and the icon and theme style [D G12].
- **On-canvas modifiers.** The defaults below come from the 2.99.12 dev notes [D G6]; all are customizable in Preferences > Canvas Interaction > Modifiers [D G7, G12].

  | Input | Action |
  |---|---|
  | Middle-drag | Pan |
  | Ctrl + middle-drag | Zoom |
  | **Shift + middle-drag** | **Rotate view** |
  | Shift+Ctrl + middle-drag | 15° rotation steps (confirmed in the 3.2 docs [D G7]) |
  | Alt + middle | Pick a layer |
  | **Alt + right-drag** | **Resize brush on canvas** |

- **Brush size keys.** [ / ] change size by 1 and { / } by 10; Ctrl+Alt+wheel also works [D G18].
- **View rotation and flip.** View > Flip & Rotate offers flip H/V, ±15°, ±90°, 180° and "Other rotation angle…". **!** resets the view [D G8, G9]. This has existed since 2.10 [D G24]. Pinch-rotate exists [D G6].
- **Other navigation.** Space pans; it can instead toggle the Move tool or do nothing [D G19]. The wheel *scrolls* and Ctrl+wheel *zooms* (the opposite of Aseprite's default); **1** = 100 % [D G9, G20].
- **Action search.** **/** opens "Search and Run a Command" (also Help menu) [D G10, G11]. Since 3.0 it shows each action's menu location, has a help button, and finds GEGL filters [D G12].
- **Sliders.**
  - 2.10.18 introduced **compact sliders**: drag to change, Shift- or right-drag for small steps, Ctrl-drag for big steps [D G5].
  - In 3.0 the number sliders are larger and easier to grab, with +/− icons [D G12].
  - I found no "Simple vs Compact" toggle in the 3.2 docs [I].
- **Tablet setup.**
  - Preferences > Input Devices has a Windows-only **Pointer Input API** choice, Windows Ink or Wintab, with Ink "preferred" for newer devices. It also has the "Configure Extended Input Devices" button, which Edit > Input Devices opens too [D G14, G15].
  - The per-device **pressure curve** lives in that dialog. A 3.0.0 user on Windows Ink saw no axis or curve options there, while 2.10 worked [C/S GF1].
  - Dynamics can be switched off with an "Enable Dynamics" checkbox (3.0) [D G12]. Fine-tuning curves are in the Paint Dynamics editor [D G26].
- **Save vs Export.**
  - **Save (Ctrl+S) writes only XCF**; saving to another format gives an error with a link to Export [D G16].
  - **Export As** is Ctrl+Shift+E, plus "Export to <file>" (Ctrl+E) and "Overwrite <file>" [D G17, G10].
  - Rationale and criticism [T/S]: David Revoy calls it a sound design that protects source files, but "not consistent with my other softwares… I can't 'save' an .ora anymore". In 2019 he added that things were "still exactly the same" [GT1]. A 2012 mailing-list thread was titled "HATE the new save vs. export behavior" [S GT2].
  - Photoshop users expect Save to write the open PNG or JPG in place [I].
- **Colour picking and tool keys.** Ctrl+click picks a colour while painting, where Photoshop and Aseprite use Alt [D G22].
  - Pencil **N**, Paintbrush **P**, Eraser **Shift+E**, Color Picker **O**
  - Rect Select **R**, Move **M**, Paths **B**
  - X swaps colours [D G23]. These differ from Photoshop [I].
- **3.0 improvements relevant to drawing** [D G12]:
  - GTK3 brings much better HiDPI scaling and tablet input.
  - **Paste creates a new layer** instead of a floating selection.
  - Paint tools can auto-expand layers past their bounds.
  - Multi-select of layers.
  - Non-destructive filters.
  - The Welcome dialog.
  - A dark Windows title bar, which can merge with the menu.
- **3.2 improvements relevant to drawing** [D G13]:
  - An **Overwrite** paint mode recommended for pixel art with the Pencil.
  - **Shift+X swaps between the last two tools.**
  - MyPaint brushes v2 respect canvas rotation and barrel rotation, and gain a pressure "Gain" slider.
  - Theme-coloured brush previews and a System colour scheme.
  - Theme preferences also offer font scaling from 50 % to 200 % and an icon-size override [D G21].
- **Criticism** [S]. In a 2025 pixls.us thread a long-time user said GIMP "has gone in the other direction" from familiarity. Others pointed to limited manpower and vague complaints [GP1].

---

## Implications for DoodleNook [I]

- Adopt the **context bar** idea: one strip that shows only the current tool's options.
- Adopt **hold-to-use quick tools**: Alt eyedropper, Space hand, Ctrl move.
- Use **left/right-click for two colours** with **X to swap**.
- In the Pixel Art workspace: integer zoom on the 1–6 keys, a pixel-perfect toggle, RotSprite, shading ink on palette ramps, an indexed mode with presets, symmetry, tiled mode and an F7-style floating preview.
- Fix Aseprite's gaps:
  - Add view rotation and flip (GIMP's Shift+middle-drag plus "!" to reset).
  - Make **Tab hide all panels**, as in Photoshop, Krita and GIMP.
  - Offer non-integer UI scaling such as 150 % for a 1080p 13″ display, while keeping integer *canvas* zoom available.
  - Add pressure → opacity.
  - Default to Windows Ink and offer Wintab as a fallback, with clear feedback.
- Avoid GIMP-style Save/Export friction: Ctrl+S should save the open file's format, with a lossy-format warning.
- Keep GIMP's "/" action search, which matches Aseprite's Ctrl+Space Run Command.

---

## Source legend

**Aseprite (official)**
A1 https://www.aseprite.org/docs/workspace/ · A2 https://www.aseprite.org/docs/workspace-layout/ · A3 https://www.aseprite.org/docs/context-bar/ · A5 https://www.aseprite.org/docs/color-bar/ · A6 https://www.aseprite.org/docs/timeline/ · A7 https://www.aseprite.org/docs/status-bar/ · A8 https://www.aseprite.org/docs/menu-bar/ · A9 https://www.aseprite.org/docs/basics/ · A10 https://www.aseprite.org/docs/drawing/ · A11 https://www.aseprite.org/docs/ink/ · A12 https://www.aseprite.org/docs/shading/ · A13 https://www.aseprite.org/docs/brushes/ · A14 https://www.aseprite.org/docs/dynamics/ · A15 https://www.aseprite.org/docs/tablet/ · A16 https://www.aseprite.org/docs/wintab/ · A17 https://www.aseprite.org/docs/eyedropper/ · A18 https://www.aseprite.org/docs/color-mode/ · A19 https://www.aseprite.org/docs/tutorial/color-bar-tutorial/ · A20 https://www.aseprite.org/docs/default-palette/ · A21 https://www.aseprite.org/docs/layers/ · A22 https://www.aseprite.org/docs/sprite/ · A23 https://www.aseprite.org/docs/onion-skinning/ · A24 https://www.aseprite.org/docs/tags/ · A25 https://www.aseprite.org/docs/linked-cels/ · A26 https://www.aseprite.org/docs/animation/ · A27 https://www.aseprite.org/docs/preferences/ · A28 https://www.aseprite.org/docs/symmetry/ · A29 https://www.aseprite.org/docs/tiled-mode/ · A30 https://www.aseprite.org/docs/sprite-properties/ · A31 https://www.aseprite.org/docs/preview-window/ · A32 https://www.aseprite.org/docs/selecting/ · A33 https://www.aseprite.org/docs/rotate/ · A34 https://www.aseprite.org/docs/flip/ · A35 https://www.aseprite.org/docs/move-selection/ · A36 https://www.aseprite.org/docs/zoom/ · A37 https://www.aseprite.org/docs/mouse-wheel/ · A38 https://www.aseprite.org/docs/drag-value/ · A39 https://www.aseprite.org/docs/keyboard-shortcuts/ · A40 https://www.aseprite.org/docs/run-command/ · A41 https://www.aseprite.org/docs/undo-history/ · A42 https://www.aseprite.org/docs/new-sprite/ · A43 https://www.aseprite.org/docs/data-recovery/ · A44 https://www.aseprite.org/quickref/ (embedded official v1.3 Quick Reference Google Doc) · A45 https://www.aseprite.org/release-notes/ · A46 https://blog.aseprite.org/2023/11/27/aseprite-v13/ · A47 https://blog.aseprite.org/2025/09/23/aseprite-v13153/ · A48 https://blog.aseprite.org/2025/06/11/aseprite-v1314/ · A49 https://blog.aseprite.org/2025/12/11/aseprite-v1316/ · A50 https://blog.aseprite.org/2026/07/22/aseprite-v1318/ · A51 https://www.aseprite.org/faq/ · A52 https://www.aseprite.org/ · A53 https://www.aseprite.org/api/app_command · A55 https://www.aseprite.org/docs/text-tool/ · A56 https://www.aseprite.org/docs/rotate-canvas/ · A57 https://www.aseprite.org/docs/sprite-editor/ · A58 https://www.aseprite.org/docs/transparent-color/ · A61 https://www.aseprite.org/docs/layer-group/ · A62 https://www.aseprite.org/docs/eraser/ · A63 https://www.aseprite.org/docs/frame-duration/ · A64 https://www.aseprite.org/docs/cel/ · A66 https://www.aseprite.org/docs/flip-canvas/ · A67 https://www.aseprite.org/docs/transformations/

**Aseprite community [C]**
C1 https://community.aseprite.org/t/rotate-canvas-feature/10560 · C2 https://community.aseprite.org/t/canvas-rotation-inquiry-about-development/27074 · C3 https://community.aseprite.org/t/standard-zoom-settings-steps-ui-colors/3009 · C4 https://community.aseprite.org/t/50-zoom-on-200-scaling-to-show-the-unscaled-pixel-size/20078 · C5 https://community.aseprite.org/t/is-possible-to-manually-change-screen-scaling/830 · C6 https://community.aseprite.org/t/can-i-work-on-borderless-fullscreen-mode/6530 · C7 https://community.aseprite.org/t/my-palette-is-hided/24212 · C8 https://community.aseprite.org/t/huion-kamvas-tablet-only-draws-single-pixels/5695 · C9 https://community.aseprite.org/t/wintab-option-no-longer-working/13580 · C10 https://community.aseprite.org/t/windows-ink-on-aseprite-cant-detect-pen-cursor/9641 · C11 https://community.aseprite.org/t/flip-view-horizontally/1663

**Third-party** T1 https://thkaspar.itch.io/aseprite-150-theme (search listing only, not opened) · T2 https://www.producthunt.com/products/aesprite/reviews (search snippet)

**GIMP (official)**
G1 https://docs.gimp.org/3.2/en/gimp-concepts-main-windows.html · G2 https://docs.gimp.org/3.2/en/gimp-windows.html · G3 https://docs.gimp.org/3.2/en/gimp-view-fullscreen.html · G4 https://docs.gimp.org/3.2/en/gimp-prefs-toolbox.html · G5 https://www.gimp.org/news/2020/02/24/gimp-2-10-18-released/ · G6 https://www.gimp.org/news/2022/08/27/gimp-2-99-12-released/ · G7 https://docs.gimp.org/3.2/en/gimp-prefs-canvas-modifiers.html · G8 https://docs.gimp.org/3.2/en/gimp-view-flip-rotate.html · G9 https://docs.gimp.org/3.2/en/key-reference-view.html · G10 https://docs.gimp.org/3.2/pdf/gimp-keys-en.pdf · G11 https://docs.gimp.org/3.2/en/gimp-help-search-and-run.html · G12 https://www.gimp.org/release-notes/gimp-3.0.html · G13 https://www.gimp.org/release-notes/gimp-3.2.html · G14 https://docs.gimp.org/3.2/en/gimp-prefs-input-devices.html · G15 https://docs.gimp.org/3.2/en/gimp-edit-input-devices.html · G16 https://docs.gimp.org/3.2/en/gimp-save-dialog.html · G17 https://docs.gimp.org/3.2/en/gimp-export-dialog.html · G18 https://docs.gimp.org/3.2/en/gimp-using-variable-size-brush.html · G19 https://docs.gimp.org/3.2/en/gimp-prefs-canvas-interaction.html · G20 https://docs.gimp.org/3.2/en/gimp-image-window.html · G21 https://docs.gimp.org/3.2/en/gimp-prefs-theme.html · G22 https://docs.gimp.org/3.2/en/gimp-tool-pencil.html · G23 https://docs.gimp.org/3.2/en/key-reference-tools.html · G24 https://www.gimp.org/release-notes/gimp-2.10.html · G25 https://www.gimp.org/news/2026/11/02/gimp-3-2-8-released/ (from gimp.org news index) · G26 https://docs.gimp.org/3.2/en/gimp-tool-dynamics.html

**GIMP third-party/community** GT1 https://www.davidrevoy.com/article146/my-thoughts-about-the-save-export-refactor-in-gimp-2-8/show · GT2 https://lists.gnome.org/archives/gimp-user-list/2012-August/msg00040.html (thread title via search) · GF1 https://www.gimp-forum.net/Thread-Stylus-pressure-works-on-2-10-broken-on-3-0 · GP1 https://discuss.pixls.us/t/gimp-is-falling-behind-where-is-the-ux/48708
