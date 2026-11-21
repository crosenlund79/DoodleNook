# Mission 011 playtest: Under the Hood

About 20 minutes, best with the Kamvas 13 and its pen, on your own pictures (use copies). Nothing should *look* or *feel*
different: the brushes, tools, shortcuts and menus are exactly as in Mission 010. What should change is that heavy
moments are quicker and long sessions stay smooth. Your hands and eyes are the judge (the pen has only been simulated
here).

1. Download **DoodleNook-win-x64** from the newest green run on
   [this branch](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F011-under-the-hood),
   extract it and start `DoodleNook.exe`. The title says **DoodleNook — M011**.

## A. Does it still feel the same?

1. Draw for a few minutes as you usually do: pencil, ink, soft brush, eraser, the quick palette (right-click / pen
   button). Do strokes look and respond exactly as before (thickness, pressure, softness, texture)?
2. Smudge, Clone, Repair, Liquify, Perspective and Warp on a painting: anything different from Mission 010?

## B. Is it quicker where it was slow?

1. **Undo on a big layered picture.** Open (or make) a large picture with many layers, e.g. 2560 × 1440 with 10+
   layers. Paint a long stroke, then Ctrl+Z and Ctrl+Shift+Z several times. Undo should feel immediate.
2. **Big soft brushes over many layers.** On the top layer of that picture, paint with a big soft brush (200–400 px)
   in circles. Does the stroke keep up with the pen?
3. **Saving.** Ctrl+S on the big picture, and keep drawing straight away. Does the window hesitate when you save?
   (Autosave copies run in the same way.)
4. **Opening.** Open a big `.dnk` again: does it open a bit faster?
5. **Game Edit Mode.** Open a game folder with many PNGs. Does the catalog stay responsive while the thumbnails appear?
6. **A long session.** Switch tools and workspaces often during an hour of drawing. Does anything get slower over time?
7. **Selections.** Make a selection and paint inside it. The marching ants should march as usual.

## C. Help › Diagnostics…

1. Open **Help › Diagnostics…** (in Quick Sketch: the ☰ menu). It shows the version, the picture's size and layers,
   memory, recent timings (canvas frames, the last stroke, undo, save…) and caches. The numbers refresh every second.
2. Press **Copy diagnostics** and paste somewhere: only numbers, no picture, file names or folders.
3. Close it. It should not appear anywhere during ordinary drawing.

### Questions

1. Did anything look or feel different from Mission 010 (strokes, smudge, liquify, the wheel)? Even small things.
2. Is undo on your biggest pictures quick now? Any operation that still makes you wait?
3. Do big brushes keep up with the pen on your layered paintings?
4. Did saving or autosave ever interrupt drawing?
5. Is the Diagnostics window clear enough to send to me if something feels slow? Anything it should show?
