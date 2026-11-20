# DoodleNook research, design and iteration notes

| Mission | What it covers | Start here |
|---|---|---|
| 001 | GIMP's architecture, painting engine and Windows tablet input; technology options; licensing | [research/001-gimp-architecture-and-tablets/report.md](research/001-gimp-architecture-and-tablets/report.md) |
| 002 | Photoshop, Clip Studio Paint, Krita and Aseprite as drawing experiences; small pen displays; five workspace ideas; feature inventory; interview plan | [research/002-drawing-experience-and-workspaces/report.md](research/002-drawing-experience-and-workspaces/report.md) |
| 003 | An interactive workspace playground: five workspaces at real Kamvas 13 and desktop sizes, plus three interaction experiments (approved visual direction) | [design/003-workspace-playground/README.md](design/003-workspace-playground/README.md) |
| 004 | **The Missing Ink**: the first native Windows DoodleNook — real pen drawing in the Quick Sketch workspace, with a downloadable Windows build | [iterations/004-the-missing-ink/README.md](iterations/004-the-missing-ink/README.md) · [playtest guide](iterations/004-the-missing-ink/PLAYTEST.md) |
| 005 | **The Canvas Comes Alive**: all five workspaces around one layered picture — layers, nine brushes, selections and transforms, adjustments, pixel art, the quick palette, the .dnk format and PNG/JPEG/WebP export | [iterations/005-canvas-comes-alive/README.md](iterations/005-canvas-comes-alive/README.md) · [playtest guide](iterations/005-canvas-comes-alive/PLAYTEST.md) |
| 006 | **Make It Move**: Animation Studio, the sixth workspace — frame-by-frame hand-drawn and pixel-art animation with a compact timeline, holds, onion skins, playback, sprite-sheet/PNG-sequence/GIF export, `.dnk` version 2, autosave and crash recovery, and Customize workspace | [iterations/006-make-it-move/README.md](iterations/006-make-it-move/README.md) · [playtest guide](iterations/006-make-it-move/PLAYTEST.md) |
| 007 | **The Artist's Workshop**: the Brush Laboratory (custom tips, texture, scatter, pressure response, a scribble area), 17 curated brushes, stamps and stamp brushes, patterns and pattern fill, palettes (from pictures, `.gpl`/`.hex`, Palette Lock and Remap to Palette), an optional reference board, a creative library with `.dnkpack` asset packs, and the export-dialog freeze fix | [iterations/007-artists-workshop/README.md](iterations/007-artists-workshop/README.md) · [playtest guide](iterations/007-artists-workshop/PLAYTEST.md) |

Missions 001–003 are research and design only. Missions 004–007 are application code (in `src/` and `tests/` at the repository root).

**Interface philosophy (permanent):** [design/INTERFACE-PHILOSOPHY.md](design/INTERFACE-PHILOSOPHY.md) — read it, and the root
[`CLAUDE.md`](../CLAUDE.md), before any significant UI or workspace change.
Each research folder has a `notes/` subfolder with the detailed findings behind its report, including source links.
