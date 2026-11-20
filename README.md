# DoodleNook

A cosy Windows drawing application for sketching, painting and editing — made to make you want to draw.

**Current milestone: Mission 008 "The Designer's Toolkit"** — editable text, editable shapes, layer groups, masks,
adjustment layers, and drawing assistants (rulers and guides, snapping, symmetry with a movable centre and radial
symmetry, one- and two-point perspective, Shift for straight lines). No new workspace, and the default screens stay as
calm as before. It also makes *The Personal Tool Principle* part of [`CLAUDE.md`](CLAUDE.md).
Read [docs/iterations/008-designers-toolkit](docs/iterations/008-designers-toolkit/README.md) and try it with the
[playtest guide](docs/iterations/008-designers-toolkit/PLAYTEST.md).
Earlier milestones: [Mission 007 "The Artist's Workshop"](docs/iterations/007-artists-workshop/README.md) (Brush
Laboratory, stamps, patterns, palettes, Palette Lock, reference board),
[Mission 006 "Make It Move"](docs/iterations/006-make-it-move/README.md) (Animation Studio),
[Mission 005 "The Canvas Comes Alive"](docs/iterations/005-canvas-comes-alive/README.md) (five workspaces, layers,
brushes, editing, pixel art) and [Mission 004 "The Missing Ink"](docs/iterations/004-the-missing-ink/README.md) (the
pen-drawing foundation).

**Less is more — but capable when needed. Ease of use first.** The interface rules are in [`CLAUDE.md`](CLAUDE.md) and
[docs/design/INTERFACE-PHILOSOPHY.md](docs/design/INTERFACE-PHILOSOPHY.md).

- Download the Windows build: [Actions → Windows build](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F008-designers-toolkit) → newest green run → artifact **DoodleNook-win-x64**.
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
