# Mission 011 — Under the Hood

Faster, more responsive and more reliable, without changing how DoodleNook feels. No new tools, no redesign, no new
workspace: the only new interface is **Help › Diagnostics…**. The method was *measure → identify → improve → measure
again*. Every optimisation that stayed is pixel-identical to Mission 010, and each was kept only after a before/after
measurement on the same machine. The measurements, method and tradeoffs are in [PERFORMANCE.md](PERFORMANCE.md).

**Status:** implemented and verified by automated tests on Linux and Windows CI and a smoke test of the packaged
`DoodleNook.exe`. Awaiting your playtest ([PLAYTEST.md](PLAYTEST.md)). Pen feel on the Kamvas 13 is unverified: every
test used a mouse or simulated input.

## Key results

Measured on the same machine, before (M010) and after (M011); details, method and caveats in
[PERFORMANCE.md](PERFORMANCE.md).

| Moment | M010 | M011 |
|---|---|---|
| Ctrl+Z after a stroke on a 2560 × 1440 picture with 12 layers | 196–258 ms | 35–90 ms |
| A 300 px soft brush on the top layer of 12, per pen event (p95) | 4.5 ms (10.8) | 1.4 ms (3.8) |
| Saving that picture: time the window is held / whole save | 421 / 1,825 ms | 117 / 1,012 ms |
| Opening it | 742 ms | 509 ms |
| Undo memory after 30 big strokes | 310 MB | 195 MB |
| Game Edit with 300 PNGs: longest pause while thumbnails load | 1,021 ms | 95 ms |
| Listeners reacting to every change after 180 tool changes | 55 (growing) | 24 (steady) |

Thin strokes are a few hundredths of a millisecond per pen event in both versions (4–13% quicker head to head).
Every picture DoodleNook draws is identical to Mission 010, pixel for pixel.

## What changed

### 1. Undo and redo (engine)

Undoing a stroke swapped back only the tiles the stroke changed, but then recomposited the whole rectangle around those
tiles. For a diagonal or curving stroke across a big picture, that rectangle is most of the picture: on a 2560 × 1440
picture with 12 layers one Ctrl+Z took about a quarter of a second. Undo and redo now recomposite just the changed tiles
(joined into runs along each row) and tell the screen about them once. Every pixel ends up exactly as before; a test
compares the projection with a full recomposite after each undo and redo.

### 2. Compositing arithmetic (engine)

"Source over" and the opacity fade, the operations behind every layer, brush dab, onion skin and export, now work on
two colour channels at a time in 16-bit lanes. Each lane does exactly the old per-channel rounding, so the results are
bit-for-bit the same. A test compares the new and old formulas for every alpha and channel value, plus two million
random pixel pairs. Plain layers (Normal mode, no mask, no clipping) use a tight loop. Blend modes, masks, clipping,
groups and adjustment layers take the same code paths as before.

### 3. Painting over many layers (engine)

While a layer is painted, everything under it (paper, onion skins, the layers below with their groups, masks and
adjustments) composes to the same pixels on every pen event. For big brushes the document now keeps that part per 64 px
tile and composes only the painted layer and the layers above it. Rules that keep it exact and safe:

- **Where it applies:** only a top-level paint layer (not inside a group), only when nothing floats over the picture or
  previews it, and only for pen events that change at least a 128 × 128 area (big brushes). Thin lines compose directly,
  which measured quicker for them.
- **When it is dropped:** any other recomposite (undo, a property or structure change, another layer, a frame change,
  onion skins, a resize). It is never stale.
- **Why it is exact:** both halves run the compositor's own loop, split at the layer.
- **Tests:** they compare the projection with a full composite after every single pen event, with groups, clipping,
  adjustments, masks, onion skins, a palette lock and switches between layers.
- **Memory:** one buffer the size of the picture (14 MB at 2560 × 1440), kept with the document while painting with
  big brushes on a layer that has at least two layers below it.

### 4. Saving, autosave and PNG (engine)

- **The save snapshot no longer recomposites.** Saving (and autosave) copies the picture on the UI thread and compresses
  it in the background. That copy used to include composing every layer again just for the file's thumbnail. When
  nothing is drawn over the picture (onion skins, floating pixels, previews), the on-screen composite already is exactly
  that picture, so it is copied instead. The thumbnail is byte for byte the same (tested), and the window stays
  responsive for much less time on save.
- **The PNG encoder and decoder run one loop per row filter.** They used to switch per byte. The same filter choice and
  the same bytes come out (the M010 hashes are tests). This speeds up `.dnk` saving and opening, PNG export and Game Edit
  saves. Data safety is unchanged: temporary file, then replace; edits made during a background save stay unsaved.

### 5. Undo memory (engine)

An undo step keeps the original pixels of each changed 64 px tile. A tile that was all one value (typically fully
transparent: the untouched part of a new layer under a line of ink, or a flat background) now keeps just that value
instead of 16 KB. Undo and redo restore exactly the same pixels. The history's memory figure is now computed from what
the steps actually hold, so its 1 GiB / 200-step limits apply to real memory.

