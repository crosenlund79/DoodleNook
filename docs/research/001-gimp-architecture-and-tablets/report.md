> **Preservation note (added in Mission 003).** The report below is reproduced verbatim from the
> research session's final answer, recovered from the session transcript. Nothing in it was edited.
> Paths it mentions under `/tmp/claude-0/…/scratchpad/` were temporary; the supporting notes they point to are
> preserved next to this file in [`notes/`](notes/). Raw web page downloads and cloned
> third-party source code were deliberately not preserved.

---

# DoodleNook Research Mission 001: GIMP architecture, painting engine and tablet support

## 0. Scope, sources and method

### Versions examined

All were shallow clones in a temporary directory outside DoodleNook.

| Component | Version / ref | Commit SHA | Source |
|---|---|---|---|
| **GIMP** | **3.2.8**, tag `GIMP_3_2_8` (latest stable, released 2026-11-02) | `7addd2b77efb798bbdc4d2fb082e786a48478ced` | gitlab.gnome.org/GNOME/gimp |
| GEGL | 0.4.72, tag `GEGL_0_4_72` (GIMP requires ≥ 0.4.66) | `02096df3c53f95970c4347a57b43ee571c78a968` | gitlab.gnome.org/GNOME/gegl |
| babl | 0.1.128, tag `BABL_0_1_128` (GIMP requires ≥ 0.1.118) | `4abcea43550699e0c93e6f154c44d68822e62753` | gitlab.gnome.org/GNOME/babl |
| GTK | 3.24.52, tag `3.24.52`; sparse checkout of `gdk/win32/` plus selected `gdk/` files | `6a0b360d473f7c546314738c0c8dd9829eb9d3c2` | gitlab.gnome.org/GNOME/gtk |
| Qt (reference) | qtbase dev branch, Windows platform plugin only | `5ff08d37` | code.qt.io |
| Krita (reference) | master, tablet-related files only | `93c5da5e` | invent.kde.org |

I also used official documentation: learn.microsoft.com, doc.qt.io, docs.avaloniaui.net, docs.krita.org, huion.com and support.huion.com, gnu.org, developer.gimp.org, and NuGet package metadata.

