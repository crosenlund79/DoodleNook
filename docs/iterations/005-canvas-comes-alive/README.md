# Mission 005 — The Canvas Comes Alive

Five workspaces around one picture: **Pure Sketch, Quick Sketch, Painting, Editing and Pixel Art**.
They share one document, one canvas, one undo history and one set of colours. Switching workspace swaps only
the bars and panels around the canvas; the picture is never reloaded.

**Status:** implementation complete and verified by automated tests on Linux and Windows CI.
**Awaiting your review and hardware playtest** on the Huion Kamvas 13 ([playtest guide](PLAYTEST.md)).
Mission 004's drawing feel has also not yet been confirmed on the Kamvas; that test still stands.

## Download and run

1. Open the [Windows build runs for this branch](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F005-canvas-comes-alive) and pick the newest green run. You must be signed in to GitHub to download artifacts.
2. Under **Artifacts**, download **DoodleNook-win-x64** (delivered as `DoodleNook-win-x64.zip`).
3. Extract it and double-click **`DoodleNook.exe`**. Nothing to install.
4. If Windows says "Windows protected your PC", click *More info → Run anyway*. The test build is unsigned.

DoodleNook remembers your last workspace. The first start opens Quick Sketch, as in Mission 004.

## The five workspaces

Switch with the workspace buttons at the top right, **Alt+1 … Alt+5**, the *Window* menu, or Pure Sketch's corner menu.

| | Workspace | Around the canvas | For |
|---|---|---|---|
| A | **Pure Sketch** | Nothing but a small corner button. Right-click or the pen's barrel button opens the quick palette at the pen. **Tab** returns to the workspace you came from. | Drawing with no interface at all |
| B | **Quick Sketch** | Mission 004's single bar: brush chip, size, opacity, colour, erase, undo/redo, plus a **layers chip** and the workspace switcher. Seven tools on the left. | Spontaneous doodling |
| C | **Painting** | Menu row, options bar for the current tool, nine tools, and a dock with **Colour**, **Brushes** and **Layers** | Finished artwork |
| D | **Editing** | Menu row, options bar, thirteen tools, a dock with **Layers**, **Properties** and **History**, and a status bar | Selections, transforms, crop, resize, adjustments |
| E | **Pixel Art** | Menu row (with *Sprite* instead of *Image*), pixel options bar, ten pixel tools, a **palette column** and a floating **1:1 preview** | Exact pixels for game art |

Each workspace remembers its own tool, dock width, collapsed or closed panels, and (in Pixel Art) grid, tiled view and preview.

## What was built

**Layers** (shared by every workspace)
- Add, delete, duplicate, rename (double-click), reorder (drag a row, or Ctrl+[ / Ctrl+]), merge down.
- Visibility, opacity, eight blend modes (Normal, Multiply, Screen, Overlay, Soft light, Darken, Lighten, Add).
- Lock, lock transparency, clip to the layer below, colour labels (right-click a row).
- Thumbnails, the active layer highlighted, and a **paper** row: show/hide the paper to see transparency, or set it to the background colour.
- Every layer change is an undo step. Layer groups are not included (see *Not included*).

**Brushes and colour**
- Nine presets, each a genuinely different brush:
  - *Sketch pencil* with paper grain;
  - *Ink pen* with a strong pressure taper;
  - *Fine liner* with an even line;
  - *Hard round* for colour blocking;
  - *Soft round* for shading and glows;
  - *Marker* with a flat chisel tip;
  - *Airbrush* that builds up while held, even standing still;
  - *Chalk* with heavy grain;
  - *Flat wash* for even translucent colour.
- Brush settings: hardness, smoothing, grain, flow, pressure → size and opacity, Soft / Normal / Firm pressure feel, mirror symmetry (↔, ↕, both).
- The eraser keeps its own settings.
- Colour: saturation/value square and hue strip, hex, RGB and HSV values, saved swatches (+ to add, right-click to remove), recent colours, and the eyedropper (**I**, or hold **Alt**). **X** swaps, **D** resets.

**Painting helpers**
- Paint bucket with tolerance, contiguous or global, and sampling this layer or all layers.
- Linear and radial gradients, foreground → background or → transparent.
- Line, rectangle and ellipse shapes: outline or filled, smooth or hard-edged.
- Mirror symmetry for brushes and the pixel pencil.

