# DoodleNook

A cosy Windows drawing application for sketching, painting and editing — made to make you want to draw.

**Current milestone: Mission 005 "The Canvas Comes Alive"** — five workspaces (Pure Sketch, Quick Sketch,
Painting, Editing, Pixel Art) around one layered picture, with brushes, selections, transforms, adjustments,
pixel tools, the `.dnk` format and PNG/JPEG/WebP export.
Read [docs/iterations/005-canvas-comes-alive](docs/iterations/005-canvas-comes-alive/README.md) and try it with the
[playtest guide](docs/iterations/005-canvas-comes-alive/PLAYTEST.md).
The previous milestone, [Mission 004 "The Missing Ink"](docs/iterations/004-the-missing-ink/README.md), is the pen-drawing foundation.

- Download the Windows build: [Actions → Windows build](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F005-canvas-comes-alive) → newest green run → artifact **DoodleNook-win-x64**.
- Earlier research and the approved design prototype: [docs/](docs/README.md).
- Sound identity sketches: [sound-experiments/](sound-experiments/README.md).

## Code

| Folder | What |
|---|---|
| `src/DoodleNook.Engine` | The drawing engine (pixels, layers, brushes, selections, operations, undo, .dnk). No UI dependencies. |
| `src/DoodleNook.App` | The Windows app (Avalonia), builds `DoodleNook.exe`. |
| `tests/` | Engine tests and app tests (including the real window, driven headlessly). |

Build and test with the .NET 10 SDK:

```bash
dotnet test DoodleNook.slnx
```

```bash
dotnet run --project src/DoodleNook.App
```
