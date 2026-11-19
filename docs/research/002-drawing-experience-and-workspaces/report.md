> **Preservation note (added in Mission 003).** The report below is reproduced verbatim from the
> research session's final answer, recovered from the session transcript. Nothing in it was edited.
> Paths it mentions under `/tmp/claude-0/…/scratchpad/` were temporary; the supporting notes they point to are
> preserved next to this file in [`notes/`](notes/) and the five static wireframes in [`wireframes/`](wireframes/). Raw web page downloads and cloned
> third-party source code were deliberately not preserved.

---

# DoodleNook Research Mission 002: drawing experience, interface design and workspaces

## What this is based on

**Applications and versions studied** (official documentation, November 2026):
- Adobe Photoshop 28.0
- Clip Studio Paint 5.1.5 (CSP)
- Krita 5.3.4
- Aseprite 1.3.18
- GIMP 3.2.8 (secondary reference)

**Other sources:**
- Huion's Kamvas 13 product page, manual and support articles.
- Microsoft's Windows scaling and pen/touch guidance.
- Published research on pen interfaces.

**Method:**
- Five research strands ran in parallel. I re-checked the most important claims against the downloaded official pages myself.
- I couldn't run any of the applications, so this is documentation and research, not hands-on testing.
- Nothing here ranks one brush engine's "feel" above another. That needs testing with the user.

**Labels:**

| Label | Meaning |
|---|---|
| [D] | Vendor documentation |
| [R] | Peer-reviewed research |
| [T] | Third-party tutorial or review |
| [S] | Community opinion |
| [I] | My inference |

Numbers in brackets point to the sources at the end. Fuller source lists are in the notes files listed in Appendix B.

**Visuals:** five static wireframes (sent above) drawn at the Kamvas 13's real 1920×1080 pixel grid with 150% Windows scaling. They are research concepts, not designs.

**Repository:** DoodleNook was not modified, committed to or pushed. It is still at `ff933ea`.

---

## 1. Executive summary and major findings

1. **The four apps share one core drawing "grammar", and DoodleNook should use it.** It includes:
   - single-key tools;
   - hold-a-key-to-borrow-a-tool ("spring-loaded");
   - resizing the brush by dragging on the canvas;
   - a modifier key to pick colour;
   - Space to pan and a key to rotate the view;
   - Tab to hide the interface.

   Photoshop defined most of it, and the others copy or offer compatibility modes. Krita ships Photoshop, SAI and CSP shortcut schemes [35]. Huion's express keys default to Photoshop shortcuts [50]. Users stumble where apps break the grammar:
   - CSP's `[ ]` step through size presets instead of resizing smoothly.
   - Krita picks colour with Ctrl, not Alt.
   - Aseprite has no view rotation.

2. **The most pen-friendly pattern found is a palette that opens at the pen tip.**
   - Krita's right-click Pop-up Palette contains favourite brushes, a colour ring, recent colours, rotation, zoom and mirror [29].
   - Marking-menu research found that once people learn the item positions, flicking to them was 3.5× faster than choosing from the open menu, and pen use was faster than mouse use [55].
   - Pen-display research identifies trips to edge palettes as costly because of reach, hand occlusion and precision [54][56].

3. **Contextual "next step" controls help when they stay out of the drawing area.** Examples:
   - Aseprite's context bar shows only the current tool's options [38].
   - CSP's Selection Launcher and Krita 5.3's Selection Action Bar appear only when a selection exists [18][36].
   - Photoshop's Contextual Task Bar is the cautionary case: users complain it sits in the way and forgets where it was moved [6].

