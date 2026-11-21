# Mission 011 — Performance report

All figures below were measured, none estimated. "M010" is the code of Mission 010's final commit (702aa2f), and
"M011" this mission's code. Both ran the **same benchmark files** on the **same machine**, one after the other, so the
comparison is like for like. The raw JSON of every run is in [measurements/](measurements/).

## 1. Environment

| | |
|---|---|
| Machine | Cloud Linux VM, 4 logical processors (Intel Xeon @ 2.10 GHz), 15 GB RAM, no GPU |
| OS / runtime | Linux 6.18, .NET SDK 10.0.404 (runtime 10.0.15), Release build, workstation GC |
| UI | Avalonia 12.1.6 on its headless platform, with Skia drawing in memory (software, no GPU) |
| Secondary data | Windows CI (Windows Server 2025, 4 logical processors, Release): the same engine benchmark on M011 ([measurements/m011-engine-windows-ci-run28.json](measurements/m011-engine-windows-ci-run28.json)) and the packaged app's smoke test |

The VM is shared, so single runs vary by up to about ±20%. Each engine figure is the mean of two paired rounds
(M010, M011, M010, M011). Within a round, each stroke is drawn three times and the median run is reported. The engine
benchmark warms up with three seconds of strokes before measuring. A shorter warm-up left the first measurements
half-compiled; see section 6.

## 2. Method

**Measure → identify → improve → measure again.** Each change was kept only if two things held:

1. **Correctness.** It is pixel-identical to M010. 33 hashed scenarios recorded from the M010 engine are a test.
2. **Benefit.** It measured better on the workloads it targets.

Two changes were adjusted after measuring:

- **The painting cache** is now used only for big pen events, after it made thin strokes over many layers slower.
- **An "optimise immediately" attribute was removed from the PNG loops.** It made warmed-up saving slower.

Benchmarks (in the repository, runnable with `dotnet test -c Release --filter`):

- `UnderTheHoodBenchmarks` (engine) covers:
  - pen events (average and high percentiles, allocations), undo, full compositing;
  - masks, groups and adjustments; palette lock and remap; pixel art;
  - animation (frame changes, onion skins, exports, GIF);
  - saving and opening; undo memory; Liquify memory;
  - a 400-stroke session; a big creative library.
- `HeadToHeadBenchmarks` (engine): thin brushes and saving/PNG in a long-warmed process. These costs are too small or
  too sensitive to warm-up for the long run.
- `UnderTheHoodAppBenchmarks` (the real window, headless) covers:
  - how many listeners react to every change, and whether that grows;
  - frame times with and without a selection;
  - pen events through the canvas input, undo and redo;
  - replacing pictures;
  - Game Edit Mode with 300 PNGs.

"Cold" is the first run in a process (it includes compiling code), and "warm" the median of the following runs. The
documents are synthetic but realistic: broad painted strokes on every layer, a few layers at reduced opacity or in
Multiply, one clipped.

## 3. Workloads

| Area | Documents |
|---|---|
| Painting | 1920 × 1080 with 1 and 3 layers; 2560 × 1440 with 12 layers (the active layer in the middle or on top); pencil with grain, 24 px ink, 120 px hard, 300 px soft, charcoal (image tip), leaves (colour tip) |
| Masks and adjustments | 1920 × 1080: four layers, two in a group, a painted mask, a Hue/Saturation adjustment on top |
| Palette lock | 1920 × 1080, three layers, locked to the 32-colour palette before painting; a remap of an unlocked picture |
| Pixel art | 64 × 64 and 256 × 256, pixel pencil (1–3 px), undo, PNG |
| Animation | 1920 × 1080 × 21 frames (a held background, a drawing per frame); 1920 × 1080 × 48 frames with the character on twos |
| Files | 2560 × 1440 with 12 layers: save (UI-thread copy and the whole save), open, PNG |
| Memory | 30 big strokes on 2560 × 1440 × 12; 60 ink strokes on a new layer; 5 Liquify sessions; a 400-stroke session with undo, redo and layers added and removed |
| Window | a 1280 × 800 window; 180 tool changes and 18 workspace switches; 24 pictures replaced; Game Edit with 300 PNGs (32, 128 and 512 px) |

## 4. What was optimised (and why)

