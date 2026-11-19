# Mission 005 playtest: five workspaces on the Huion Kamvas 13

About 30–40 minutes. Tick what works, and note what feels wrong. Mission 004's pen-feel questions
([its playtest](../004-the-missing-ink/PLAYTEST.md), sections B–D) still apply. If you haven't done them yet, start there.

## Before you start

1. In the Huion driver, keep **Enable Windows Ink** on.
2. Download **DoodleNook-win-x64** from the newest green run on this branch, extract it, and start `DoodleNook.exe`. If Windows asks, choose *More info → Run anyway*.
3. DoodleNook opens in Quick Sketch the first time. Later it opens in the workspace you used last.

## A. Switching workspaces

- [ ] Draw something in Quick Sketch, then press **Alt+3** (Painting), **Alt+4** (Editing), **Alt+5** (Pixel Art), **Alt+1** (Pure Sketch). The drawing stays exactly as it is and nothing reloads.
- [ ] The workspace buttons at the top right do the same; the current one shows its name.
- [ ] In Pure Sketch, **Tab** takes you back to where you came from.
- [ ] Pick a tool in Painting (say G, the bucket), switch to Editing and back: Painting still has the bucket.
- [ ] Close DoodleNook in Editing; reopen: it starts in Editing.
- [ ] Each workspace looks like its Mission 003 mock-up (warm dark colours, same layout).

## B. Pure Sketch and the quick palette

- [ ] In Pure Sketch, right-click (or press the pen's lower barrel button if it is set to right-click). The quick palette opens **where the pen is**.
- [ ] Tap a colour; tap the eraser; drag the size in the centre up/down.
- [ ] Undo/redo from the palette. Esc or a tap outside closes it.
- [ ] Hold **R** and drag to rotate the view; the small corner menu has *Straighten view* and *Flip view*.

## C. Layers (Painting or Quick Sketch's layers chip)

- [ ] **Ctrl+Shift+N** (or +) adds a layer; paint on it; the layer below is untouched.
- [ ] Click a row to paint on that layer. Drag a row to reorder. Double-click the name to rename it.
- [ ] Hide/show with the eye; change opacity and the blend mode (try *Multiply* over a sketch).
- [ ] Lock transparency (the grid-square button), then paint over a shape: only the shape gets colour.
- [ ] Clip to the layer below (the hooked arrow): colour stays inside the lower layer's paint.
- [ ] Right-click a row for a colour label. Merge down (Ctrl+E) and duplicate (Ctrl+J).
- [ ] Hide the paper (eye on the *Paper* row) to see transparency.

## D. Brushes and colour (Painting)

- [ ] Try all nine brushes. Each should feel different. In particular:
  - *Sketch pencil* now has a paper grain (a change from Mission 004);
  - *Marker* has a flat chisel tip;
  - *Airbrush* builds up while the pen rests;
  - *Chalk* is rough;
  - *Flat wash* is even.
- [ ] *Brush settings* (in the Brushes panel) has hardness, smoothing, grain/flow, pressure and the mirror.
- [ ] Colour: the square and hue strip, hex, **RGB · HSV** numbers, + to save a swatch, right-click to remove it.
- [ ] Bucket (**G**): change *Tolerance* and see what it fills. Gradient (**Shift+G**) and shapes (**U**).

## E. Editing

- [ ] **M** and drag a rectangle; Shift-drag adds, Alt-drag subtracts. Try the lasso (**L**) and magic wand (**W**).
- [ ] With a selection: **Ctrl+T**. Drag inside to move, corners to scale (Shift keeps proportions), outside a corner to rotate. **Enter** applies, **Esc** cancels. Try *Pixels / Smooth / Sharp*.
- [ ] Move tool (**V**) drags the selection or whole layer; arrow keys nudge.
- [ ] Image › *Hue / Saturation…*: the canvas previews live; Cancel leaves it unchanged; Apply is one undo step.
- [ ] Image › *Image size…*, *Canvas size…*, *Rotate*, *Flip*; Crop tool (**C**: drag, then Enter).
- [ ] The History panel: click an older step to go back, then a greyed step to go forward again.

## F. Pixel Art

- [ ] **Ctrl+N** offers sprite sizes; pick 32 × 32. The background is transparent.
- [ ] Zoom (wheel or +/−) steps in whole numbers; the grid appears when zoomed in; **Ctrl+'** toggles it.
- [ ] Pixel pencil: single clean pixels, no soft edges. Draw a diagonal with *Pixel-perfect* on and off.
- [ ] Try the mirror (↔), *Tiled*, and the 1:1 preview at the top right (click it for 2× / 3×).
- [ ] Palette: left-click a swatch for foreground, right-click for background. Set *Right-click on canvas: Paint BG* and paint with the right button.
- [ ] Select part of the sprite and move it: it stays on whole pixels.
- [ ] File › Export as PNG, then open the PNG elsewhere: exact pixels, transparent background.

## G. Files

- [ ] Save a layered picture (**Ctrl+S**): it is saved as **.dnk**. Close and reopen it: layers, names, blend modes and opacity are back.
- [ ] File › Export as JPEG: the dialog says the layers are merged into a flat copy; the open picture keeps its layers.
- [ ] Open a photo (JPEG) and check it isn't sideways.

## H. Overall

- Which workspace would you use most? Which feels least finished?
- Are the buttons and panels comfortable with the pen at your Windows scaling?
- Anything that felt slow (large brushes, big pictures, many layers)?
- What would you change first?

## If something seems wrong

Note the workspace, what you did, what you expected and what happened. A screenshot helps. For pen problems, include the **F12** diagnostics panel.
