# Mission 016 — The Completion Pass

No new creative features. This mission fixed what was reported, checked the existing workflows the way they are used,
with real files from other programs, and fixed what those checks found.

**Status:** fixed and verified by automated tests on Linux and Windows CI, independent decoders, real files made by
other people and programs, and the packaged program's smoke test (see [Verification](#verification)). Awaiting your
playtest ([PLAYTEST.md](PLAYTEST.md)). Every test used a mouse or simulated input; the pen hasn't been tested.

The details, numbers and every check (including the ones that failed first) are in
[QUALITY-REPORT.md](QUALITY-REPORT.md). The project-wide [playtest status](../../PLAYTEST-STATUS.md) and the
[repository consolidation recommendation](../../REPOSITORY-CONSOLIDATION.md) were written in this mission too.

## What changed for you

| | Before | Now |
|---|---|---|
| **GIF export** | Anti-aliased drawings on white paper turned slate grey (an orange shirt became #3C4B61) | Colours as drawn: average error 0.04 levels on the reported walk, where it was 22 |
| **Reference picker** | Its preview showed the scene behind the figure, so references inserted earlier looked like part of the chosen cycle | Only the chosen cycle, on a plain page the size of the canvas; dragging still places the feet |
| **Storybook** | Brush options, tools, dock and timeline stayed around it, and shortcuts or menu commands could change the drawing hidden underneath | It has the window to itself below the menus. Nothing reaches the hidden drawing unseen, and everything comes back when you return to drawing |
| **Copy** | Only inside DoodleNook | Also to other programs, with transparency where they support it (PNG, 32-bit bitmap, plus a bitmap on white) |
| **Palette folders** | A 5,414-colour palette was cut to 1,024 silently and imported again every time; indexed PNG palettes were refused | The summary says what was kept, and a second import adds nothing; indexed and grey PNGs import |
| **Sprite sheets** | The first guess was wrong on all 10 real sheets tried | Right on 7 of 10: characters standing on their cell's edge, and sheets with gaps between frames |
| **Sit down, Stand up, Laughing** | Hands hanging in mid-air (Laughing) or reaching past the knees (Sit down) | Hands rest on the thighs |
| **Dialogs at 150 %** | Customize's buttons were cut off on the Kamvas; the reference dialog's summary scrolled away | Title and buttons always in view; the part between them scrolls when needed |
| **Cut on a locked layer** | Said "Cut" but left the pixels | Copies, and says the layer is locked |

![GIF export before and after](gif-before-after.png)

## 1. GIF colour corruption

**Cause:** the GIF palette's median cut. When one colour held more than half of a box's pixels and sorted last on the
split channel, the split fell back to taking off one colour. White paper is exactly that colour on the red channel.
Each of the 255 splits took one dark colour off, while the paper, the artwork's colours and all their blends were
averaged into one off-white entry. The palette never contained an orange.

**Fix:** a variance-based median cut (the box furthest from its own average is split first, where both halves are
closest to theirs), then three refining passes. Colours are matched exactly, and counting no longer stops after
100,000 colours.

Transparency, timing, disposal, looping and exact pixel-art palettes are unchanged; DoodleNook's own GIF import reads
the new files pixel for pixel as PIL does. Eight regression fixtures (`GifColourTests`) cover:
- anti-aliased art on white, including the orange-on-slate case;
- transparent art;
- soft gradients, dithered and not;
- limited-palette pixel art;
- more than 256 colours;
- colours that appear only in a later frame.

Five of the eight fail on the old encoder.

## 2. Reference picker preview

The preview drew the document's whole current frame behind the figure. It now draws a plain page in the canvas's paper
colour (or the checkerboard) at the canvas's size, with only the chosen cycle on it. The document can hold any number
of references, as before.

![Reference picker before and after](reference-preview-before-after.png)

## 3. Storybook chrome, and the drawing underneath

- While the storybook is shown, the drawing workspace's options bar, tool strip, dock, timeline, docked preview and
  view chip are hidden. The menus and workspace switcher stay.
- They come back as they were after Esc, **Back to drawing**, opening a panel's drawing, or choosing a workspace.
- Found by the workflow audit: keys and menu commands could change the drawing hidden underneath. Now:
  - no shortcut reaches it;
  - Ctrl+N and Ctrl+O go back to drawing first;
  - Ctrl+S says the storybook saves itself;
  - a drawing command chosen from a menu goes back to the drawing first, so you see what it does;
  - Edit › Undo and Redo act on the storybook, as Ctrl+Z does.
- A floating preview window stays where you put it, as with Tab.

## 4. Copy to other programs

**Edit › Copy** (Ctrl+C) and **Cut** (Ctrl+X) put the copied pixels (only the copied area) on the Windows clipboard:
- "PNG", which keeps transparency (Paint.NET, GIMP, Krita, browsers, Office);
- a 32-bit bitmap with alpha;
- a bitmap on white, for programs that ignore transparency and would otherwise show it black.

The pictures are prepared off the drawing thread. Ctrl+V inside DoodleNook still pastes DoodleNook's own copy in
place; something copied in another program afterwards still wins. If Windows can't take the picture, a message says so
and the copy still works inside DoodleNook.

## 5. Real files from other programs

Checked with palette files from GIMP, Inkscape and LibreSprite, sprite sheets by other artists (OpenGameArt, CC0;
Kenney's 1-Bit Pack, CC0), and GIF, APNG and PNG sequences from other tools. The files were used for checking only and
weren't added to the repository. Found and fixed:
- **Long palettes:** a palette keeps at most 1,024 colours. The import now says so ("the first 1024 of 5414 colours")
  and recognises the palette when the folder is imported again; before, it added the palette again every time.
- **Indexed and grey PNG palettes** (common for small palette pictures) import. They were refused with a message about
  a "project file".
- **Game Edit's palette picker** offered only .gpl and .hex; it now offers all six formats.
- **Sprite sheet first guess:** a sprite may touch its cell border (characters stand on the bottom row) as long as
  nothing crosses it, and a gap of 1–2 transparent pixels between frames is found. Sheets whose tiles touch, with no
  transparency to go by, still need the frame size typed in.
- **Checked, no defect found:**
  - animation import: GIF and APNG decode exactly as PIL decodes them;
  - a mixed indexed/RGBA PNG sequence imports pixel for pixel;
  - palette colours match an independent parser in every one of 69 palettes;
  - cutting sheets is exact.

## 6. Sit down, Stand up, Laughing

The body movement of all three reads naturally and was kept. In Laughing, the left hand now rests on the thigh between
slaps and lifts before the next one, instead of hanging in mid-air. In Sit down, the hands rest on the thighs once
seated, so Stand up, which plays it backwards, pushes up from them. No new references or rigs.

![Sit down and Laughing before and after](motion-before-after.png)

## 7. Workflow audit, UI review and document safety

- **Workflow audit (A–H):** draw and export, an animation taken apart and put back, references, Game Edit, sprite
  sheets, Storybook, the clipboard and personal palettes, each driven end to end through the real window. One defect:
  the storybook issue above.
- **UI review at 150 %:** all six workspaces and the main dialogs were captured at the Kamvas's 150 %; every dialog
  fits, keeps its buttons visible and closes with Esc. Two fixes:
  - Customize's buttons had been off screen;
  - the reference dialog's summary had scrolled out of view.
- **Document safety:** closing, opening another file, a missing file, switching workspaces, the storybook's panel
  drawings and autosave were checked; nothing unsaved is lost without asking. No defect found.

## Verification

| | Engine tests | App tests (real window) | Smoke test of the packaged program |
|---|---|---|---|
| Linux (development) | RESULTS-LINUX-ENGINE | RESULTS-LINUX-APP | RESULTS-LINUX-SMOKE |
| Windows CI | RESULTS-WIN-ENGINE | RESULTS-WIN-APP | RESULTS-WIN-SMOKE |

New in this mission:
- **Engine tests:** GIF colours 8, DIB encoder 2, PNG decoding 3, palette limit 1, sprite sheet guess 1.
- **App tests:** reference preview 1, storybook chrome 1, clipboard 3, workflow audit 7, UI review 1, document safety 4.
- **Smoke test:** 3 real Windows clipboard checks.

## Performance

PERFORMANCE-SECTION

## Known limitations

- **Sprite sheets** whose tiles touch, with no transparent line or gap between them, aren't guessed: type the frame
  size (or *Split into* columns × rows).
- **GIF** is still limited to 256 colours per animation and to on/off transparency (a soft shadow below 50 % opacity
  disappears). Dithering, on by default, hides the banding in soft gradients.
- **Copy to other programs** gives them a flat picture of the copied pixels on that layer, not layers.
- **The Game Edit catalog** still keeps the drawing tools around it; the storybook no longer does (a decision for you).
- **A failed save** can't be simulated in the test sandbox. The protection (a temporary file first, replaced only when
  complete) is unchanged from earlier missions.