| # | Bottleneck found | Change | Kept because |
|---|---|---|---|
| 1 | Undo/redo recomposited the whole rectangle around a stroke's tiles (most of a big picture for a diagonal stroke) | Recomposite only the changed tiles, in row runs | Undo 60–80% faster on layered pictures; identical pixels |
| 2 | Per-channel source-over and fade in every compositing loop | Exact two-channel (16-bit lane) arithmetic; a tight loop for plain Normal layers | Full composites 5–30% faster; bit-exact (exhaustively tested) |
| 3 | Every pen event recomposited all layers under the brush | Cache what is under the painted layer per tile, for big pen events only | Big brushes over many layers up to ~70% faster per event; identical pixels |
| 4 | Saving recomposited every layer on the UI thread for the file's thumbnail | Copy the up-to-date projection when nothing extra is shown | UI-thread part of saving ~70% shorter; identical thumbnail |
| 5 | PNG filters switched per byte | One loop per filter | Saving and opening faster; identical bytes |
| 6 | Undo kept a full copy of tiles that were all one value (usually transparent) | Keep that one value | Undo memory 20–40% lower in the measured cases |
| 7 | Pop-over panels rebuilt with the options bar stayed subscribed forever | Listen only while on screen | Listener counts stay flat; each size change about 45% cheaper; heap growth over 180 tool changes 16 → 3 MB |
| 8 | Game Edit refreshed every tile for every arriving thumbnail (quadratic) | Refresh only that tile | 300 PNGs: longest UI stall 1.0 s → 0.1 s |
| 9 | Marching ants redrew the canvas every 120 ms even when a frame took longer (software rendering) | Pace the ants by the measured frame time (unchanged with a GPU) | No back-to-back redraws on slow screens |
| 10 | The brush preview cache grew with every ink colour | Bounded (400 entries) | Bounded memory |

## 5. Before and after at a glance

The moments the owner would notice, from the tables below:

| Moment | M010 | M011 | Change |
|---|---|---|---|
| Ctrl+Z after a stroke, 2560 × 1440, 12 layers (engine) | 196–258 ms | 35–90 ms | −62 to −83% |
| Ctrl+Z in the window, same picture, 24 px stroke | 212 ms | 43 ms | −80% |
| Big soft brush (300 px) on the top layer of 12, per pen event | 4.5 ms (p95 10.8) | 1.4 ms (p95 3.8) | −70% |
| Big soft brush on the middle layer of 12, per pen event | 5.1 ms (p95 12.7) | 3.0 ms (p95 7.9) | −40% |
| Saving: time the window is held (2560 × 1440, 12 layers) | 421 ms | 117 ms | −72% |
| Saving: whole save in the background | 1,825 ms | 1,012 ms | −45% |
| Opening that picture | 742 ms | 509 ms | −31% |
| Recompositing the whole picture (12 layers) | 313–329 ms | 250–258 ms | −20 to −22% |
| Undo memory after 30 big strokes (12 layers) | 310 MB | 195 MB | −37% |
| Game Edit, 300 PNGs: longest UI stall while thumbnails load | 1,021 ms | 95 ms | −91% |
| Listeners after 180 tool changes (state / library) | 55 / 63, growing | 24 / 2, steady | — |
| One size change (slider or quick palette drag) after a session | 3.4 ms | 1.9 ms | −46% |
| Thin strokes, per pen event (head to head) | 0.020–0.055 ms | 0.019–0.048 ms | −4 to −13% |

## 6. Results in detail

### Engine: final code, mean of two paired rounds (rounds 7 and 8)

### Pen events (engine, per event of 2 coalesced samples)

