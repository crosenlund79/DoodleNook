# Mission 012 — The Artist's Companion

Five things that make everyday drawing easier, with no new workspace and no redesign:

1. Import an animation to draw over it.
2. Layer opacity in Animation Studio.
3. Pasting pictures from other programs, and Paste as reference.
4. Palettes by other pixel artists, credited.
5. An illustrated Quick Start guide.

Each one reuses what DoodleNook already had: the four layers are ordinary animation layers, the paste uses Place
image's transform handles, the palettes are ordinary library palettes, and the guide is the documentation itself.

**Status:** implemented and verified by automated tests on Linux and on Windows CI, plus a smoke test of the packaged
`DoodleNook.exe`. The smoke test includes the real Windows clipboard. Awaiting your playtest
([PLAYTEST.md](PLAYTEST.md)). The pen hasn't been tested: every test used a mouse or simulated input.

## 1. Import an animation to draw over it

**File › Import animation…** (also in the **Animation** menu) reads:

- **GIF files.** DoodleNook's own decoder handles every frame's position, partial frames, transparency, interlacing
  and the three disposal methods (keep, clear to transparent, restore what was there).
- **Animated PNGs (APNG).** All PNG colour types and bit depths, with frame offsets, the "replace" and "draw over"
  blend modes, and the keep, clear and restore disposal methods. Interlaced APNGs are refused with a message.
- **Numbered PNG pictures.** Choose one picture and its whole sequence comes along: the files in the same folder whose
  names differ only in the number, in natural order (`walk_2` before `walk_10`). Unrelated PNGs in the folder are never
  included. Pictures of different sizes are placed at the top left of the largest size, with a note.

One compact window shows a filmstrip of every frame, the frame count, the size and the timing. A click leaves a frame
out (or brings it back). *From* and *to* pick a range. For a sequence it asks for the frame rate, defaulting to the
current animation's rate or 12. The style (hand-drawn or pixel art) defaults to pixel art for small pictures. A memory
estimate warns before a very large import. Decoding runs in the background; after 300 ms a progress card with Cancel
appears.

**Timing is kept exactly.** The most common frame delay becomes the frame rate, and frames with other delays keep
their own duration. GIF delays of 0 or 1 hundredth of a second play at 100 ms, as browsers do.

**The result** is an ordinary animation at the source's size, with four layers (top to bottom):

- **Coloring**, **Outline** and **Sketch:** empty, at 100 %. Sketch is selected.
- **Reference:** one drawing per imported frame, at 35 %, locked and visible. Identical neighbouring frames share one
  drawing as a hold. Empty frames stay empty. It is an ordinary layer: it can be unlocked, hidden, faded or deleted.

The timeline grows if needed so all four layers are in view. The source files are only read. Nothing is decoded with
a video framework.

**Proof that frames are composed correctly:** nine test files cover every disposal and blend method, offsets, partial
frames, interlacing, palette and grey PNGs and transparency. Each frame is compared, pixel for pixel, with what
Chromium's own image decoder (WebCodecs `ImageDecoder`) shows for the same file. All 50 frames match exactly.

## 2. Layer opacity in Animation Studio

Each layer in the timeline shows its opacity at the right (accented below 100 %). A click opens a pop-over with a
slider (live preview while dragging, one undo step when released) and a box for an exact percentage (Enter).

Opacity belongs to the layer, so it applies on every frame and everywhere: the canvas, playback, frame switching,
onion skins (an onion of the active layer is as faint as the layer), GIF, PNG-sequence and sprite-sheet exports, and
saving and reopening. It is never baked into the pixels. Opacity was already part of `.dnk` layers, so nothing changed
in the file format.

The lock mark on a locked layer's row unlocks it with one click, without changing the layer you draw on.

## 3. Paste from other programs, and Paste as reference

**Ctrl+V** pastes the newest thing copied: DoodleNook's own copy (as before), or a picture copied in another program.

- **What it reads:** the "PNG" clipboard format (screenshot tools, browsers, Paint.NET, GIMP; transparency kept), a
  picture file copied in Explorer, and the Windows bitmap formats every program provides (CF_DIBV5 / CF_DIB).
