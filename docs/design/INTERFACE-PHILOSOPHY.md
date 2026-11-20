# DoodleNook interface philosophy

> **LESS IS MORE — BUT CAPABLE WHEN NEEDED. Ease of use first.**

This is DoodleNook's permanent design rule for every workspace, present and future. The root
[`CLAUDE.md`](../../CLAUDE.md) lists the sixteen short principles; this document explains how to apply them.
Read it before adding or changing anything the user sees, and run the [checklist](#the-ui-decision-checklist)
before adding any permanently visible control.

## 1. Functionality is not the same as visible complexity

A feature can be powerful without being permanently visible. The user should see what the current creative
task needs, not everything the application can do.

- **Capability** is what DoodleNook can do: layers, blend modes, onion-skin falloff, sprite-sheet padding…
- **Visible complexity** is how much the user must look at, read and decide while working.

Grow the first without growing the second. A new feature's default visible cost should be zero or one control,
and that control should only appear where and when it is useful.

Minimalism does **not** mean hiding everyday actions behind obscure menus. Hiding a constantly used action costs
more effort than showing it. The goal is fewer decisions, fewer clicks and fewer distractions in the work the
artist actually does.

## 2. Three levels of access

| Level | What belongs here | Where it lives in DoodleNook |
|---|---|---|
| **1 · Everyday** | Actions performed constantly in this workspace: brush, eraser, colour, size, undo, new frame, play | Tool strip, the workspace's main bar (Quick Sketch bar, options bar, timeline bar), the quick palette at the pen, single-key shortcuts |
| **2 · Contextual** | Controls that matter only during a particular task: transform options while transforming, the next-step bar while something is selected, exposure commands on a timeline cell, onion-skin settings when adjusting onion skins | The options bar (it changes with the tool and the task), pop-overs opened from a Level 1 control (▾), right-click context menus, dialogs that open only when needed |
| **3 · Occasional / advanced** | Settings changed rarely: playback preferences, export options, autosave interval, palette management, timeline zoom | The menus, focused dialogs, the ⋯ overflow menus, and **Customize workspace** |

Rules:

- Level 2 controls **appear when relevant and go away afterwards**. The transform bar replaces the tool options
  while a transform is active; when it is applied, the normal options come back.
- Every Level 2 and Level 3 function stays **discoverable**: it is in a menu with its shortcut, or reachable
  from a visible control (▾, ⋯, right-click), and its tooltip says what it does.
- A function may live at more than one level. Onion skinning, for example, has an always-visible on/off toggle
  (Level 1), a settings pop-over (Level 2) and a menu entry (Level 3).

## 3. Everyday versus occasional

Before choosing a level, ask how often a typical artist uses the action **in this workspace**, and how much it
costs to reach it if it is not visible:

- Used every few seconds or minutes (brush size, undo, new frame, play): **Level 1**.
- Used several times in a session, always during a specific task (crop confirmation, cel exposure): **Level 2**,
  shown by that task.
- Used once per session or less (export settings, preferences, canvas size): **Level 3**.
- If some users use an action constantly and others rarely, keep it at Level 2 or 3 by default and make it
  **pinnable through Customize workspace**.

## 4. Contextual panels and pop-overs

- Open them from the control they belong to (the ▾ next to a toggle, a right-click on the thing being edited).
- Close them when the user returns to the canvas. They must never trap keyboard focus: drawing shortcuts keep working.
- Keep them small and focused on one topic. A pop-over with more than about eight settings should be split or
  simplified.
- Changes apply immediately with live feedback where possible. Use OK/Cancel dialogs only for operations that
  change the whole picture (image size, export) or need a decision.

## 5. Workspace customization

The defaults are deliberately minimal. **Customize workspace** (Window menu) lets the user pin optional controls
and panels they use often, **independently for each workspace**. Rules for this mechanism:

- Offer a short, curated list of options. Each option must correspond to a real, working control.
- An option describes what it shows, in plain words ("Previous/next frame buttons", "Onion-skin opacity slider").
- Choices are saved per workspace and survive restarts. **Reset to default layout** restores the shipped defaults
  for that workspace only.
- Unknown or retired option names in saved settings are ignored safely; nothing breaks when an option disappears.
- Never require customization. No first-launch wizard. No drag-and-drop toolbar designer.

## 6. Sensible defaults

A new user should be comfortable immediately: a white 1920 × 1080 picture, a natural pencil, a sensible size,
visible undo, onion skins that are on and gently tinted in Animation Studio, autosave on. When choosing a default,
pick what most artists want most of the time, and let the setting stay discoverable for those who don't.

## 7. Canvas first

Drawing space is the product. Each workspace has a chrome budget:

- On a **Kamvas 13 at 150 % (1280 × 720 logical pixels)** the canvas should keep at least about 70 % of the window
  in the drawing workspaces, and the timeline's default height in Animation Studio should leave the canvas
  clearly dominant.
- Bars are a single row (40 px). Docks collapse to an icon strip. **Tab** hides everything; Pure Sketch shows nothing at all.
- Panels that only some users need are off by default and can be pinned.

## 8. Compact screens

DoodleNook must stay fully usable at 1280 × 720 logical pixels (1920 × 1080 at 150 %) and at 100 %, 125 % and 150 %
Windows scaling:

- Never make controls unreachable. If a bar can overflow, it scrolls (the mouse wheel scrolls bars sideways)
  or moves less-used items into a ⋯ menu.
- Never solve space problems by shrinking hit targets or text. Prefer progressive disclosure: collapsible
  sections, pop-overs, resizable and collapsible panels.
- Test new UI at Kamvas 13 / 150 % and on a 27″ 2560 × 1440 desktop (the screenshot tests capture both).

## 9. Pen-friendly controls

- Buttons are at least 28 px (normally 36 px) logical; draggable handles are at least 10 px wide and have a
  larger invisible grab area.
- Everyday actions never depend on double-click, hover-only information or tiny targets. Right-click actions
  also work from the pen's barrel button.
- Dragging on the canvas paints; dragging on UI never paints. Pop-overs never keep keyboard focus.
- Pen behaviour on the real Kamvas 13 is only confirmed by the user's playtest.

## 10. Clear naming and discoverability

- Use the artist's words: "New frame", "Duplicate drawing", "Hold", "Onion skin", not internal names.
- Every button has a tooltip: what it does, plus its shortcut in parentheses.
- Menus list everything, including Level 1 actions, with shortcuts. A user who never reads a manual should
  find every feature by browsing menus and right-clicking.
- Shortcuts follow Photoshop conventions where one exists; don't change an existing shortcut without a strong reason.

## 11. Avoiding feature bloat

- Don't add a feature because a big application has it. Add it when it removes friction from real DoodleNook work.
- Prefer one flexible control over three special-purpose ones.
- Prefer smarter defaults over new settings.
- Remove or demote controls that playtests show are rarely used.
- Out of scope unless the user asks: PSD/CMYK, plugins, cloud, AI generation, skeletal animation, video editing.

## 12. Examples from the six workspaces

| Workspace | Level 1 (visible) | Level 2 (contextual) | Level 3 (menus, dialogs, Customize) |
|---|---|---|---|
| **Pure Sketch** | Nothing but the canvas; the quick palette at the pen (colours, brush, eraser, undo/redo, size) | — | Corner menu: workspaces, files, view |
| **Quick Sketch** | One bar: brush, size, opacity, colour, erase, undo/redo, layers chip | The bar's middle changes with the tool (lasso modes, transform buttons) | The ☰ menu; Customize workspace |
| **Painting** | Tool strip, brush options bar, Colour/Brushes/Layers dock | Transform bar, next-step bar with a selection, brush settings behind *Brush settings* | Image adjustments, layer properties, Customize workspace |
| **Editing** | Selection and transform tools, Layers/Properties/History dock, status bar | Next-step bar (Transform, Fill, Crop…), crop confirmation, transform bar | Image size, canvas size, adjustments |
| **Pixel Art** | Pixel pencil, size, pixel-perfect, grid, palette column, 1:1 preview | Filled-shape toggle for pixel shapes, symmetry options | Palette management (⋯), tiled view, Customize workspace |
| **Animation Studio** | Drawing tools, compact timeline, New frame, Duplicate frame, Play/Pause, frame counter, onion-skin toggle | Onion-skin settings (▾ next to the toggle), cel commands on right-click (hold, break hold, duplicate, delete), playback options (on the fps chip) | Export animation dialog, animation menu, preview panel, previous/next buttons and onion opacity slider (pinnable via Customize workspace) |

### Example: Animation Studio's twenty controls

The animation features include play, pause, stop, next frame, previous frame, first frame, last frame, duplicate,
insert, delete, onion previous and next counts, onion opacity, onion tints, GIF/PNG/sprite-sheet export, exposure
settings, playback settings, timeline zoom and frame properties. They are **not** twenty permanent buttons:

- **Visible by default:** drawing tools, the compact timeline, *New frame*, *Duplicate frame*, Play/Pause, the frame
  counter, the onion-skin toggle and a small fps chip.
- **Contextual:** exposure and cel commands (right-click a cell, or drag a hold's end), onion-skin settings (▾),
  loop and playback options (click the fps chip), timeline zoom (Ctrl+wheel on the timeline).
- **Menus and optional controls:** export, frame duration, first/last frame, detailed playback options;
  previous/next buttons, the onion opacity slider, the frame-duration control and the preview panel can be pinned
  through **Customize workspace**.

The result is capable but uncluttered: an artist can start drawing frames immediately, and every deeper option
is one obvious step away.

## 13. The Personal Tool Principle and the three levels

DoodleNook is its owner's tool (see *The Personal Tool Principle* in [`CLAUDE.md`](../../CLAUDE.md)). The three levels
of access decide **where** a capability lives; the Personal Tool Principle decides **whether** it belongs at all, and
how high it may climb:

- **Admission.** A capability enters DoodleNook only when it makes something the owner actually enjoys doing easier.
  Parity with another application is not a reason; "it might be useful to someone" is not a reason.
- **Starting level.** New capabilities start at Level 2 or 3 (contextual, menus, Customize workspace), even when
  they're substantial. They move to Level 1 only when the owner's playtests show everyday use.
- **Reuse before invention.** A new tool should borrow an existing workspace, panel, pop-over or interaction
  (the options bar, the layers panel, right-click menus) before anything new is drawn on screen.
- **Protection.** What the owner already enjoys (the calm default workspaces, the drawing feel, the Mission 003
  look) outranks any new feature. If a feature can't be added without disturbing them, it waits.
- **Omission is a decision.** Leaving a feature out, or keeping it one step away, is a valid and often the best
  outcome; it should be recorded as such rather than treated as unfinished work.

## The UI decision checklist

Before adding a **permanently visible** control, answer these questions in the change's description or review:

1. Is this action used frequently during ordinary work?
2. Is it relevant to this workspace?
3. Is it already accessible through another intuitive interaction (shortcut, right-click, quick palette)?
4. Would a contextual panel, pop-over or menu be better?
5. Should it be an optional control the user can pin through Customize workspace instead?
6. Does it meaningfully reduce the drawing area?
7. Does the interface stay comfortable on a 13.3-inch 1080p display at 150 %?
8. If it is hidden, is it still discoverable (menu entry, tooltip, shortcut)?
9. Does it preserve DoodleNook's established visual identity (Mission 003)?

10. Does it make something the owner actually enjoys doing easier, or does it merely make DoodleNook bigger?

If the answers to 1 and 2 are not a clear "yes", the control does not belong on the main screen by default.