| Picture · brush | M010 avg / p95 | M011 avg / p95 | Change avg / p95 |
|---|---|---|---|
| 1920x1080, 1 layer · 6 px pencil (grain) | 0.022 / 0.040 ms | 0.019 / 0.033 ms | -11% / -18% |
| 1920x1080, 1 layer · 24 px ink | 0.033 / 0.055 ms | 0.025 / 0.041 ms | -25% / -25% |
| 1920x1080, 1 layer · 120 px hard | 0.177 / 0.318 ms | 0.134 / 0.226 ms | -25% / -29% |
| 1920x1080, 1 layer · 300 px soft | 0.691 / 2.027 ms | 0.508 / 1.527 ms | -27% / -25% |
| 1920x1080, 3 layers (top active) · 6 px pencil (grain) | 0.030 / 0.053 ms | 0.028 / 0.045 ms | -8% / -15% |
| 1920x1080, 3 layers (top active) · 24 px ink | 0.052 / 0.087 ms | 0.051 / 0.080 ms | -4% / -9% |
| 1920x1080, 3 layers (top active) · 120 px hard | 0.494 / 0.846 ms | 0.424 / 0.698 ms | -14% / -18% |
| 1920x1080, 3 layers (top active) · 300 px soft | 1.548 / 4.840 ms | 1.123 / 3.337 ms | -27% / -31% |
| 2560x1440, 12 layers (middle active) · 24 px ink | 0.146 / 0.227 ms | 0.139 / 0.217 ms | -5% / -4% |
| 2560x1440, 12 layers (middle active) · 120 px hard | 1.286 / 2.043 ms | 1.143 / 2.038 ms | -11% / -0% |
| 2560x1440, 12 layers (middle active) · 300 px soft | 5.053 / 12.721 ms | 3.038 / 7.863 ms | -40% / -38% |
| 2560x1440, 12 layers (top active) · 24 px ink | 0.157 / 0.259 ms | 0.119 / 0.183 ms | -24% / -29% |
| 2560x1440, 12 layers (top active) · 120 px hard | 1.248 / 2.104 ms | 1.014 / 1.631 ms | -19% / -23% |
| 2560x1440, 12 layers (top active) · 300 px soft | 4.535 / 10.800 ms | 1.365 / 3.814 ms | -70% / -65% |
| 1920x1080, 3 layers · Charcoal (charcoal) | 0.311 / 0.459 ms | 0.327 / 0.538 ms | +5% / +17% |
| 1920x1080, 3 layers · Leaves (leaves) | 0.141 / 0.177 ms | 0.108 / 0.180 ms | -23% / +1% |
| masks+group+adjustment 1920x1080 · 24 px ink | 0.115 / 0.160 ms | 0.099 / 0.168 ms | -14% / +5% |
| masks+group+adjustment 1920x1080 · 300 px soft | 1.856 / 5.861 ms | 1.238 / 3.653 ms | -33% / -38% |
| palette-locked 1920x1080, 3 layers · 24 px ink | 0.041 / 0.067 ms | 0.050 / 0.088 ms | +20% / +31% |
| palette-locked 1920x1080, 3 layers · 300 px soft | 1.369 / 4.944 ms | 0.857 / 3.021 ms | -37% / -39% |

### Undo of one stroke (engine)

| Picture · brush | M010 | M011 | Change |
|---|---|---|---|
| 1920x1080, 1 layer · 6 px pencil (grain) | 6.42 ms | 1.28 ms | -80% |
| 1920x1080, 1 layer · 24 px ink | 7.39 ms | 1.25 ms | -83% |
| 1920x1080, 1 layer · 120 px hard | 9.22 ms | 2.66 ms | -71% |
| 1920x1080, 1 layer · 300 px soft | 11.6 ms | 3.87 ms | -67% |
| 1920x1080, 3 layers (top active) · 6 px pencil (grain) | 30.6 ms | 7.30 ms | -76% |
| 1920x1080, 3 layers (top active) · 24 px ink | 30.5 ms | 7.38 ms | -76% |
| 1920x1080, 3 layers (top active) · 120 px hard | 35.6 ms | 13.4 ms | -62% |
| 1920x1080, 3 layers (top active) · 300 px soft | 43.4 ms | 21.0 ms | -52% |
| 2560x1440, 12 layers (middle active) · 24 px ink | 196 ms | 37.8 ms | -81% |
| 2560x1440, 12 layers (middle active) · 120 px hard | 224 ms | 56.9 ms | -75% |
| 2560x1440, 12 layers (middle active) · 300 px soft | 258 ms | 89.7 ms | -65% |
| 2560x1440, 12 layers (top active) · 24 px ink | 204 ms | 34.7 ms | -83% |
| 2560x1440, 12 layers (top active) · 120 px hard | 226 ms | 61.1 ms | -73% |
| 2560x1440, 12 layers (top active) · 300 px soft | 232 ms | 88.7 ms | -62% |
| 1920x1080, 3 layers · Charcoal (charcoal) | 36.4 ms | 8.10 ms | -78% |
| 1920x1080, 3 layers · Leaves (leaves) | 34.9 ms | 9.05 ms | -74% |
| masks+group+adjustment 1920x1080 · 24 px ink | 53.8 ms | 13.2 ms | -76% |
| masks+group+adjustment 1920x1080 · 300 px soft | 59.8 ms | 27.5 ms | -54% |
| palette-locked 1920x1080, 3 layers · 24 px ink | 16.1 ms | 4.33 ms | -73% |
| palette-locked 1920x1080, 3 layers · 300 px soft | 24.5 ms | 10.5 ms | -57% |

