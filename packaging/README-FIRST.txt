DoodleNook — Mission 006 "Make It Move"
=======================================

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
Alt+1 … Alt+6 workspaces · F1 all shortcuts

Animation Studio (Alt+6)
------------------------
N new frame · Shift+N duplicate frame · Enter play/pause · Esc stop · , and . previous/next frame
Home/End first/last frame · O onion skin on/off
Two sample animations are in the Samples folder (also: Help › Sample animations).
Window › Customize workspace chooses which optional controls each workspace shows.

Files and settings
------------------
Pictures and animations are saved as DoodleNook files (.dnk); export PNG, JPEG, WebP, sprite sheets,
PNG sequences and animated GIFs from the File menu.
Brush settings, colours, workspace layouts and the window position are remembered in
%APPDATA%\DoodleNook\settings.json. Recovery copies of unsaved work (File › Preferences sets how often)
are kept in %LOCALAPPDATA%\DoodleNook\Recovery and removed when you save or close normally.
To remove DoodleNook, delete this folder (and those settings folders if you like).

Nothing is collected or sent anywhere. Third-party licences: THIRD-PARTY-NOTICES.txt
