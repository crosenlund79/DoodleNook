# Mission 015 playtest — The Game Artist's Workshop

About 30 minutes. Use the Kamvas for the drawing parts; note anything that feels slow, confusing or in the way.

## 1. Sprite sheets (5 min)

1. Export an animation you have: **File › Export animation…**, *Sprite sheet*, *Grid*, 4 columns, tick *JSON metadata*.
2. **File › Import sprite sheet…** and choose that PNG. The grid should match the frames exactly (it says it comes from
   the .json). Import it as *Animation*: it should play the same.
3. Import a sheet from somewhere else (a game, an asset you have) without a .json:
   - Is the first guess right? If not, drag the grid, drag the first frame's corner, or use *Split into* columns × rows.
   - Are empty cells left out? Click a frame in the strip to leave it out.
   - Try *Reference to redraw*: Reference locked at 35 %, Sketch selected, ready to trace.
4. **File › Sprite sheet from pictures…**: choose one PNG of a numbered sequence. Leave one out, set the columns, save.
   Open the result. Is anything blurred or shifted?

## 2. Live Animation Preview (8 min)

1. In Animation Studio: **View › Animation preview**. It plays in the corner.
2. Go to frame 3 and draw. The preview keeps playing all frames; your frame never jumps. Does the new drawing show when
   you lift the pen? Does drawing feel exactly as before? This is the important one.
3. Undo, hide a layer, change a layer's opacity, add a frame, change the fps: the preview follows.
4. Pause and loop (bottom left), zoom *Fit* and *1×* (pixel art should be crisp).
5. Background button (bottom right): black, a colour, then a picture. Export a GIF: no background in it.
6. ⤢ opens it in its own window. Move it (to a second screen if you have one), resize it, switch workspaces: it stays.
   Close it with its ×, restart DoodleNook: it stays closed. Open it again: it comes back where you left it.
7. **View › Behind transparent areas**: the canvas shows the same choices.

## 3. Palettes (5 min)

1. Download a few palettes from Lospec in different formats (or use a folder you have).
2. **Window › Palettes… › Import folder…**, choose the folder. Read the summary: anything surprising?
3. Import the same folder again: nothing new is added.
4. Close DoodleNook, move or delete the downloaded folder, open DoodleNook: the palettes are still there.
5. Rename one, delete one. Use one to **Lock** a pixel-art picture, and as a Game Edit project palette.

## 4. Storybook Studio (12 min)

1. **Help › A little walk (storybook)**. Look through both pages (thumbnails left, Previous / Next, Page Down).
2. Click a panel: the right side says its size and resolution. Double-click it: the drawing opens in the editor. Change
   something, Ctrl+S, then **Back to the storybook** (bottom right). Does the panel show your change? The bottom panel
   of page 1 shares its drawing with the top one: did both change?
3. **File › New storybook…**: A4, 300 dpi, *Comic*. Select an empty panel, **New drawing**: is the size right without
   any calculating? Draw something quick, save, come back.
4. **Link a drawing…** with a small picture (say 600 × 400) into a big panel: you should get the low-resolution question.
   Try both answers.
5. Change the page's **Layout** to *Two stacked*, then back to *Comic*: nothing lost? Try Ctrl+Z.
6. Add a page, duplicate it, move it, delete it (right-click a page thumbnail).
7. **Export ›** this page as PNG, and all pages as PDF. Open them: correct size, nothing stretched, borders where
   expected?
8. Move the whole storybook folder somewhere else and open it again: the drawings are still linked.

## What to tell me

- Anything that made drawing feel different with the preview open.
- Whether the sprite sheet first guess was right on your real sheets.
- In Storybook: was anything hard to find? Is the right-hand panel too busy, or missing something you reached for?
- Pen behaviour on the Kamvas (only you can check that).
