# Mission 017 playtest — The Second Pass

About 20 minutes. Nothing new to learn: these are small things that should now simply behave. Use the Kamvas at your
usual 150 % where you can.

## 1. "· edited" (1 min)

1. Open one of your pictures. The menu row shows its name and size.
2. Draw one stroke. "· edited" should appear as soon as you lift the pen.
3. Ctrl+Z: it should disappear (you're back to the saved picture). Ctrl+Shift+Z brings it back; Ctrl+S takes it away.

## 2. When a save can't be written (2 min)

1. Save a picture, then in File Explorer right-click its `.dnk` › **Properties** › tick **Read-only** › OK.
2. Back in DoodleNook, draw something and press Ctrl+S. The message should say the file on disk was not changed and
   suggest Save As; the picture stays "· edited".
3. Untick Read-only (or use Save as…): it should save as usual.

## 3. After a crash (3 min)

1. Draw on a picture and wait a minute (autosave keeps a recovery copy; Preferences sets how often).
2. End DoodleNook in Task Manager (**End task**).
3. Start DoodleNook again: it should offer **Recover unsaved work?** with the picture's name. Recover, then Ctrl+S.

## 4. Dialogs and keys at 150 % (3 min)

1. **Image › Canvas size…**: the chosen anchor cell should be clearly highlighted; click another and the highlight
   moves.
2. **Image › Colour balance… (Ctrl+B)**: are the three labels ("Cyan ↔ Red", "Magenta ↔ Green",
   "Yellow ↔ Blue") fully readable?
3. **F1**: try a few of the keys it lists in the workspaces you use. Anything that doesn't do what the list says?

## 5. Storybook from Quick Sketch (2 min)

1. In Quick Sketch, open a storybook with **☰ › Open…** (choose a `.donkbook`).
2. The drawing bar should step aside while the storybook is shown, and come back with Esc.

## 6. Small things from the workflow audit (4 min)

1. **Brush Laboratory:** drag a slider such as Size or Hardness; the number beside it should follow the knob. Rename the brush
   and Save: the options bar should show the new name at once.
2. **Text:** type some text and press **Ctrl+Enter**; it should finish (not add a line).
3. **Stamp tool:** press [ and ]; the message should give the stamp's size.
4. **Liquify:** push something, Ctrl+Z; the message should say it took back the Liquify stroke.
5. **Pixel Art:** choose another palette in the palette ⋯ menu. The picture should show "· edited", Ctrl+Z should bring
   the old palette back, and the menu's tick should be on the picture's palette.
6. **Menus:** open Edit and View. Is every entry readable, with nothing running into its shortcut?

## 7. Your old files (2 min)

Open a few `.dnk` files you made with earlier versions, including an animation and, if you have one, a palette-locked
pixel-art file. They should look exactly as before.

## What to tell me

- Anything in these seven areas that still looks or feels wrong.
- Anything at 150 % on the Kamvas that is cut off, crowded or hard to read.
- Pen behaviour on the Kamvas (only you can check that).