**Editing**
- Selections:
  - rectangle (**M**), ellipse (**Shift+M**), lasso (**L**), magic wand (**W**, with tolerance);
  - new / add / subtract / intersect, or hold Shift (add) and Alt (subtract);
  - select all, deselect, invert.
- Painting, fills, adjustments, copy and clear all respect the selection.
- A *next-step* bar appears when something is selected: Transform, Fill, Clear, Crop, Invert, Deselect.
- **Move** (**V**): drag the selection or the whole layer. Arrow keys nudge by 1 px, Shift by 10 px.
- **Free transform** (**Ctrl+T**):
  - handles to scale (Shift keeps proportions); drag outside a corner to rotate;
  - flip and 90° rotation;
  - the resampling choice: *Pixels* (nearest), *Smooth* (bilinear), *Sharp* (bicubic);
  - **Enter** applies, **Esc** cancels.
- Copy, cut, paste as a new layer. Place an image file as a new layer, ready to transform.
- Whole picture:
  - crop (Crop tool, or Image › Crop to selection);
  - image size (resample, keep proportions);
  - canvas size (with anchor);
  - rotate 90° / 180°, flip horizontally or vertically.
- Adjustments with live preview and a preview toggle: Brightness/Contrast, Hue/Saturation (**Ctrl+U**), Blur, Sharpen. Also Grayscale (**Ctrl+Shift+U**) and Invert (**Ctrl+I**).
- History panel: click any step to go back or forward to it.

