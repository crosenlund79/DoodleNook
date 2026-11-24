DoodleNook — Mission 018 "Quick Adjust"
======================================

Start
-----
Double-click DoodleNook.exe. Nothing needs installing: everything DoodleNook needs is in this folder.

The first time, Windows may say "Windows protected your PC", because this test build is not signed.
Click "More info", then "Run anyway". (If you'd rather avoid that: right-click the downloaded .zip,
choose Properties, tick "Unblock", click OK, and extract it again.)

Your Huion Kamvas 13
--------------------
* DoodleNook reads the pen through Windows Ink. In the Huion driver (HuionTablet), keep
  "Enable Windows Ink" switched on, otherwise Windows only reports a mouse without pressure.
* Press F12 in DoodleNook for the pen diagnostics panel: it shows whether the input is "Pen" or
  "Mouse", the pressure Windows sends, and the brush size that results.
* A pen side button set to "Right click" in the driver lets you drag on the canvas to resize the brush.
  Set to "Middle click", it pans the canvas.

A few shortcuts
---------------
B brush · E eraser · I or hold Alt eyedropper · hold Space to pan · [ and ] brush size
Ctrl+Z undo · Ctrl+Shift+Z or Ctrl+Y redo · mouse wheel zoom · Ctrl+0 fit · Ctrl+1 100 %
Ctrl+N new · Ctrl+O open · Ctrl+S save · Tab hide interface · F11 full screen
Hold B, E or Z: zoom, size, opacity and smoothing at the pen (let go to keep, Esc undoes)
S stamp · G paint bucket · T text · Alt+1 … Alt+6 workspaces · F1 all shortcuts

Animation Studio (Alt+6)
------------------------
N new frame · Shift+N duplicate frame · Enter play/pause · Esc stop · , and . previous/next frame
Home/End first/last frame · O onion skin on/off
Two sample animations are in the Samples folder (also: Help › Sample animations).
Window › Customize workspace chooses which optional controls each workspace shows.

The Artist's Workshop
---------------------
* Brushes: the brush picker now has 17 brushes in Sketch, Ink, Paint and "Texture & stamps", a search box and
  favourites (right-click a brush). "Brush Laboratory…" at the bottom of the picker (or Window › Brush Laboratory)
  edits a brush with a scribble area that never touches your picture.
* Stamps: the stamp tool (S, in Painting and Pixel Art). Edit › Save selection as stamp… makes your own.
* Patterns: the paint bucket's Pattern button, or Edit › Fill with pattern. Edit › Save selection as pattern…
* Palettes: Window › Palettes… (GIMP .gpl and .hex files in and out, colours from a picture).
* Reference board: Window › Reference board. Pictures on it are never part of your artwork or exports.
* Palette Lock: lock a picture to a palette (palette menu, or Image › Lock to palette); Image › Remap to palette…
  converts artwork to another palette with a preview you can adjust. Transparency is never changed.
