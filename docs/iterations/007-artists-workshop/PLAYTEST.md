# Mission 007 playtest: the artist's workshop

A relaxed weekend session: the first round takes about 45 minutes, then play as long as you like.
You don't need the tablet; if you have it, use it (it's still untested with a real pen, so tell us how it feels).
Tick what works, and above all **note any friction**: anything fiddly, slow, surprising, cluttered or hard to find.
The defaults are first guesses; your preferences decide. Nothing you write down turns into changes until you say so.

## Before you start

1. Download **DoodleNook-win-x64** from the newest green run on [this branch](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F007-artists-workshop), extract it and start `DoodleNook.exe` (*More info → Run anyway* if Windows asks).
2. Press **Alt+3** for Painting. **F1** lists every shortcut.
3. The `Samples` folder next to the exe has a starter pack, a palette and a reference board for steps 3–6.

## Suggested first session

### 1. Try the new brushes

- [ ] Open the brush picker (the brush chip at the top left, or the Brushes panel on the right). Brushes are grouped
      into Sketch, Ink, Paint and *Texture & stamps*.
- [ ] Draw a few strokes with each kind: **Graphite pencil**, **Charcoal**, **Dry ink brush**, **Soft paint brush**,
      **Watercolour wash** (layer two strokes to see it deepen), **Spatter**, **Grass tufts**, **Leaves**.
- [ ] Your old favourites (Sketch pencil, Ink pen, Fine liner…) should feel exactly as before.
- [ ] Type in *Find a brush*. Right-click a brush › *Add to favourites*: it moves to the top under ★.
- [ ] Notice how they feel: light versus firm pressure, slow versus fast strokes.

### 2. Customize one brush

- [ ] Right-click **Sketch pencil** › *Duplicate*. The copy is now your brush: open *Brush Laboratory…* at the bottom of
      the picker (or right-click the copy › *Edit in the Brush Laboratory…*).
- [ ] In the Laboratory, scribble in the light area on the left: it never touches your picture.
      *Example* draws a standard stroke; *Clear* wipes it.
- [ ] **Texture**: choose another texture (try *Rough paper* or *Canvas weave*) and change its strength and scale.
- [ ] **Pressure**: move *Response* towards "light touch" or "firm hand" and watch the little graph.
- [ ] Give it a name (the box at the top) and **Save**. *Use this brush*, draw with it.
- [ ] Close DoodleNook, start it again: your brush is still in *My brushes*.

### 3. Create a stamp

- [ ] Draw a small object (a flower, a rock, a sprite) on its own layer.
- [ ] Select it with the lasso and choose *Edit › Save selection as stamp…* (or, with nothing selected, *Save layer as
      stamp…*). Give it a name and a collection.
- [ ] Press **S** (the Stamp tool): the stamp follows the pen as a preview. Click to place it a few times. Try
      **[** and **]** for size, the rotation and opacity sliders, and the two flip buttons. **Ctrl+Z** undoes one stamp.
- [ ] Optional: right-click your stamp in the stamp picker › *Make a brush from this stamp*, and paint a trail of it.

### 4. Try patterns

- [ ] Draw something small that could repeat, select it and choose *Edit › Save selection as pattern…*. The preview
      shows it tiled, so you can see the seams.
- [ ] Or use a built-in one: choose the paint bucket (**G**) and switch it to **Pattern** in the options bar. The
      picker shows the pattern tiled.
- [ ] Click inside an outlined area (or make a selection first): it fills with the pattern. Try scale, rotation,
      offset and opacity. One Ctrl+Z undoes the fill.
- [ ] Optional: *Window › Creative library… › Import a pack…* and choose `Samples\Woodland starter pack.dnkpack`.
      You get a *Pixel stones* tile, two stamps, three brushes (*Pine needles*, *Stipple shading*, *Cobbled paint*)
      and a palette.

### 5. Make a palette

- [ ] *Window › Palettes… › From picture…* takes colours from your painting; *Image file…* there takes them from any
      picture (a photo you like) without opening it.
- [ ] Move *How many* and watch the proposal change; nothing is saved until you click *Save as palette*.
- [ ] *Use this palette*: the swatches in the Colour panel now show it. Paint with it.
- [ ] Optional: in Pixel Art (**Alt+5**), choose the palette from the palette column's menu: its exact colours become
      the sprite's palette. *Import…* also reads `Samples\Autumn walk.gpl`.

### 6. Test references

- [ ] *Window › Reference board*. Add a picture or two with **+** (or open `Samples\Sample references.dnkboard` from
      the board's ··· menu).
- [ ] Move and resize pictures, zoom with the wheel, drag the board by its title and resize it by its corner.
      Try the grey or light background.
- [ ] Draw on your picture while the reference stays visible. Pick a colour from a reference with **Alt**+click.
- [ ] Export your picture (*File › Export*): the references are not in it. Hide the board (×) and draw where it was.
- [ ] Restart DoodleNook and show the board again: your pictures are still there.

### 7. Test animation export (the freeze from last time)

- [ ] *File › New animation…*, Hand-drawn, **1920 × 1080**, **21 frames** (or open your own long animation).
      Draw a few marks on several frames.
- [ ] *File › Export animation…*. Switch between **Animated GIF**, **Sprite sheet** and **PNG sequence** many times,
      in any order. The dialog should stay responsive every time.
- [ ] Export the GIF and open it in a browser: all frames, right timing.

### 8. Evaluate ease of use

Take a step back and think about these. Short notes are perfect.

## Questions

1. Does the default interface remain uncluttered?
2. Are everyday actions immediately available?
3. Are advanced tools easy to discover?
4. Is the Brush Laboratory understandable?
5. Can brushes be customized without feeling overwhelmed?
6. Does saving creative assets feel effortless?
7. Are asset collections easy to browse?
8. Do the features feel integrated?
9. What takes too many clicks?
10. What feels unnecessary?
11. **What would make DoodleNook more enjoyable?**

## If you have more time

- *Window › Customize workspace…*: add the Stamp tool to Quick Sketch, Editing or Animation Studio, show your
  favourite brushes as one-click buttons, or keep the reference board on, per workspace.
- In Animation Studio, paint with *Grass tufts* on one frame: only that frame changes. (The Stamp tool can be added
  there too, through *Customize workspace*.)
- *Window › Creative library…* shows where your assets are kept and exports everything as one backup pack.
- If something ever goes wrong, DoodleNook now tells you and keeps your work safe; the details are in
  `%LOCALAPPDATA%\DoodleNook\Logs\errors.log`. Please send that file along with your notes.

Anything else: write it down as it happens. Small irritations are exactly what we want to hear about.
