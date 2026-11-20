# DoodleNook

A cosy Windows drawing application for sketching, painting and editing — made to make you want to draw.

**Current milestone: Mission 007 "The Artist's Workshop"** — a Brush Laboratory for making and tuning brushes,
17 curated brushes, stamps, pattern fills, palettes with Palette Lock and Remap to Palette, and an optional reference
board, all kept in one creative library, plus a fix for the export-dialog freeze. No new workspace, and the default screens stay as calm as before.
Read [docs/iterations/007-artists-workshop](docs/iterations/007-artists-workshop/README.md) and try it with the
[playtest guide](docs/iterations/007-artists-workshop/PLAYTEST.md).
Earlier milestones: [Mission 006 "Make It Move"](docs/iterations/006-make-it-move/README.md) (Animation Studio),
[Mission 005 "The Canvas Comes Alive"](docs/iterations/005-canvas-comes-alive/README.md) (five workspaces, layers,
brushes, editing, pixel art) and [Mission 004 "The Missing Ink"](docs/iterations/004-the-missing-ink/README.md) (the
pen-drawing foundation).

**Less is more — but capable when needed. Ease of use first.** The interface rules are in [`CLAUDE.md`](CLAUDE.md) and
[docs/design/INTERFACE-PHILOSOPHY.md](docs/design/INTERFACE-PHILOSOPHY.md).

- Download the Windows build: [Actions → Windows build](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F007-artists-workshop) → newest green run → artifact **DoodleNook-win-x64**.
- Earlier research and the approved design prototype: [docs/](docs/README.md).
- Sound identity sketches: [sound-experiments/](sound-experiments/README.md).

## Code

| Folder | What |
|---|---|
| `src/DoodleNook.Engine` | The drawing engine (pixels, layers, brushes, selections, operations, animation, undo, .dnk, exports, the creative library). No UI dependencies. |
| `src/DoodleNook.App` | The Windows app (Avalonia), builds `DoodleNook.exe`. |
| `tests/` | Engine tests and app tests (including the real window, driven headlessly). |

Build and test with the .NET 10 SDK:

```bash
dotnet test DoodleNook.slnx
```

```bash
dotnet run --project src/DoodleNook.App
```
