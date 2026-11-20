# Mission 008 playtest: the designer's toolkit

A relaxed session: the exercises below take about 45 minutes, then play as long as you like. You don't need the
tablet; if you have it, use it (it's still untested with a real pen, so tell us how it feels). Tick what works and,
above all, **note any friction**: anything fiddly, slow, surprising, cluttered or hard to find. Your experience decides
what Mission 009 does, so small irritations are exactly what we want to hear about. Nothing you write down turns into
changes until you say so.

## Before you start

1. Download **DoodleNook-win-x64** from the newest green run on [this branch](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F008-designers-toolkit), extract it and start `DoodleNook.exe` (*More info → Run anyway* if Windows asks).
2. Press **Alt+3** for Painting. **F1** lists every shortcut.
3. First look at Painting and Quick Sketch (**Alt+2**) as they open: they should feel like Mission 007, with one new
   tool (**T**, Text) in Painting.

## Exercises

### 1. A game-style title with editable text

- [ ] *File › New picture…* (any size, e.g. 1600 × 1000). Paint or fill a background.
- [ ] Press **T** and click near the top. Type a title (Enter starts a new line). **Ctrl+Enter** or **Esc** finishes.
- [ ] In the options bar: choose a font (your computer's fonts are all there), make it bigger, **B**old, centred.
- [ ] *Effects ▾*: add an **outline** and a **shadow**; try their colour, width, distance and softness.
- [ ] Press **V** (Move) and drag the title: move it inside, scale it at a corner, turn it just outside a corner
      (Shift for 15° steps). *Align ▾ › Centre* puts it in the middle of the picture.
- [ ] Click the title again with **T** and change a word. Each change is one **Ctrl+Z**.

### 2. A button or panel with editable shapes

- [ ] Press **U** (Shapes). In the options bar switch **Pixels → Editable** and choose the rounded rectangle.
- [ ] Drag a button. Fill is your foreground colour; switch on *Outline* (your background colour) and change its width
      and the corners.
- [ ] *Effects ▾ › Shadow* for a soft shadow.
- [ ] Add a label with **T** on top of it, then the label's ⋯ › *Centre on the layer below*.
- [ ] Move, resize and turn the button with **V**; watch it snap to the canvas centre and to the label (pink line).
      **Ctrl+J** duplicates it for a second button; the arrow keys nudge.

### 3. A perspective object with guides

- [ ] **Ctrl+R** shows the rulers. Drag from the top ruler down onto the picture for a horizon guide.
- [ ] Choose the brush, click the **▾** next to the mirror button and pick **2-point**.
- [ ] With **V**, drag the numbered points onto your horizon.
- [ ] With the brush, draw the edges of a box: strokes follow the line towards a vanishing point, or a vertical, whichever
      matches the direction you start in. Hold **Shift** for a straight line.
- [ ] Set perspective back to **Off** and draw freely: nothing should hold your strokes any more.
- [ ] Optional: try **radial symmetry** (✳) in the same pop-over, and drag its centre (✛) with **V**.

### 4. Organise a character's layers into groups

- [ ] Paint a simple character on three or four layers (head, body, arms…).
- [ ] Choose one and press **Ctrl+G**. Drag the others onto the group's row. Rename the group (double-click).
- [ ] Fold and unfold it (the arrow), hide it with the eye, lower its opacity.

### 5. A mask: paint without erasing

- [ ] Choose a layer and click the **mask button** under the layers.
- [ ] Paint with **black** to hide part of the layer, **white** (or the eraser) to bring it back. The chip at the top of the
      canvas and the ring on the mask thumbnail show you are painting on the mask.
- [ ] Click the layer thumbnail to paint on the layer again; **Shift+click** the mask thumbnail to switch the mask off
      and on.

### 6. Apply and later edit a colour adjustment

- [ ] Click the **◐** button under the layers and choose *Hue / Saturation* (or Levels, Colour balance,
      Brightness / Contrast). Change the colours of everything below it; OK.
- [ ] Hide it, show it, lower its opacity. Double-click its ◐ symbol in the layers panel and change it again.
- [ ] Put one inside your character's group: it changes only the character.

### 7. Save and reopen

- [ ] *File › Save as…* a `.dnk`, close DoodleNook, start it again and open the file: text and shapes are still
      editable, groups, masks, adjustment layers and guides are all there.
- [ ] *File › Export as PNG…*: guides, rulers and perspective lines are not in the picture.

## Ask the owner

1. Were the new tools easy to discover?
2. Did they feel intuitive?
3. Was anything unnecessarily visible?
4. Did any operation take too many clicks?
5. Did the added capabilities interfere with normal drawing?
6. Was the canvas still the focus?
7. Which tools would you actually use frequently?
8. Which tools should stay hidden by default?

## If you have more time

- In Animation Studio, *Window › Customize workspace…* › *Text tool*, then add a title to an animation: it shows on
  every frame and the timing doesn't change.
- Lock a picture to a palette and add text and shapes: they use only the palette's colours.
- Try the defaults we guessed: should the Shapes tool start on *Editable*? Is 48 px a good first text size? Are the
  handles easy to hit with the pen?
- If something goes wrong, DoodleNook tells you and keeps your work safe; the details are in
  `%LOCALAPPDATA%\DoodleNook\Logs\errors.log`. Please send that file along with your notes.
