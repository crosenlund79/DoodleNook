# Mission 012 playtest: The Artist's Companion

About 30 minutes, ideally with the Kamvas 13 and its pen (it has only been simulated here). Use copies of your own
files. Everything you already know works as before. What's new: animation import, layer opacity in Animation Studio,
pasting from other programs, Paste as reference, the Lospec palettes and the Quick Start guide.

1. Download **DoodleNook-win-x64** from the newest green run on
   [this branch](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F012-artists-companion).
2. Extract it and start `DoodleNook.exe`. The title says **DoodleNook — M012**.

## A. Quick Start guide

1. **Help › Quick Start Guide** (in Quick Sketch: the ☰ menu). Eight short sections with pictures of DoodleNook itself.
2. Is it the right length? Is anything missing, unclear or wrong?

## B. Import an animation to draw over

1. **File › Import animation…** (also in the Animation menu). Choose a GIF you like.
2. The window shows the frame count, the size and the file's timing. Click a few frames in the strip to leave them
   out (click again to bring one back), or type a range in *from* / *to*.
3. **Import.** You land in Animation Studio. The timeline shows four layers:
   - **Reference:** your frames, at 35 % and locked.
   - **Sketch**, **Outline** and **Coloring:** empty.

   Sketch is selected. Draw on a few frames. Press Enter to play: is the timing the same as in the original GIF?
4. Try an **animated PNG**, and a folder of **numbered PNGs**: choose any one picture of the sequence and the others
   come along in number order (`walk_2` before `walk_10`). Other PNGs in that folder must not appear. For a sequence,
   the window asks for the frame rate.
5. Save as `.dnk`, close, open again: everything is still there. Your GIF/PNG files are never changed.

## C. Layer opacity in Animation Studio

1. In the timeline, each layer shows its opacity at the right (e.g. *35%* on Reference). Click it for a slider and a
   box. Drag the slider, or type a number and press Enter.
2. Step through the frames, play, turn on onion skin (O): the layer is equally faint on every frame.
3. The lock mark on Reference unlocks it with one click (the layer menu can lock it again).
4. Export a GIF and a sprite sheet: the opacity is in the exports, and hidden layers are left out.
5. Ctrl+Z takes back an opacity change.

## D. Paste from other programs

1. In a browser, right-click a picture › **Copy image**. In DoodleNook, press **Ctrl+V**. It lands on a new layer
   with handles. Move or scale it, then press Enter. Transparent PNGs should stay transparent.
2. Try a screenshot (Win+Shift+S), something copied in Paint, and a picture file copied in Explorer (Ctrl+C on the
   file).
3. Copy something inside DoodleNook (Ctrl+C) and paste (Ctrl+V): it still pastes DoodleNook's own copy, in place.
   Then copy a picture elsewhere and press Ctrl+V again: the newest copy wins.
4. Copy some text and press Ctrl+V: you get a short message, nothing else happens.
5. Select a **locked** layer and paste: the locked layer is untouched (the paste gets its own layer).

## E. Paste as reference

1. Copy a picture elsewhere. **Edit › Paste as reference.** It appears at 35 % with handles: place and size it, then
   press Enter. It is now locked, so drawing can't land on it by accident.
2. In an imported animation, do it on one frame: the reference is on that frame only, and the imported Reference
   layer is unchanged.

## F. Palettes

1. **Window › Palettes…**: the new **Lospec** collection has ten palettes, among them PICO-8 and Resurrect 64. Each
   shows its colour count, creator, terms and source.
2. Use one in Pixel Art, lock a picture to it, remap a picture to it, and pick one as a Game Edit project palette.
3. The palettes you asked for that aren't included (Resurrect 32, Apollo, Endesga 32/64, SLSO8, Fantasy 24,
   Midnight ablaze, JustParchment8, BloodMoon21, Tranquil Fantasy 23) have no clear permission to be shipped. You can
   download them from Lospec and use **Palettes… › Import…**. The reasons are in
   [PALETTE-ATTRIBUTION.md](../../PALETTE-ATTRIBUTION.md).

### Questions

1. Does importing a GIF and drawing over it feel natural? Are the four layers the right ones, and is 35 % the right
   strength for the reference?
2. Is the opacity control in the timeline easy to find and precise enough?
3. Did pasting work from every program you tried? Was anything pasted wrong (colours, transparency, size)?
4. Is "Paste as reference, locked once placed" how you want references to behave?
5. **SLS08:** did you mean **SLSO8** (8 colours, by Luis Miguel Maldonado)? And which palette is **"Fantasy_"**? The
   candidates are listed in [PALETTE-ATTRIBUTION.md](../../PALETTE-ATTRIBUTION.md).
6. Is the Quick Start guide helpful? Would you add or remove anything?
