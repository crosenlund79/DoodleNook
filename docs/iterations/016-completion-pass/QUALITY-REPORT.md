# Mission 016 quality report

What was checked, how, and what was found. Measurements are from this mission's Linux sandbox (a cloud VM with
4 logical processors, no GPU) unless they say Windows CI. Failed checks are listed as failed. Nothing is hidden behind
a green build.

## 1. GIF export colours

**Reproduced:** the walk reference over white paper, 8 frames, exported with `GifEncoder` and decoded independently
with Python PIL. The orange limbs (#E89A5E) came out slate (#3C4B61) and the paper came out off-white.

**Root cause:** the palette's median cut split each box at the median of its pixel count. When one colour held more
than half of a box's pixels and sorted last on the split channel (white paper on the red channel), the search loop
ended without finding a split point and fell back to cut = 1. Each of the 255 splits then took one dark colour off the
big box. White, every artwork colour and every blend ended up in a single box, which became one off-white palette
entry. The palette never contained an orange.

**Fix:** a variance-based median cut. The box whose pixels are furthest from their average is split first, on its most
varied channel, where both halves are closest to their own averages. Then three k-means passes refine the palette. Two
related weaknesses went with it:
- the nearest-colour cache keyed colours at 6 bits per channel; it now uses the exact colour;
- colour counting stopped at 100,000 colours, so colours that appeared only in later frames were never counted; past
  65,536 colours they are now counted in small groups that keep their true averages.

Transparency, timing, disposal, LZW and exact pixel-art palettes are unchanged.

**Independent check:** every fixture was exported and then decoded twice, by PIL and by the engine importer. The two
decodes were compared pixel by pixel (0 differences in every fixture), and PIL's decode was compared with the source
frames. "Mean" and "max" are the per-channel error, in levels out of 255, of opaque pixels across all frames.

| Fixture | M015 mean / max | M016 mean / max | Pixels off by > 30 (M015 → M016) |
|---|---|---|---|
| Anti-aliased walk on white, 8 frames | 21.99 / 172 | 0.04 / 4 | 32,905 → 0 |
| Same, dithered | — | 0.05 / 10 | — → 0 |
| Soft gradients, 4 frames (thousands of colours) | 1.75 / 5 | 0.45 / 5 | 0 → 0 |
| Transparent figure, 8 frames | 0.90 / 6 | 0.19 / 3 | 0 → 0 |
| Colour wheel with noise (> 256 colours, nothing dominant) | 6.87 / 21 | 6.40 / 19 | 0 → 0 |
| 12-colour pixel art with transparency, 4 frames | exact | exact | — |

Timing (80–220 ms per frame), disposal (2 with transparency, 1 without), looping and transparency all matched
in PIL. A before/after picture is in `gif-before-after.png`.

**Regression tests:** `GifColourTests` has 8 tests:
- anti-aliased art on white;
- orange and slate on paper;
- transparent art;
- soft gradients, with and without dithering;
- limited-palette pixel art;
- more than 256 colours;
- colours that appear only in a later frame.

Each test decodes with the test suite's own independent decoder and checks that the engine importer reads the same
pixels. Run against the M015 encoder, 5 of them fail: white paper, orange/slate, both gradient variants, and the
later-frame test. The pinned Mission 010 GIF hash in `PixelEquivalenceTests` was updated on purpose; the new bytes
decode within 0.1 levels of the rendered frames on average.

**Speed:** 24 frames at 960 × 540, three runs each.
- Without dithering: 388–500 ms, against 359–552 ms with the old encoder (the same).
- With dithering: 744–775 ms, against 590–719 ms (up to about 20 % slower).

Export runs in the background.

## 2. Reference picker preview

**Reproduced** with a window test: a scene with magenta artwork and an earlier, fully opaque walk reference. In the
picker's preview, 2,128 of the sampled pixels were scene colours, and the earlier reference showed beside the new one.
**Cause:** the preview drew the whole current frame of the document behind the figure.

**Fix:** the preview shows the canvas as a plain page (the paper colour, or the checkerboard without paper) at the same
size and position, with only the chosen cycle on it. After the fix: 0 scene pixels and 0 earlier-reference pixels;
dragging still places the feet on the canvas; the document keeps both references. Before and after:
`reference-preview-before-after.png`.

## 3. Storybook chrome

While the storybook was shown, brush options, the tool strip, the dock, the timeline and the docked animation preview
stayed around it. They are now hidden (the menus and workspace switcher stay). They come back exactly as before after
Esc, Back to drawing, opening a panel's drawing, or choosing a workspace. Tested in the window at 100 % (1440 × 900) and
150 % (1280 × 720), checking every host's visibility and the storybook's width, and reviewed visually.

## 4. Clipboard copy

Copy and Cut now also put the copied pixels (only the copied area) on the Windows clipboard:
- "PNG", which keeps transparency;
- CF_DIBV5, 32-bit with straight alpha;
- CF_DIB, 24-bit on white, for programs that ignore alpha.

The engine's `DibEncoder` round-trips through `DibDecoder` exactly. Ctrl+V inside DoodleNook still pastes DoodleNook's
own copy in place, while a newer copy in another program still wins. If the clipboard can't be written, a message says
so and the copy still works inside DoodleNook. Window tests (with a fake clipboard) cover:
- transparency;
- a selection's crop;
- an in-place paste;
- a write failure;
- an empty layer;
- Copy and Cut on a locked layer.

Cut on a locked layer used to say "Cut" while leaving the pixels; this was found while writing the tests and fixed.
**Real clipboard on Windows CI:** three new smoke checks have the packaged program copy, then read back the raw Windows
formats (PNG and CF_DIBV5 equal the picture exactly; CF_DIB is on white), then paste in place. Runs #44 and later pass
them; a failed smoke check fails the run.

## 5. Real-world import validation

**Palettes:** a folder of 92 files in 5 subfolders.
- GIMP: 40 `.gpl` palettes.
- Inkscape: 23 `.gpl` palettes.
- LibreSprite: 8 `.gpl` palettes. These and the GIMP and Inkscape files are GPL, downloaded into the sandbox for this
  check only and not added to the repository.
- PICO-8 (CC0) colours as `.hex`, JASC `.pal`, RIFF `.pal`, Paint.NET `.txt`, `.ase` and two `.png` styles, plus
  indexed and grey PNG variants. These were written by small independent scripts (Python and PIL), not by DoodleNook.
  The independent ASE library could not be installed in the sandbox, so the ASE file was also written by a script.
- 8 broken files and 2 other files.

| Check | Result |
|---|---|
| First import | 84 palette files found · 69 imported · 9 already in the library · 6 damaged or empty · 4 other files skipped |
| Duplicates across programs | Inkscape's WebHex = GIMP's Web; LibreSprite's db16 = bundled DawnBringer 16; every PICO-8 format = bundled PICO-8 palette |
| Name clashes | 8 (GIMP vs Inkscape: Blues, Gold, …), imported as "Blues (2)" etc. |
| Colours | All 69 imported `.gpl` palettes match an independent Python parse exactly (colour, order, count) |
| Persistence | A fresh library object (as after a restart) holds the same names, colours and order |
| **Defect found** | Inkscape's Munsell chart (5,414 colours) was cut to 1,024 silently and **imported again on every re-import**. Fixed: the summary says "the first 1024 of 5414 colours", and a second import adds 0 |
| **Defect found** | Indexed and grey PNG palettes were refused ("Unsupported PNG layout in project file"). Fixed with `PngCodec.DecodeAny` |
| **Defect found** | Game Edit's palette picker listed only .gpl and .hex. It now lists all six formats |
| Note, not changed | Before importing, the folder summary counts files by extension (86); the result counts actual palettes (84). Here, a README.txt and a photo .png were counted as palette files beforehand |

**Sprite sheets:** sheets made by other artists, all CC0, downloaded for this check only:
- OpenGameArt's "Ars Notoria" hero spritemaps (3 sheets, 46 × 50 grid);
- the characters from "A platformer in the forest" (2 sheets, 32 × 32, with unused cells) and its tileset;
- Kenney's 1-Bit Pack tile sheets, packed and with 1 px spacing.

| Check | Result |
|---|---|
| Cutting with the true grid | Exact on all 10 sheets; empty cells found correctly (19 per character sheet); nothing left over |
| First guess, M015 | 0 of 10 right |
| First guess, M016 | 7 of 10 right: all 5 character sheets and both spaced Kenney sheets. Two fixes: a sprite may touch its cell border (standing on the bottom row) as long as nothing crosses it, and 1–2 px transparent gaps are found when no gapless grid fits |
| Still needing the size typed in | Packed tile sheets whose tiles touch (one gets a wrong but plausible guess, one opaque sheet stays a single frame) and an irregular tileset |

**Animation import:**
- GIF and APNG files from other tools were decoded by the engine and compared with PIL frame by frame: 0 pixels differ
  and the durations match. The files were the CC0 `run.gif`, a PIL GIF with disposal 2, two PIL APNGs (one blended) and
  DoodleNook's own exports after the GIF fix.
- A PIL-made PNG sequence mixing indexed and RGBA pictures, imported through the window, came in as 6 frames, in order,
  pixel for pixel.

## 6. Sit down, Stand up, Laughing

All three were reviewed in every view (each drawing plus an onion overlay). The body motion of all three reads
naturally and was kept. Two arm details looked visibly unnatural and were changed:
- **Laughing:** in five of eight drawings the left hand hung forward in mid-air between slaps. It now rests on the
  thigh and lifts before the next slap.
- **Sit down:** once seated, the hands reached forward past the knees. They now rest on the thighs, and Stand up, which
  plays Sit down backwards, now pushes up from them.

No new references, poses or rigs; all 32 reference tests pass. Before and after: `motion-before-after.png`.

## 7. Workflow audit (A–H)

`WorkflowAuditTests` drives the real window through every workflow:

| Workflow | What it does | Result |
|---|---|---|
| A | Draw, undo and redo, save, reopen, export PNG and JPEG | Pass |
| B (and E) | GIF out and back in (frames, timing, colours); sprite sheet with JSON out and back in | Pass |
| C | Reference placed, adjusted, undone, saved, reopened, left out of exports when hidden | Pass |
| D | Game Edit: save with backup, copy from one game image into another | Pass |
| F | Storybook with the drawing underneath | **Defect found and fixed** |
| G | Clipboard between pictures and out to other programs | Pass |
| H | Palette folder imported; one palette locks a pixel-art picture | Pass |

**F, the defect:** keys the storybook didn't handle went on to the hidden drawing. Delete, Ctrl+V, Ctrl+J and others
changed it out of sight (the test saw a layer appear), and the Edit, Image, Layer and Select menus did the same. Now:
- no shortcut reaches the hidden drawing;
- Ctrl+N and Ctrl+O go back to drawing first;
- Ctrl+S says the storybook saves itself;
- a drawing command chosen from a menu goes back to the drawing first, where its effect is seen;
- Edit › Undo and Redo act on the storybook, as Ctrl+Z does.

Some audit failures were mistakes in the audit itself, not app defects, and the audit was corrected:
- coordinates converted twice;
- a list button scrolled out of view;
- the GIF's default "no paper" setting;
- reading palette-locked soft edges without the palette conformer.

**Observations left for the owner (deliberate behaviour, not changed):**
- A visible reference layer is included in exports (Mission 013's documented rule); hide it to leave it out.
- Selecting counts as an unsaved change (the selection is part of the picture's history), so after save → select →
  copy, opening the next game image asks to save.
- The Game Edit catalog keeps the drawing tools around it; the storybook no longer does.
- Under Palette Lock, soft edges are stored as the palette colour at their own alpha and read back exactly by the
  conformer. A plain straight-alpha conversion is off by one level, which is how Mission 007 designed it.

## 8. UI review at 150 %

`UiReviewScreenshots` captured, at 1280 × 720 at 150 % (the Kamvas 13 at Windows' 150 %):
- all six workspaces;
- New picture, Export animation, Insert reference cycle, Export as PNG, Preferences, Customize, Palettes and Import
  sprite sheet;
- the storybook and its New drawing dialog.

It checks that each dialog card and its buttons are inside the window and visible, and that Esc closes each one.

**Found and fixed:**
- **Customize Animation Studio** was taller than the window, so its Done and Reset buttons were cut off. The dialog
  frame now keeps the title and buttons in view for every dialog, and the part between them scrolls when the window is
  too short.
- **Insert reference cycle:** the line saying what will happen to the timeline was scrolled off below the controls. It
  now stays in view above the buttons.

The workspaces themselves showed no clipped bars, overlaps or unreadable labels at 150 %. No styling was changed.

## 9. Document safety

`DocumentSafetyTests`:
- closing with unsaved changes asks; Cancel keeps everything, and Save writes the picture and then closes;
- a missing file gives a message and changes nothing;
- File › New asks first, and Cancel keeps everything;
- switching through all six workspaces keeps the picture, its changes and its undo history without asking;
- in the storybook, a panel drawing's unsaved changes survive going back to the storybook (the chip says "not saved
  yet"), autosave writes a recovery copy while the storybook is shown, opening another panel asks first (Cancel keeps
  them; Save writes them and then opens).

Saves still go to a temporary file first and replace the original only when complete. A real write failure could not
be injected in this sandbox, because the tests run as root and file permissions don't stop root writing, so that
guarantee rests on the existing design and wasn't fault-tested in Mission 016.

## 10. Performance

See README, section "Performance".