### Compositing the whole picture (engine, warm median)

| Picture | M010 | M011 | Change |
|---|---|---|---|
| recomposite everything · 1920x1080, 1 layer | 11.0 ms | 5.45 ms | -50% |
| flatten · 1920x1080, 1 layer | 13.6 ms | 9.20 ms | -32% |
| recomposite everything · 1920x1080, 3 layers (top active) | 47.9 ms | 38.3 ms | -20% |
| flatten · 1920x1080, 3 layers (top active) | 50.5 ms | 54.6 ms | +8% |
| recomposite everything · 2560x1440, 12 layers (middle active) | 329 ms | 258 ms | -22% |
| flatten · 2560x1440, 12 layers (middle active) | 327 ms | 254 ms | -22% |
| recomposite everything · 2560x1440, 12 layers (top active) | 313 ms | 250 ms | -20% |
| flatten · 2560x1440, 12 layers (top active) | 334 ms | 256 ms | -23% |
| recomposite everything · masks+group+adjustment 1920x1080 | 75.8 ms | 60.0 ms | -21% |

### Saving and opening (engine, 2560 × 1440, 12 layers)

| Step | M010 | M011 | Change |
|---|---|---|---|
| Save: copy on the UI thread (cold) | 422 ms | 110 ms | -74% |
| Save: copy on the UI thread (warm) | 421 ms | 117 ms | -72% |
| Save to disk, whole (warm) | 1,825 ms | 1,012 ms | -45% |
| Open (cold) | 754 ms | 527 ms | -30% |
| Open (warm) | 742 ms | 509 ms | -31% |
| PNG encode of the flattened picture (warm) | 805 ms | 724 ms | -10% |
| PNG decode (warm) | 77.5 ms | 49.7 ms | -36% |
| File size | 7.80 MB | 7.80 MB | +0% |

### Animation (engine)

| Workload | M010 | M011 | Change |
|---|---|---|---|
| 21 frames: frame change, first visit (avg) | 13.6 ms | 8.06 ms | -41% |
| 21 frames: frame change, first visit (p95) | 16.5 ms | 10.3 ms | -37% |
| 21 frames: frame change, cached (avg) | 0.889 ms | 0.922 ms | +4% |
| 21 frames: onion skins 2 + 2 (warm) | 12.4 ms | 8.85 ms | -29% |
| 21 frames: pen event on a frame (avg) | 0.030 ms | 0.034 ms | +15% |
| 21 frames: undo a stroke on a frame | 9.45 ms | 2.21 ms | -77% |
| 21 frames: render all frames for export (warm) | 61.4 ms | 121 ms | +97% |
| 21 frames: GIF encode | 2,034 ms | 2,152 ms | +6% |
| 21 frames: frame cache | 166 MB | 166 MB | +0% |
| 48 frames on twos: frame change, first visit (avg) | 6.55 ms | 5.09 ms | -22% |
| 48 frames on twos: save copy on the UI thread (warm) | 11.3 ms | 8.60 ms | -24% |
| 48 frames on twos: .dnk write | 242 ms | 276 ms | +14% |
| 48 frames on twos: .dnk read | 154 ms | 148 ms | -4% |
| 48 frames on twos: managed memory after building | 413 MB | 396 MB | -4% |
| 48 frames on twos: frame cache | 380 MB | 380 MB | +0% |

### Pixel art, palette and library (engine)