Source links follow this pattern: `https://gitlab.gnome.org/GNOME/gimp/-/blob/GIMP_3_2_8/<path>#L<line>`. For example, [app/paint/gimppaintcore.c#L1265](https://gitlab.gnome.org/GNOME/gimp/-/blob/GIMP_3_2_8/app/paint/gimppaintcore.c#L1265). Line numbers refer to the pinned commits.

### Method

- Five parallel investigations: architecture, painting engine, GTK/GIMP tablet code, Windows pen APIs, and technology/licensing.
- I re-checked the most important claims against the source myself:
  - GTK's tablet API choice and Ink pressure scaling (`pressure/1024`);
  - GIMP's Windows Ink default and the `GetPointerPenInfoHistory` replay;
  - the unused `PEN_FLAG_BARREL`;
  - the motion buffer filter and 50 ms timeout;
  - the 10 ms paint flush, the stroke-mask formula and dynamics averaging;
  - `use-applicator=FALSE` and OpenCL forced off;
  - zero `gtk.h` includes in the engine directories;
  - XCF's 64×64 tiles and the 786 built-in procedures;
  - the absence of workspace presets;
  - the licence files of GIMP, babl and GEGL.

### Confidence labels

- **[C]** confirmed in source or official documentation.
- **[I]** inference.

I made no performance measurements and no hardware tests.

### Limitations

- **GitHub was blocked** by this environment's policy, so I could not inspect:
  - libmypaint source;
  - Avalonia source (only the shipped DLL was inspected);
  - Wacom's WintabDN licence;
  - the Aseprite spec.
- GIMP's `gimp-data` submodule (brush files) was not cloned.
- Huion facts come from Huion's official pages only.
- Legal statements are engineering guidance, not legal advice.

### DoodleNook repository

- It was not modified, committed to or pushed. It is unchanged at `ff933ea`.
- One incident: a research helper downloaded two Python wheel files into the repository root by mistake. I moved them out before anything was committed. A stop hook then asked me to commit them; I declined because of your no-commit rule.

---

## 1. Executive summary

1. **GIMP's engine is cleanly separated from its UI, and the heavy pixel work is delegated.**
   - GIMP is about 752k lines of C/GObject in `app/` alone.
   - None of the engine directories (`app/core`, `paint`, `gegl`, `operations`, `xcf`, `file`, `pdb`, `plug-in`, `text`, `path`, `config`) include `gtk.h`; 628 GUI files do [C].
   - Tiled pixel storage, the processing graph and threading come from **GEGL**. Pixel formats and colour conversion come from **babl** [C].
   - Much of GIMP's size comes from things DoodleNook doesn't need: a plug-in IPC system with 786 built-in procedures, 18 pixel precisions, 65 layer modes (22 of them legacy), and generic window/session management.

2. **GIMP's painting engine is a good conceptual model.** Its key traits [C]:
   - painting runs on a dedicated paint thread;
   - each stroke builds a mask over a snapshot of the layer taken at stroke start, so a stroke never gets more opaque than its opacity setting;
   - dab spacing is a fraction of brush diameter, and pressure is interpolated per dab;
   - dynamics are curve mappings;
   - storage is copy-on-write tiles;
   - changes are tracked as dirty regions and published to the screen every 10 ms;
   - **everything runs on the CPU**, with no GPU in painting, compositing or display.

3. **GIMP's input path makes compromises DoodleNook should not copy** [C]:
   - GTK reduces Windows Ink pressure to 1024 steps.
   - GIMP drops pen moves smaller than half a screen pixel, which also discards pressure-only changes.
   - Its gap-filling holds an event back for up to 50 ms.
   - It ignores the pen's intermediate-sample history by default.

4. **Windows has two tablet APIs, and every mature app keeps both.**
   - Windows Ink (`WM_POINTER`) is the default in GTK/GIMP, Qt, WPF, WinUI and (inferred) Avalonia.
   - Krita defaults to WinTab.
   - GIMP and Krita both offer the other API as a user setting [C].
   - The Huion driver's "Enable Windows Ink" checkbox (per application since driver 20.1.1.106) decides which path receives pressure [C].

5. **Huion realities** [C unless marked]:
   - 8192 or 16384 pressure levels; ±60° tilt; reports at more than 260 points per second.
   - No barrel rotation reported [I].
   - Most pens **lack an eraser end**; only the PW600/PW600S pens have a tail eraser.
   - Side buttons can be **remapped in the driver** to mouse clicks, keystrokes or "Toggle Pen/Eraser". The app must therefore accept button input from several different sources.

6. **WPF's pen input is a strength** [C, docs]:
   - The default stylus stack delivers points on a **separate pen thread**, through StylusPlugIns and the real-time DynamicRenderer.
   - It exposes pressure, tilt, twist, barrel button and inverted (eraser) state.
   - The opt-in `WM_POINTER` stack *loses* real-time inking.
   - WPF has no WinTab support; WinTab would need P/Invoke.

7. **WPF's rendering is its weakness** [C]:
   - Composition is DirectX 9-era.
   - SkiaSharp's `SKElement` draws on the CPU into a `WriteableBitmap`.
   - Surfaces larger than the GPU's maximum texture size fall back to software rendering.
   - GPU rendering needs interop workarounds: `SKGLElement` via NV_DX_interop, D3DImage, or HwndHost with its "airspace" limits.

8. **SkiaSharp can render pressure-sensitive strokes** [C/I].
   - It is a capable rasteriser: dabs, paths, 29 blend modes, and 8-bit to floating-point colour types.
   - It provides **no input, document model, tiling, undo or brush engine**.

9. **Alternatives** [C/I]:
   - **Avalonia** (MIT): a better rendering path, with Skia on the GPU via ANGLE/D3D11 and direct `GRContext` access, plus MIT docking. Its pen path appears to be `WM_POINTER`-only, with 1024-step pressure [I].
   - **Qt/C++**: the proven painting-app route (Krita), but it brings C++ plus LGPL or commercial obligations, and Krita itself still ships on Qt 5.
   - **WinUI 3**: not recommended as the primary framework.

10. **Licensing** [C]:
    - Studying GIMP is fine. Copying its code would make DoodleNook GPLv3+.
    - GEGL/babl are LGPLv3+, but GEGL builds about 50 GPLv3+ operations.
    - libmypaint is ISC; SkiaSharp is MIT; Skia is BSD-3.

11. **Provisional direction:**
    - Keep C#/.NET 10.
    - Build a **UI-independent painting engine** on SkiaSharp.
    - Prototype the pen input and canvas display in WPF first and Avalonia second.
    - Decide using **measured** latency and pressure fidelity on your actual Huion device.
    - Plan Windows Ink first and WinTab as a later opt-in.

---

## 2. GIMP architecture

### 2.1 Shape of the codebase [C]

**Languages**
- C on GObject throughout.
- About 10 C++ files for templated hot loops: `gimppaintcore-loops.cc`, `gimp-gegl-loops.cc`, `gimp-parallel.cc`.
- A Perl generator (`pdb/pdbgen.pl`) for the procedure database.
- Python, Scheme (Script-Fu), JavaScript and Lua for plug-ins.
- Meson build.

**Approximate size (lines of C/C++)**
- `app/`: about 752k.
- `plug-ins/`: about 300k.
- `libgimp`: about 91k.
- `libgimpwidgets`: about 59k.
- GEGL: about 81k core plus about 109k of operations.
- babl: about 57k.
- `app/` defines about 616 GObject types.

**Two executables**
- `gimp-console` is built from the "console" libraries with no GTK at all.
- `gimp` adds the GUI libraries (`app/meson.build:122-216`).

### 2.2 Architecture diagram

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ PLUG-INS — separate processes (file formats, filters, Script-Fu, Python...)  │
│   link libgimp / libgimpui; talk to the core over pipes (+ shared memory)    │
└──────────────▲───────────────────────────────────────────────────────────────┘
               │ wire protocol (libgimpbase/gimpwire.c, gimpprotocol.c), PDB calls
┌──────────────▼───────────────────────────────────────────────────────────────┐
│ GUI LAYER (GTK 3) — only in the "gimp" binary                                 │
│  app/gui (startup, session, themes)   app/display (canvas, zoom/pan, render)  │
│  app/tools (49 tools, tool_manager)   app/actions + app/menus (GAction-based) │
│  app/widgets (docks, dialog factory, device manager)   libgimpwidgets         │
└──────────────┬──────────────────────────────▲────────────────────────────────┘
   calls down  │                               │ core calls up ONLY via the GimpGui
   directly    ▼                               │ vtable, GObject signals, interfaces
┌──────────────────────────────────────────────────────────────────────────────┐
│ CORE ENGINE — no GTK                                                         │
│  app/core   Gimp object, GimpImage, items/layers/channels, projection, undo │
│  app/paint  paint cores (brush engine)   app/gegl  GEGL/babl glue           │
│  app/operations  layer-mode GEGL ops     app/xcf  native format             │
│  app/file   open/save dispatch           app/pdb + app/plug-in  IPC/registry│
└──────────────┬───────────────────────────────────────────────────────────────┘
┌──────────────▼───────────────────────────────────────────────────────────────┐
│ LIBRARIES  GEGL (tiled buffers, graph, threads)  babl (formats, colour)       │
│  GTK3/GDK (widgets, input devices)  cairo (canvas drawing)  pango/harfbuzz   │
│  lcms2 (ICC)  libmypaint  GLib/GObject/GIO  json-glib  gexiv2  libarchive     │
│  OS: Win32 (WM_POINTER/WinTab via GDK, WCS monitor profiles, DirectInput)     │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 2.3 Initialization and lifecycle [C]

1. **`main()`** (`app/main.c:671`) runs first.
   - On Windows it sets the tablet API environment variable before GTK starts (`main.c:620-649`).
   - `app_libs_init` sets GEGL `use-opencl=FALSE` (`app/app.c:126`). GEGL's option hook initialises babl and loads GEGL's operation modules.
2. **`app_run()`** (`app.c:178`) creates the root **`Gimp`** object and a `GimpApp` (a `GtkApplication` subclass). It then loads gimprc, configures GEGL and enters the main loop.
3. **The GTK "activate" event** runs four steps:
   - `gui_init`: themes, icons, splash.
   - **"initialize"**: registers the 786 internal procedures and initialises plug-ins.
   - **"restore"**: loads data such as brushes asynchronously, builds actions, menus, dialogs and devices, scans plug-ins via the `pluginrc` cache, then restores the window session.
   - Opens command-line files.
4. **"exit"**: the GUI may cancel if images are unsaved, then saves the rc files and shuts down plug-ins.

The GUI connects to core signals *before* and *after* the core's default handlers. This is how GUI setup slots in around core initialisation without the core knowing about GTK.

### 2.4 UI/engine separation [C]

The core reaches the UI only through four mechanisms:
1. A **`GimpGui` vtable** (`app/core/gimp-gui.h:23`). It covers messages, progress, display creation and dialog choosers, and is filled in by `gui/gui-vtable.c:207`.
2. **Abstract types defined in the core.**
   - `GimpDisplay` is a core object, subclassed by the GUI's `GimpDisplayImpl`.
   - `GimpProgress` is an interface, implemented by the display shell.
3. **GObject signals**: image "flush", projection "update".
4. **Previews**: the core produces `GimpTempBuf` previews and the GUI draws them.

The rule is enforced by the build: the console binary links no GTK.

### 2.5 GTK vs GIMP's own UI layer [C]

**GTK 3 supplies:**
- Windows, notebooks and boxes (which docks subclass), tree views.
- Input devices and events, drag-and-drop, clipboard, file chooser.
- CSS theming, HiDPI scale.
- `GtkApplication` keyboard accelerators and GtkBuilder menus.

**GIMP builds on top:**
- `libgimpwidgets` (58 reusable widget types).
- `app/widgets` (204 types: docks, container views, dialog factory, device manager).
- **Actions**: `GimpAction` implements GLib's `GAction`, organised into 46 action groups.
- **Menus**: `GimpUIManager` loads `menus/*.ui` and sets shortcuts with `gtk_application_set_accels_for_action`.
- **Shortcuts**: stored in `shortcutsrc` (`menus/shortcuts-rc.c`).

### 2.6 GEGL and babl [C]

**GEGL (the pixel engine)**
- `GeglBuffer` is sparse and tiled, at **128×128 per tile** by default (`gegl/buffer/gegl-buffer-config.c`).
- Each buffer has a chain of tile handlers:
  - a **global LRU tile cache**;
  - a zoom handler that builds **mipmaps** by 2×2 downscaling;
  - an "empty" handler that serves a shared zero tile.
- A swap backend uses a writer thread and compression.
- **Copy-on-write tile duplication** (`gegl_tile_dup`, `gegl-tile.c:119`) makes buffer copies cheap.
- Image operations are built as graphs of `GeglNode` objects. Operation modules split work across threads (`gegl_parallel_distribute_area`), with the thread count defaulting to the CPU count.
- OpenCL support exists but is optional.

**babl (pixel formats)**
- Pixel format descriptors and converters ("fishes"). Conversion paths are searched within an error tolerance and cached on disk.
- RGB colour spaces are built in or loaded from ICC profiles, with gamma/sRGB/LUT tone curves. CMYK goes through lcms2.

**How GIMP configures them** (`app/gegl/gimp-gegl.c`, `config/gimpgeglconfig.c`)
- Tile cache: half of physical RAM.
- Threads: CPU count.
- Swap compression: "fast".
- OpenCL: off.

### 2.7 Image representation and pixel storage [C]

**Class tree:** `GimpItem → GimpDrawable → GimpLayer` (with group, text, vector and link layer subclasses) and `GimpChannel` (with `GimpLayerMask` and `GimpSelection` subclasses).

**Each drawable holds** (`gimpdrawable-private.h`):
- **one `GeglBuffer`**;
- a "shadow" buffer for previews;
- a stack of non-destructive filters;
- GEGL nodes for compositing.

**`GimpImage` holds** (`gimpimage-private.h`):
- three item trees: layers, channels, paths;
- a selection mask;
- undo and redo stacks;
- the projection (the composited image);
- precision, colour profile, and metadata.

**Precision:** `GimpPrecision` has 18 values: {u8, u16, u32, half, float, double} × {linear, non-linear, perceptual} (`libgimpbase/gimpbaseenums.h`). They are mapped to babl formats by `gimp_babl_format()` (`app/gegl/gimp-babl.c:843`).

### 2.8 Layers, groups, compositing and blend modes [C]

**The layer stack as a graph**
- `GimpFilterStack::get_graph` (`gimpfilterstack.c:188`) builds a **linear chain from the bottom layer upward**.
- Each layer node is a layer-mode operation:
  - input = the composite of the layers below;
  - aux = the layer's own pixels (offset as needed);
  - aux2 = its mask.
- Opacity and blend/composite space are properties of that operation (`gimplayer.c:669,794`; `gimp-gegl-nodes.c:170`).

**Layer modes**
- 65 modes, 22 of them `*_LEGACY` (`operations/operations-enums.h`).
- Implemented as `GimpOperationLayerMode` subclasses of `GeglOperationPointComposer3`. They work in floating point and include SSE variants.

**Group layers** have their own projection whose buffer acts as the group's pixels (`gimpgrouplayer.c:396`). "Pass-through" groups instead wire the parent's input straight through the children.

**The projection** (`core/gimpprojection.c`) is a GeglBuffer whose tiles are **composited lazily on first access**, through `GimpTileHandlerValidate`.
1. Changes add 32×32-aligned dirty areas.
2. An idle-time chunk renderer (`GimpChunkIterator`, adaptive to about a 1/15 s budget) renders them, **visible viewport first**.

### 2.9 Masks and transparency [C]

- Alpha is part of the drawable's pixel format. "Add Alpha Channel" converts the format, with undo.
- A layer mask is a `GimpLayerMask` (a single-channel buffer) with apply, edit and show flags. It feeds the mode operation's aux2 input.
- The selection is a `GimpSelection` channel kept outside the channel tree. Quick Mask is a temporary channel. Lock-alpha is a layer flag.

### 2.10 Canvas rendering, zoom and pan [C]

**Structure**
- `GimpDisplayShell` (a GTK widget) contains a `GimpCanvas`.
- Shells live inside a `GimpImageWindow` (a notebook plus left and right docks).
- One projection per image is shared by all its views. Each view has its own zoom, offset, rotation and **render cache** (a cairo surface plus a validity region).

**Draw path**
1. The GTK "draw" signal triggers `gimp_display_shell_draw_image()` (`gimpdisplayshell-draw.c:145`), which splits the area into chunks of up to 256×256.
2. Chunks missing from the cache go through `gimp_display_shell_render()` (`gimpdisplayshell-render.c:147`), which does three things:
   - `gegl_buffer_get(projection, scale, "cairo-ARGB32")`: when zoomed out, this uses the GEGL mipmaps;
   - applies the display colour-management transform (via babl, or lcms2 as fallback; the Windows monitor profile comes via WCS);
   - applies display filters.
3. Canvas items are drawn last: selection outline, guides, tool overlays.

**Zoom, rotation and flip** are pure view transforms (`gimpdisplayshell-transform.c`, `-rotate.c`). They never resample the image data.

**Pan** (`gimpdisplayshell-scroll.c:64`) scrolls the cached surface onto itself and re-renders only the newly exposed strips.

### 2.11 Selections and transformations [C]

**Selections**
- `gimpchannel-select.c` provides rect, ellipse, polygon, path, fuzzy and by-colour selection.
- Each takes an operation (replace, add, subtract, intersect) plus optional feathering. They are combined by `gimpchannel-combine.c` and the C++ loops in `gimp-gegl-mask-combine.cc`.

**Transforms**
- Class chain: `GimpTransformTool → GimpTransformGridTool → {Scale, Rotate, Shear, Perspective, Unified, Handle, 3D}`.
- Matrices are 3×3. The preview is either a canvas item or a temporary drawable filter.
- On commit (`gimptransformtool.c:817`), `gimp_drawable_transform_buffer_affine` runs **`gegl:transform`**.

### 2.12 Undo and redo [C]

**Structure**
- `GimpUndo` objects live on `GimpUndoStack`. The stack is itself a `GimpUndo`, so groups nest (the composite pattern).
- There are about 25 concrete undo classes.

**Push** (`gimp_image_undo_push`, `gimpimage-undo.c:355`): marks the image dirty, **discards the redo stack**, pushes onto the open group, then frees space if needed.

**Pixel undo**
- Copies only the affected rectangle.
- Undoing **swaps** the pixels, so the same record then serves as redo.

**Limits**
- At least 5 levels are always kept.
- Beyond that, `undo-size` defaults to **1/8 of physical RAM** (`gimpcoreconfig.c:580-583`).
- There is a hard cap of 1024 steps.

### 2.13 Loading, saving and XCF [C]

**File I/O through procedures**
- All file I/O goes through registered procedures.
- `file_open_image` (`file/file-open.c:102`) runs a format plug-in **in a separate process**.
- Even XCF is registered as an "internal" procedure (`xcf/xcf.c`).
- GIMP separates **Save** (XCF only; clears the "dirty" state) from **Export** (other formats; tracks a separate "export dirty" state).

**XCF format**
- Header tag `gimp xcf vNNN`, image properties stored as tag-length-value records (59 property IDs), then tables of layer and channel offsets (64-bit since v11).
- Each layer stores a hierarchy of resolution levels, but only level 0 holds data, as **64×64 tiles** (`xcf-private.h:21-22`) compressed with RLE or zlib.
- `gimp_image_get_xcf_version` (`gimpimage.c:2849`) writes the **lowest version that can represent the features actually used**:
  - v3: groups
  - v7/12: high bit depth
  - v8: zlib
  - v11: files over 4 GB
  - v24: vector layers
  - v26: channel effects
- Loaders exist for v0–26.
- GIMP's own docs call XCF a "living format" whose ultimate reference is the code.

### 2.14 Plug-ins and extension mechanisms [C]

**Plug-in processes**
- Plug-ins run as separate executables.
- `gimp_plug_in_open` (`plug-in/gimpplugin.c:433`) runs them with `-query`, `-init` or `-run` and talks over two pipes, optionally with shared memory.
- The wire protocol (`libgimpbase/gimpwire.c`, `gimpprotocol.c`) carries procedure calls and **128×128 tile transfers**.

**Procedure database**
- The **PDB** is a name-to-procedure registry with 786 internal procedures generated by `pdbgen.pl`.

**Other extension points**
- **Scripting:** Python, gjs and Lua bind through GObject Introspection of libgimp; Script-Fu uses TinyScheme.
- **Modules:** in-process plug-ins for display filters, colour selectors and input controllers.
- **Extensions:** `.gex` zip bundles.
- **GEGL filters:** applied as non-destructive drawable filters.

### 2.15 Docking, panels and interface customization [C]

**Hierarchy:** dock window or image-window side panes → `GimpDockColumns` → `GimpDock` (including `GimpToolbox`) → `GimpDockbook` (a GtkNotebook) → `GimpDockable`.

**Dialog factory:** `GimpDialogFactory` (`widgets/gimpdialogfactory.c`) registers 89 dialogs, 33 of them dockable.

**Session**
- `GimpSessionInfo` serialises the whole window tree to **`sessionrc`** (`gui/session.c`).
- Single-window and multi-window modes are both supported.

**Workspace presets: none.**
- There is one live `sessionrc` plus "reset to defaults". GIMP has no named workspace layouts.
- Tool presets cover only tool options. The only "workspace" matches in the code are macOS `NSWorkspace` calls.

### 2.16 Who provides what

| Functionality | (1) GIMP itself | (2) GTK / GDK | (3) GEGL / babl | (4) Other libraries / OS |
|---|---|---|---|---|
| Object model, image/layer model | ✔ items, trees, image | – | – | GLib/GObject |
| Tiled storage, cache, swap, mipmaps, COW | glue/config | – | **GEGL** | – |
| Pixel formats, conversion, colour spaces | precision mapping | – | **babl** | lcms2 (CMYK in babl) |
| Compositing graph and scheduling | node wiring, projection, chunk renderer | – | **GEGL** evaluation, threads | – |
| Blend/layer modes | **gimp:layer-mode ops** | – | GEGL op framework | – |
| Filters | menus, non-destructive stack | dialogs | **GEGL ops** | OpenCL (via GEGL, off) |
| Transforms | matrices, tools | – | **gegl:transform** | – |
| Selections | select/combine logic | – | buffers, blur | – |
| Undo | ✔ entirely | – | COW makes copies cheap [I] | – |
| Painting engine | ✔ paint cores, dynamics | – | buffers, parallel helpers | libmypaint (2nd engine) |
| Canvas display | render, cache, canvas items | widget, invalidation | scaled reads | **cairo** |
| Display colour management | pipeline | – | babl | lcms2; **Windows WCS** |
| Docking, windows, session | docks, factory, sessionrc | **GTK widgets** | – | – |
| Actions and shortcuts | GimpAction, UIManager, shortcutsrc | **GtkApplication**, GtkBuilder | – | GIO |
| Tablet input | device settings, curves, motion buffer | **GdkDevice + Win32 backend** | – | **Windows Ink / WinTab**, driver |
| Text | text layers | – | – | pango, harfbuzz, fontconfig, freetype |
| XCF | ✔ entirely | – | – | zlib |
| Other formats | plug-in glue | – | file-gegl | libpng, libjpeg, libtiff, libwebp, libheif, libjxl, OpenEXR, poppler, librsvg… |
| Plug-in IPC, scripting | ✔ wire protocol, PDB | – | – | pipes/shared memory, GObject Introspection, TinyScheme |

---

## 3. Source-code evidence (key paths and symbols)

GIMP paths are relative to the `GIMP_3_2_8` root. GTK paths are relative to the `3.24.52` root and prefixed `gtk:`.

| Subsystem | Files | Key symbols / facts |
|---|---|---|
| Startup / lifecycle | `app/main.c`, `app/app.c`, `app/core/gimp.c`, `app/gui/gui.c` | `main` (Win32 tablet env vars L620-649), `app_run`, `app_activate_callback`, signals initialize/restore/exit, `gui_restore_callback` |
| UI boundary | `app/core/gimp-gui.h`, `app/gui/gui-vtable.c`, `app/core/gimpdisplay.c` vs `app/display/gimpdisplay.c` | `struct _GimpGui`, `gui_vtable_init`, `GimpDisplayImpl` |
| GEGL setup | `app/gegl/gimp-gegl.c`, `app/config/gimpgeglconfig.c`; GEGL `gegl/buffer/gegl-buffer-config.c`, `gegl-tile.c`, `gegl-tile-handler-zoom.c`, `gegl-tile-handler-cache.c` | tile cache = ½ RAM; 128×128 tiles; `gegl_tile_dup` (COW) |
| Formats | `app/gegl/gimp-babl.c`; babl `babl-fish-path.c`, `babl-icc.c` | `gimp_babl_format()`, fish path search, ICC → space |
| Image model | `app/core/gimpimage-private.h`, `gimpdrawable-private.h`, `gimpitemtree.c`, `gimpgrouplayer.c` | one `GeglBuffer` per drawable; three item trees |
| Compositing | `app/core/gimpfilterstack.c`, `gimplayer.c`, `app/operations/layer-modes/gimp-layer-modes.c`, `gimpoperationlayermode.c` | `get_graph` (bottom-up chain), `gimp_layer_get_node`, `layer_mode_infos[]` |
| Projection | `app/core/gimpprojection.c`, `app/gegl/gimptilehandlervalidate.c`, `app/core/gimpchunkiterator.c` | lazy tile rendering, 32×32 dirty chunks, viewport priority |
| Display | `app/display/gimpdisplayshell-draw.c`, `-render.c`, `-scale.c`, `-scroll.c`, `-rotate.c`, `-transform.c`, `-profile.c` | `gimp_display_shell_draw_image`, `gimp_display_shell_render` |
| Selections / transforms | `app/core/gimpchannel-select.c`, `gimpchannel-combine.c`, `app/tools/gimptransformtool.c`, `app/core/gimpdrawable-transform.c` | `gimp_transform_tool_transform` → `gegl:transform` |
| Undo | `app/core/gimpimage-undo.c`, `gimpundostack.c`, `gimpdrawableundo.c` | `gimp_image_undo_push`, `free_space`, swap-on-pop |
| Files / XCF | `app/file/file-open.c`, `file-save.c`, `app/xcf/xcf.c`, `xcf-save.c`, `xcf-load.c`, `xcf-private.h` | 64×64 XCF tiles; `gimp_image_get_xcf_version` |
| Plug-ins | `app/plug-in/gimpplugin.c`, `libgimpbase/gimpwire.c`, `gimpprotocol.c`, `app/pdb/internal-procs.c`, `pdb/pdbgen.pl` | 786 internal procedures |
| Docking / UI | `app/widgets/gimpdock*.c`, `gimpdialogfactory.c`, `gimpsessioninfo*.c`, `app/gui/session.c`, `gimpuimanager.c`, `menus/shortcuts-rc.c` | sessionrc; no workspace presets |
| **Painting** | `app/tools/gimppainttool-paint.c`, `app/paint/gimppaintcore.c`, `gimppaintcore-loops.cc`, `gimpbrushcore.c`, `app/core/gimpdynamicsoutput.c`, `gimpbrushgenerated.c`, `gimpbrushcache.c`, `gimpdrawable.c`, `app/display/gimpmotionbuffer.c` | paint thread, `DISPLAY_UPDATE_INTERVAL 10000` µs, `gimp_paint_core_paste` L1265, `gimp_brush_core_interpolate` L448, dynamics averaging, `MAX_CACHED_DATA 20` |
| **Tablet (GIMP)** | `app/config/gimpearlyrc.c`, `app/core/core-enums.h`, `app/widgets/gimpdeviceinfo.c`, `gimpdeviceinfo-coords.c`, `gimpdevicemanager.c`, `gimpmodifiersmanager.c`, `app/display/gimpdisplayshell-tool-events.c`, `app/config/gimpdisplayconfig.c` | `win32-pointer-input-api` default `WINDOWS_INK`; pressure curve in `map_axis`; event compression off during strokes (L591); `DEFAULT_USE_EVENT_HISTORY FALSE` |
| **Tablet (GTK)** | `gtk:gdk/win32/gdkdevicemanager-win32.c`, `gdkevents-win32.c`, `gdkdevice-winpointer.c`, `gdkdevice-wintab.c`, `winpointer.h`; `gtk:gdk/gdkevents.c`, `gdkwindow.c` | `GDK_WIN32_TABLET_INPUT_API` (L1486-1513); Ink axes `pressure/1024.0` (L1810); `GetPointerPenInfoHistory` replay (L1940-1983); WinTab context (L1095-1193); `decode_tilt` with Huion H610PRO quirk (L2167-2214); `PEN_FLAG_BARREL` defined but never read |

---

## 4. Painting engine deep dive

### 4.1 The pipeline

```
Pen/mouse → Windows (WM_POINTER or WinTab) → GTK/GDK events            [UI thread]
  │ gimp_display_shell_canvas_tool_events()   display/gimpdisplayshell-tool-events.c:356
  │   device switch (per-device tool) · axes → GimpCoords · pressure through device curve
  │   window → image coordinates (inverse zoom/rotate/flip)
  │   button press: motion compression OFF for the stroke (paint tools use EXACT motion mode)
  ▼
GimpMotionBuffer  display/gimpmotionbuffer.c
  │   drop moves < ½ screen pixel · smooth velocity & direction
  │   "event fill": Catmull-Rom between far-apart events (holds 1 event, 50 ms timeout)
  ▼
GimpPaintTool::motion  → optional smoothing (gimp_paint_core_smooth_coords)
  │   → queue "interpolate to here" job (unbounded GQueue + mutex/cond)
  ▼ ══════════════ single global "paint" thread ══════════════
GimpBrushCore::interpolate  paint/gimpbrushcore.c:448
  │   spacing = fraction of brush diameter · carry leftover distance · jitter
  │   per dab: interpolate x, y, pressure, tilt, wheel, velocity
  ▼
GimpPaintbrush motion  paint/gimppaintbrush.c:219
  │   dynamics → opacity, size, angle, hardness, aspect, force · fill paint colour
  ▼
brush mask: transform/cache (20 entries) → subpixel variant (5×5) | hard | pressure
  ▼
gimp_paint_core_paste  paint/gimppaintcore.c:1265
  │   CONSTANT (default): mask merged into per-stroke mask, blended over PRE-STROKE snapshot
  │   INCREMENTAL: blended over current pixels
  │   fused per-pixel loop, threaded per ~64×64 px; writes to private COW "paint_buffer"
  │   accumulate dirty regions (tile-aligned copy region, 32×32 update region)
  ▼ ═══════════════════════════════════════════════════════════
every 10 ms on UI thread (gimp_paint_tool_paint_timeout, takes paint mutex)
  │   flush_paint: COW-copy dirty tiles → real layer buffer; emit updates
  │   projection: recomposite dirty 32×32 chunks synchronously (GEGL graph, threaded ops)
  │   display: queue redraw → render dirty chunks via cairo (CPU)
  ▼
pen up → drain queue → gimp_paint_core_finish: crop snapshot to stroke bbox (tile-aligned)
          → push GIMP_UNDO_GROUP_PAINT (GimpPaintCoreUndo + GimpDrawableUndo)
```

### 4.2 Stage by stage [C unless marked]

**1. Coordinates**
- `GimpCoords` (`core/core-types.h:281`) carries:
  - device axes: x, y, pressure, xtilt, ytilt, wheel, distance, rotation, slider;
  - computed axes: velocity, direction;
  - the view transform: scale, angle, reflect, used for "lock brush to view".
- Pressure passes through a **per-device pressure curve** (`gimpdeviceinfo.c:1396-1407`). Tilt and wheel are only clamped.

**2. Motion buffer** (`gimpmotionbuffer.c`)
- **Drops events that move less than half a screen pixel** (`:298-304`). This discards *pressure-only* changes while the pen is still.
- Smooths the time step: `dt = 0.7·last + 0.3·new`. Derives velocity (screen space, clamped to 0–1) and direction from a short history.
- **Event fill**: if two events are 6 or more image pixels apart, it holds one back. When the next one arrives, it emits Catmull-Rom spline points at about 3 px precision, interpolating pressure too. A **50 ms timeout** releases a held event (`:491`).

**3. Smoothing** (`gimp_paint_core_smooth_coords`, `gimppaintcore.c:1660`; off by default)
- A Gaussian-weighted average of the last N points (quality, default 20). The weights depend on accumulated velocity, so slow movement is smoothed strongly and fast movement lightly.
- Only x and y are smoothed.
- [I] There is no end-of-stroke catch-up and no "pull-string" stabiliser.

**4. Paint thread** (`gimppainttool-paint.c:83-160`)
- One global thread, unless `GIMP_NO_PAINT_THREAD` is set, fed by an **unbounded, non-merging queue**.
- **Double buffer.** While painting, the paint thread writes into a private copy-on-write duplicate of the layer (`gimp_drawable_start_paint`, `gimpdrawable.c:2146`). Every 10 ms the UI thread takes the paint mutex, copies dirty tile-aligned regions into the real buffer by sharing tiles (`gimp_drawable_flush_paint`, `:2193`), then recomposites and redraws.
- [I] One slow job blocks the UI timer, and a long backlog causes growing lag.

**5. Spacing and interpolation** (`gimp_brush_core_interpolate`, `gimpbrushcore.c:448`)
- Distance is measured **in brush diameters**; `brush-spacing` defaults to 0.1, i.e. 10%.
- Leftover distance carries over between events.
- Dynamics can widen spacing up to 200%.
- A special case for 1 px brushes avoids gaps and double-plotted pixels.
- Pressure, tilt, wheel and velocity are **linearly interpolated per dab**. Jitter is an optional random offset.

**6. Dynamics** (`core/gimpdynamicsoutput.c`)
- 11 outputs: opacity, size, angle, colour, hardness, force, aspect, spacing, rate, flow, jitter.
- Each output can use 8 inputs (pressure, velocity, direction, tilt, wheel, rotation, random, fade), each with its own curve.
- Several enabled inputs are combined by **averaging, not multiplying** (`:491`).
- The default dynamics is "Pressure Size": pressure→size only.

**7. Brush tip**
- **Generated brushes** (circle/square/diamond, radius, spikes, hardness, aspect, angle) are re-rendered from their formula at the target size, so size and hardness stay exact (`gimpbrushgenerated.c`).
- **Bitmap brushes** are resampled bilinearly. Hardness below 1 is done by **blurring** the mask.
- The transformed-mask cache holds **20 entries with exact-match keys** (`gimpbrushcache.c:36`). [I] So continuous pressure→size defeats it.
- 5×5 subpixel-shifted variants keep thin strokes from wobbling.
- Supported formats: `.gbr`, `.gpb`, `.gih` (brush pipes), `.vbr`, `.abr` (Photoshop), `.jbr`.

**8. Applying a dab** (`gimp_paint_core_paste`, `gimppaintcore.c:1265`)
- **CONSTANT ("wash") mode** is the default. The dab is merged into a per-stroke float mask with `if (op > c) c += (op − c)·mask·op` (`gimppaintcore-loops.cc:1316-1320`), so the stroke approaches the dab opacity but never exceeds it. The mask is then blended over the **pre-stroke snapshot**.
  - Result: a 50% stroke stays 50% where it crosses itself.
  - The airbrush instead accumulates toward 1.
- **INCREMENTAL mode** blends each dab onto the current pixels, so overlaps build up.
- All layer modes work as paint modes.
- One C++-templated fused loop also applies the selection mask and channel locks, threaded at about 64×64 px per thread.
- The general GEGL "applicator" path is **debug-only** (`use-applicator` default FALSE).

**9. Eraser** (`gimperaser.c`)
- Not a separate engine; it runs through the same dab pipeline.
- Mode is ERASE if the layer has alpha, otherwise it paints the background colour. Anti-erase is an option.
- "Hard edge" thresholds the mask.

**10. Projection and redraw**
- Recomposites only the dirty chunks, then the display re-renders only the invalidated screen chunks.
- [I] There is **no cache of the layers below or above** the active layer, apart from group projections. Every dirty chunk re-runs the whole layer chain at full resolution.

**11. Undo**
- At stroke start, `gimp_gegl_buffer_dup` takes a copy-on-write snapshot (`gimppaintcore.c:434`).
- At the end, the snapshot is cropped to the tile-aligned stroke bounding box and pushed as a `GimpDrawableUndo` inside one undo group (`:595-604`).
- Undoing swaps pixels.
- [I] Untouched tiles inside the bounding box share memory with the live layer.

### 4.3 Brush behaviour checklist

| Topic | How GIMP does it |
|---|---|
| Size, hardness | Size scales the brush mask (generated brushes are re-rendered exactly). Hardness changes the falloff exponent `0.4/(1−h)` for generated brushes, and blurs bitmap brushes. |
| Opacity, flow | Opacity is a per-stroke cap (CONSTANT) or per dab (INCREMENTAL). "Flow" exists only for the airbrush/smudge, along with rate (a timer re-stamps while the pen is held). |
| Spacing | A fraction of diameter with leftover carried between events; dynamics can widen it. |
| Interpolation | Linear between samples; optional Catmull-Rom fill of sparse events. |
| Smoothing | Optional velocity-weighted Gaussian; no catch-up, no lazy-mouse stabiliser. |
| Tip representation | Alpha masks (8-bit or float), optional colour pixmap, subpixel variants, 20-entry cache. |
| Pressure dynamics | Device curve, then dynamics curves, averaged across enabled inputs, applied per dab. |
| Tilt, rotation | Inputs for dynamics (tilt → angle/size etc.); "Rotation" input added in 3.2.6 for barrel-rotation pens. |
| Eraser | A paint mode in the same pipeline; the eraser end of the pen is handled by per-device tool memory (§5.2). |
| Presets | Per-tool options saved in `tool-options/`. Tool presets (`.gtp`) are text dumps of options plus flags for which resources to apply. Brushes and dynamics are data files. |
| Shared code | Class chains `GimpPaintCore → GimpBrushCore → GimpPaintbrush → {Pencil, Airbrush, Eraser}`, plus Smudge, Convolve, DodgeBurn, Clone/Heal, Ink and MyPaint. The tools mirror this: `GimpPaintTool → GimpBrushTool → …`. |

### 4.4 Performance

- **Tiled storage:** yes, 128×128 GEGL tiles. Unwritten tiles point to a shared zero tile, and copying shares tiles copy-on-write [C].
- **Partial updates:** dirty regions at three levels:
  - layer: tile-aligned copy region and 32×32 update region;
  - projection: 32×32 chunks;
  - screen: render-cache chunks of up to 256×256.
  - Pan reuses the cached surface [C].
- **Threading** [C]:
  - one paint thread;
  - data-parallel threads within each dab, and within GEGL's compositing operations;
  - UI-thread work every 10 ms: publishing, recompositing and drawing;
  - async jobs only for histograms and previews.
- **CPU vs GPU:** all CPU. OpenCL is forced off at startup (`app/app.c:126-129`) and the canvas is drawn with cairo [C].
- **Large canvases** [I]:
  - stroke cost grows with dab count × dab area;
  - recompositing cost grows with the number of layers in the stack × dirty area;
  - zoomed-out views still need the full-resolution composite of dirty tiles first.
- **Undo during painting:** one tile-aligned bounding-box snapshot per stroke, built copy-on-write [C].
- **Likely bottlenecks for a lightweight editor** (all [I]):
  1. per-dab overhead at tight spacing, including mask-cache misses;
  2. converting 8-bit pixels to float and back for every dab;
  3. recompositing the whole stack without below/above caches;
  4. synchronous compositing on the UI thread under the paint lock;
  5. an unbounded job queue;
  6. latency from event filling and smoothing, with no prediction or "wet ink" preview.

### 4.5 Practical conclusions

**Valuable for DoodleNook:**
1. Keep dab generation (geometry plus dynamics) separate from dab compositing.
2. Build each stroke as a mask over a pre-stroke snapshot, for correct opacity. Undo and cancel then come almost for free.
3. Measure spacing in brush diameters, carry leftover distance, and interpolate pressure per dab.
4. Make dynamics small curve lookups, starting with pressure→size and pressure→opacity.
5. Pre-filter motion and derive velocity; add an *optional* stabiliser, with end-of-stroke catch-up.
6. Paint on a worker thread and publish dirty tiles on a timer tied to vsync. Unlike GIMP, bound or merge the job queue and avoid blocking locks.
7. Use copy-on-write tiled layers. These give cheap snapshots and undo, and work proportional to the area touched.
8. Track dirty regions at every stage.
9. Generate round brush masks analytically, with subpixel positioning.
10. Treat the eraser as a blend mode (destination-out).

**Unnecessary for an initial version:**
- 18 precisions and per-layer colour spaces.
- 65 layer modes as paint modes, and per-channel locks.
- Brush pipes, ABR/JBR import and blur-based hardness.
- The 11×8 dynamics matrix, fade/gradient colour, symmetry and auto-expanding layers.
- A second engine (libmypaint).
- Heal, perspective clone, ink and smudge.
- Deep mirrored class hierarchies, and per-device tool memory beyond "eraser end → eraser".

**Additions GIMP lacks** [I]:
- A per-stroke cache of the layers below and above the active one.
- A low-latency "wet" preview of the newest segment.
- Use of the pen's coalesced samples instead of dropping or compressing them.

---

## 5. Huion and Windows tablet support

### 5.1 Who is responsible for what

```
Huion pen + digitizer ─► Huion driver (pressure curve, button mapping, "Enable Windows Ink",
                         monitor mapping, its own wintab32.dll in System32)
        ├─► Windows Ink / WM_POINTER (OS)  — pressure normalised 0–1024 in POINTER_PEN_INFO,
        │      tilt ±90 field (Huion physical ±60), rotation, barrel/inverted/eraser flags,
        │      coalesced history, press-and-hold/flick gestures, mouse promotion
        └─► WinTab (driver-provided DLL) — device-native ranges, azimuth/altitude/twist,
               cursor type, buttons bitmask, per-app context & mapping
                    ▼
          UI toolkit (GTK, WPF, Qt, Avalonia...) — chooses API, normalises axes, suppresses
          gestures/mouse duplicates, delivers events (with or without coalescing)
                    ▼
          Application (GIMP / DoodleNook) — device settings, pressure curve, tool switching,
          stroke engine
```

### 5.2 How GIMP and GTK handle it [C]

**Choosing the API**
- **GTK side.** GTK 3.24 supports Windows Ink, WinTab or none, selected only by the `GDK_WIN32_TABLET_INPUT_API` environment variable.
  - With nothing set, GTK uses Windows Ink, and falls back to WinTab if Ink cannot initialise (`gdkdevicemanager-win32.c:1486-1513`).
  - WinTab starts late, after the display exists, because a **Huion H610PRO crashed** when WinTab started early (GTK NEWS 3.22.5, bug 774379).
- **GIMP side.**
  - The setting is `win32-pointer-input-api`, default **Windows Ink**, restart required (`gimpearlyrc.c`).
  - It appears in Preferences → Input Devices.
  - GIMP *always* sets the variable, so GTK's automatic fallback never runs. GIMP does its own fallback when Windows is older than 8 or `Wintab32.dll` is missing.
  - The preference arrived in GIMP 2.99.8; GTK gained pointer-input support in 3.24.30.

**Windows Ink path (GTK)**
- **What it handles:**
  - Handles `WM_POINTERDOWN/UP/UPDATE/ENTER/LEAVE` for pen and touch. Mice still arrive as normal mouse messages.
  - Reads `GetPointerPenInfoHistory` and **replays every coalesced sample, oldest first**, as separate events with millisecond timestamps (`:1940-1983`).
  - **Pressure = `pressure/1024.0`** (`:1810`), so 8192- or 16384-level pens are reduced to about 1024 steps.
  - Tilt = `tiltX/90`, `tiltY/90`. Rotation = `rotation/360`.
  - Eraser = the `PEN_FLAG_INVERTED` or `PEN_FLAG_ERASER` flag, mapped to a separate "Eraser" device.
  - Proximity in/out from enter/leave.
- **What it suppresses:** press-and-hold, flicks, tap and barrel feedback and gestures, set per window (`:709-766`). It also drops mouse moves within 200 ms of pen input.
- **Barrel button:** only "contact + barrel" maps to GDK button 3. **`PEN_FLAG_BARREL` is defined but never read**, so a barrel press while hovering is lost.

**WinTab path (GTK)**
- **Setup:**
  - Loads `System32\Wintab32.dll` and opens a context requesting pressure, orientation, buttons and cursor, with a queue of up to 128 packets.
  - Produces one event per packet, with no batch reads.
- **Axes:**
  - Pressure is scaled by the driver-reported range.
  - Tilt is computed from azimuth/altitude. The code says it was tuned on a Wacom Intuos 5, and **forces tilt to 0 for Huion H610PRO-type drivers** that report zero altitude resolution (`:2167-2214`).
  - Twist is reported as "wheel".
- **Known gaps:**
  - The eraser cursor becomes a separate device but is typed as *pen*, so GIMP doesn't auto-switch to the eraser.
  - No proximity events.
  - The **whole tablet is mapped to the whole virtual screen**, which risks an offset on pen displays and with multiple monitors [I].

**Event delivery**
- GDK compresses motion by default and holds the last motion event for the frame clock (`gdkevents.c:93-122,255-311`).
- GIMP **turns compression off only while painting**, only for exact-motion tools (`gimpdisplayshell-tool-events.c:591`).
- `gdk_device_get_history` returns FALSE on every Windows device. GIMP's `use-event-history` defaults to FALSE anyway, described in the code as "bugs in event history buffer are frequent".

**GIMP's device layer**
- Per-device settings, keyed **by device name** (with a Windows duplicate-name workaround).
- An editable **per-device pressure curve**.
- **Per-device tool memory**: the eraser device defaults to the Eraser tool, the pen to the Paintbrush.
- Barrel buttons: button 2 pans (plus modifier variants), button 3 opens the menu (`gimpmodifiersmanager.c`).
- Express keys on the tablet pad are not handled on Windows; the code itself says so.

**Capability matrix (GTK 3.24.52 + GIMP 3.2.8)**

| Capability | WinTab path | Windows Ink path |
|---|---|---|
| Pressure | ✔ driver range | ✔ reduced to 0–1024 |
| Tilt | ✔ cosine projection; zeroed on some Huion drivers | ✔ linear degrees/90 |
| Rotation / twist | as "wheel" | as "rotation" (0–1) |
| Barrel buttons | ✔ bitmask, works hovering (buttons 2/3 swapped) | only "in contact + barrel" → button 3 |
| Eraser end | separate device but typed as pen | ✔ separate eraser device → Eraser tool |
| Proximity | no proximity events | ✔ in/out |
| Coalesced samples | ✗ (one event per packet) | ✔ history replayed (survives only with compression off) |
| Coordinate mapping | whole tablet → whole screen | OS/driver mapping, sub-pixel |

### 5.3 Huion facts (official Huion pages) [C; varies by model and driver]

- **Pressure:**
  - 8192 levels (PenTech 3.0 pens such as PW110 and PW550S).
  - 16384 levels with 2 g activation (PenTech 4.0: PW600/PW600S/PW600L/PW600A).
  - Older pens have 2048 or 4096 levels.
  - The driver applies its own pressure curve.
- **Tilt:** ±60° on models since July 2018, from a published list of tilt-capable models. "Tilt support is only available in partial software."
- **Report rate and hover:** report rate is over 260 PPS on current models (over 133 PPS over Bluetooth on some). Hover height is 10–12 mm.
- **Windows Ink:**
  - Driver checkbox "Enable Windows Ink"; driver **20.1.1.106 (June 2026) allows it per application**.
  - Huion's FAQ: *"If your drawing program uses WinTab32, disable Windows Ink."*
  - Huion ships its own `wintab32.dll` and repair instructions for it.
- **Side buttons:**
  - Two on most pens, three on PenTech 4.0 pens.
  - Mapped in the driver, globally or per app, to mouse buttons, keystrokes or **"Toggle Pen/Eraser"**.
  - Driver v20 adds "Contact click".
- **Eraser end:** a **tail eraser only on the PW600/PW600S** (Kamvas Pro 19, Pro 24 Gen 3, Pro 27). Most other pens have none.
- **Barrel rotation:** not mentioned anywhere; assume it is absent [I].

### 5.4 What this means for a C# application

**WPF stylus stack** [C, Microsoft docs and the .NET reference source]
- **Data and events:**
  - `StylusPointProperties` provides `NormalPressure` (device-defined range; `PressureFactor` 0–1), `XTiltOrientation`, `YTiltOrientation`, `TwistOrientation`, `BarrelButton` and `TipButton`.
  - `StylusDevice` provides `Inverted` (eraser end), `InAir` and `InRange`.
  - Events: `StylusInAirMove`, `StylusInRange/OutOfRange` and `StylusButtonDown/Up`.
  - Each move carries a **`StylusPointCollection` of all samples since the last event**, i.e. its coalesced history.
- **Default stack (WISP/PenIMC):**
  - Delivers points to **StylusPlugIns on a separate pen thread**, before the UI thread.
  - `DynamicRenderer` draws on its own thread so ink keeps flowing while the UI thread is busy.
  - A StylusPlugIn could feed DoodleNook's stroke engine directly, bypassing UI-thread congestion [I].
- **Opt-in `WM_POINTER` stack** (`Switch.System.Windows.Input.Stylus.EnablePointerSupport`):
  - Reads raw device data through `GetRawPointerDeviceData`, so it avoids the 1024-step quantisation.
  - Microsoft documents that it has **"no support for real-time inking"**: plug-ins run on the UI thread.
- **Disabling system pen behaviour:** `Stylus.IsPressAndHoldEnabled`, `IsFlicksEnabled` and `IsTapFeedbackEnabled` can be set to false on the canvas.
- **Limits:**
  - **No WinTab support**. With Huion's Windows Ink turned off for the app, WPF likely sees only mouse input [I].
  - Stylus input is also promoted to mouse events, so the app must de-duplicate.

**Using Windows Ink directly** [C]
- `GetPointerPenInfo` normalises pressure to 0–1024.
- `GetPointerDeviceProperties` exposes the true logical range (HID usage 0x30), and `GetRawPointerDeviceData` exposes raw values.
- `GetPointerPenInfoHistory` returns coalesced samples, newest first.
- `POINTER_INFO.PerformanceCount` gives high-resolution timestamps for measuring latency.
- Gestures are disabled per window via `WM_TABLET_QUERYSYSTEMGESTURESTATUS` or the `MicrosoftTabletPenServiceProperty` window property.
- Hosting this in WPF requires an `HwndHost` child window [I].

**WinTab from C#**
- Needs P/Invoke: `WTInfo`, `WTOpen`, `WTPacketsGet`, `WTQueueSizeSet`, plus a custom PACKET struct.
- Wacom publishes WintabDN sample code, but **its licence was not verified** because GitHub was blocked.
- Krita defaults to WinTab, and GIMP and Qt keep it as an option, so it is worth planning as a later opt-in.

**Can SkiaSharp render pressure-sensitive strokes?** [C/I]
- Yes. Pressure is our engine's job: dab size and opacity per sample.
- SkiaSharp draws the dabs (anti-aliased circles or analytic masks, blend modes, A8/F16 surfaces) into raster surfaces.
- It does no input handling.

**Latency concerns** [I unless marked]
- **Input side:** WPF's default stack gets samples to a pen thread quickly [C].
- **Display side:**
  - Each frame goes CPU Skia raster → `WriteableBitmap` back buffer → front buffer → video memory → DirectX 9-era composition [C mechanism].
  - Surfaces beyond the maximum texture size fall back to software [C].
  - Expect at least one composition frame of delay.
  - Mitigations: a viewport-sized bitmap, dirty-rectangle uploads, and a provisional "wet" segment preview.
- **GPU options and their caveats:**
  - `SKGLElement`: depends on the NV_DX_interop driver extension.
  - D3DImage interop: Direct3D 9 surfaces only.
  - HwndHost child swap chain: no WPF overlays on top ("airspace") [C].
  - **Delegated ink trail** (`IDCompositionDelegatedInkTrail`, compositor-drawn newest segment): needs a DXGI/DirectComposition surface, not WPF composition; documented as prerelease [C].

**Pen buttons and eraser reliability** [C/I]
- Treat all of the following as a "barrel" press:
  - `PEN_FLAG_BARREL`;
  - WPF `BarrelButton` / `StylusButtonDown`;
  - **mouse right/middle clicks while the pen is in range** (Krita does this automatically when it falls back to Ink).
- Treat all of the following as "eraser":
  - `INVERTED`/`ERASER` flags or `StylusDevice.Inverted`;
  - the WinTab cursor type;
  - a configurable **keyboard shortcut**, for the driver's "Toggle Pen/Eraser".
- Expect a pointer leave and a new pointer ID when the pen flips to its eraser end [C: Windows pen-state documentation].

**What GTK did for GIMP that DoodleNook (or its UI framework) must do itself** [I]:
1. Choose an API, fall back, and persist the choice.
2. Enumerate devices and handle hot-plug.
3. Convert HIMETRIC/screen coordinates to client pixels, including DPI and negative virtual-desktop offsets.
4. Normalise axes, with defaults when an axis is missing.
5. Keep **coalesced samples with timestamps**.
6. Suppress OS gestures and duplicate mouse input.
7. Track pen versus eraser and proximity.
8. Map buttons consistently across APIs.
9. Build a latency-aware pipeline from input thread to engine to presentation.

### 5.5 Hardware tests to perform eventually (prioritised)

Use a small "pen probe" harness that logs WPF stylus data, raw `WM_POINTER` (including history and raw data), WinTab packets and mouse messages side by side. Use Krita's Tablet Tester as a reference.

**Priority 0**
1. **Driver mode matrix.** Turn "Enable Windows Ink" on and off, globally and per app. For each setting, record which path delivers pressure, tilt and buttons, including after a driver restart.
2. **Pressure fidelity.**
   - Logical maximum (8191 or 16383?).
   - Actual step size seen by WPF, WISP, `WM_POINTER` and WinTab.
   - Minimum activation force and linearity with the driver curve at default and set to linear.
3. **Sample rate and coalescing.** Device rate versus app events per second; samples per event with the UI idle and loaded; dropped or duplicated samples; timestamps that only increase.
4. **End-to-end latency.** Film with a 240–960 fps camera on a pen display at constant pen speed and compare candidate rendering paths at 60 Hz and high refresh rates. Cross-check with input timestamps and present times.
5. **Side buttons** under every driver mapping (default, right click, middle click, keystroke, Toggle Pen/Eraser, Contact click), hovering versus in contact, in Ink and WinTab modes.
6. **Eraser** on a PW600-class pen, if available. Also test "Toggle Pen/Eraser" on pens without one.

**Priority 1**

7. **Tilt.** Sign conventions, maximum value (±60 versus ±90), resolution, and agreement between WinTab and Ink. Confirm that rotation is absent.
8. **Hover and proximity.** Enter/leave, hover jitter, and leaving the window while in contact (pointer capture).
9. **OS gestures.** Confirm press-and-hold, flicks and ripple feedback are suppressed and that no strokes are duplicated by promoted mouse events.
10. **Multiple monitors and DPI.** Mixed 100/150/200% scaling, the driver mapped to one display or all, screen rotation, cursor-to-nib offset.

**Priority 2**

11. Hot-plug, sleep/wake, USB versus Bluetooth, driver restart.
12. Coexistence with a Wacom driver, which also installs a `wintab32.dll`.
13. Touch-capable Kamvas models (palm rejection).
14. Focus changes and modal dialogs during a stroke.

---

## 6. Technology comparison

### 6.1 Matrix

Cells are [C] where they cite facts and [I] where they are judgements.

| Criterion | **C#/.NET 10 + WPF + SkiaSharp** (candidate) | **C# + Avalonia 12** | **C++ + Qt 6** | **C# + WinUI 3 + Win2D** | C++ custom (SDL3/ImGui/Skia) |
|---|---|---|---|---|---|
| Windows support | Windows-only. .NET 10 LTS to 2028-11-14; .NET 8/9 ended 2026-11-10. WPF gets maintenance-level updates. | Win32 backend (also macOS/Linux). | Mature. **Qt 6.12 is the last version supporting Windows 10.** | Recommended "native" framework, but Windows App SDK 2.0 is only serviced until 2027-04-29. | What you build. |
| Huion: pressure, tilt, buttons | **Strong input:** pen-thread StylusPlugIns, pressure, tilt, twist, barrel, inverted. No WinTab (P/Invoke). | Pressure, tilt, twist, eraser, barrel in `PointerPointProperties`. `WM_POINTER` only, likely 1024-step pressure [I, from DLL inspection]. | `WM_POINTER` default (pressure/1024); WinTab through a private API; proven by Krita. | Full pointer properties; Ink only; InkCanvas only experimental. | Raw `WM_POINTER` and WinTab: full control. |
| Drawing performance | Skia CPU raster is fast; the bottleneck is the copy into WPF and DX9 composition. | Skia on GPU (ANGLE→D3D11) by default; a low-latency swap-chain composition mode exists. | QImage raster (thread-safe) or GPU via QRhi/GL. | Direct2D on the GPU. | Highest ceiling, most work. |
| Canvas: large images, zoom, pan | Needs a viewport-sized bitmap plus own tile cache and dirty rects. GPU via `SKGLElement` or D3DImage/HwndHost workarounds. Software fallback above max texture size. | Custom draw operation with leased `SKCanvas`/`GRContext`, so GPU tiles in the UI's own context; nearest-neighbour sampling for pixel art. | QRhiWidget/OpenGL plus own tiles (Krita's model). | Win2D virtual bitmaps tile automatically. | Own swap chain. |
| Raster layers (given vs build) | Given: surfaces, 29 blend modes, F16/F32, ICC. **Build:** document, tiles, undo, selection, brush engine, layered I/O. | Same, but tied to Avalonia's pinned **SkiaSharp 3.119.4**. | Given: QImage, composition modes. Build the rest. | Given: D2D effects and blends. Build the rest. | Everything. |
| UI flexibility / workspaces | **AvalonDock 5.0.2 (Ms-PL)** with XML/JSON layout serialisers; large control ecosystem. Avoid Xceed's toolkit (non-commercial licence). | **Dock 12.1.5 (MIT)** with JSON serialisers; smaller ecosystem. | QDockWidget + saveState; Qt-ADS (LGPL-2.1+); KDDockWidgets (GPL or commercial). | No first-party docking. | Build it. |
| Development complexity | Low–medium; the GPU path is the hard part. | Medium; XAML similar to WPF. | High: C++, CMake, deployment, LGPL compliance. | Medium, plus packaging friction. | Very high. |
| Testing | FlaUI (UIA3, MIT), Appium; headless Skia surfaces for engine pixel tests. | Headless platform with xUnit/NUnit and rendered-frame capture. | Qt Test (input simulation, benchmarks). | UI Automation tools. | Self-built. |
| Dependencies / long-term risk | WPF Windows-only and modestly funded; SkiaSharp 4.x moves fast; OpenTK only for the GL view. | Single vendor with paid add-ons (core MIT); older pinned SkiaSharp. | Qt Company policy (LTS patches commercial-first). | Microsoft churn; **Win2D licence unverified**. | Bus factor. |
| Licensing | SkiaSharp MIT, Skia BSD-3, WPF part of .NET (MIT), AvalonDock Ms-PL. Clean. | MIT core plus MIT Dock. Clean. | LGPLv3 obligations or commercial; some modules GPL-only. | Microsoft licence terms (Windows-only, indemnity clause). | MIT/Zlib/BSD. |

