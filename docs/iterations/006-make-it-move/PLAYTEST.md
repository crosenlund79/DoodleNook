# Mission 006 playtest: make it move

About 45 minutes, in any order you like. You don't need the tablet; if you have it, use it.
Tick what works, and above all **note any friction**: anything fiddly, slow, surprising, cluttered or hard to find.
The defaults are first guesses; your preferences decide.

## Before you start

1. Download **DoodleNook-win-x64** from the newest green run on [this branch](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F006-make-it-move), extract it and start `DoodleNook.exe` (*More info → Run anyway* if Windows asks).
2. Press **Alt+6** for Animation Studio. **F1** lists every shortcut.

## A. Your first animation: a bouncing ball (6–8 frames)

- [ ] Draw a ball near the top. Press **N**: a new, empty frame, with the last ball faintly behind it (the onion skin).
- [ ] Draw the ball a bit lower. **N** again, and so on down to the ground (a squashed ball there).
- [ ] Try **Shift+N** instead: it copies the current drawing into a new frame, ready to change.
- [ ] Press **Enter** to play, Enter to pause, **Esc** to stop (it jumps back to where you started).
- [ ] Step through with **,** and **.** (or the arrow keys). **Home**/**End** jump to the ends.
- [ ] Draw while it plays: it pauses on that frame, and your stroke goes there.

## B. Onion skin

- [ ] **O** (or the *Onion skin* button) turns it off and on.
- [ ] Open the ▾ next to it: show 2 drawings before and after, change the strength, try the other colours, turn
      *Fade distant drawings* off, try *Whole frames*.
- [ ] Do the colours help you see where the ball was and where it goes? Are they too strong or too faint?

## C. The timeline

- [ ] Click cells to jump around; drag along the frame numbers to scrub.
- [ ] Drag the right end of a drawing to hold it for more frames, and back. The bar then says *Held N frames*; click it to give this frame its own copy.
- [ ] Right-click a cell: duplicate, delete, insert, move a frame, set a frame's duration.
- [ ] Click the **fps** chip: try 8, 12 and 24 fps, loop off, ping-pong.
- [ ] Drag the timeline's top edge to make it taller (drawing thumbnails appear); collapse it with the chevron.
      Ctrl+wheel over the timeline zooms it.
- [ ] Switch to Painting and back: the timeline keeps its size.

## D. A hand-drawn animation

- [ ] *File › New animation…*, Hand-drawn, 1280 × 720. Animate something small (a blink, a wave, a bouncing creature)
      with your favourite brushes, on two layers (a held background and a moving character: "+ Layer" in the timeline).
- [ ] Open *Help › Sample animations › Bouncing ball* and look at how it's built (the ground is one drawing held across all frames).

## E. A pixel animation

- [ ] *File › New animation…*, Pixel art, 32 × 32, 4 frames, 8 fps. Draw a small sprite with **Shift+N** between poses.
- [ ] The pixels stay exact at every zoom; the grid and onion skins are crisp.
- [ ] Open *Help › Sample animations › Pixel slime* and play it.
- [ ] Turn on *Animation › Preview panel* and watch it loop while you draw.

## F. Saving

- [ ] **Ctrl+S** your animation as a `.dnk`. Close DoodleNook. Reopen it and open the file: frames, holds, timing and layers are all there and editable.
- [ ] (Optional) Draw something, wait a minute (autosave), then end DoodleNook from Task Manager without saving.
      Start it again: it should offer to **recover** your work. (*File › Preferences* sets how often it autosaves.)

## G. Export (*File › Export animation…*)

- [ ] **Sprite sheet**, as a grid with 4 columns and a little spacing, with JSON. Open the PNG: are the frames in order and exact?
- [ ] **PNG sequence**: you get `name_0001.png`, `name_0002.png`, …
- [ ] **Animated GIF**: open it in a browser. Is the timing right? Does it loop?

## H. Customize workspace (*Window › Customize workspace…*)

- [ ] Switch on the frame navigation buttons and the onion-skin opacity slider. Pin anything you'd use often.
- [ ] Go to another workspace and back, then restart DoodleNook: your choices are still there.
- [ ] Try *Reset to default*.
- [ ] In Painting, hide the view controls on the canvas. Animation Studio is not affected.

## Questions

1. Does the interface feel simple enough?
2. Is the canvas given enough space?
3. Can you quickly create a new frame?
4. Is duplication intuitive?
5. Does the timeline make sense?
6. Do onion skins help you draw?
7. Does playback feel immediate?
8. Are important controls easy to reach?
9. Is anything visible that you rarely need?
10. Is anything hidden that you need constantly?
11. Does workspace customization feel straightforward?
12. **What would make the whole process more enjoyable?**

Anything else: write it down as it happens. Small irritations are exactly what we want to hear about.
