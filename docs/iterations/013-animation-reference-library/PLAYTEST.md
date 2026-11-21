# Mission 013 playtest: Animation Reference Library

About 20–30 minutes, ideally on one of your own game animations (use a copy) and with the Kamvas 13. The pen has only
been simulated here.

1. Download **DoodleNook-win-x64** from the newest green run on
   [this branch](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F013-animation-reference-library).
2. Extract it and start `DoodleNook.exe`. The title says **DoodleNook — M013**.

## A. A walk under a scene you already started

1. Open one of your animations: a background, a character, a few frames. Select the layer you draw the character
   on.
2. **Animation › Insert reference cycle…** Walk is chosen. The preview plays it over your current frame.
3. **Drag in the preview** to put the feet on your floor. Set the **Height** to your character's size, and the
   **Facing**.
4. Read the sentence under the options: it says which frames the walk covers and whether frames are added.
5. **Insert.** You get a new layer "Walk reference (side)" just under your character layer: faint (35 %) and locked.
   Your character layer is still the one you draw on.
6. Play (Enter), step through frames, turn on onion skin (O). Does the reference sit where you wanted on every
   frame?
7. Draw a few frames over it. Ctrl+Z after inserting takes the whole insert back in one step.

## B. Try the library

1. Look through the cycles: Movement (idle, walk, run, sneak, jump, fall, land), Combat (slash, thrust, hurt, death,
   cast, shoot) and Interaction (pick up, drop, push, pull, climb, sit down, stand up, hammer, dig).
2. Switch views: **Side**, **Top-down** and **Three-quarter**, with their facings (← → in side view, four directions
   from above, four diagonals at three-quarter).
3. Try **Each drawing lasts 2** (on twos), **Starts at** the current frame, and **Repeat to the end** on or off.
4. Use a short animation (say 4 frames) and insert an 8-drawing cycle: does the window tell you clearly that frames
   will be added? Untick **Add frames if needed** and see it stop at the last frame instead.

## C. Change it later

1. Right-click the reference layer in the timeline › **Adjust reference cycle…** (also in the Animation menu when
   that layer is selected).
2. Move it, make it bigger, turn it, or swap Walk for Run. **Apply**, then Ctrl+Z, then Ctrl+Shift+Z.
3. Change its opacity with the percentage in the timeline; hide it with the eye; unlock it with the lock mark.
4. Save, close and open the file again: the reference and its settings are still there.
5. Export a GIF or sprite sheet with the reference visible, then hidden.

## D. Pixel art

In a small pixel-art animation (e.g. 64 × 64), insert a cycle. It should have hard pixel edges and a sensible
height. Does it help at that size?

### Questions

1. Which cycles do you actually use? Which feel wrong (timing, poses, weight) for your games?
2. Are the frame counts right (4–8 drawings)? Would you rather have more or fewer?
3. Are the three views the ones you need? Is anything missing (e.g. a front view, eight directions at once)?
4. Is dragging in the preview a comfortable way to place the figure? Is the ground marker useful?
5. Is "a faint, locked layer under the layer you draw on" the right default?
6. Is the orange-right / blue-left colouring of the limbs helpful?
