# Mission 004 playtest: DoodleNook on the Huion Kamvas 13

About 15–20 minutes. You decide whether the drawing feels right — automated tests can't.

## Before you start

1. In the Huion driver (HuionTablet), keep **Enable Windows Ink** switched on. Without it, Windows reports the pen as a plain mouse with no pressure.
2. Download `DoodleNook-win-x64` from the GitHub Actions run (you must be signed in to GitHub), extract it, and double-click `DoodleNook.exe`.
3. If Windows shows "Windows protected your PC", click **More info → Run anyway**. The test build is unsigned.

## A. First launch

- [ ] DoodleNook opens.
- [ ] It looks like the Mission 003 Quick Sketch workspace: warm dark bar, tool strip on the left, white canvas.
- [ ] The buttons are comfortable to hit with the pen at your Windows scaling (100, 125 or 150 %).
- [ ] You can drag the window onto the Kamvas and maximise it. F11 gives full screen; Tab hides the interface.

## B. First drawing

With the default brush (Sketch pencil), draw:

- [ ] slow curves
- [ ] fast sweeping curves
- [ ] circles
- [ ] short hatching strokes
- [ ] tiny details (zoom in with the mouse wheel or Ctrl++)
- [ ] a small freehand sketch

Look for gaps, jumps, jagged edges, blobs, or delays between the pen and the ink. Note which ones you see.

## C. Pressure

- [ ] Draw one stroke that starts light, gets heavier and ends light. The line should swell and taper.
- [ ] Press **F12** for pen diagnostics. **Input** should say **Pen (Windows Ink)**, and **Pressure** should change as you press.
- [ ] After the stroke, the graph shows your pressure across that stroke.
- [ ] Try **Pressure feel: Soft / Normal / Firm** in the brush pop-over (the chip left of *Size*). Which one feels natural?

If Input says **Mouse** while you use the pen, Windows Ink is off in the Huion driver. Turn it on and restart DoodleNook.

## D. Responsiveness

- [ ] The ink follows the pen tip and feels immediate.
- [ ] It doesn't feel overly smoothed or artificial (Smoothing is 0 % by default).
- [ ] A fast stroke doesn't leave the ink visibly behind the pen.
- [ ] A stroke starts the instant the nib touches, with no press-and-hold circle.

## E. Everyday actions

- [ ] Brush size: the Size slider, the `[` and `]` keys, or right-drag on the canvas (or a pen side button set to right-click).
- [ ] Opacity slider.
- [ ] Colour: the round chip or the left colour square. Hold **Alt** and tap the canvas to pick a colour.
- [ ] Eraser: **E**, the **Erase** button or the eraser tool. **B** returns to the brush.
- [ ] Undo and redo: Ctrl+Z and Ctrl+Shift+Z, or the arrow buttons. One stroke is one undo step.
- [ ] Pan with Space + drag, the hand tool or the middle button. Zoom with the wheel. **Fit** and **100 %** are in the bottom-left chip.
- [ ] Save (Ctrl+S), close DoodleNook, then reopen the PNG (Ctrl+O). It should look the same.

## F. Overall feeling

- Does it feel natural?
- What feels particularly good?
- What feels awkward?
- What would you change first?
- Would you enjoy continuing to draw in DoodleNook?

## If something seems wrong

Note what you did, what you expected and what happened. A screenshot with the **F12** panel open helps most, especially its *Input*, *Pressure*, *Events* and *Buttons* lines.

The Kamvas's express keys and pen side buttons send whatever the Huion driver assigns. The **Buttons** line shows what DoodleNook actually receives.