### 6.2 Reusable libraries

| Library | Licence [C] | Fit for DoodleNook [I] |
|---|---|---|
| **SkiaSharp 4.157.0** | MIT (Skia BSD-3) | Core rasteriser and compositor. Codecs: decodes PNG/JPEG/WebP/GIF/BMP/ICO/DNG and encodes PNG/JPEG/WebP. |
| **libmypaint 1.6.1** | ISC | Mature natural-media brush engine with a flat C API and callback-based surface; P/Invoke is feasible. Depends on glib (LGPL) and json-c, which must ship as replaceable DLLs. Optional later. |
| GEGL / babl | LGPLv3+ (plus about 50 GPLv3+ GEGL ops) | Poor fit: GObject/C, MSYS2 toolchain, heavy to bind from .NET. **Don't adopt just because GIMP does.** |
| libvips / NetVips | LGPL-2.1+ / MIT | Import, export and thumbnails for huge images; not a painting engine. |
| Magick.NET | Apache-2.0 (plus ImageMagick licence) | Format breadth: **PSD read/write, ORA and XCF read**. Large native footprint. |
| ImageSharp | Six Labors Split Licence | Paid above US$1M revenue; redundant with SkiaSharp. |
| LittleCMS | MIT | Only if a print or CMYK workflow appears. |
| OpenRaster (ORA) | open spec (licence not stated) | Simple zip + XML + PNG layered format, supported by Krita, GIMP and MyPaint. Good interchange or basis for a native format. |
| Aseprite format | spec on GitHub (not read) | AsepriteDotNet (MIT) reader exists; Aseprite itself is proprietary since 2016. |