4. **"Drawing feel" levers are well documented, but which feels best is not.** The levers are:
   - smoothing (Photoshop's 4 modes, CSP's per-tool stabilization with speed adjustment and stroke preview, Krita's 4 modes with speed-adaptive sampling) [2][16][31];
   - pressure calibration (CSP learns a curve from strokes you draw; Krita has a global curve and a tablet tester; Photoshop has no app-wide curve) [17][32][8];
   - "erase with the same brush" (Krita's E, CSP's transparent colour on C, Photoshop's hold-~) [34][20][13].

   Which combination feels best can only be settled by testing with the user.

5. **The Kamvas 13 is a demanding screen.** It is 13.3", 1920×1080 and about 166 pixels per inch.
   - Windows will probably recommend 150%, which leaves about 1280×720 logical pixels [51]. That is exactly Photoshop 28's documented *minimum* display [7].
   - A conventional Photoshop layout leaves roughly 60–67% of the screen for canvas at 150%.
   - A right-handed person's hand and arm typically hide about 38% of a 12" pen screen [53].
   - The PW517 pen has 2 side buttons and **no eraser end**. The 8 express keys sit on the left bezel and default to Photoshop shortcuts [48][49][50].

6. **The answer is "Photoshop's structure, slimmer by default"**, not "drop Photoshop's layout":
   - tools on the left, options on top, panels on the right that collapse to an icon strip;
   - layers available as a pop-over in lighter workspaces;
   - a radial palette for the frequent actions;
   - no hover-to-reveal panels on the pen display;
   - status messages top-left, outside the area the hand covers.

7. **Every app saves workspaces, but none adapts them to the screen in use.** Krita's Window Layouts remember which monitor each window was on, the closest thing found [7 in smallscreen notes]. Quick, express-key workspace switching and sensible layouts for a pen display versus a desk monitor are an opportunity.

8. **Complexity to avoid:**
   - CSP spreads settings across five places, renamed its tool hierarchy in v5, and shares keys between tools.
   - Krita's dockers behave inconsistently, and its brush editor is deep.
   - Photoshop has AI-first onboarding, tools hidden in flyouts, three brush panels, and a flip-view that jumps.
   - GIMP's Save (XCF only) versus Export split confuses people.
   - Aseprite only allows UI scaling in whole steps.

9. **Pixel art has a strong, compact reference in Aseprite:**
   - context bar, two colours on left/right click, held "quick tools";
   - whole-number zoom on keys 1–6;
   - pixel-perfect strokes, indexed palettes and shading ink;
   - RotSprite rotation, tiled mode, a floating 1:1 preview.

   Its known gaps are no view rotation/flip, UI scaling only in whole steps, and pressure that cannot control opacity [38][41][42][43][44].

10. **Tablet setup friction is universal.**
    - All four apps expose a Windows Ink versus WinTab choice. Huion tells users to match the driver's Windows Ink checkbox to the app [8][32][22].
    - Kamvas 13 owners report lost pressure in Photoshop and Krita.
    - This confirms Mission 001's conclusion that DoodleNook must make tablet setup painless.

---

## 2. Comparative analysis

### 2.1 Aspect by aspect

| Aspect | Photoshop | Clip Studio Paint | Krita | Aseprite | Strongest reference |
|---|---|---|---|---|---|
| **Interface layout** | Tools left, options bar top, panels right (collapse to icons), document tabs, Home screen. 28.0 adds an "AI Assisted Editor" mode [D] | Command bar top. Tools, tool groups and colour on the left. Navigator and layers on the right. Material tabs on the far right (v5 layout) [D] | One top toolbar (brush, blend, opacity, size), two-column toolbox left, dockers right [D] | Context bar top, tools right, palette left, timeline bottom. Default or Mirrored layouts [D] | Photoshop for familiarity; Aseprite for minimal chrome |
| **Toolbar** | Single or double column, grouped fly-outs (optionally open on hover since Nov 2026), *Edit Toolbar* to hide rare tools in "Extra Tools" [11] | 3 levels: tool → tool group → Tool Settings. Each "tool" is really a saved preset [D] | Every tool visible. Toolbars configurable but always the same whatever tool is active [D] | Grouped strip that compacts when the window is small [D] | Photoshop's customisable toolbar |
| **Drawing workflow** | Brush, Pencil, Mixer Brush. Opacity = cap per stroke; Flow = build-up. Painting on a non-pixel layer creates a new layer automatically [13] | Pen, pencil, brush and airbrush families. Re-editable vector layers. Fills that read other layers. Smart Shape [24][25] | Presets across 18 brush engines. E toggles erase on the current brush. Drawing assistants [D] | Pixel pencil, pixel-perfect, inks; left click = foreground colour, right = background [D] | CSP for line art and flats; Aseprite for pixels |
| **Brush choice & size** | Right-click picker. Options bar: size, opacity, flow, smoothing, pressure toggles. **Alt+right-drag**: size (horizontal) and hardness (vertical). `[ ]` resize; number keys set opacity [D/T] | Preset list with stroke previews. Brush Size palette. **Ctrl+Alt+drag**. `[ ]` *step through presets* [21] | Presets docker with tags. Pop-up ring. **Shift+drag**. `[ ]`; I/O for opacity. Deep brush editor (F5) [D] | Size in context bar, +/−, Ctrl+wheel [D] | Photoshop's on-canvas size/hardness drag |
| **Colour** | Foreground/background (X, D). HUD picker on Shift+Alt+right-click (strip or wheel). Alt = temporary eyedropper [4] | Wheel, set, history, intermediate and approximate grids, mixing palette. Alt or right-click eyedropper. Transparent "colour" [D] | Advanced selector. Pop-up colour ring and history. **Ctrl** samples. H/U pop-ups. K/L darker/lighter [D] | Palette first, indexed mode, shading ink, Alt picks [41][45] | CSP for range; Krita's pop-up for speed |
| **Layers** | Mature panel: locks, clipping (Alt-click), masks, groups, Ctrl+J/G [D] | Clip to layer below, **reference** and **draft** layers, colour tags, lock transparency [D] | "Inherit alpha" instead of clipping masks; groups isolated by default; R+click picks a layer [34] | Timeline doubles as the layers panel [D] | Photoshop conventions plus CSP's reference/draft layers |
| **Selection & transform** | M, L, W; AI Select Subject; heavy *Select and Mask*. Ctrl+T scales proportionally by default [D] | Floating **Selection Launcher**. Ctrl+T, mesh, liquify [18] | Vector selections; **Selection Action Bar** (5.3); transform modes incl. warp, cage, liquify, mesh [36] | Simple tools; RotSprite; X/Y/W/H fields in the context bar [D] | CSP's launcher pattern |
| **Canvas navigation** | Space pan. **R** rotate (hold for temporary). **H** bird's-eye view. Ctrl+0/1. Flip view has no default key and "jumps" [9][14] | Space pan, Shift+Space rotate. Flip via the Navigator. Some keys assume a Japanese keyboard (e.g. Ctrl+@) [D] | Space, Ctrl+Space zoom, Shift+Space rotate, **4/6/5** rotate steps and reset, **M** mirror view, 1/2/3 zoom presets [37] | Zoom keys 1–6 (100–3200%); **no view rotation or flip** [44] | Krita (mirror on M, rotation keys); Photoshop's H |
| **Keyboard** | Single letters, Shift cycles a group, hold-to-switch with a 200 ms threshold, named shortcut sets [1] | Shortcut and Modifier Key settings. Workspaces can swap shortcuts. Some letters shared (B = brush/airbrush/decoration) [D] | Photoshop, SAI and CSP schemes. Configurable canvas input. **Ctrl+Enter** action search [35] | Photoshop-like letters, held quick tools, **Ctrl+Space** command palette [40] | Krita's compatibility schemes; Photoshop's letters |
| **Tablet** | Per-brush pressure and tilt. Pressure toggles on the options bar. **No global curve.** Windows Ink default. Officially tests only Wacom [8] | **Pressure settings learned from strokes you draw**; per-tool curves (pressure, tilt, velocity); Wintab or Windows Ink setting [17] | Global curve plus per-brush sensors. **Tablet Tester.** WinTab default [32] | Dynamics for size, angle and gradient only. Windows Ink default [43] | CSP's calibration; Krita's tester |
| **Workspaces** | Built-ins incl. *Painting*. Saves panels, shortcuts, menus and toolbar. Lock Workspace [12] | Illustration and Comic built-ins. Saves palettes, shortcuts and command bar. Palette locks [26] | Docker layout only; **Window Layouts** across monitors; sessions [D] | Default and Mirrored plus user layouts (1.3.16) [39] | Photoshop for completeness; Krita for multiple monitors |
| **Distraction-free** | Tab / Shift+Tab. F cycles screen modes. Hidden panels auto-show when hovering at the edge [5] | Tab hides palettes; Shift+Tab hides title and menu bars [19] | **Tab = canvas-only, configurable.** Pop-up palette and Ctrl+Enter still work [30] | Tab only hides the timeline. Ctrl+F hides the bars. F11 [D] | Krita |
| **Contextual / floating** | Contextual Task Bar (criticised); HUD colour picker; on-canvas resize overlay | Selection and Transform launchers; pop-up palettes on shortcuts; Quick Access; phone as a remote (Companion mode) [28] | **Pop-up Palette**; Selection Action Bar; on-canvas brush sliders; on-canvas messages | Context bar | Krita's pop-up; CSP's launchers |
| **Discoverability** | Discover panel (Ctrl+F); rich tooltips with video [15] | **No global command search** (Quick Access search only); settings spread across 5 places | Ctrl+Enter action search | Ctrl+Space Run Command; searchable preferences | A command palette (Krita, Aseprite, GIMP "/") |
| **Time to first stroke** | Home → New (Art & Illustration presets) → B. AI onboarding can intervene [D] | **Creates a canvas automatically** after launch (first launch adds the pressure wizard) [D] | Welcome page → Ctrl+N; brush already active [D] | Home tab → New Sprite (32×32 default), pencil ready [D] | CSP |

### 2.2 Short verdicts per application

**Photoshop**
- **Good:** the vocabulary.
  - Letters, spring-loaded keys and on-canvas resizing let the hand stay put.
  - Brush settings reveal themselves gradually: picker → Brushes panel → Brush Settings → tool presets [13].
- **Frustrating for a hobbyist drawer:**
  - flip view jumps across the screen;
  - the Contextual Task Bar covers the canvas;
  - no global pressure curve;
  - non-Wacom tablets are not officially supported, and Huion pressure-loss reports exist;
  - GPU-dependent navigation;
  - AI-first onboarding;
  - three overlapping brush panels;
  - tools that vanish after updates [D/S].

**Clip Studio Paint**
- **Best documented line-art and drawing helpers:**
  - per-tool stabilization with speed adjustment, post-correction and a stroke preview that hides lag;
  - pressure calibration by drawing;
  - erasing with a transparent "colour";
  - a vector eraser that erases up to where lines cross;
  - fills that close gaps and read other layers;
  - Smart Shape;
  - selection and transform launchers.
- **Weak spots:**
  - discoverability and settings sprawl;
  - Simple Mode (its minimal UI) exists only on tablets and phones [23];
  - many Photoshop habits break (size keys, right-click eyedropper, shared letters).

**Krita**
- **Strongest pen-display interaction model:**
  - Pop-up Palette;
  - configurable canvas-only mode;
  - mirror and rotation keys;
  - global plus per-brush pressure curves with a tester;
  - per-brush eraser toggle;
  - compatibility schemes.
- **Weak spots:**
  - first impression and docker inconsistency;
  - brush-editor depth;
  - Ctrl rather than Alt for picking colour;
  - "inherit alpha" in place of Photoshop-style clipping masks.

**Aseprite**
- **The model for a minimal, focused tool:** one context bar, two-colour mouse model, held quick tools, palette-first workflow, whole-number zoom.
- **Gaps on a pen display:** no view rotation or flip, coarse UI scaling, limited pressure use.

**GIMP**
- **Useful details:**
  - `/` command search;
  - all on-canvas modifier gestures configurable (Shift+middle-drag rotates; Alt+right-drag resizes the brush) [46];
  - Shift+X swaps the last two tools (3.2).
- **Lesson:** Save versus Export confuses Photoshop users [47].

---

## 3. Particularly successful interface patterns

| Pattern | Seen in | Why it works |
|---|---|---|
| **Single-key tools, Shift cycles related tools** | Photoshop (copied by Aseprite and Krita schemes) | One stable letter per tool *family* builds motor memory without needing a key per tool. Huion's express keys already use these defaults [50]. |
| **Spring-loaded (hold-to-borrow) tools** | Photoshop (200 ms threshold) [1], CSP, Krita, Aseprite quick tools | The temporary mode lasts only while the key is held, so you can never be stranded in the wrong tool. The tap-vs-hold threshold lets one key do both jobs. |
| **A context bar showing only the active tool's options** | Aseprite [38], Photoshop options bar | One predictable place to look, and it hides what's irrelevant, so it scales from simple to complex tools without new panels. |
| **Radial palette at the pen tip** | Krita Pop-up Palette [29], Photoshop HUD colour picker [4] | No reach, no edge targets. Direction-based choices tolerate imprecise aim. Research shows a smooth novice-to-expert path (look, then flick) [55]. |
| **Contextual action bar near the object** | CSP Selection Launcher [18], Krita Selection Action Bar [36], Photoshop Contextual Task Bar [6] | Suggests the likely next step where the pen already is. Must stay clear of the drawing area, be movable and pinnable, and remember its position (the Photoshop complaints). |
| **Canvas-only mode with escape hatches** | Krita (Tab plus pop-up and Ctrl+Enter) [30] | Hides the UI without removing capability; nothing becomes unreachable. |
| **Command search** | Krita Ctrl+Enter, Aseprite Ctrl+Space, GIMP `/`, Photoshop Discover | Makes rarely used functions reachable without cluttering menus or panels. This is what lets a minimal UI stay capable. |
| **Settings revealed in layers** | Photoshop brush picker → panel → settings; CSP Tool Settings vs Advanced, where an eye checkbox promotes any setting to the short list | Casual users see a few controls; power users choose what to promote. |
| **Panels collapse to icons that pop out** | Photoshop, CSP [19] | Keeps the panel's position familiar while reclaiming width. A pop-out closes after use. |
| **Layout locking** | Krita docker locks ("sensitive tablet stylii") [D], Photoshop Lock Workspace, CSP palette locks | Pens drag things by accident; locks stop panels being knocked off the edge. |
| **Shortcut compatibility profiles** | Krita (Photoshop, SAI, CSP) [35] | Respects habits from other apps instead of forcing new ones. |
| **Two colours on left and right click, X swaps** | Aseprite, Photoshop (X/D) | Two "loaded" colours with zero UI. Ideal for pixel art and value sketching. |

## 4. Particularly successful drawing interactions (the feeling of drawing)

The documented behaviour is listed first; "why" is reasoning. Rankings of feel need user testing; see interview Session 2.

**Brush response and smoothness**
- **What exists:**
  - Photoshop: Smoothing 0–100 (default 10%) with Pulled String, Stroke Catch-Up, Catch-Up on Stroke End and Adjust for Zoom. Adobe says higher values "may slightly delay the stroke preview" [2].
  - CSP: stabilization per tool, "Adjust by speed", post-correction, and a **stroke preview** that draws a predicted stroke to hide lag (v4.2) [16][22].
  - Krita: None, Basic, Weighted, Stabilizer. 5.3 adapts sampling to speed (less smoothing when fast) and adds a dead-zone "Delay" for sharp corners [31].
- **Why it matters:** smoothing trades steadiness for latency.
  - The documented ways to soften that trade are making it speed-adaptive, showing a predicted stroke, and setting it per tool.
  - Users complain when catch-up behaviour is unpredictable: "you have to guess when you should stop" [S, Photoshop].

**Cursor and brush preview**
- **What exists:**
  - Brush outline vs crosshair options everywhere.
  - Photoshop: "Show only crosshair while painting" [3].
  - Krita: "effective outline size", which shows the maximum size so the outline doesn't jitter with pressure, and **"No Cursor" for pen displays** [33].
  - CSP: the cursor can follow the *stabilized* position [22].
- **Why it matters:** on a laminated pen display the nib is the cursor.
  - An outline while hovering (the PW517 senses 10 mm above the surface) tells you the brush size before you touch.
  - Extra cursor clutter while drawing is noise.

**Pressure adjustment**
- **What exists:**
  - CSP learns a curve from strokes the user draws, with Stronger/Lighter nudges [17].
  - Krita has a global curve plus a Tablet Tester [32].
  - Photoshop has only per-brush dynamics and the driver curve [8].
- **Why it matters:** calibrating by drawing replaces reading a graph with the user's own strokes, which suits a hobbyist. Separating the device curve from per-brush curves keeps "my hand" separate from "this brush".

**Pencil versus paintbrush**
- **What exists:** Photoshop's Pencil is aliased (hard-edged) [13]; CSP's pencils are textured; Aseprite's pencil works pixel by pixel with optional pixel-perfect cleanup; Krita 5.3 adds a "Pixel" smoothing mode for clean 1-px lines [31].
- **Why it matters:** "pencil" means two things to users, a sketchy textured tool or a hard pixel tool. The interview should establish which they expect (Session 1).

**Brush-size gestures**
- **What exists:**
  - Photoshop Alt+right-drag (horizontal size, vertical hardness or optionally opacity).
  - CSP Ctrl+Alt+drag. Krita Shift+drag. GIMP Alt+right-drag.
  - `[ ]` keys everywhere, except CSP, where they step through presets [21].
- **Why it matters:** an on-canvas drag shows the actual brush footprint where the eyes already are.
  - Photoshop's version silently switches between hardness and opacity after updates [S], so show the mode inside the overlay.

**Brush ↔ eraser**
- **What exists:**
  - Krita **E** toggles the same preset into erase mode, with its own size and opacity.
  - CSP **C** switches to the transparent colour.
  - Photoshop: E for the eraser tool, hold E for a temporary switch, or hold **~** to erase with the current brush [34][20][13].
- **Why it matters:** erasing with the *same* brush keeps texture and pressure behaviour ("the same pencil, reversed"), and no tool switch is needed.
  - The PW517 has no eraser end [48], so this toggle needs a key, express key or side button.

**Colour picking while drawing**
- **What exists:**
  - Alt in Photoshop, CSP and Aseprite; Ctrl in Krita and GIMP.
  - CSP's right-click picks colour by default.
  - Photoshop's sampling ring and HUD picker [4].
  - Sampling the current layer vs all layers is an option in all of them.
- **Why it matters:** picking colour is the most frequent mid-stroke action for painters.
  - A held modifier is the fastest path when the left hand is on the keyboard.
  - A pen button or express key is the fastest when it isn't.

**Canvas rotation and panning**
- **What exists:**
  - Photoshop R (hold for temporary), Shift for 15° steps, Esc to reset [9].
  - Krita Shift+Space, 4/6 for steps, 5 to reset, **M** to mirror the view [37].
  - CSP Shift+Space.
  - Aseprite has none.
- **Why it matters:** rotating matches the stroke to the wrist's natural arc. Flipping reveals proportion errors.
  - Photoshop's flip jumps the view off-centre, a long-standing complaint [14]; flipping in place matters.

**Undo/redo during continuous sketching**
- **What exists:**
  - Ctrl+Z multi-step everywhere.
  - Photoshop keeps 50 history states by default (pages conflict; another says 20).
  - CSP and Krita can merge rapid strokes into one undo step.
- **Why it matters:** sketchers undo constantly, so undo must be one express-key or pen-button press away.
  - Merging strokes trades precision for less clicking; it's worth asking about rather than assuming.

**Avoiding hand travel**
- **What exists:** express keys (Photoshop defaults) [50]; pen side buttons; radial palette; CSP's phone remote (Companion mode) [28]; Krita's Touch Docker (large on-screen buttons).
- **Why it matters:** a 13.3" display on the desk usually takes the keyboard's place [I]. Research found mouse users reaching for Ctrl+Z on the keyboard out of habit [54].

## 5. Interface complexity to avoid

1. **Settings spread across many places.** CSP has Tool Settings, Advanced Tool Settings, Preferences, Modifier Keys and Shortcuts; modifier rules are shared between tools without that being visible. Krita has dockers that each behave differently.
2. **Deep tool hierarchies whose items are secretly presets.** CSP's tool group → tool → settings, renamed in v5. Accidental edits persist unnoticed.
3. **Several overlapping panels for one concept.** Photoshop has the brush picker, Brushes panel, Brush Settings and tool presets. Fine for experts, confusing as defaults.
4. **Floating helpers that sit on the canvas and forget their position** (Photoshop's Contextual Task Bar).
5. **Hover-to-reveal on a pen display.** Hover junk caused 20% of pen hesitation errors in one study [54]; Photoshop's edge auto-show triggers when strokes run near the edge.
6. **Modes that hide things without saying so.** Aseprite's accidental Ctrl+F hides the palette; Krita's "dirty" presets hold unsaved changes; CSP workspaces swap your shortcuts.
7. **Breaking widespread conventions for no gain.** CSP's `[ ]`, Krita's Ctrl+Shift+A to deselect, GIMP's Save vs Export.
8. **Specialist features in the default UI.** Select and Mask, 3D, AI generation, animation timelines, comic panels, material libraries.
9. **AI-first or promotional start screens** before the canvas (Photoshop 27.8–28.0).
10. **Exposing driver plumbing** (Windows Ink vs WinTab, config files) without guidance.

## 6. Recommendations for small drawing displays (Kamvas 13)

### 6.1 Facts that drive the design

**Display** (model **GS1331**, not the 2024 Gen 3) [48][49]:
- 293.76 × 165.24 mm active area, about 166 pixels per inch, fully laminated (minimal gap between glass and image).
- Accuracy ±0.5 mm in the centre and ±3 mm in the corners.

**Pen:** PW517, with 8192 pressure levels, ±60° tilt, 10 mm hover sensing, 2 side buttons, no eraser end.

**Express keys:** 8, on the left bezel in landscape. They can be mapped per app and default to Photoshop shortcuts [50].

**Scaling:**
- 150% gives a 1280×720 logical screen; 125% gives 1536×864. Microsoft's table points to 150% for similar panels [51].
- At 150%, 32 logical pixels measure 7.3 mm, close to Microsoft's 7.5 mm touch target [52]. At 100% they measure only 4.9 mm.

**Hand occlusion:** on a 12.1" display the hand and arm hid about 38% (up to 47%), mostly the lower two-thirds along the right for right-handers [53]. Research advises keeping status and alerts out of the bottom-right and placing pop-ups south-west of the pen [53].

**Screen edges:** a mouse cursor stops at the screen edge, which makes edge targets fast. A pen gets no such help; corners are the least accurate area and the top edge suffers parallax and reach [54].

### 6.2 How much canvas each layout leaves (from the wireframes)

| Layout | Kamvas 13 at 100% | Kamvas 13 at 150% | 27" 2560×1440 at 100% |
|---|---|---|---|
| A Pure Sketch | 100% | 100% | 100% |
| B Quick Sketch (one top bar + slim tool strip) | ≈94% | ≈92% | ≈96% |
| C Painting (right column open / collapsed) | ≈81% / 90% | ≈72% / 85% | ≈88% / 95% |
| D Editing (Photoshop-like) | ≈77% | ≈67% | ≈83% |
| E Pixel Art | ≈83% | ≈75% | ≈88% |
| Typical full Photoshop setup (menu + options + tabs + status, 320-pixel dock) | ≈72% | ≈60% | ≈79% |

### 6.3 Is conventional Photoshop docking appropriate on this screen?

Partly. The *structure* (tools left, options top, panels right) carries the user's muscle memory and fits the left-bezel express keys. A *permanently open* 260–320-pixel dock at 150%, though, uses 20–25% of the width for things the user mostly glances at.

Recommendations:
1. **Keep Photoshop's positions; change how much is open by default.** The right dock collapses to an icon strip whose pop-outs close after use, and lighter workspaces use a layers pop-over.
2. **Move frequent actions to the pen:** a radial palette (side button or express key) with at most 8 items per ring, positioned directionally, labels away from the hand. Also a few big options-bar targets: undo/redo, eraser toggle, size/opacity.
3. **No hover-reveal on the pen display.** Use explicit toggles instead: Tab, an express key, or a visible edge handle. Turn off Windows press-and-hold and flicks on the canvas.
4. **Occlusion-aware placement.** Status top-left. Pop-ups open up and to the left of the pen. A handedness switch mirrors docks and pop-up direction (like Aseprite's Mirrored layout); the Kamvas can be rotated 180° for left-handed use [49].
5. **Size targets in logical pixels against real millimetres:**
   - at least 36–40 for frequent pen targets (8.3–9.2 mm at 150%);
   - at least 32 for dense controls, with 8 between targets;
   - never below 24;
   - nothing frequent in the corners.

   A "Pen / Standard / Compact" density setting lets 100% users still have usable targets.
6. **Design for 1280×720 logical first, and verify on desktop.** On a 27" monitor the same layouts get Fitts's-law edge benefits, and fuller docks become affordable.
7. **Dual screen.** If the Kamvas sits beside a main monitor, panels can live on the main screen, but the pen is mapped to one display [49]. So everything needed mid-stroke must be on the Kamvas, the radial palette, or keys. Layouts should be remembered per monitor.

## 7. Five preliminary workspace concepts (for review, not approval)

Assumptions:
- Right-handed user, Kamvas 13 at 150% (to confirm).
- Photoshop shortcuts throughout.
- An express key or **Tab** toggles between "this workspace" and "Pure Sketch".
- A workspace picker sits in the top-right (or a Window menu).
- Every workspace remembers its own panel layout. Shortcuts stay global unless the user chooses otherwise, avoiding CSP's surprise shortcut swaps.

### A. Pure Sketch (wireframe A)

```
┌──────────────────────────────────────────────────────────────┐
│[Brush 12px · 80% · Layer 3]  ← status pill, top-left, fades   │
│                                                              │
│                     ╭──── radial ────╮                       │
│                   Pencil  Ink  Soft                          │
│                Hide UI  ( colour )  Marker                    │
│                 Layer+   ( ring  )  Erase                     │
│                   Flip  Redo  Undo                            │
│                     ╰── at the pen ──╯                       │
│                       full-screen canvas                     │
└──────────────────────────────────────────────────────────────┘
```

| Question | Proposal |
|---|---|
| Always visible | Nothing; a status pill appears for about 1.5 s after a change |
| Hidden | Everything else |
| Access | Pen button or express key opens the radial (brushes, colour, undo/redo, erase, flip, new layer, show UI); single keys; Space/R/H; command search; Tab back to the previous workspace |
| On the Kamvas | 100% canvas; radial buttons about 57 px (8.7 mm) |
| Advantages | Maximum immersion and canvas; everything comes to the pen |
| Risks | Hidden state (which layer? which brush?) needs strong transient feedback. Discoverability depends on learning the radial. A side button is then committed to the radial. No layer overview. |

### B. Quick Sketch (wireframe B)

```
┌☰│Pencil▾│Size━●━│Opacity━●━│Smooth━●━│●│Erase│↶│↷│Layers▾│…│Quick Sketch▾┐
├──┬────────────────────────────────────────────┬───────────┐────────────┤
│Br│                                            │ Layers    │            │
│Pn│                                            │ pop-over  │            │
│Er│                 canvas                     └───────────┘            │
│Pk│                                                                     │
│La│                                                                     │
│Mv│                                                                     │
│Fl│                                                                     │
│Hd│                                                                     │
└──┴─────────────────────────────────────────────────────────────────────┘
```

| Question | Proposal |
|---|---|
| Always visible | One top bar (menu ☰, brush, size/opacity/smoothing sliders, colour chip, eraser toggle, undo/redo, layers button, workspace picker) plus an 8-tool strip on the left |
| Hidden | Panels, full menus, selection extras, adjustments |
| Access | Layers pop-over (pick a layer, it closes); ☰ menu; shortcuts; radial; command search |
| On the Kamvas | ≈92% canvas; 54 px tool buttons; strip sits beside the express keys |
| Advantages | Feels like a sketchbook with a pencil case; big pen targets for the actions people repeat; still familiar |
| Risks | The top bar's contents vary by tool and may need overflow; the pop-over hides layer thumbnails and context |

### C. Painting (wireframe C)

```
┌ File Edit Image Layer Select View Window ─────────── Painting ▾ ┐
├ Brush▾ │ Size │ Opacity │ Flow │ Smoothing │ Pressure→size/opac ┤
├──┬───────────────────────────────────────────────┬──────────────┤
│Br│                                               │ colour field │
│Pn│                                               │ hue + recent │
│Er│               canvas                          ├──────────────┤
│Pk│                                               │ brush presets│
│..│                                               ├──────────────┤
│  │                                               │ layers (5)   │
└──┴───────────────────────────────────────────────┴──────────────┘
```

| Question | Proposal |
|---|---|
| Always visible | Menus, options bar, tool strip, right column with colour, brush presets (thumbnails) and compact layers |
| Hidden | Selection/transform details, adjustments, history |
| Access | Each section collapses; the column collapses to an icon strip with pop-outs; HUD colour picker; Alt or pen-button eyedropper |
| On the Kamvas | ≈72% canvas open, ≈85% collapsed; consider moving the column to the main monitor in dual-screen setups |
| Advantages | Colour and brush switching without menus; closest to Photoshop's Painting workspace and CSP's defaults |
| Risks | The column is in the hand's path when reaching right; the colour field is small at 150%; ordering inside the column is a preference question |

### D. Editing (wireframe D)

```
┌ File Edit Image Layer Select Filter View Window Help ── Editing ▾ ┐
├ [Marquee]  □⊕⊖⊗ │ Feather │ Anti-alias │ Select and Mask… ───────┤
├──┬────────────────────────────────────────────────┬─────────────┤
│Mv│                                                │ Properties  │
│Mq│        ┌ ─ ─ selection ─ ─ ┐                   ├─────────────┤
│La│        └ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┘                   │ Layers      │
│Wd│     [Fill·Stroke·Transform·Deselect·Invert]    │ (8 rows)    │
│..│                                                ├─────────────┤
│  │                                                │ History     │
├──┴────────────────────────────────────────────────┴─────────────┤
│ status: size · zoom · layers · mode                             │
```

| Question | Proposal |
|---|---|
| Always visible | Full menus, options bar, full toolbar with Photoshop's tool groups, right dock (properties/adjustments, layers, history), status bar |
| Hidden | Colour mixing, brush preset grid (in a tab or pop-out) |
| Access | Photoshop shortcuts; contextual bar under a selection (switchable); command search |
| On the Kamvas | ≈67% canvas at 150% (≈60% for a full Photoshop setup); comfortable on a 27" monitor (≈83%) |
| Advantages | Maximum familiarity for selections, transforms and layer housekeeping |
| Risks | Cramped on the Kamvas; the contextual bar must stay clear of the selection; this is where unneeded features tend to accumulate |

### E. Pixel Art (wireframe E)

```
┌ File Edit Sprite Layer Frame Select View ───────────── Pixel Art ▾ ┐
├ Pencil ■1px │ Pixel-perfect ✓ │ Ink ▾ │ Symmetry │ Grid │ Tiled │ 800% ┤
├──┬───────────────────────────────────────────────┬───────┬─────────┤
│Pn│              ┌─────────────┐                  │Preview│ FG/BG   │
│Er│              │ sprite with │                  │  1:1  │ ▓▓▓▓    │
│Pk│              │ pixel grid  │                  └───────┘ palette │
│Fl│              └─────────────┘                            ▓▓▓▓    │
│..│                                                         ▓▓▓▓    │
├──┴─────────────────────────────────────────────────────────────────┤
│ Timeline (collapsed) · Frame 1/4 · Onion skin off  ▲              │
```

| Question | Proposal |
|---|---|
| Always visible | Context bar (pixel-perfect, ink, symmetry, grid, tiled, zoom), tool strip, palette column with foreground/background, collapsed timeline strip |
| Hidden | Painting panels, brush presets, adjustments |
| Access | Left/right click = foreground/background, X swaps, Alt picks, keys 1–6 for whole-number zoom, floating 1:1 preview, timeline expands when frames exist |
| On the Kamvas | ≈75% canvas; palette swatches 39 px (6 mm), adequate for a pen but tight for 32+ colours |
| Advantages | Aseprite's proven minimal model inside a Photoshop-familiar frame; add what Aseprite lacks (view rotation/flip, 150% UI scaling, pressure → opacity if wanted) |
| Risks | Aseprite users expect the palette on the left and tools on the right, whereas Photoshop has tools on the left (offer Mirrored). Animation scope is a big question (Session 9). |

## 8. Grouped feature inventory (research classification only)

**Columns**
- **Ref** = a good reference app: PS = Photoshop, CSP = Clip Studio Paint, Kr = Krita, Ase = Aseprite, GIMP.
- **Area** = Sk sketching · Pa painting · Ed editing · Px pixel art.
- **Class:**
  - F = likely foundational
  - A = worth asking about
  - S = specialist or advanced
  - U = probably unnecessary for a minimalist first version

These are provisional research classifications, not product decisions.

### Canvas & navigation

| Feature | What it does | Ref | Area | Class | Depends on |
|---|---|---|---|---|---|
| Pan (Space/middle drag) | Move the view | all | all | F | – |
| Zoom (keys, wheel, scrubby) | Magnify | PS | all | F | – |
| Fit / 100% | Instant reset of the view | PS | all | F | – |
| Rotate view (R, steps, reset) | Turn the canvas for natural strokes | PS, Kr | Sk Pa | F | GPU display path |
| Flip view in place (M) | Check proportions | Kr | Sk Pa | F | Rotate view |
| Bird's-eye zoom-out on a held key | Temporary overview, then jump | PS | Pa Ed | A | Zoom |
| Navigator/overview panel | Thumbnail navigation | Kr, CSP | Pa Ed | A | Panels |
| Whole-number zoom steps | Crisp pixels | Ase | Px | F (Px) | – |
| Multiple views / split view | Same image at two zooms | Kr, Ase | Px Pa | A | Window management |
| Wrap-around / tiled mode | Seamless textures | Kr, Ase | Px | A | – |

### Brushes & strokes

| Feature | What it does | Ref | Area | Class | Depends on |
|---|---|---|---|---|---|
| Brush tool (round, soft/hard) | Basic painting | PS | Sk Pa | F | Stroke engine |
| Pencil (hard/aliased) | Crisp lines and pixel work | PS, Ase | Sk Px | F | Stroke engine |
| Size, opacity, flow, hardness | Core parameters | PS | all | F | – |
| On-canvas size/hardness drag | Resize where you look | PS | Sk Pa | F | Cursor overlay |
| Pressure → size/opacity toggles | One-click dynamics | PS | Sk Pa | F | Tablet input |
| Per-brush pressure/tilt curves | Brush-specific response | CSP, Kr | Pa | A | Curves UI |
| Smoothing/stabilizer | Steadier lines | PS, CSP, Kr | Sk | F | Stroke engine |
| Speed-adaptive smoothing, stroke preview | Less lag from smoothing | CSP, Kr 5.3 | Sk | A | Smoothing |
| Brush presets with thumbnails | Pick by look | CSP, Kr | Pa | F | Preset storage |
| Preset tags/folders | Organise many brushes | Kr | Pa | A | Presets |
| Textured/stamp brush tips | Pencil grain, natural media | CSP, PS | Pa | A | Brush engine |
| Full brush editor | Build brushes from scratch | Kr, PS | Pa | S | Brush engine |
| Import Photoshop .abr brushes | Reuse brush libraries | CSP, Kr | Pa | A | Brush engine |
| Airbrush / build-up | Paint accumulates while held | PS | Pa | A | – |
| Blend/smudge/mixer brush | Mix colours on canvas | CSP, PS | Pa | A | Engine |
| Shift-click straight line | Straight segment | PS, Ase | Sk Px | F | – |
| Smart Shape (hold to straighten) | Clean shapes from strokes | CSP | Sk | A | Stroke analysis |
| Symmetry/mirror painting | Mirrored strokes | PS, Kr, Ase | Sk Px | A | – |
| Pixel-perfect stroke | Remove doubled pixels | Ase, Kr 5.3 | Px | F (Px) | Pencil |

### Erasing

| Feature | What it does | Ref | Area | Class | Depends on |
|---|---|---|---|---|---|
| Eraser tool | Remove pixels | PS | all | F | – |
| Erase with the current brush | Same texture and pressure in reverse | Kr, CSP, PS (~) | Sk Pa | F | Blend modes |
| Separate eraser size memory | Eraser keeps its own size | Kr | Sk Pa | A | Eraser toggle |
| Vector "erase up to intersection" | Clean overshoot | CSP | Sk | S | Vector layers |

### Colour

| Feature | What it does | Ref | Area | Class | Depends on |
|---|---|---|---|---|---|
| Foreground/background, X/D | Two loaded colours | PS, Ase | all | F | – |
| Colour wheel or square | Choose hue/saturation/value | CSP, PS | Pa | F | – |
| Sliders (HSV/RGB/hex) | Precise values | PS | Ed | F | – |
| Eyedropper on a modifier | Pick while drawing | PS | Pa | F | – |
| HUD/radial colour picker | Choose colour at the pen | PS, Kr | Pa | A | Radial UI |
| Recent colour history | Re-use colours | CSP, Kr | Pa | F | – |
| Swatches/palettes | Saved colour sets | all | Pa Px | F | Palette files |
| Indexed colour mode | Palette-limited images | Ase | Px | A | Palette |
| Shading ink | Step along palette ramps | Ase | Px | A | Palette |
| Colour mixing area | Scratch blending | CSP, Kr | Pa | S | Mixer brush |
| Intermediate/approximate grids | Generated variations | CSP | Pa | S | – |
| Pick colour from outside the app | Screen sampling | CSP, Kr | all | A | OS API |

### Layers

| Feature | What it does | Ref | Area | Class | Depends on |
|---|---|---|---|---|---|
| Raster layers (add/delete/reorder/hide) | Separate parts of the image | all | all | F | Document model |
| Opacity per layer | Fade a layer | all | all | F | – |
| Blend modes (common set) | Multiply/screen/overlay, etc. | PS | Pa Ed | F | Compositor |
| Lock transparency (alpha lock) | Paint only inside existing pixels | PS, CSP | Pa | F | – |
| Clipping to layer below | Shade inside line art/flats | PS, CSP | Pa | F | Compositor |
| Groups/folders | Organise layers | PS | Ed | F | Document model |
| Layer masks | Non-destructive hiding | PS | Ed | A | Masks |
| Reference layer for fills | Fill flats from line art | CSP | Pa | A | Fill tool |
| Draft layer | Excluded from export/fill | CSP | Sk | A | Export |
| Colour labels | Visual organisation | PS, CSP | Ed | A | – |
| Merge down / flatten | Combine layers | PS | all | F | – |
| Adjustment layers | Non-destructive colour edits | PS | Ed | S | Adjustments |
| Layer styles/effects | Outline, shadow | PS | Ed | S | Effects |
| Vector layers | Re-editable strokes | CSP | Sk | S | Vector engine |

### Selection & transform

| Feature | What it does | Ref | Area | Class | Depends on |
|---|---|---|---|---|---|
| Rect/ellipse marquee (M) | Basic selections | PS | Ed Px | F | Selection model |
| Lasso/polygon lasso (L) | Freehand selections | PS | all | F | – |
| Magic wand / select by colour | Select regions | PS, Ase | Ed Px | F | – |
| Add/subtract/intersect modifiers | Combine selections | PS | Ed | F | – |
| Contextual selection bar | Next-step actions | CSP, Kr | Ed | A | Selection |
| Paint a selection (quick mask) | Brush-based selection | PS, Kr | Ed | A | Masks |
| Select & Mask / refine edge | Hair-level refinement | PS | Ed | U | – |
| AI subject/object selection | Automatic selection | PS | Ed | U | ML |
| Free transform (Ctrl+T) | Move/scale/rotate | PS | all | F | Resampling |
| Flip layer/selection | Mirror pixels | all | all | F | – |
| Distort/perspective | Skew corners | PS | Ed | A | Transform |
| Warp/mesh | Bend images | PS, Kr | Ed | S | Transform |
| Liquify | Push pixels like a brush | PS, CSP, Kr | Pa | S | – |
| Pixel-art rotation (RotSprite) | Rotate without blur | Ase | Px | A (Px) | Transform |

### Fill, shapes & text

| Feature | What it does | Ref | Area | Class | Depends on |
|---|---|---|---|---|---|
| Bucket fill (tolerance, contiguous) | Fill regions | all | Pa Px | F | – |
| Gap closing / fill from other layers | Flatting line art | CSP | Pa | A | Reference layers |
| Lasso fill | Fill drawn shapes | CSP | Pa | A | Lasso |
| Gradient (incl. dithered) | Smooth/dither fills | PS, Ase | Pa Px | A | – |
| Line/rect/ellipse tools | Geometric shapes | Ase | Px | A | – |
| Text | Lettering | PS, Ase | Ed | A | Fonts |

### Adjustments & filters

| Feature | What it does | Ref | Area | Class | Depends on |
|---|---|---|---|---|---|
| Hue/Saturation, Brightness/Contrast | Basic colour fixes | PS | Ed | A | – |
| Levels/Curves | Tonal control | PS | Ed | S | – |
| Blur/Sharpen | Basic filters | PS | Ed | A | – |
| Large filter gallery | Effects | PS, GIMP | Ed | U | – |

### Guides & drawing aids

| Feature | What it does | Ref | Area | Class | Depends on |
|---|---|---|---|---|---|
| Grid / pixel grid / snap | Precision | Ase | Px Ed | F (Px) | – |
| Rulers & guides | Alignment | PS | Ed | A | – |
| Perspective rulers/assistants | Perspective drawing | Kr, CSP | Sk | S | Snapping |
| Reference image window | Keep references visible | Kr, CSP | Sk Pa | A | Window management |

### Animation / game assets

| Feature | What it does | Ref | Area | Class | Depends on |
|---|---|---|---|---|---|
| Frames + timeline | Sprite animation | Ase | Px | A | Document model |
| Onion skin | See neighbouring frames | Ase | Px | A | Timeline |
| Tags / loop directions | Name and loop animations | Ase | Px | S | Timeline |
| Sprite-sheet export | Game-ready assets | Ase | Px | A | Export |
| Pixel aspect ratio | Non-square pixels | Ase | Px | U | – |
| Floating 1:1 preview | See actual size | Ase | Px | A | Second view |

### History, safety & files

| Feature | What it does | Ref | Area | Class | Depends on |
|---|---|---|---|---|---|
| Multi-step undo/redo | Recover mistakes | all | all | F | Undo model |
| History panel | Jump to a state | PS | Ed | A | Undo model |
| Autosave / crash recovery | Protect work | Ase | all | F | File format |
| Native layered format | Save everything | all | all | F | Document model |
| PNG/JPEG/WebP export | Share and use | all | all | F | Codecs |
| Ctrl+S saves the current format (with lossy warning) | Avoid GIMP's Save/Export confusion | PS | all | A | Files |
| PSD import/export | Exchange with Photoshop | PS, CSP, Kr | Ed | A | Format library |
| ORA / Aseprite import | Exchange with other apps | Kr, Ase | Px | A | Format library |
| New-document presets | Fast start | PS, CSP | all | F | – |
| Paste as new image/layer | Quick start from the clipboard | Ase, GIMP | all | A | – |

### Workspace, input & customisation

| Feature | What it does | Ref | Area | Class | Depends on |
|---|---|---|---|---|---|
| Saved workspaces + quick switcher | Layouts per task | PS, CSP, Kr | all | F | Docking |
| Canvas-only mode (Tab) | Distraction-free | Kr, PS | Sk | F | – |
| Collapsible panels / pop-outs | Reclaim space | PS, CSP | all | F | Docking |
| Radial palette at the pen | Pen-first access | Kr | Sk Pa | A | Pen buttons |
| Command search | Find any action | Kr, Ase, GIMP | all | F | Action registry |
| Shortcut editor + Photoshop preset | Keep habits | PS, Kr | all | F | Action registry |
| Toolbar customisation | Hide rare tools | PS | all | A | – |
| Panel/layout lock | Prevent pen accidents | Kr, PS | all | A | Docking |
| Density/handedness settings | Small screens, left-handers | Ase (mirror) | all | A | Layout system |
| Pressure calibration by drawing | Personal pressure curve | CSP | all | A | Tablet input |
| Global pressure curve + pen tester | Diagnose and tune | Kr | all | F | Tablet input |
| In-app pen button mapping | Side buttons and express keys | Ase 1.3.17, Kr | all | A | Input layer |
| Windows Ink / WinTab choice with guidance | Driver compatibility | Kr, CSP | all | F | Input layer (Mission 001) |
| Cursor options (outline/crosshair/none) | Pen-display comfort | Kr, PS | all | F | – |

## 9. Proposed user-interview sessions

Use short sessions of 10–15 minutes each. Where possible, the user **demonstrates** in their usual app rather than describing. Each session ends with: "What annoys you most here?" and "What would you miss if it disappeared?"

**Session 0: Your setup and drawing habits**
1. What do you draw most: sketches, finished illustrations, game sprites, UI or textures? Roughly what percentage of each?
2. Is the Kamvas 13 your only screen, or does it sit next to a monitor? Where is the keyboard while you draw?
3. Which Windows scaling does the Kamvas use (100/125/150%)? Do you find the text and buttons comfortable?
4. Which app do you open first for a quick sketch, and which for a finished piece? Why?
5. What made you stop using Krita, GIMP or another app for drawing, if anything?
6. Do you draw right- or left-handed?

**Session 1: Sketching and brushes**
1. Show me how you change brush size in Photoshop: `[ ]`, Alt+right-drag, or the options bar?
2. How many brushes do you actually use in a typical session? Name your favourites.
3. When you say "pencil", do you mean a textured sketch pencil or a hard pixel pencil?
4. Do you use Opacity and Flow differently, or only Opacity?
5. Do you ever make or edit brushes, or only pick and resize them?
6. Clip Studio's brush list shows a stroke preview; Photoshop's shows tip shapes. Which helps you choose?

**Session 2: How drawing feels (hands-on)**
1. Draw the same test in Photoshop and Clip Studio Paint: long curves, quick hatching, a circle, slow wobbly lines. Which felt better, and why?
2. Do you use Photoshop Smoothing or Clip Studio stabilization? At what level? For which tools?
3. Have you noticed the line lagging behind the pen? Does it bother you?
4. Have you adjusted pen pressure, in the Huion driver, Clip Studio's pressure settings, or nowhere? Is it right now?
5. Do you prefer a brush-outline cursor, a crosshair, or no cursor on the Kamvas?
6. Do you use tilt for anything?

**Session 3: Erasing, undo and corrections**
1. How do you erase: E for the eraser tool, a key held, a pen button, or the driver's "Toggle Pen/Eraser"?
2. Should the eraser behave like your current brush, as in Krita and Clip Studio, or be a separate tool?
3. How often do you undo while sketching? With Ctrl+Z, an express key, or a pen button?
4. Do you ever use the History panel, or only undo?
5. In Clip Studio, have you used the vector eraser or line correction tools?

**Session 4: Colours**
1. Show me how you pick colours while painting: Alt-eyedropper, a colour panel, the HUD picker, or swatches?
2. Do you prefer a colour wheel, a square, or sliders?
3. Do you keep palettes for projects or games?
4. Clip Studio has colour history and in-between colour grids. Have you used them?
5. Should the eyedropper sample all visible layers or only the current one?

**Session 5: Layers**
1. How many layers does a typical piece have? Do you use groups?
2. Which do you use: clipping masks, lock transparency, layer masks, blend modes (which ones)?
3. Have you used Clip Studio's reference layer for fills, or draft layers?
4. While sketching, do you want layers always visible, or only when you ask for them?
5. Which layer actions do you do by shortcut?

**Session 6: Selections and transformations**
1. Which selection tools do you actually use: marquee, lasso, magic wand, Select Subject?
2. After you select something, what do you usually do next: move, transform, fill, copy, delete?
3. Do you like or dislike Photoshop's Contextual Task Bar or Clip Studio's Selection Launcher?
4. Do you use warp, perspective or liquify?
5. How do you transform pixel art without blurring it?

**Session 7: Canvas navigation**
1. Do you rotate the canvas while drawing? With R, Shift+Space, or something else?
2. Do you flip the view to check proportions? How often?
3. How do you zoom: keys, wheel, scrubby zoom, or bird's-eye (H)?
4. Do you work with two views of the same image?

**Session 8: Keyboard, pen buttons and express keys**
1. What are your two pen side buttons set to today?
2. What are the 8 Kamvas express keys set to? Do you use them?
3. Which 10 Photoshop shortcuts are pure muscle memory?
4. Are there Clip Studio or Krita shortcuts you prefer to Photoshop's?
5. Would you use a radial menu opened by a pen button? What 6–8 things would go on it?

**Session 9: Workspaces and screen layout** (show the five wireframes)
1. Which wireframe feels closest to how you want to work? Which feels wrong?
2. Which panels must always be visible on the Kamvas, and which can hide?
3. Would you use different workspaces for sketching, painting and editing, or one adaptable layout?
4. How would you like to switch workspaces: express key, menu, Tab?
5. If you use two screens, should panels live on the main monitor?

**Session 10: Pixel art and game assets**
1. What sizes and palettes do you use (e.g. 16×16, 32×32; restricted palettes)?
2. Do you animate? How many frames? Do you need onion skin, tags or sprite sheets?
3. Which Aseprite features are essential: pixel-perfect, indexed mode, shading ink, symmetry, tiled mode, the preview window?
4. Does the lack of canvas rotation in Aseprite bother you?
5. What do you export for your games, and how (PNG, sprite sheets, scaled exports)?

**Session 11: Files and exporting**
1. Do you need to open or save PSD files? With whom?
2. Should Ctrl+S save a PNG in place, as Photoshop does, or always save a project file?
3. What export settings do you repeat (size, format, transparency, scaling)?
4. How important is autosave or recovery to you?

## 10. Open questions and design tradeoffs

1. **Radial palette or visible panels.** Radial is fast and pen-first but hidden. Panels are discoverable but cost space and reach. A hybrid (radial plus collapsible panels) is likely, but how much goes on the radial needs Session 8.
2. **Pure Photoshop conventions or best-of-breed.** For example:
   - Krita's "E erases with the current brush" versus Photoshop's E eraser tool;
   - CSP's right-click eyedropper versus Photoshop's right-click brush picker.

   Each break from Photoshop costs muscle memory; each adoption may improve feel.
3. **Smoothing defaults.** Off (lowest latency) or on (steadier)? Per tool (CSP) or per brush preset? Speed-adaptive by default?
4. **Pressure setup.** A drawn-strokes calibration on first launch (CSP) or a global curve and tester in settings (Krita)? How does it coexist with the Huion driver's own curve?
5. **150% versus 125% versus 100% on the Kamvas.** This affects target size and canvas area. Needs testing on the device.
6. **One display or two.** If panels can move to a main monitor, the Kamvas layout can be much slimmer, but everything needed mid-stroke must still be reachable on the Kamvas.
7. **Hover behaviour.** Hover outline: yes. Hover-reveal panels: probably not on the pen display. Tooltips: delayed or off over the canvas.
8. **Contextual bars.** Helpful next steps versus canvas obstruction. Default on or off? Pinned position?
9. **Workspace semantics.** Does switching workspaces change only layout, or also tool presets and shortcuts? CSP swaps shortcuts, which surprises users.
10. **Pixel Art layout.** Tools on the left (Photoshop) or the right (Aseprite)? Is animation in scope?
11. **Save versus export.** Photoshop's in-place saving is familiar. GIMP's split protects work but confuses people.
12. **Discoverability in minimal workspaces.** Relying on command search and a radial means hidden features must be learnable. How much onboarding is welcome?
13. **Handedness and occlusion.** Mirror the layout for left-handed use? Confirm the user's handedness first.
14. **Feel can't be settled by research.** Brush response, smoothing feel and pressure need side-by-side tests with the user on the actual Kamvas 13 (Session 2). Mission 001's hardware tests still apply.

---

## Key sources

Full lists, about 230 URLs, are in the notes files in Appendix B.

**Photoshop (helpx.adobe.com/photoshop/desktop/…)**
- [1] …/set-up-toolbars-panels/spring-loaded-shortcuts.html
- [2] …/repair-retouch/clean-restore-images/create-smoother-more-polished-brush-strokes-with-stroke-smoothing.html
- [3] …/get-started/settings-and-preferences/change-tool-pointers.html
- [4] …/adjust-color/choose-colors/choose-a-color-while-painting.html
- [5] …/get-started/learn-the-basics/hide-show-panels.html
- [6] …/get-started/learn-the-basics/boost-workflows-with-the-contextual-task-bar.html, and community thread https://community.adobe.com/questions-700/contextual-task-bars-now-in-photoshop-beta-668168
- [7] …/get-started/technical-requirements-installation/adobe-photoshop-on-desktop-technical-requirements.html
- [8] https://helpx.adobe.com/photoshop/kb/tablet-support-faq-photoshop.html
- [9] …/get-started/learn-the-basics/navigate-and-pan-images.html
- [11] …/get-started/set-up-toolbars-panels/customize-the-toolbar.html
- [12] …/get-started/learn-the-basics/save-custom-workspaces.html
- [13] …/apply-painting-techniques/brushes-presets/paint-with-brush-or-pencil-tool.html
- [14] https://community.adobe.com/bug-reports-711/p-view-flip-horizontal-is-broken-in-newer-versions-of-photoshop-658818
- [15] …/get-started/learn-the-basics/access-discover-panel.html

**Clip Studio Paint (help.clip-studio.com/en-us/manual_en/…)**
- [16] 810_subtools/C.htm (Correction / stabilization)
- [17] 240_brushes/Adjusting_pen_pressure.htm
- [18] 330_selection/Selection_Launcher.htm
- [19] 690_interface/Palettes.htm
- [20] 240_brushes/Eraser_tools.htm
- [21] 780_shortcuts/Optional_Shortcuts.htm and Shortcuts_usable_during_operation.htm
- [22] 720_preferences/Preferences.htm
- [23] https://support.clip-studio.com/en-us/faq/articles/20230045 (Simple Mode: tablet and smartphone versions only)
- [24] 420_fill/Fill_Tool.htm
- [25] 450_figure/Smart_Shape.htm
- [26] 690_interface/Register_and_manage_your_workspace.htm
- [27] https://www.clipstudio.net/en/dl/release_note_old/v5/
- [28] 840_options/Companion_Mode.htm

**Krita (docs.krita.org/en/…)**
- [29] reference_manual/popup-palette.html
- [30] reference_manual/preferences/canvas_only_mode.html
- [31] reference_manual/tools/freehand_brush.html
- [32] reference_manual/preferences/tablet_settings.html
- [33] reference_manual/preferences/general_settings.html
- [34] user_manual/introduction_from_other_software/introduction_from_photoshop.html
- [35] reference_manual/preferences/shortcut_settings.html
- [36] https://krita.org/en/release-notes/krita-5-3-release-notes/
- [37] user_manual/getting_started/navigation.html

**Aseprite and GIMP**
- [38] https://www.aseprite.org/docs/context-bar/
- [39] https://www.aseprite.org/docs/workspace-layout/
- [40] https://www.aseprite.org/docs/keyboard-shortcuts/
- [41] https://www.aseprite.org/docs/color-mode/
- [42] https://www.aseprite.org/docs/preferences/
- [43] https://www.aseprite.org/docs/dynamics/
- [44] https://community.aseprite.org/t/canvas-rotation-inquiry-about-development/27074
- [45] https://www.aseprite.org/docs/shading/
- [46] https://docs.gimp.org/3.2/en/gimp-prefs-canvas-modifiers.html
- [47] https://docs.gimp.org/3.2/en/gimp-save-dialog.html

**Hardware, Windows and research**
- [48] https://www.huion.com/products/kamvas-13
- [49] https://www.huion.com/manual/kamvas-13/169
- [50] https://support.huion.com/en/support/solutions/articles/44002318166
- [51] https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dpi-related-apis-and-registry-settings
- [52] https://learn.microsoft.com/en-us/windows/apps/develop/input/guidelines-for-targeting
- [53] Vogel et al., CHI 2009, hand occlusion: https://www.dgp.toronto.edu/~ravin/papers/chi2009_occlusion.pdf
- [54] Vogel & Balakrishnan 2010, direct pen interaction: https://www.dgp.toronto.edu/public_user/ravin/papers/hcijournal2010_peninteraction.pdf
- [55] Kurtenbach & Buxton, marking menus: https://www.billbuxton.com/MMExpert.html
- [56] Fitzmaurice et al., tracking menus: https://research.autodesk.com/app/uploads/2023/03/tracking-menus.pdf_recneisFEkhnKU1oG.pdf

## Appendix A: Limitations

- I couldn't run any application. All claims come from documentation, release notes, community reports and research papers.
- Adobe's help pages blocked some direct fetches; those claims rest on search-indexed copies and are marked in the notes.
- Several vendor pages contradict themselves:
  - Photoshop's default history states (50 vs 20).
  - Clip Studio's `[ ]` direction and Deselect/Reselect shortcuts.
  - Aseprite's brush-size drag shortcut.
- Kamvas 13 details still to confirm on the device:
  - Windows' recommended scale.
  - The PW517's default side-button functions.
  - Report rate: the spec page says 266 PPS, the manual says more than 233.

## Appendix B: Research notes

These files are in the scratchpad, which can be cleared, so copy anything worth keeping:

- `/tmp/claude-0/-home-user-DoodleNook/df687d61-edb1-54dd-a35e-0e0e6f941aa1/scratchpad/research2/notes/photoshop.md`
- `/tmp/claude-0/-home-user-DoodleNook/df687d61-edb1-54dd-a35e-0e0e6f941aa1/scratchpad/research2/notes/csp.md`
- `/tmp/claude-0/-home-user-DoodleNook/df687d61-edb1-54dd-a35e-0e0e6f941aa1/scratchpad/research2/notes/krita.md`
- `/tmp/claude-0/-home-user-DoodleNook/df687d61-edb1-54dd-a35e-0e0e6f941aa1/scratchpad/research2/notes/aseprite-gimp.md`
- `/tmp/claude-0/-home-user-DoodleNook/df687d61-edb1-54dd-a35e-0e0e6f941aa1/scratchpad/research2/notes/smallscreen.md`
- Wireframes and their generator: `/tmp/claude-0/-home-user-DoodleNook/df687d61-edb1-54dd-a35e-0e0e6f941aa1/scratchpad/research2/wireframes/`

That completes Mission 002. I haven't started another phase.
