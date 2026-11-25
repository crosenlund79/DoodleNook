# Mission 019 playtest: Bare Bones

About 20 minutes, on the Kamvas with the pen, at your usual 150 %. Bare Bones is experimental: tell me what feels
wrong as much as what works.

## 1. The sample (3 min)

1. **Help › Wave hello (Bare Bones).** The view opens over the canvas, with the character on the stage.
2. Press **Enter** to play. The character stands, blinks, lifts the right arm and waves, lowers it, sways a little,
   and loops. Press **Enter** again to stop.
3. Click frames on the timeline and drag along its numbers to scrub, forwards and backwards. The blink and the wave
   should look the same whichever way you come to a frame.
4. Press **H** to hide the bones and see only the drawing. Press it again to bring them back.

## 2. Pose it (5 min)

1. In **Animate**, go to a frame without keys, for example frame 30.
2. With the pen, drag the **left arm** (on the bone or on the sleeve itself) to turn it about the shoulder. A diamond
   appears on its row at frame 30, and one at frame 1 that keeps the rest pose.
3. Drag the **head's joint** (the circle at the neck) to move it a little.
4. **Ctrl+Z** undoes a whole drag in one step.
5. Select a key and press **Ease** (or right-click the key › Ease in and out). Play: the movement into the next key
   should start and end more gently.
6. Copy a pose: select nothing (click an empty spot on the stage), **Ctrl+C** at frame 1, go to another frame,
   **Ctrl+V**. Every bone gets a key there.
7. Drag a key along its row to move it to another frame.

## 3. Build your own (8 min)

1. **File › New skeletal animation…**: choose a size, then where to save it.
2. On the empty stage, **drag** to draw the first bone (for a character, from the hips upwards).
3. Keep dragging **from the end of a bone** to add its children: the body, the head, an arm…
4. Select a bone and choose **New drawing…** on the right. Draw the part (it opens in Painting, or Pixel Art for a pixel
   art project), press **Ctrl+S**, then **Back to Bare Bones** at the bottom right. The drawing shows on the bone,
   upright, the way you drew it.
5. In **Setup**, drag the sprite of the selected bone to place it. Drag the bone itself to turn it, and its square tip to
   make it longer.
6. For a sprite that plays by itself (a blink, a wave): choose **New drawing…**, pick **Animated**, draw a few frames in
   Animation Studio, save, come back. In **Animate**, click **○** next to a sprite to show it from the current frame on.
   Choose **Play once** or **Loop**, the **Speed**, and **Restart**.
7. **Export** at the top: an animated GIF, a sprite sheet or PNG files. Open the result: no bones in it.

## 4. Coming and going (2 min)

1. **Esc** goes back to the drawing you had open. Everything there is as you left it.
2. **File › Bare Bones “…”** brings the view back.
3. Close DoodleNook and open the `.dnkbones` file again with **File › Open…**: everything is where it was.

## What to tell me

- Does posing with the pen feel natural: grabbing a bone or a sprite, turning, moving the joint?
- Is the difference between Setup and Animate clear?
- Is the "New drawing → draw → Back to Bare Bones" trip comfortable, or should drawing happen inside Bare Bones?
- Do animated sprites (blink, wave) do what you'd expect when switched on the timeline?
- Anything missing for the characters you actually want to make, or anything that is too much.