### 6. Listeners that piled up (app)

Every change of tool rebuilds the options bar. The brush chip's pop-over brush panel, the stamp and pattern pickers and
the pixel-art zoom group stayed subscribed to the session for the rest of the run. After a few hundred tool changes,
every size or colour change ran dozens of invisible panels, and they stayed in memory. Panels in pop-overs (brush,
colour, stamps and patterns, the quick palette's brush picker) and the zoom group now listen only while they are on
screen, and refresh when they come back. A test changes tools and workspaces repeatedly and checks that the listener
counts stay the same.

### 7. Game Edit catalog (app)

Each arriving thumbnail refreshed every tile in the catalog, so loading N thumbnails did N × N tile updates on the UI
thread. Now each thumbnail refreshes only its own tile.

### 8. Smaller things (app)

- **Marching ants on slow screens.** The ants redraw the whole canvas every 120 ms. With software rendering (no GPU, e.g.
  remote desktop) and a big picture, one frame can take longer than that, and the redraws then followed each other back
  to back. The ants now slow down only when frames are slow (at most a quarter of the renderer's time). With a GPU they
  march exactly as before.
- **The brush previews cache is bounded.** It keeps one preview per brush and ink colour, so over a long session with
  many colours it grew without limit. It now starts afresh past 400 entries.

### 9. Help › Diagnostics…

A small window from the Help menu (in Quick Sketch: the ☰ menu). It shows:

- **DoodleNook:** version, runtime, system and how the canvas is drawn (GPU or software).
- **Picture:** size, layers by kind, frames and drawings, palette lock, the memory the picture's pixels take, the undo
  history (steps and memory against its limits), the painting cache and the frame cache.
- **Memory:** managed memory and heap, working set (now and peak), garbage collections.
- **Recent timings:** canvas frames, the last stroke's pen events, and the last open, save, autosave, undo and redo.
  Each appears only once it has happened.
- **Caches:** the creative library, uploads to the screen and Game Edit thumbnails.

**Copy diagnostics** copies the same numbers as text: no artwork, no file names, no folders. The values refresh every
second while the window is open and stop when it closes. While it is closed, the only cost is two clock readings around
work that happens anyway (a canvas frame, a save, an undo). Nothing is written to disk or sent anywhere, and nothing is
added to the canvas.

## Investigated and deliberately left alone

- **Blend modes other than Normal** (Multiply, Screen, Overlay…) use floating-point formulas. A faster version would not
  be bit-identical, so they were left as they are. The painting cache removes most of their cost per pen event anyway.
- **Undo limits (200 steps, 1 GiB) and the animation frame cache (512 MB)** stay as they are. Lowering them would mean
  fewer undo steps or slower playback. Their memory is now visible in Diagnostics.
- **Animation drawings are full-picture images.** A sparse, tiled storage would save memory in big animations, but it
  is an engine redesign outside this mission's scope. It is measured in [PERFORMANCE.md](PERFORMANCE.md).
- **GIF export and the palette remap analysis** run in the background or inside a dialog and were not changed.
- **Software rendering in the headless tests** costs about 130 ms per frame for a 1920 × 1080 picture. That is
  Skia drawing without a GPU; the Windows app normally uses the GPU texture path.
- **Replaced pictures:** an investigation found no picture kept after it is replaced. A test checks this.

## Verification

- **Pixel equivalence:** 33 scenarios are hashed and compared with hashes recorded from the Mission 010 engine
  (commit 702aa2f). They cover:
  - every brush kind, erasing, transparency lock and a selection;
  - opacity, blend modes, clipping, groups, masks and adjustment layers;
  - palette lock and remap, undo and redo;
  - animation frames, onion skins, GIF and sprite sheets;
  - smudge, clone, repair, liquify and perspective;
  - PNG bytes.
- **Exactness of the new arithmetic:** exhaustive and randomised comparisons with the old formulas.
- **Painting cache:** a full composite compared after every pen event in many configurations.
- **Undo, save thumbnail, listeners, released pictures, Diagnostics (with Copy):** dedicated tests.
- **Smoke test section 16 (packaged app):**
  - painting over five layers shows exactly the full composite;
  - undo restores the earlier composite;
  - tool changes add no listeners;
  - Diagnostics reports without folders.
- **Full suites:** engine and app tests, and the existing performance tests with generous limits.
- **Benchmarks:** `UnderTheHoodBenchmarks` (engine) and `UnderTheHoodAppBenchmarks` (window). They write JSON to
  `tests/*/out/`; the before/after files are in [measurements/](measurements/).

## Delivery

Branch `iteration/011-under-the-hood`, started from Mission 010's final commit (702aa2f). The Windows build is the
artifact **DoodleNook-win-x64** of the newest green run of
[Windows build on this branch](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F011-under-the-hood).
The Mission 011 benchmarks run in their own CI step after the tests; their JSON is in the run's **test-results**
artifact.

<!-- CI-RESULTS -->