- **Bitmaps with an empty alpha byte:** 32-bit bitmaps whose alpha byte is zero everywhere, as Paint and many
  programs copy them, are read as opaque.
- **Which copy is newest:** DoodleNook's own Copy doesn't touch the system clipboard. DoodleNook remembers the
  clipboard's change number at its own copy; if another program has copied since, that wins.
- **Where it lands:** on a new layer above the active one, centred, with transform handles (Enter to finish). A
  locked active layer is never touched. A picture larger than the canvas is scaled to fit, with nearest-neighbour
  scaling in pixel art, and the message says so. Under Palette Lock, its colours are matched to the palette.
  Everything is undoable.
- **When it can't paste:** an empty clipboard, text, a busy clipboard, a damaged or unreadable picture, a picture
  larger than DoodleNook supports, or a copied file that isn't a picture each give a one-line message and change
  nothing.

**Edit › Paste as reference** does the same, with these differences:

- The new layer is called *Pasted reference*, sits at 35 % and stays visible.
- It goes just under the layer you draw on.
- It locks as soon as its placement is finished (Enter, another tool, any other command), and the layer you were
  drawing on is active again.
- In an animation it is that frame's drawing only, and an imported Reference layer is never replaced.

Windows reading is done directly with the Win32 clipboard functions, so transparency is under DoodleNook's control.
Other platforms use Avalonia's clipboard. The bitmap decoder is tested against Skia reading the same data as a .bmp
file.

## 4. Palettes by other pixel artists

Ten palettes are in the library's new **Lospec** collection. Each one is bundled only because its creator has said it
may be used freely:

| Palette | Colours | Creator | Terms |
|---|---:|---|---|
| PICO-8 palette | 16 | Lexaloffle Games | CC0 |
| Resurrect 64 | 64 | Kerrie Lake | creator's permission (informal) |
| ARQ16 | 16 | ENDESGA | free for anyone and anything |
| DawnBringer 16 | 16 | DawnBringer | free to use (informal) |
| CL8UDS | 8 | _Nicola | free to use in any way |
| Japanese Woodblock | 12 | Polyducks | free to use in any way |
| AYY4 | 4 | Polyducks | free to use in any way |
| Cave4 | 4 | Polyducks | free to use in any way |
| A Blood-Steeped Corridor | 5 | Polyducks | free to use in any way |
| 1bit Monitor Glow | 2 | Polyducks | free to use in any way |

How the data was checked:

- The colours were copied from each palette's own Lospec download, in the published order.
- Each list was checked against the page's colour count and checked again, independently, against the raw downloads.
- No colour was typed by hand or read from a picture.
- The sources, the creators' exact words and how each was checked are in
  [PALETTE-ATTRIBUTION.md](../../PALETTE-ATTRIBUTION.md). It is also next to `DoodleNook.exe`.

In DoodleNook:

- The Palettes window lists each palette's colour count. Choosing one shows its creator, mood, terms and source.
- The palette menu shows the counts too.
- Lospec is never contacted.
- They work like every other palette: Palette Lock, Remap, Game Edit project and per-image palettes, Painting,
  Pixel Art and Animation. Tests cover lock, remap and save/reopen, the Game Edit project palette and a locked pixel
  pencil.

**Not bundled:**

- Resurrect 32, Apollo, Endesga 32 and 64, SLSO8, Fantasy 24, Midnight ablaze, JustParchment8, BloodMoon21 and
  Tranquil Fantasy 23. Lospec publishes no licence, and none of these creators has stated terms, so redistribution is
  unclear. Each can be downloaded from Lospec and brought in with **Palettes… › Import…** (`.hex`, `.gpl`).
- **"SLS08"** is almost certainly SLSO8. **"Fantasy_"** is unresolved: five candidates are listed in the attribution
  file. Both need your answer.
- No 24- or 32-colour palette by another artist with clear permission was found. Nook 32 covers 32.

## 5. Quick Start guide