* In the Samples folder: "Woodland starter pack.dnkpack" (Window › Creative library… › Import a pack…),
  "Autumn walk.gpl" (Window › Palettes… › Import…) and "Sample references.dnkboard" (the board's ··· menu › Open board…).

The Game Artist's Workshop (new)
--------------------------------
* Sprite sheets: File › Import sprite sheet… shows the sheet with the grid it will cut (drag it, drag the first
  frame's corner, or type the frame size; empty cells start left out) and imports it as an animation, or as a
  reference to redraw (Reference locked at 35 %, Sketch, Outline, Coloring). A sheet's .json gives the exact grid.
  File › Sprite sheet from pictures… lays numbered pictures out on one sheet. File › Export animation… makes a sheet
  from an animation, as before.
* Live Animation Preview: View › Animation preview loops the animation while you draw any frame. Docked in Animation
  Studio's canvas corner, or ⤢ for its own window. Pause, loop, zoom (Fit, 1×–4×) and a background behind transparent
  areas (display only, never exported). View › Behind transparent areas does the same for the canvas.
* Palettes: Window › Palettes… › Import folder… imports every palette in a folder at once (.gpl, .hex, .pal, .txt,
  .ase, .png), with one summary. Palettes you already have are skipped; imported ones are your own copies.
* Storybook Studio: File › New storybook… arranges your drawings into comic and storyboard pages (six layouts). Every
  panel has the same pixels per centimetre; New drawing makes one at exactly the right size. Double-click a panel to
  draw in it, "Back to the storybook" to see the page. Export as PNG (with the print resolution) or one PDF.
  Help › A little walk (storybook) opens a sample (made in Documents\DoodleNook\Samples).

The Gentle Guide
----------------
* Help › Guided Tutorials… › "Your First Animation": a bouncing ball in about 10 minutes, done in the real
  Animation Studio. A small guide points at the control to use next (a soft orange outline) and notices when each
  step is done. You do all the drawing.
* It opens a new 256 × 256 practice animation at 8 fps. If the picture you have open has unsaved changes, you're
  asked first (Save / Don't save / Cancel), as for any new picture. Nothing is ever saved without you choosing where.
* Back, Skip step and "I've done this" are always there; the × closes the guide at any point and your drawing stays.
  Drag the guide's title bar to move it. "Show me" brings back a control you've hidden.

Animation Reference Library
---------------------------
* Animation › Insert reference cycle…: 35 ready-made motion guides for game animation as a simple mannequin in side,
  top-down or three-quarter view, facing the way you need:
  - Movement: idle, walk, run, sneak, jump, fall, land, carry heavy, limp walk, stumble.
  - Combat: two attacks, hurt, death, cast, shoot.
  - Interaction: pick up, drop, push, pull (a rope), climb, sit down, stand up, hammer, dig.
  - Emotion: fear / startle, hesitation, sadness / crying, laughing, exhausted idle.
  - Transitions: idle → walk, walk → run, run → stop, stand → crouch, turn 180°. Each starts or ends on the drawing of
    the cycle it joins, so they chain at the same spot.
* Drag in the preview to place the feet; choose the height, where it starts and how long each drawing lasts.
* It is added as its own faint (35 %), locked layer just under the layer you draw on. Nothing already in the
  animation changes; if the animation is too short, frames are added at the end (other layers hold there).
* Animation › Adjust reference cycle… (or right-click the layer in the timeline) moves, resizes, turns or swaps it.
* The mannequin's right arm and leg are orange, the left ones blue.

The Artist's Companion
----------------------
* Help › Quick Start Guide: short illustrated sections, built in (no internet needed).
* File › Import animation… (also in the Animation menu): a GIF, an animated PNG or numbered PNG pictures (choose one;
  the rest of its sequence comes along) become an animation with four layers: Reference (the frames, 35 % and locked),
  Sketch, Outline and Coloring. Timing is kept. Click frames to leave them out. Your files are only read.
* Layer opacity in Animation Studio: click a layer's percentage in the timeline for a slider and a box. It applies to
  that layer on every frame, in playback, onion skin, exports and sprite sheets.
* Ctrl+V pastes pictures copied in other programs (a browser, Paint, a screenshot, a picture file copied in
  Explorer), transparency included, on a new layer ready to move. Edit › Paste as reference does the same at 35 %
  and locks the layer once it is placed (Enter).
* Ten palettes by other pixel artists in Window › Palettes… (collection "Lospec"), each with its creator and terms.
  Credits are in PALETTE-ATTRIBUTION.md next to DoodleNook.exe.

Under the Hood
--------------
Nothing new to learn: DoodleNook is quicker and steadier. Undo and redo on big pictures with many layers are several
times faster, big brushes over many layers keep up better, saving holds the window for a fraction of the time, and
long sessions no longer slow down as tools are changed. Help › Diagnostics… shows the picture's size, memory and
recent timings; "Copy diagnostics" copies those numbers (no artwork, names or folders) if you want to report a problem.

The Finishing Touches
---------------------
* Quick palette (right-click or the pen button; the Alt+1 wheel): drag the centre to size the brush (short moves
  fine-tune, long drags change a lot, Shift for extra fine). The brush pill under the wheel chooses another brush:
  recent ones first, your favourites and all brushes, your own included. Each brush remembers the size you last used.
* Smudge, Clone and Repair: one button in the Painting and Editing tool strips (right-click it for the others;
  Shift+B, Shift+S, J). Clone: Alt+click where to copy from, then paint. Repair: paint over a speck to fill it
  from its surroundings, or Alt+click a source to bring in its texture.
* Liquify: Edit > Liquify... (Ctrl+Shift+X). Drag to push, inflate, pinch, twirl, smooth or restore. Enter applies,
  Esc cancels, Ctrl+Z takes back a stroke, Alt reverses.
* Perspective and Warp: Edit > Perspective / Warp, or Free / Perspective / Warp in the transform's bar (Ctrl+T).
  Drag the corners or the grid's points; Enter applies, Esc cancels.

Game Edit Mode
--------------
File › Game Edit Mode… and choose your game's graphics folder: every PNG in it (and its subfolders) appears
in a catalog with its progress. Click one to edit it. Ctrl+S saves your editable version in a "_DoodleNook"
folder inside the game folder and replaces the game's PNG in place (the original is backed up first, in
_DoodleNook\Originals, if you leave that ticked). Saving makes an image "In progress"; "Mark complete" is up to you.
Saving a "template_" image (e.g. template_player.png) asks whether to create a replacement without the prefix
(player.png, same folder): the template stays as it is, and later saves go to player.png.
"Project palette…" in the catalog chooses (or imports) a palette for the whole game and can lock all graphics to it.
Images are checked when you open them: other colours go through the remap preview first, nothing is recoloured
without Apply. The palette button on a tile gives one image its own palette or no lock.

The Designer's Toolkit
----------------------
* Text: the Text tool (T, in Painting and Editing). Click to type, click text to edit it; font, size, bold, italic,
  alignment and Effects (outline, shadow, spacing) are in the options bar. Text stays editable when saved.
* Editable shapes: the Shapes tool (U) › Editable. Move, resize and turn them later with the Move tool (V).
* Groups (Ctrl+G), masks (the mask button under the layers; paint black to hide, white to show) and adjustment
  layers (the ◐ button under the layers): all in the layers panel and the Layer menu.
* Rulers (Ctrl+R; drag from a ruler to add a guide), guides (Ctrl+;) and snapping (Ctrl+Shift+;) are in the View menu.
* Drawing assistants (the ▾ next to the mirror button, or View › Drawing assistants…): symmetry with a movable
  centre and radial symmetry, one- and two-point perspective, snapping. Hold Shift for a straight brush line.

Files and settings
------------------
Pictures and animations are saved as DoodleNook files (.dnk); export PNG, JPEG, WebP, sprite sheets,
PNG sequences and animated GIFs from the File menu.
Brush settings, colours, workspace layouts and the window position are remembered in
%APPDATA%\DoodleNook\settings.json. Recovery copies of unsaved work (File › Preferences sets how often)
are kept in %LOCALAPPDATA%\DoodleNook\Recovery and removed when you save or close normally.
Your own brushes, stamps, patterns, palettes and the reference board are kept in %APPDATA%\DoodleNook\Library
(pictures are copied in, so moving or deleting the originals breaks nothing; updates never touch it).
Window › Creative library… exports everything as one .dnkpack file for a backup or another computer.
To remove DoodleNook, delete this folder (and those settings folders if you like).

Nothing is collected or sent anywhere. Third-party licences: THIRD-PARTY-NOTICES.txt