| Workload | M010 | M011 | Change |
|---|---|---|---|
| 64 × 64: pixel pencil event (avg) | 0.009 ms | 0.007 ms | -24% |
| 64 × 64: undo + redo | 0.045 ms | 0.015 ms | -67% |
| 64 × 64: PNG encode (warm) | 0.650 ms | 0.550 ms | -15% |
| 256 × 256: pixel pencil event (avg) | 0.008 ms | 0.007 ms | -6% |
| 256 × 256: undo + redo | 0.455 ms | 0.150 ms | -67% |
| 256 × 256: PNG encode (warm) | 4.70 ms | 3.30 ms | -30% |
| Palette remap: analyse (1920 × 1080, 3 layers) | 229 ms | 243 ms | +6% |
| Palette remap: apply and lock | 112 ms | 81.7 ms | -27% |
| Library with 200 brushes, 100 stamps, 60 patterns: load (warm) | 1.50 ms | 1.70 ms | +13% |

### Memory (engine)

| Workload | M010 | M011 | Change |
|---|---|---|---|
| Undo history after 30 big strokes (2560 × 1440, 12 layers) | 310 MB | 195 MB | -37% |
| Undo all 60 steps | 11,360 ms | 4,699 ms | -59% |
| Redo all 60 steps | 12,404 ms | 5,037 ms | -59% |
| Undo history: 60 ink strokes on a new layer | 30.9 MB | 24.1 MB | -22% |
| Undo memory per ink stroke | 526 KB | 411 KB | -22% |
| Liquify: managed memory left after 5 cancelled sessions | 2.70 MB | 2.70 MB | +0% |
| Long session: whole stroke (avg) | 72.7 ms | 53.8 ms | -26% |
| Long session: whole stroke (p95) | 205 ms | 143 ms | -30% |
| Long session: managed heap after 0 strokes | 45.9 MB | 40.9 MB | -11% |
| Long session: managed heap after 100 strokes | 339 MB | 307 MB | -9% |
| Long session: managed heap after 200 strokes | 632 MB | 572 MB | -9% |
| Long session: managed heap after 300 strokes | 648 MB | 597 MB | -8% |
| Long session: managed heap after 400 strokes | 647 MB | 595 MB | -8% |
| Benchmark process: working set at the end | 1,099 MB | 973 MB | -11% |
| Benchmark process: peak working set | 3,890 MB | 2,306 MB | -41% |

### Window (headless, one paired run)

The listener counts are the number of handlers attached to the session's change events: **StateChanged** (every
tool, size or colour change), **LibraryChanged** and **DocumentReplaced**. The last column is the time of one size
change with all of them attached.

| When | M010 listeners (state / library / document) | M011 listeners | One size change |
|---|---|---|---|
| At start | 22 / 7 / 13 | 17 / 2 / 13 | 10.1 ms → 5.99 ms |
| After 120 tool changes | 32 / 37 / 13 | 17 / 2 / 13 | 2.62 ms → 1.55 ms |
| After 18 workspace switches | 50 / 48 / 22 | 24 / 2 / 22 | 3.47 ms → 2.07 ms |
| After 60 more tool changes | 55 / 63 / 22 | 24 / 2 / 22 | 3.44 ms → 1.85 ms |