**Pixel Art**
- Pixel pencil and pixel eraser (1, 2 or 3 px) with **pixel-perfect** lines (removes doubled corners) and mirror symmetry.
- Pixel line, rectangle and ellipse (outline or filled).
- Every pixel is exact, with no anti-aliasing. Paint bucket fills exact colours.
- Display:
  - whole-number zoom steps;
  - nearest-neighbour display;
  - pixel grid (**Ctrl+'**) from 500 %;
  - tiled view for seamless patterns;
  - a 1:1 preview (click it for 2× and 3×).
- Move and transform stay on whole pixels (nearest-neighbour; rotation snaps to 90°).
- Palette column: big foreground/background squares and the *Nook 32* palette. Left-click a swatch for foreground, right-click for background. Add/remove colours, or make a palette from the picture.
- Right-click on the canvas opens the quick palette, or can be set to **paint with the background colour** (Aseprite style).
- New picture offers sprite sizes (16 × 16 to 256 × 144), transparent by default. Export PNG keeps the exact pixels and transparency.

**Pure Sketch and the quick palette**
- The quick palette opens at the pen with right-click or the barrel button.
- It holds:
  - recent colours around the edge;
  - undo and redo;
  - brush, eraser and eyedropper;
  - layers (a pop-over);
  - swap colours;
  - hide the interface or full screen;
  - **the brush size in the centre: drag it**.
- It works in every workspace, not only Pure Sketch.

**Navigation (all workspaces)**
- Zoom: wheel at the cursor, Ctrl+/Ctrl−, Fit (Ctrl+0), 100 % (Ctrl+1).
- Pan: Space-drag, middle button, or the hand tool (H).
- **View rotation**:
  - hold **R** and drag (Shift snaps to 15°);
  - the ⟲ ⟳ buttons rotate 15° at a time;
  - click the angle to straighten.
- **View flip**. Rotation and flip never change the picture itself.

**Files**
- **.dnk** is DoodleNook's own layered format:
  - a ZIP with a versioned JSON manifest and one PNG per layer;
  - it keeps layers, their properties, the paper, the palette and the active layer;
  - saving writes a temporary file first, so a failed save never damages the old file;
  - newer versions or damaged files are refused with a clear message.
- **Open**: .dnk, PNG, JPEG, WebP, BMP, GIF. Photos are turned the right way up using their orientation tag.
- **Export** (File › Export) writes a flattened copy:
  - PNG, lossless with transparency;
  - JPEG, with a quality setting; transparent areas become white;
  - WebP, with a quality setting and transparency.
  - The export dialog says when layers are merged. The open picture keeps its layers.
- **A layered picture is never flattened by saving.**
  - Ctrl+S on a .dnk saves the .dnk.
  - A single-layer picture opened from a PNG can be saved back to that PNG, as in Mission 004.
  - Anything with more than one layer goes to *Save as* .dnk.

**Undo** covers strokes, fills, gradients, shapes, selections, transforms, layer changes, crop/resize/rotate and adjustments: 200 steps or 1 GB, whichever comes first.

**Shortcuts**: Photoshop-style throughout. Press **F1** for the full list. Highlights:
- B, E, I, H, G, Shift+G, U, M, L, W, V, C select tools;
- Ctrl+T, Enter / Esc for transforms;
- Ctrl+Z, Ctrl+Shift+Z / Ctrl+Y for undo and redo;
- Ctrl+A / D / Shift+I for selections;
- Ctrl+J / E / Shift+N for layers;
- Space-drag pans; [ ] change the size; Tab hides the interface.

## How it looks

Each image shows the approved Mission 003 mock-up (top) and the native app (bottom), on a 27″ 2560 × 1440 monitor at 100 %.
The native app draws its own test picture (a layered landscape); the mock-ups used a prepared illustration.

| | |
|---|---|
| ![A](screenshots/compare-desktop27-A-pure-sketch.png) **A Pure Sketch** | ![B](screenshots/compare-desktop27-B-quick-sketch.png) **B Quick Sketch** |
| ![C](screenshots/compare-desktop27-C-painting.png) **C Painting** | ![D](screenshots/compare-desktop27-D-editing.png) **D Editing** (a selection being freely transformed) |
| ![E](screenshots/compare-desktop27-E-pixel-art.png) **E Pixel Art** (a 24 × 24 sprite drawn with the pixel tools) | ![Quick palette](screenshots/compare-quick-palette.png) **Quick palette** (Mission 003 experiment vs native) |

The same comparisons on the **Kamvas 13 at 150 %** (1280 × 720 logical pixels):
[A](screenshots/compare-kamvas13-150-A-pure-sketch.png) ·
[B](screenshots/compare-kamvas13-150-B-quick-sketch.png) ·
[C](screenshots/compare-kamvas13-150-C-painting.png) ·
[D](screenshots/compare-kamvas13-150-D-editing.png) ·
[E](screenshots/compare-kamvas13-150-E-pixel-art.png)

More native frames at Kamvas 13 / 150 %:

| | |
|---|---|
| ![Quick palette](screenshots/native-kamvas13-150-A-quick-palette.png) Quick palette in Pure Sketch | ![Layers pop-over](screenshots/native-kamvas13-150-B-layers-popover.png) Quick Sketch layers pop-over |
| ![Painting](screenshots/native-kamvas13-150-C-painting.png) Painting | ![Editing](screenshots/native-kamvas13-150-D-editing.png) Editing |
| ![Pixel Art](screenshots/native-kamvas13-150-E-pixel-art.png) Pixel Art | ![Brushes](screenshots/native-popover-brushes.png) The nine brushes (previews drawn by the real engine) |
| ![File menu](screenshots/native-menu-file.png) File menu | ![Image size](screenshots/native-dialog-image-size.png) Image size |
| ![Hue/Saturation](screenshots/native-dialog-hue-saturation.png) Hue/Saturation with live preview | ![Export JPEG](screenshots/native-dialog-export-jpeg.png) Export as JPEG explains the flattening |
| ![Diagnostics](screenshots/native-pen-diagnostics.png) Pen diagnostics (F12) still available | |

**How the screenshots were made**
- All are real frames of the native app, rendered by its own code (Avalonia + Skia) in headless tests.
- The scene was drawn through the same input path a pen uses, with pressure.
- Text is rasterised by the Linux test machine, so it can look slightly different on Windows.
- The CI run also captures one frame per workspace from the packaged `DoodleNook.exe` on Windows (artifact *test-results*, folder `smoke-test`).

## Departures from the Mission 003 mock-ups

- **No animation timeline in Pixel Art.** The mock-up showed a "Frames" strip marked *optional · animation not decided*. Animation is not built, so the strip and the *Frame* menu are left out rather than shown as fake UI.
- **No text tool or zoom tool** in Editing (the mock-up had icons for both). Zoom is on the wheel, the view chip and the status bar; text is a later feature.
- **The options bar shows only working options.**
  - The mock-up's *Feather* and *Ink: Simple* controls are left out.
  - A soft selection edge comes from *Anti-alias* (ellipse and lasso).
  - Pixel ink modes are a later feature.
- **Windows' own title bar is kept** (as in Mission 004). The menu row sits below it with the brand, menus, file name and workspace switcher. F11 gives the chrome-free look.
- **Docked colour and brush panels are compact.**
  - Colour shows RGB/HSV numbers behind a toggle.
  - Brushes show their settings behind *Brush settings*.
  - This keeps Layers visible on smaller screens. The pop-over versions show everything.
- **Quick Sketch gained a layers chip** (the mock-up's layers pop-over experiment, B).

## Not included in this mission

- **Layer groups**: not practical this milestone; the .dnk format has room to add them later.
- Animation, text, PSD, CMYK, plugins, cloud, AI and analytics. No telemetry of any kind.
- Brush tilt and rotation (tilt is shown in diagnostics only). There is no custom brush editor beyond the settings above.
- Feathering a selection by a radius; quick mask; layer masks; adjustment layers.
- Copy/paste with other applications (the clipboard is DoodleNook's own). *Place image* imports files instead.

## Measured performance

Measured by `PerformanceTests` (engine only, Release build, a 4-core 2.3 GHz Xeon container on Linux).
The figures cover brush work plus recompositing every visible layer under the brush; they exclude drawing to the screen.
The test documents have broad paint on every layer, with Multiply, 70 % opacity and clipping mixed in.
The pen stroke is a fast 2000 px curve sent as 200 events of 2 samples, as a 200 Hz pen delivers it; a 200 Hz pen sends an event every 5 ms.
The Windows CI run repeats the measurement (artifact *test-results*, `tests/DoodleNook.Engine.Tests/out/performance.json`).

| Picture | Layers | 6 px pencil | 24 px ink | 120 px hard | 300 px soft |
|---|---|---|---|---|---|
| 1920 × 1080 | 1 | 0.08 ms | 0.10 ms | 0.19 ms | 0.51 ms |
| 1920 × 1080 | 6 | 0.04 ms | 0.06 ms | 0.42 ms | 1.4 ms |
| 2560 × 1440 | 12 | 0.06 ms | 0.11 ms | 0.91 ms | 3.3 ms |

Average milliseconds per pen event. Worst single events stayed under 1.7 ms, except with the 300 px soft brush in layered pictures: those spiked to between 8 and 41 ms, varying from run to run.

| Picture | Layers | Recomposite everything | Undo/redo a large stroke | Save .dnk | Open .dnk | File size |
|---|---|---|---|---|---|---|
| 1920 × 1080 | 1 | 9 ms | 8 ms | 0.2 s | 0.07 s | 0.7 MB |
| 1920 × 1080 | 6 | 67 ms | 65 ms | 0.5 s | 0.17 s | 3.0 MB |
| 2560 × 1440 | 12 | 235 ms | 170 ms | 1.4 s | 0.7 s | 8.6 MB |

Saving and opening compress and decode the layers on all cores. Before that change, the 12-layer save took 3.5 s.

## Known limitations (expect these in the playtest)

- **Performance concerns:**
  - Dragging a layer's opacity, or toggling visibility or blend mode, recomposites the whole layer each step: about 67 ms with 6 layers at 1080p and 235 ms with 12 layers at 1440p. The slider gets sluggish in big, many-layer pictures.
  - Very large soft brushes in layered pictures have occasional slow events (above).
  - Saving runs on the interface thread, so a big picture freezes the window for up to about 1.5 s while saving.
- **Data safety:**
  - There is no autosave or crash recovery yet. Unsaved work is lost if the app or PC crashes.
  - Closing, New and Open ask before discarding changes.
  - Saves are written to a temporary file first, so a failed save never damages the previous file.
  - Exports never change the open picture.

- **Narrow windows.** Painting's and Editing's options bars need about 1150 px. In a narrower window the end of the bar is cut off; the mouse wheel over the bar scrolls it sideways.
- **Smaller screens.** On the Kamvas 13 at 150 %, Painting's dock shows Colour and Brushes first, so Layers starts lower down. Scroll the dock, or close a section by clicking its title. DoodleNook remembers that.
- **Memory.** Each layer is a full-size picture in memory (about 8 MB per layer at 1920 × 1080). Undo is kept in memory: 200 steps or 1 GB.
- **Thumbnails** update about a third of a second after you stop painting. During a stroke the panels deliberately do no work, to keep the pen path light.
- **The clipboard is DoodleNook's own**, so copy/paste does not exchange pictures with other programs. Use *Place image* to bring files in.
- **Mission 004 limitations still apply:**
  - pressure has 1024 steps (Windows Ink);
  - there is no WinTab;
  - tilt is not used by brushes;
  - the brush cursor may trail the system pointer by a frame;
  - the build is unsigned.

## Architecture

```
src/DoodleNook.Engine     Pixels, layers, compositor, history, selections, brushes, pixel tools, operations
                          (fill, gradient, shapes, transforms, resize, adjustments), view transform, .dnk + PNG codec.
                          References only .NET.
src/DoodleNook.App        Avalonia app. One CanvasView and one EditorSession for the whole window.
  Shell/                  Workspaces, bars (title/menu, options, Quick Sketch, tool strip, status, view chip),
                          dock, layers/colour/brush/history/properties panels, palette column, quick palette,
                          1:1 preview.
  MainWindow.*.cs         Window set-up, workspace switching, commands, files, keyboard, menus.
  Input/                  CanvasInputController: turns pointer input into strokes, selections, transforms, …
  Files/                  Image import/export (SkiaSharp codecs) and atomic writes.
tests/                    Engine tests; app tests (input controller, files, real window headlessly, workflows,
                          screenshots).
```

- **One document, many workspaces.** Workspaces are pure layout. `MainWindow.SetWorkspace` swaps the hosts' contents and restores the workspace's remembered state. It never touches the document, history or view.
- **Compositing.** Each layer has content bounds, and the projection is recomposited only where something changed. The GPU texture receives only the changed rectangle (as in Mission 004).
- **Undo.** Stroke-like edits store 64 × 64 tile patches. Whole-picture operations store full states. Every change is an `IHistoryAction`.
- **Previews.** Transforms float their pixels above the layer until applied. Adjustments preview through a temporary layer override. Cancelling leaves the layer untouched and adds no undo step.

Rules enforced by tests: the engine references nothing but .NET; the input controller holds no document state; the editor session has no UI-framework types.

## Verification

| What | How | Result |
|---|---|---|
| Engine | 129 tests (including the performance measurement). Mission 004's stroke, pressure, coordinate and undo tests are kept. New: layers (properties, blend modes against the W3C formulas, clipping, lock/alpha lock, merge, reorder), history limits, selections and modes, fill tolerance, gradient, shapes, pixel-perfect lines, symmetry, airbrush build-up, grain, chisel, transforms and resampling, crop/resize/rotate, adjustments with preview, .dnk round trip, version and damage checks, atomic saves, and a multi-tool end-to-end scenario | Pass (Linux; Windows CI) |
| App | 71 tests. Input controller: every tool through synthetic pen/mouse input. Files and settings, including workspace states. **The real window driven headlessly**: each workspace's chrome, switching by buttons and Alt+1..5, per-workspace memory, 40 rapid switches keeping document, history and pixels, quick palette, layers panel (add, hide, duplicate, merge, select, rename, label), Ctrl+T transform and Esc, image size dialog, adjustment preview and cancel, menus, status bar, pixel art zoom, palette column, a narrow window scrolling the options bar. Workflows: sketch → paint on a new layer → save .dnk → reopen; open PNG → select → paste → transform → grayscale → export (Ctrl+S refuses to flatten into the PNG); single-layer PNG saves back; new sprite → pixel tools → move → exact PNG. Screenshots of all five workspaces | Pass (Linux; Windows CI) |
| Packaged app on Windows | CI extracts the ZIP and runs `DoodleNook.exe --smoke-test`. It draws pen and mouse strokes, erases, undoes and redoes; adds a layer and paints on it in Painting; selects, flips and inverts in Editing; saves and reopens a .dnk; exports PNG, JPEG and WebP; draws a sprite in Pixel Art and exports it exactly; visits Pure Sketch and returns with Tab; and saves a frame of each workspace | See the CI result in the report |
| Real app on Linux | The same smoke test under a virtual X display | Pass (24 checks) |
| **Feel on the Kamvas 13** | Only you can test this | **Not yet tested** |

Issues the tests found and that were fixed:
- the quick palette opened in the corner instead of at the pen;
- edge handles crowded thin selections, so dragging inside scaled instead of moving;
- a closed window could still steal keyboard focus when a pop-over opened elsewhere (test-only, but a real leak);
- the radial size label was re-parented;
- the Windows file lock reports `UnauthorizedAccessException`, not `IOException`.

## Building from source

Requires the .NET 10 SDK.

```bash
dotnet test DoodleNook.slnx
```

```bash
dotnet run --project src/DoodleNook.App
```

Avalonia's optional build-time statistics are switched off by `Directory.Build.targets`. Third-party licences are in [`THIRD-PARTY-NOTICES.txt`](../../../THIRD-PARTY-NOTICES.txt).
