# Playtest status

Mission 016, 22 November 2026; updated in Missions 017 (23 November) and 018 (24 November 2026). What the owner has actually reported, what the automated checks verify, and what only
hands-on use can confirm. It is meant to make the next playtest short and focused.

**Rules for this page.** Only owner feedback that is written down is recorded: in the repository, in the Mission 016
assignment, or in the owner's messages during development. Nothing is inferred. General praise isn't read as approval
of a particular mission. No mission has a formal playtest result on record: every mission README still says it is
awaiting playtest. Real pen behaviour on the Huion Kamvas 13 can only be confirmed by the owner, so it is never marked
verified here.

## What the owner has reported

| Observation | Source | What happened since |
|---|---|---|
| The core drawing experience works extremely well; the owner enjoys basic drawing | Mission 016 assignment | Protected: no change in Mission 016 touches the brush engine or pen input. The Mission 011 benchmarks were rerun against Mission 011 (QUALITY-REPORT, section Performance). |
| Extreme pressure resolution doesn't matter | Mission 004 README ("You said…") | The decision for Windows Ink through Avalonia stands. |
| Alt+1 brush sizing was difficult | Mission 016 assignment | Reworked in Mission 010. **No owner feedback on the rework is recorded.** |
| External image pasting was requested | Mission 016 assignment | Built in Mission 012. Mission 016 adds the other direction: Copy now reaches other programs. |
| Palette selection and importing were expanded | Mission 016 assignment | Mission 012 (bundled palettes), Mission 015 (folder import). Mission 016 checked them with real palette files and fixed what it found. |
| Animation references first looked artificial; natural-motion refinements improved them; the newer animations look much better | Mission 016 assignment | The refinement pass and Expansion Pack 1. Mission 016 only adjusted the hands in Sit down, Stand up and Laughing (README, section 6). |
| "The new stuff looked good" | Owner's message after Expansion Pack 1 | General praise of Expansion Pack 1, recorded as such. It isn't treated as acceptance of any particular cycle or mission. |
| The reference picker seemed to show scene content inserted earlier | Mission 016 assignment | Reproduced and fixed in Mission 016: the preview shows only the chosen cycle. |
| The Mission 003 look is the approved visual foundation | CLAUDE.md ("Keep the approved look") | Kept. Mission 016 changes no visual style. |
| When adjusting brush size, show the pixel size in a pop-up, because the pen covers the view; an easy zoom drag in the drawing area (hold Z); the same for brush and eraser size; then: "when either is held a popup appears where you can adjust zoom, brush size, opacity, smoothing… once you let go of the key the change is made" | Owner's notebook page, sent 24 November 2026 | Built in Mission 018: hold B, E or Z for the quick adjust card at the pen; size read-outs above the pen. **Awaiting your playtest.** |

## By area: automated verification and what still needs hands-on checks

"Automated" means the Windows CI run (engine and window tests, plus the smoke test of the packaged program on Windows),
the same tests on Linux, and, for Mission 016, independent checks recorded in the QUALITY-REPORT.

| Area (mission) | Owner observation on record | Verified automatically | Needs a hands-on check by the owner |
|---|---|---|---|
| Drawing with the pen, Quick Sketch (004) | Works extremely well | Stroke engine, pressure curve and undo tests; smoke test on Windows | Pen pressure, tilt and feel on the Kamvas (only the owner can confirm) |
| Layers, tools, six workspaces (005) | — | Window tests for each workspace; screenshots at 100 % and 150 % (Mission 016 UI review) | Daily use at 150 % on the Kamvas: anything in the way? |
| Animation Studio (006) | — | Timeline, onion skin, playback and export tests | Drawing frame by frame with the pen |
| Brushes, stamps, palettes, Palette Lock (007) | Palette work expanded (above) | Engine and window tests | Brush feel |
| Text, shapes, rulers, masks (008) | — | Engine and window tests | — |
| Game Edit Mode (009) | — | Catalog, save, backup tests; Mission 016 audit D | A real game folder |
| Alt+1, smudge, clone, liquify (010) | Alt+1 was difficult (before the rework) | Tests | **Is Alt+1 comfortable now?** |
| Performance (011) | — | Benchmarks rerun in Mission 016; a long-session measurement since Mission 017 | Large pictures on the owner's PC |
| Paste, animation import, palettes, Quick Start (012) | Pasting was requested | Fake-clipboard tests; the real Windows clipboard in the smoke test | Pasting from the owner's usual programs |
| Reference cycles (013, refinement, Expansion Pack 1) | Looked artificial, then much better | Pose tests; contact sheets reviewed in Mission 016 | Sit down, Stand up, Laughing after the Mission 016 hands change |
| Guided tutorial (014) | — | Tutorial tests, smoke test | Whether the tutorial is helpful |
| Sprite sheets, live preview, palette folders, Storybook (015) | — | Tests; Mission 016 real-file validation; audit B, F, H | Storybook on the Kamvas; a sprite sheet from the owner's own game |
| **Mission 016 fixes** | GIF colours, picker preview (reported) | See the QUALITY-REPORT | The four checks below |
| **Mission 018 Quick Adjust** | Requested in the notebook page (above) | Window tests, the real program with X11 input, the smoke test | Hold B / E / Z with the pen: delay, placement, size of the card |
| **Mission 017 fixes** | — | See [its QUALITY-REPORT](iterations/017-second-pass/QUALITY-REPORT.md): every menu command at 150 %, the F1 keys, files from earlier versions, the real program crashed and its saves made to fail, a workflow audit with real input, exports read by Pillow, a long session | The four checks after the Mission 016 ones |

## The four Mission 016 checks only the owner can do

1. **GIF export:** export a coloured animation on white paper as a GIF and open it in a browser. The colours should
   match the drawing (they used to turn slate grey).
2. **Copy to other programs:** select part of a drawing, Ctrl+C, paste into the program you use most (Paint, a
   browser, Discord, Office). Transparency should survive where that program supports it.
3. **Storybook:** with a storybook open, no drawing tools, dock or timeline should be in the way; Esc brings them back.
4. **Reference picker:** with a reference already in the scene, Animation › Insert reference cycle… should show only
   the chosen cycle.

## The four Mission 017 checks only the owner can do

1. **"· edited" and a failed save:** draw a stroke ("· edited" should appear at once, and go with Ctrl+Z). Then make the
   picture's `.dnk` read-only in File Explorer (Properties) and press Ctrl+S: the message should say the file wasn't
   changed.
2. **After a crash:** draw, wait a minute, end DoodleNook in Task Manager, start it again: it should offer to recover.
3. **Pixel Art palettes:** choose another palette in the palette ⋯ menu; Ctrl+Z should bring the old one back.
4. **At 150 % on the Kamvas:** are the menus (Edit, View, the ⋯ menus) and dialogs readable, with nothing cut off?

## The Mission 018 check only the owner can do

**Hold B, E or Z** with the pen over the drawing. Does the card come up at the right moment, in a comfortable place,
with numbers you can read while dragging? Does letting go keep what you wanted? (See the
[Mission 018 playtest](iterations/018-quick-adjust/PLAYTEST.md).)