| Workload | M010 | M011 | Change |
|---|---|---|---|
| Managed heap growth over 180 tool changes | 16.1 MB | 3.10 MB | -81% |
| Workspace switch (avg) | 175 ms | 167 ms | -5% |
| Workspace switch (p95) | 364 ms | 348 ms | -4% |
| Frame, no selection | 135 ms | 138 ms | +3% |
| Frame with a rectangle selection | 2,869 ms | 131 ms | -95% |
| Frame with an ellipse selection | 2,975 ms | 131 ms | -96% |
| Pen event in the window, 2560x1440, 12 layers · 24 px (avg) | 0.215 ms | 0.192 ms | -11% |
| Pen event in the window, 2560x1440, 12 layers · 24 px (p95) | 0.340 ms | 0.297 ms | -13% |
| Undo in the window, 2560x1440, 12 layers · 24 px | 212 ms | 43.3 ms | -80% |
| Redo in the window, 2560x1440, 12 layers · 24 px | 220 ms | 38.1 ms | -83% |
| Pen event in the window, 2560x1440, 12 layers · 300 px soft (avg) | 5.04 ms | 3.37 ms | -33% |
| Pen event in the window, 2560x1440, 12 layers · 300 px soft (p95) | 11.6 ms | 8.26 ms | -29% |
| Undo in the window, 2560x1440, 12 layers · 300 px soft | 241 ms | 96.0 ms | -60% |
| Redo in the window, 2560x1440, 12 layers · 300 px soft | 249 ms | 104 ms | -58% |
| Pen event in the window, 1920x1080, 3 layers · 24 px (avg) | 0.082 ms | 0.069 ms | -16% |
| Pen event in the window, 1920x1080, 3 layers · 24 px (p95) | 0.146 ms | 0.126 ms | -14% |
| Undo in the window, 1920x1080, 3 layers · 24 px | 33.9 ms | 10.6 ms | -69% |
| Redo in the window, 1920x1080, 3 layers · 24 px | 37.5 ms | 11.5 ms | -69% |
| Managed heap after replacing 12 pictures | 34.9 MB | 26.0 MB | -26% |
| Managed heap after replacing 24 pictures | 71.3 MB | 53.5 MB | -25% |
| Game Edit, 300 PNGs: open with all thumbnails | 1,229 ms | 330 ms | -73% |
| Game Edit: reload all thumbnails | 1,034 ms | 381 ms | -63% |
| Game Edit: UI thread busy while reloading | 1,021 ms | 143 ms | -86% |
| Game Edit: longest single UI stall | 1,021 ms | 94.8 ms | -91% |
| Game Edit: managed heap | 45.9 MB | 50.0 MB | +9% |
| First window (cold, includes compiling) | 1,861 ms | 1,810 ms | -3% |

Notes on the window figures:

- **Frames with a selection.** In the headless tests, the canvas is drawn by Skia without a GPU (about 130 ms per
  frame for a 1920 × 1080 picture). M010's marching ants asked for a redraw every 120 ms, so redraws followed each other
  back to back (2.9 s per captured frame). On Windows with a GPU, a frame takes about 1.7 ms (the packaged app's smoke
  test reports a 1.74 ms average canvas frame), so the ants there are unchanged.
- **Replacing pictures.** The heap figures still grow in this long benchmark method in both versions. A dedicated probe
  found no picture kept after replacement: a fresh process with all workspaces visited, 20 painted 1920 × 1080 pictures
  replaced, and the managed heap stayed at 31.9 MB. A window test checks with weak references that replaced pictures
  are released. The remaining growth here is attributed to the benchmark itself (one long test method holding
  temporaries), not to DoodleNook. This was not investigated further.

### Head to head (engine, long warm-up)

Thin brushes cost a few hundredths of a millisecond per pen event, which is close to the long benchmark's
resolution on this VM. Saving and PNG depend strongly on whether the runtime has finished optimising the code. So these
are measured after a four-second warm-up, 15 strokes or 5–41 repetitions each, median per round. Thin strokes: rounds
5–8. Saving and PNG: rounds 7–8, with the final PNG code.

| Workload | Rounds | M010 | M011 | Change |
|---|---|---|---|---|
| Thin stroke, 1 layer: 24 px ink (per pen event) | 4 | 0.0267 ms | 0.0246 ms | -8% |
| Thin stroke, 1 layer: 6 px pencil (per pen event) | 4 | 0.0198 ms | 0.0192 ms | -4% |
| Thin stroke, 3 layers: 24 px ink (per pen event) | 4 | 0.0547 ms | 0.0476 ms | -13% |
| Thin stroke, 3 layers: 6 px pencil (per pen event) | 4 | 0.0295 ms | 0.0274 ms | -7% |
| save .dnk, 1920 × 1080 animation, 24 drawings | 2 | 1,815 ms | 1,144 ms | -37% |
| save .dnk, 2560 × 1440 picture, 6 layers | 2 | 1,042 ms | 537 ms | -48% |
| PNG encode 256 × 256 sprite | 2 | 4.16 ms | 2.84 ms | -32% |
| PNG decode 256 × 256 sprite | 2 | 1.02 ms | 0.96 ms | -6% |
| PNG encode 2560 × 1440 picture | 2 | 717 ms | 626 ms | -13% |
| PNG decode 2560 × 1440 picture | 2 | 88 ms | 61 ms | -31% |

### The earlier missions' performance tests, paired on this machine

The `PerformanceTests` from Mission 005 onwards, run alone in a fresh process, three times each, alternating versions.

