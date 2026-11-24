# DoodleNook

A cosy Windows drawing application for sketching, painting and editing — made to make you want to draw.

**Current milestone: Mission 018 "Quick Adjust"**, from your notebook. **Hold B, E or Z** and a small card appears
just above the pen with zoom, size, opacity and smoothing. Drag on it, or anywhere on the canvas; let go of the key
to keep the change, or press Esc to undo it. The brush size now also shows above the pen while you resize, where the
pen can't hide it. Read [docs/iterations/018-quick-adjust](docs/iterations/018-quick-adjust/README.md) and the
[playtest guide](docs/iterations/018-quick-adjust/PLAYTEST.md); [docs/PLAYTEST-STATUS.md](docs/PLAYTEST-STATUS.md)
sums up what has been tested by you and what by the automated checks.
Earlier milestones: [Mission 017 "The Second Pass"](docs/iterations/017-second-pass/README.md)
(every menu and dialog at 150 %, old files, crash and save safety in the real program, eighteen fixes),
[Mission 016 "The Completion Pass"](docs/iterations/016-completion-pass/README.md)
(GIF colours, Storybook chrome, Copy to other programs, real-file checks), [Mission 015 "The Game Artist's Workshop"](docs/iterations/015-game-artists-workshop/README.md)
(sprite sheets, the Live Animation Preview, palette folders, Storybook Studio), [Mission 014 "The Gentle Guide"](docs/iterations/014-the-gentle-guide/README.md) (Help › Guided
Tutorials… and "Your First Animation"),
[Mission 013 "Animation Reference Library"](docs/iterations/013-animation-reference-library/README.md)
(now 35 reference cycles for game animation, with Expansion Pack 1: carrying, limping, emotions, transitions),
[Mission 012 "The Artist's Companion"](docs/iterations/012-artists-companion/README.md) (animation
import for reconstruction, layer opacity, pasting from other programs, credited palettes, the Quick Start guide),
[Mission 011 "Under the Hood"](docs/iterations/011-under-the-hood/README.md) (measured speed-ups
and Help › Diagnostics), [Mission 010 "The Finishing Touches"](docs/iterations/010-finishing-touches/README.md) (Liquify,
Smudge, Clone and Repair, Perspective and Warp, the quick palette's brush sizing and choice),
[Mission 009 "Game Edit Mode"](docs/iterations/009-game-edit-mode/README.md) (a game's graphics
folder in a catalog with progress, Ctrl+S saving the `.dnk` and the game's PNG),
[Mission 008 "The Designer's Toolkit"](docs/iterations/008-designers-toolkit/README.md) (text,
shapes, groups, masks, adjustment layers, drawing assistants),
[Mission 007 "The Artist's Workshop"](docs/iterations/007-artists-workshop/README.md) (Brush
Laboratory, stamps, patterns, palettes, Palette Lock, reference board),
[Mission 006 "Make It Move"](docs/iterations/006-make-it-move/README.md) (Animation Studio),
[Mission 005 "The Canvas Comes Alive"](docs/iterations/005-canvas-comes-alive/README.md) (five workspaces, layers,
brushes, editing, pixel art) and [Mission 004 "The Missing Ink"](docs/iterations/004-the-missing-ink/README.md) (the
pen-drawing foundation).

**Less is more — but capable when needed. Ease of use first.** The interface rules are in [`CLAUDE.md`](CLAUDE.md) and
[docs/design/INTERFACE-PHILOSOPHY.md](docs/design/INTERFACE-PHILOSOPHY.md).

- Download the Windows build: [Actions → Windows build](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F014-the-gentle-guide) → newest green run → artifact **DoodleNook-win-x64**.
- Earlier research and the approved design prototype: [docs/](docs/README.md).
- Sound identity sketches: [sound-experiments/](sound-experiments/README.md).

## Code

| Folder | What |
|---|---|
| `src/DoodleNook.Engine` | The drawing engine (pixels, layers, groups, masks, text and shape layers, adjustment layers, brushes, selections, operations, animation, undo, .dnk, exports, the creative library). No UI dependencies. |
| `src/DoodleNook.App` | The Windows app (Avalonia), builds `DoodleNook.exe`. |
| `tests/` | Engine tests and app tests (including the real window, driven headlessly). |

Build and test with the .NET 10 SDK:

```bash
dotnet test DoodleNook.slnx
```

```bash
dotnet run --project src/DoodleNook.App
```