[docs/QUICK-START.md](../../QUICK-START.md) has eight short sections, each with a screenshot of DoodleNook itself:

1. Draw right away.
2. Choose a workspace.
3. Colours and palettes.
4. Pixel art.
5. Animate.
6. Draw over an existing animation.
7. Paste pictures and references.
8. Save and export.

The screenshots are in `docs/screenshots/quick-start/`. They are rendered by the app itself in a test
(`QuickStartScreenshots`), using DoodleNook's own sample art and no private material.

**Help › Quick Start Guide** (also in Quick Sketch's ☰ menu) shows the guide inside DoodleNook: the Markdown and its
pictures are built into the program, so it needs no browser or internet and always matches the documentation.

Tests check that:

- the built-in copy is current;
- every picture loads and none is missing or unused;
- every shortcut the guide names is a real DoodleNook shortcut;
- every **Menu › entry** it names exists.

A PDF was not made: the guide inside DoodleNook and the Markdown on GitHub cover reading and printing.

## Compatibility

- **`.dnk` format:** unchanged. Imported animations are ordinary version-4 animations, and opacity was already stored
  per layer. Older files open as before.
- **What is different:**
  - Edit › Paste is enabled even before DoodleNook's own copy, because another program may have something to paste.
  - The Edit menu has one more entry, Paste as reference.
  - Ctrl+C / Ctrl+X still don't change the system clipboard.
- **Engine:** `DoodleNook.Engine` still references only .NET (architecture test). The decoders, the timing, the
  four-layer builder, the bitmap reader and the palette data are all in the engine. The Win32 clipboard code and the
  window code are in the app.

## Verification

- **Engine tests:**
  - `AnimationImportTests`: every frame against Chromium, timing, damaged files, natural sorting, sizes, the four
    layers, holds and exclusions, drawing and save/reopen with opacity, export opacity and visibility, onion
    opacity.
  - `ClipboardBitmapTests`: 32-bit with empty or real alpha, 24-bit padding, top-down, V5 masks, 16-bit 565,
    1- and 8-bit palettes, damaged, compressed and huge bitmaps.
  - `CuratedPaletteTests`: exact list and counts, colours in order, no duplicates, lock, remap and save.
- **App tests:**
  - `ArtistsCompanionWindowTests`: GIF and sequence import through the window, a damaged-file message, timeline
    opacity and the lock mark, the palette credit and a locked PICO-8 pixel pencil, the Quick Start guide in Help,
    and the guide's shortcuts and menu paths.
  - `ClipboardWindowTests`: Ctrl+V of a transparent PNG with a locked active layer, newest copy wins, eight problem
    cases, an Explorer file and a bitmap, scaling to fit, Paste as reference in an animation, the bitmap decoder
    against Skia, Explorer's file list.
  - A Game Edit test with a Lospec project palette and a per-image palette.
- **Smoke test section 17 (packaged app on Windows):**
  - a GIF imports as four layers;
  - opacity appears in exports;
  - a PNG with transparency, a Paint-style bitmap and a copied file are pasted through the **real Windows
    clipboard**, the file as a reference;
  - the ten palettes are present;
  - the built-in guide has all its pictures.

## Known limitations

- Interlaced animated PNGs are refused with a message. They are rare.
- Image sequences must be PNG. A JPEG sequence isn't read.
- Pictures pasted from other programs are placed centred: their position in the other program is unknown.
  DoodleNook's own copy keeps its place.
- Copying in DoodleNook doesn't put the picture on the system clipboard for other programs. That wasn't asked for and
  is unchanged.
- Very large GIFs use memory: one full-size picture per distinct frame. The import window estimates it and warns
  above 1.5 GB.
- The real pen, and real pastes from Paint, browsers and screenshot tools on your PC, are for your playtest. CI checks
  the Windows clipboard formats those programs use, not the programs themselves.

## Delivery

Branch `iteration/012-artists-companion`, started from Mission 011's final commit (efa32ae). The Windows build is the
artifact **DoodleNook-win-x64** of the newest green run of
[Windows build on this branch](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F012-artists-companion).