| Picture · measurement (Mission 004–010 `PerformanceTests`, median of 3 paired runs) | M010 | M011 | Change |
|---|---|---|---|
| 1920 × 1080, 1 layer · 6 px pencil (per pen event) | 0.121 ms | 0.098 ms | -19% |
| 1920 × 1080, 1 layer · 24 px ink (per pen event) | 0.142 ms | 0.110 ms | -23% |
| 1920 × 1080, 1 layer · 120 px hard (per pen event) | 0.325 ms | 0.219 ms | -33% |
| 1920 × 1080, 1 layer · 300 px soft (per pen event) | 0.808 ms | 0.553 ms | -32% |
| 1920 × 1080, 1 layer · full recomposite | 11.9 ms | 9.2 ms | -23% |
| 1920 × 1080, 1 layer · undo last stroke | 13.9 ms | 12.7 ms | -9% |
| 1920 × 1080, 1 layer · redo last stroke | 12.8 ms | 8.5 ms | -33% |
| 1920 × 1080, 1 layer · .dnk save | 420.0 ms | 332.0 ms | -21% |
| 1920 × 1080, 1 layer · .dnk open | 105.0 ms | 92.0 ms | -12% |
| 1920 × 1080, 1 layer · undo history | 11 MB | 5 MB | -54% |
| 1920 × 1080, 6 layers · 6 px pencil (per pen event) | 0.037 ms | 0.034 ms | -8% |
| 1920 × 1080, 6 layers · 24 px ink (per pen event) | 0.067 ms | 0.056 ms | -16% |
| 1920 × 1080, 6 layers · 120 px hard (per pen event) | 0.569 ms | 0.442 ms | -22% |
| 1920 × 1080, 6 layers · 300 px soft (per pen event) | 1.779 ms | 0.867 ms | -51% |
| 1920 × 1080, 6 layers · full recomposite | 85.5 ms | 61.2 ms | -28% |
| 1920 × 1080, 6 layers · undo last stroke | 71.2 ms | 38.2 ms | -46% |
| 1920 × 1080, 6 layers · redo last stroke | 73.8 ms | 31.3 ms | -58% |
| 1920 × 1080, 6 layers · .dnk save | 629.0 ms | 390.0 ms | -38% |
| 1920 × 1080, 6 layers · .dnk open | 228.0 ms | 154.0 ms | -32% |
| 1920 × 1080, 6 layers · undo history | 72 MB | 45 MB | -38% |
| 2560 × 1440, 12 layers · 6 px pencil (per pen event) | 0.088 ms | 0.070 ms | -20% |
| 2560 × 1440, 12 layers · 24 px ink (per pen event) | 0.162 ms | 0.134 ms | -17% |
| 2560 × 1440, 12 layers · 120 px hard (per pen event) | 1.340 ms | 1.068 ms | -20% |
| 2560 × 1440, 12 layers · 300 px soft (per pen event) | 5.090 ms | 3.139 ms | -38% |
| 2560 × 1440, 12 layers · full recomposite | 332.9 ms | 281.4 ms | -15% |
| 2560 × 1440, 12 layers · undo last stroke | 246.7 ms | 119.6 ms | -52% |
| 2560 × 1440, 12 layers · redo last stroke | 276.0 ms | 109.5 ms | -60% |
| 2560 × 1440, 12 layers · .dnk save | 1,803.0 ms | 1,056.0 ms | -41% |
| 2560 × 1440, 12 layers · .dnk open | 739.0 ms | 552.0 ms | -25% |
| 2560 × 1440, 12 layers · undo history | 252 MB | 161 MB | -36% |

### Windows CI (secondary)

- **Smoke test.** The packaged M011 app on Windows CI reports a 1.74 ms average canvas frame (GPU texture), undo of a
  thin stroke on six layers in 1.5 ms, and 67/67 checks.
- **Engine benchmark on Windows.** M011's results are in
  [measurements/m011-engine-windows-ci-run28.json](measurements/m011-engine-windows-ci-run28.json).