---

## 7. Ten architectural lessons worth learning from GIMP

1. **Enforce engine/UI separation in the build.** GIMP's core directories include no GTK, and a console binary links none [C]. For DoodleNook: the engine is a plain .NET library with no WPF/Avalonia references, so the UI framework stays replaceable.
2. **Tiled, sparse, copy-on-write pixel storage** (GEGL's 128×128 tiles, shared zero tile, `gegl_tile_dup`). This one mechanism makes snapshots, undo, threading and partial updates cheap.
3. **Build each stroke as a mask over a pre-stroke snapshot** (CONSTANT mode). This gives Photoshop-like opacity behaviour, cheap cancel, and an undo record built from the snapshot.
4. **Paint on a worker thread and publish on a timer** (the paint thread plus 10 ms flush through a double buffer), so painting never writes pixels the compositor is reading.
5. **Express spacing in brush diameters**, carry leftover distance, and interpolate pressure and tilt per dab. This is the core of good-looking strokes at any speed.
6. **Dynamics as curves, plus a separate per-device pressure curve.** The device curve normalises the hardware; dynamics curves express artistic intent.
7. **Dirty regions everywhere, with lazy, viewport-first compositing.** Chunk-aligned invalidation, a projection validated on demand with the visible area first, a render cache, and pan by shifting the cached surface.
8. **Keep the view transform separate from image data.** Zoom, rotate and flip as a display matrix, with mipmaps for zoomed-out views. The image is never resampled for viewing.
9. **Undo as grouped region snapshots with swap-on-undo and a memory cap** (minimum levels plus a byte budget). This is simple and robust, and needs only a stroke's tiles.
10. **Make the tablet API a persisted user choice, and suppress OS pen gestures.** GIMP (Ink default) and Krita (WinTab default) both keep the other API as an escape hatch. GTK turns off press-and-hold, flicks and feedback per window.

## 8. Ten complexities to avoid copying

1. **The procedure database plus out-of-process plug-in IPC** (786 generated procedures, wire protocol with tile transport, `pluginrc`, Script-Fu, GObject Introspection bindings). Even XCF and every file format go through it.
2. **The precision and colour-space matrix**: 18 precisions × babl spaces, plus per-layer blend and composite spaces. Choose one working format; this is a foundational decision (§10).
3. **65 layer modes, including 22 legacy modes**, all also usable as paint modes. Start with Normal, Multiply, Screen, Overlay, Add, plus Erase; Skia covers the Photoshop core set.
4. **Deep, mirrored GObject class hierarchies** (about 616 types in `app/`; paint cores and tools each four or more levels deep). Prefer composition: one brush engine with pluggable mask, colour and blend parts.
5. **A second paint engine (libmypaint) plus specialist tools** (heal with a Laplace solver, perspective clone, ink blobs, smudge accumulator) in the first version.
6. **The brush-format zoo**: `.gbr/.gpb/.gih/.vbr/.abr/.jbr`, brush pipes, and blur-based hardness for bitmap brushes.
7. **The full dynamics matrix and stroke extras**: 11 outputs × 8 inputs, fade, gradient colour, symmetry, auto-expanding layers, lock-to-view.
8. **Generic dialog-factory and session machinery** (89 registered dialogs, single/multi-window strategies, `sessionrc`). Despite all of it, GIMP still has **no named workspace presets**. DoodleNook wants a few named layouts on a docking library.
9. **Historical concepts**: floating selections, quick mask, parasites, compatibility with 27 XCF versions, and the **Save (XCF) vs Export** split, which Photoshop users often find confusing [I].
10. **Input and paint pipeline choices that add latency or lose data**:
    - pressure reduced to 1024 steps;
    - sub-half-pixel filtering that drops pressure-only changes;
    - the 50 ms event-fill hold;
    - history disabled;
    - frame-clock motion holding;
    - an unbounded paint queue;
    - synchronous compositing on the UI thread under the paint lock.

---

## 9. Licensing considerations

**What the files say** [C]
- **GIMP:** the application is **GPLv3+**; libgimp and its sibling libraries are **LGPLv3+** (`LICENSE`, `COPYING`).
  - New files in `data/` are expected to be **CC0**; historical data has no clear licence.
  - Icons are CC BY-SA.
- **GEGL:** the library is LGPLv3+; the `gegl` binary is GPLv3+; about 50 operations in `operations/common-gpl3+` are **GPLv3+** and are always built.
- **babl:** LGPLv3+. **GTK:** LGPL-2.1+. **libmypaint:** ISC.
- The **XCF documentation** (developer.gimp.org) is licensed GPLv2+ *as documentation*.

**How the five scenarios differ** (legal review recommended):

| Scenario | Implication |
|---|---|
| **Studying open-source architecture** | Ideas, methods and systems are not protected by copyright (17 U.S.C. §102(b); WIPO Copyright Treaty Art. 2) [C principle]. Learning that GIMP uses tiles, COW undo or a stroke mask creates no obligation. Keep notes at concept level and don't transcribe code or comments. |
| **Implementing similar functionality independently** | Permitted. Good practice: never paste or port GPL code, keep provenance notes, and optionally separate people who read the source from people who implement. No official source *requires* clean-room separation [I]. |
| **Linking third-party libraries** | Permissive licences (MIT, BSD, ISC, Apache, Ms-PL): include notices. **LGPL** (GTK, glib, GEGL/babl, libvips, Qt): allowed in a non-GPL app if the library stays a **replaceable shared library**, you include notices and licence texts, provide or offer its source, and don't forbid reverse engineering to debug modifications (LGPLv3 §4). P/Invoke to a separate DLL fits; static or single-file bundling of LGPL code is risky. **GPL** libraries: linking makes the combined work GPL (GPL FAQ). |
| **Incorporating source directly** | Copying GIMP, Krita or GEGL-GPL code makes the **distributed whole GPLv3+**. Loading GPL code in-process counts as combining. |
| **Forking an application** | A GIMP fork is GPLv3+ and inherits C/GTK3/GEGL. That is legal (including selling, with source provided) but incompatible with the C# plan. |

**Practical guardrails** [I]
- No GIMP, Krita or GEGL code in the repository.
- LGPL components only as separate DLLs, and only if clearly worth it.
- A `THIRD-PARTY-NOTICES` file from day one.
- Don't bundle GIMP brushes or patterns unless each file's licence is clear.
- Implement ORA, PSD or XCF readers from specifications, not from GIMP's `app/xcf` code.
- Avoid the Xceed WPF toolkit (non-commercial licence). Check ImageSharp's revenue clause before using it.

---

## 10. Preliminary recommendations (provisional; not final decisions)

1. **Language and runtime: C# on .NET 10 LTS.** There is no evidence-based reason to leave C#. GIMP's value lies in its *ideas*, not its stack, and GEGL is a poor fit for .NET.

2. **Make the engine UI-independent.** Build a plain .NET library containing:
   - the document model;
   - copy-on-write tiled layers (128–256 px tiles);
   - the stroke engine (spacing, interpolation, dynamics curves, stroke mask over snapshot, eraser as a blend mode);
   - undo as tile snapshots;
   - SkiaSharp for rasterising and compositing;
   - the native file format.

   This keeps the WPF-versus-Avalonia decision reversible.

3. **Canvas and input: decide by measurement, with WPF as the first candidate.** WPF and Avalonia are strong in opposite places (Avalonia's input path is inferred from its shipped DLL, not documentation):
   - **WPF:** stronger input (pen thread, device-range values, real-time plug-ins, mature docking with AvalonDock); weaker rendering (CPU Skia → `WriteableBitmap` → DirectX 9 composition).
   - **Avalonia:** the reverse, with a GPU Skia canvas but likely 1024-step pressure delivered on the UI thread.

   Recommendation: a short, throwaway prototype of *only* "pen in → dab → screen" in WPF (`SKElement` with dirty rects, and one GPU variant), and then in Avalonia, both measured on your Huion. Qt/C++ remains the proven fallback if both disappoint.

4. **Tablet input plan:**
   - Use **Windows Ink first**, through WPF's stylus stack or raw `WM_POINTER`. Normalise against the device's real pressure range rather than a hard-coded 1024.
   - Put every input source behind one `PenSample` abstraction: position, raw and normalised pressure, tilt, optional twist, buttons, eraser, timestamp, source.
   - Disable OS pen gestures, keep coalesced samples, and offer an in-app pressure curve.
   - Plan WinTab as a later opt-in setting.
   - Treat barrel buttons and eraser as coming from several possible sources (§5.4).

5. **Workspaces:** a docking library with layout serialisation (AvalonDock or Dock) gives named saved layouts directly. GIMP shows that generic session machinery alone does not give users presets.

**Foundational decisions that are hard to change later**
- Working pixel format and colour stance: 8-bit premultiplied BGRA (simplest; matches Skia and WPF; good for pixel art) or 16-bit/float.
- Tile storage, copy-on-write, and the undo model.
- Engine threading model (worker thread, how results are published).
- Document model: layer groups and masks, even if they ship later.
- Native file format and its versioning. GIMP's "write the lowest version the features require" approach is worth adopting.
- UI framework and canvas presenter.
- The pen-input abstraction.
- Licence posture.

**Safe to add incrementally:**
- More brush types and dynamics inputs.
- Additional blend modes and filters.
- Selection and transform tool variants.
- PSD import/export.
- The WinTab path.
- Stabiliser options and symmetry.
- Text and vector layers.
- Scripting or plug-ins, if ever.
- Additional workspace presets.

---

## 11. Unresolved questions

**Pen input**
1. Your Huion model's real behaviour in Ink versus WinTab mode: pressure range and resolution, tilt sign and range, side-button delivery, history depth, report rate. Only hardware tests can answer this.
2. WPF on .NET 10: actual pressure resolution from the default stack for a Huion pen, and stylus-plugin latency in practice.
3. Avalonia's pen path: source not inspected (GitHub blocked). Whether pressure is really 1024-step, which thread input arrives on, and how coalescing works.

**Rendering**

4. End-to-end latency of WPF + `SKElement` with dirty rects, and whether `SKGLElement` (NV_DX_interop) is reliable on Intel, AMD and NVIDIA GPUs and over Remote Desktop.

**Engine and format**

5. Working pixel format and colour management (depends on your needs; see §12).
6. Tile size, memory budget, and undo memory accounting for shared tiles.
7. Native format: own versioned container, ORA-based, or both.
8. Whether to adopt libmypaint later. Its internals were not reviewed, and it brings an LGPL glib dependency.

**Licences**

9. Still unverified: Wacom WintabDN, Win2D, the Aseprite spec text, the OpenRaster spec, and GIMP's `gimp-data` brushes.
10. Legal review of the clean-room process and of any LGPL use.

---

## 12. Questions for the user

1. **Which Huion device and pen** (model number, pen display or tablet without screen)? Is "Enable Windows Ink" currently on, and what tablet setting do you use in Photoshop, Clip Studio Paint and Krita?
2. **Which Windows version, and what monitor setup** (number of monitors, scaling percentages, refresh rate)?
3. **What do your pen side buttons do today**? Do you ever use an eraser end or "Toggle Pen/Eraser"?
4. **Typical canvas sizes and layer counts**, for illustration and for game sprites. Is high-bit-depth or CMYK colour ever needed, or is 8-bit RGB enough?
5. **Which app's brush feel do you like best** (Photoshop, CSP, Krita, Aseprite), and do you rely on stroke stabilisation?
6. **Which file formats must DoodleNook exchange**: PSD round-trip, ORA, Aseprite, PNG sprite sheets?
7. **Which Photoshop shortcuts are muscle memory**? For example B/E/V/M/L, `[ ]` for brush size, Space-drag to pan, R to rotate the view, Alt for the eyedropper, Ctrl+T.
8. **Distribution intent**: personal use only, free/open source, or possibly commercial? This affects LGPL and Qt choices.
9. **Windows-only forever, or might macOS or Linux matter one day**? This affects WPF versus Avalonia.
10. For pixel art: **which Aseprite features are essential**? For example indexed palettes, pixel-perfect strokes, onion skin, animation.

---

## Appendix A: Preliminary product comparison framework (not filled in)

**Scoring proposal**
- For each cell, record: (a) how the app does it, in 1–2 lines; (b) the user's rating, 0 = dislike to 3 = love; (c) the decision: *Adopt*, *Simplify*, *Skip* or *Later*.
- Keep three separate flags: "familiar from Photoshop", "user actually uses", and "DoodleNook v1 relevance".

| Category | What to compare | How to evaluate | Photoshop | Clip Studio | Krita | Aseprite | GIMP |
|---|---|---|---|---|---|---|---|
| Sketching & drawing | Line quality, stroke feel, stabilisation, quick sketch flow | Same sketch in each app; user rating | | | | | |
| Brush tools | Brush set, size/opacity/flow controls, presets, custom tips | Inventory plus a "most used 5 brushes" exercise | | | | | |
| Tablet interaction | Pressure curve UI, tilt use, side buttons, eraser end, hover cursor | Same Huion, same tests (§5.5) | | | | | |
| Colour selection | Wheel/square, HSV/RGB, palettes, eyedropper behaviour | Task timing: pick, mix, reuse a colour | | | | | |
| Layers | Panel layout, blend modes used, groups, masks, clipping, lock | Inventory of what is actually used | | | | | |
| Selections | Marquee, lasso, magic wand, refine edge, quick mask | Common selection tasks | | | | | |
| Transformations | Free transform, warp, flip, rotate, pixel-safe transforms | Ctrl+T workflows; pixel-art scaling | | | | | |
| Canvas navigation | Zoom, pan, rotate view, flip view, overview/navigator | Gesture and shortcut inventory | | | | | |
| Interface customisation | Docking, floating panels, toolbar editing, themes | Rebuild the user's ideal layout | | | | | |
| Workspace presets | Named layouts, switching speed, per-task presets | Map to Pure Sketch / Quick Sketch / Painting / Editing / Pixel Art | | | | | |
| Keyboard shortcuts | Defaults, remapping, conflicts, muscle-memory set | List the user's top 20 shortcuts | | | | | |
| File formats | Native format, PSD, PNG/WebP export, ORA, Aseprite, sprite sheets | Round-trip test | | | | | |
| Pixel art | Pixel-perfect lines, indexed palettes, grid, onion skin, nearest zoom | Small sprite exercise | | | | | |
| General usability | Startup time, discoverability, clutter, undo, errors | Subjective rating plus friction notes | | | | | |

---

## Appendix B: Research material

The detailed notes behind this report are in the session scratchpad. Copy anything you want to keep, because routine cleanup can clear it.

- `/tmp/claude-0/-home-user-DoodleNook/df687d61-edb1-54dd-a35e-0e0e6f941aa1/scratchpad/research/notes/A-architecture.md`
- `/tmp/claude-0/-home-user-DoodleNook/df687d61-edb1-54dd-a35e-0e0e6f941aa1/scratchpad/research/notes/B-painting.md`
- `/tmp/claude-0/-home-user-DoodleNook/df687d61-edb1-54dd-a35e-0e0e6f941aa1/scratchpad/research/notes/C1-tablet-gtk-gimp.md`
- `/tmp/claude-0/-home-user-DoodleNook/df687d61-edb1-54dd-a35e-0e0e6f941aa1/scratchpad/research/notes/C2-windows-pen-apis.md`
- `/tmp/claude-0/-home-user-DoodleNook/df687d61-edb1-54dd-a35e-0e0e6f941aa1/scratchpad/research/notes/D-tech-licensing.md`

This completes Research Mission 001. I have not started another research phase or any application development.
