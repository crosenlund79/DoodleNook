# DoodleNook

A cosy Windows drawing application for sketching, painting and editing — made to make you want to draw.

**Current milestone: Mission 015 "The Game Artist's Workshop"**: four everyday jobs made easier. **File › Import sprite
sheet…** slices a sheet into an animation with the grid shown over it (and **Sprite sheet from pictures…** goes the other
way); **View › Animation preview** loops the animation while you draw any frame, docked or in its own window;
**Palettes › Import folder…** brings a whole folder of palettes into your library at once; and **Storybook Studio**
(**File › New storybook…**) arranges your drawings into comic and storyboard pages, every panel at the same pixels per
centimetre. Read [docs/iterations/015-game-artists-workshop](docs/iterations/015-game-artists-workshop/README.md) and try it
with the [playtest guide](docs/iterations/015-game-artists-workshop/PLAYTEST.md); the [Quick Start guide](docs/QUICK-START.md)
(also Help › Quick Start Guide) has new sections for sprite sheets and Storybook.
Earlier milestones: [Mission 014 "The Gentle Guide"](docs/iterations/014-the-gentle-guide/README.md) (Help › Guided
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