- **No like-for-like Windows comparison yet.** CI run #28 measured the older `PerformanceTests` *slower* than M010's run
  #27 in several rows (e.g. a 12-layer recomposite 176 → 262 ms). That run's runner was also slower on untouched work
  (Brush Laboratory and Mission 010 measurements took a median 1.24× and 1.32× longer). In that run, the heavy Mission
  011 benchmarks (2+ GB) also ran in the same process just before those tests. The same tests run alone and paired here
  are 8–60% faster in M011 (the table above).
- **Fix for future runs.** The Mission 011 benchmarks now run in a separate CI step after the tests.

## 7. Regressions, noise and tradeoffs

- **No pixel changed.** All 33 M010 hashes match. The exactness tests cover the new arithmetic, the painting cache after
  every pen event, undo and redo, and the save thumbnail.
- **The first save of a session can be slower.** The PNG loops are now separate per filter. Until the runtime has
  optimised them (after the first few images), they run slower than M010's single loop did at the same stage:
  - a fresh process's first 256 × 256 PNG takes ~14 ms instead of ~5 ms;
  - the long benchmark's 48-frame animation write, the first `.dnk` write in its process, averaged 399 ms instead of
    305 ms over eight rounds (233–402 vs 263–524 ms).

  Saves run in the background, and once warm the same work is 13–48% faster (head to head). The animation's *first*
  write in a fresh process with large drawings measured faster too (816–928 ms vs 1,196–1,294 ms).
- **Rows that flip between rounds** use code this mission didn't change, or are dominated by allocating many large
  images, so their spread is noise. Over eight rounds (M010 vs M011):
  - rendering all 21 frames for export: 50–124 vs 62–127 ms;
  - GIF encode: 1.9–2.2 vs 2.1–2.4 s;
  - palette analysis: 211–251 vs 242–250 ms;
  - charcoal per event: 0.29–0.39 vs 0.29–0.34 ms;
  - 24 px ink on the palette-locked picture: 0.040–0.044 vs 0.036–0.062 ms;
  - switching to a cached frame: 0.85–0.95 vs 0.87–1.00 ms.
- **Memory added by the painting cache:** one picture-sized buffer (8 MB at 1920 × 1080, 14 MB at 2560 × 1440) per
  picture, once a big brush has painted a layer with at least two layers under it. Undo memory fell by more than that.
- **Marching ants on software rendering** march more slowly when a frame takes longer than 30 ms. With a GPU nothing
  changes.
- **Brush previews** are drawn again after 400 cached previews (a few milliseconds each, only when the panel rebuilds
  its tiles).
- **Diagnostics** adds two clock readings around each canvas frame, save, autosave, open, undo and redo (well under a
  microsecond each). Nothing runs while its window is closed.

## 8. Memory and long sessions

- **The 400-stroke session** (1920 × 1080, three layers, undo and redo, layers added and removed): the managed heap
  levels off once the undo history reaches its 200-step limit. That is 647 MB in M010 and 595 MB in M011, almost all of
  it undo history. Strokes are 26% (average) and 30% (p95) quicker.
- **Undo history is still limited to 200 steps or 1 GiB.** Its memory is now measured from what each step actually
  holds. Tiles of one colour, typically the transparent parts of a layer, take 16 bytes instead of 16 KB.
- **Animation memory is unchanged by design.** The 48-frame 1080p animation on twos holds 25 full-picture drawings
  (~200 MB) and up to 512 MB of frame cache. Sparse drawings would be an engine redesign; Diagnostics now shows both.
- **Liquify** leaves 2.7 MB after five cancelled sessions in both versions (one-time allocations, not growing).
- **The benchmark process** peaked at 2.3 GB in M011 against 3.9 GB in M010, and ended at 973 MB against 1,099 MB.

## 9. Reproducing

```bash
dotnet test tests/DoodleNook.Engine.Tests -c Release --filter "FullyQualifiedName~UnderTheHoodBenchmarks|FullyQualifiedName~HeadToHeadBenchmarks"
```

```bash
dotnet test tests/DoodleNook.App.Tests -c Release --filter "FullyQualifiedName~UnderTheHoodAppBenchmarks"
```

The results are written to `tests/DoodleNook.Engine.Tests/out/under-the-hood.json`, `head-to-head.txt` and
`tests/DoodleNook.App.Tests/out/under-the-hood-app.json`. To compare with Mission 010, copy the three benchmark files
into a checkout of 702aa2f and run the same commands there. That is how every M010 figure in this report was made.

