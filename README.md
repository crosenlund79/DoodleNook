# DoodleNook

A cosy Windows drawing application for sketching, painting and editing — made to make you want to draw.

**Current milestone: Mission 006 "Make It Move"** — Animation Studio, a sixth workspace for frame-by-frame
hand-drawn and pixel-art animation (timeline, holds, onion skins, playback, sprite sheets, PNG sequences and GIFs),
plus autosave and crash recovery and a simple *Customize workspace*.
Read [docs/iterations/006-make-it-move](docs/iterations/006-make-it-move/README.md) and try it with the
[playtest guide](docs/iterations/006-make-it-move/PLAYTEST.md).
Earlier milestones: [Mission 005 "The Canvas Comes Alive"](docs/iterations/005-canvas-comes-alive/README.md) (five
workspaces, layers, brushes, editing, pixel art) and [Mission 004 "The Missing Ink"](docs/iterations/004-the-missing-ink/README.md) (the pen-drawing foundation).

**Less is more — but capable when needed. Ease of use first.** The interface rules are in [`CLAUDE.md`](CLAUDE.md) and
[docs/design/INTERFACE-PHILOSOPHY.md](docs/design/INTERFACE-PHILOSOPHY.md).

- Download the Windows build: [Actions → Windows build](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F006-make-it-move) → newest green run → artifact **DoodleNook-win-x64**.
- Earlier research and the approved design prototype: [docs/](docs/README.md).
- Sound identity sketches: [sound-experiments/](sound-experiments/README.md).

## Code

| Folder | What |
|---|---|
| `src/DoodleNook.Engine` | The drawing engine (pixels, layers, brushes, selections, operations, animation, undo, .dnk, exports). No UI dependencies. |
| `src/DoodleNook.App` | The Windows app (Avalonia), builds `DoodleNook.exe`. |
| `tests/` | Engine tests and app tests (including the real window, driven headlessly). |

Build and test with the .NET 10 SDK:

```bash
dotnet test DoodleNook.slnx
```

```bash
dotnet run --project src/DoodleNook.App
```
