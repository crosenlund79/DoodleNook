# Clip Studio Paint (Windows) UX research for DoodleNook

Researched 2026-11-19. Current release is **Ver. 5.1.5** (Windows/macOS, 5 Oct 2026). Ver. 5.0 came out on 11 Mar 2026. The online manual covers **Ver. 5.0** and was updated April 2026.

Labels:
- **[D]** Celsys documentation
- **[T]** third-party source
- **[S]** opinion, attributed to its author
- **[I]** my inference

**Ver. 5 renamed the tool hierarchy.** "Sub Tool" is now "Tool", and a top-level tool is now a "tool group". Sub Tool palette became **Tool Group palette**. Tool Property became **Tool Settings**. Sub Tool Detail became **Advanced Tool Settings** [D:RN5][D:MNEW]. Older tutorials, including official ones, still use the old names [D:TIPS-515][D:T1217].

## Source keys

Every key maps to a URL. **M** = `https://help.clip-studio.com/en-us/manual_en/`, **F** = `https://support.clip-studio.com/en-us/faq/articles/`.

**Manual home and release information**
- M0 https://help.clip-studio.com/en-us/First_Topic.htm
- MNEW M+030_new/030_new.htm
- RN5 https://www.clipstudio.net/en/dl/release_note_old/v5/
- RNL https://www.clipstudio.net/en/dl/release_note/latest/
- RN4 https://www.clipstudio.net/en/dl/release_note/v4/
- NEWS5 https://www.clipstudio.net/en/news/202603/11_01/

**Interface and workspace**
- MINT M+060_pc/Interface.htm
- MPAL M+690_interface/Palettes.htm
- MCB M+690_interface/Command_Bar.htm
- MQA M+690_interface/Quick_Access_Palette.htm
- MHTB M+690_interface/Hide_Title_Bar_and_Menu_Bar.htm
- MWS M+690_interface/Register_and_manage_your_workspace.htm
- MMOVE M+690_interface/Moving_tools.htm
- MSM M+090_tablet/Simple_Mode_and_Studio_Mode.htm
- MUSM M+090_tablet/Using_Simple_Mode.htm

**Tools and brushes**
- MHOW M+150_tools/How_to_use_tools.htm
- MTP M+150_tools/The_Tool_palette.htm
- MCUST M+150_tools/Customizing_the_Tool_and_Tool_Group_palettes.htm
- MDRAW M+240_brushes/Drawing_and_painting.htm
- MBS M+240_brushes/Brush_Size_palette.htm
- MTS M+240_brushes/Tool_Sliders_palette.htm
- MCBT M+240_brushes/Customizing_brush_tools.htm
- MADD M+240_brushes/Adding_new_brushes.htm
- MERA M+240_brushes/Eraser_tools.htm
- MPP M+240_brushes/Adjusting_pen_pressure.htm

**Settings glossary**
- GC M+810_subtools/C.htm
- GA M+810_subtools/A.htm
- GF M+810_subtools/F.htm
- GR M+810_subtools/R.htm

**Preferences and shortcuts**
- MPREF M+720_preferences/Preferences.htm
- MMOD M+720_preferences/Modifier_Key_Settings.htm
- MSC M+720_preferences/Shortcut_Settings.htm
- SMENU M+780_shortcuts/Menu_Shortcuts.htm
- SOPT M+780_shortcuts/Optional_Shortcuts.htm
- SOP M+780_shortcuts/Shortcuts_usable_during_operation.htm
- STOOL M+780_shortcuts/Tool_Shortcuts.htm

**Color**
- MSEL M+300_color/Selecting_colors.htm
- MPSC M+300_color/Pick_screen_color_(Windows__47_macOS).htm
- MCW M+300_color/Color_Wheel_palette.htm
- MCSL M+300_color/Color_Slider_palette.htm
- MCSET M+300_color/Color_Set_palette.htm
- MCH M+300_color/Color_History_palette.htm
- MIC M+300_color/Intermediate_Color_palette.htm
- MAC M+300_color/Approximate_Color_palette.htm
- MMIX M+300_color/Color_Mixing_palette.htm

**Layers**
- MLAY M+180_layers/Using_layers.htm
- MLB M+180_layers/Basic_operations.htm
- MLO M+180_layers/Other_layer_settings.htm
- MLREF M+180_layers/Reference_layers.htm
- MLDR M+180_layers/Draft_layers.htm
- MLPC M+180_layers/Palette_color.htm
- MLPROP M+180_layers/Layer_properties.htm
- MVEC M+180_layers/Vector_layers.htm
- MBLEND M+180_layers/Blending_modes.htm

**Selections, transforms, fill**
- MSL M+330_selection/Selection_Launcher.htm
- MSA M+330_selection/Selection_area.htm
- MSB M+330_selection/Basic_selection_functions.htm
- MTT M+360_transform/Types_of_transformations.htm
- MTBB M+360_transform/Transform_using_the_bounding_box.htm
- MLIQ M+360_transform/Liquify_tool.htm
- MFILL M+420_fill/Fill_Tool.htm
- MAFILL M+420_fill/Advanced_Fill.htm

**Canvas, devices, files, materials**
- MNAV M+270_canvas/Navigating_the_canvas.htm
- MUNDO M+270_canvas/Undo__47_Redo.htm
- MCOMP M+840_options/Companion_Mode.htm
- MTAB2 M+840_options/Clip_Studio_Tabmate_2.htm
- MEDGE M+750_gestures/Edge_keyboard_(Tablet).htm
- MGEST M+750_gestures/Setting_up_touch_gestures.htm
- MNEWC M+210_file/Creating_a_New_Canvas.htm
- MCAN M+060_pc/Setting_up_a_canvas.htm
- MMAT M+630_material/Material_palette.htm
- MSS M+450_figure/Smart_Shape.htm
- MSMART M+390_filters/Smart_Tools.htm

**Support FAQs (all under F)**
- FL5 F+20260006
- FSM F+20230045
- FPREF F+20200137
- FGEST F+20210107
- FCUR F+20190084
- FSTAB F+20200030
- FLAG F+20190019
- FPP F+20190228
- FPPR F+20190226
- FERA F+20200049
- FMM F+20190037
- FDPI F+20210117
- FOPT F+20210101

**Official Celsys TIPS**
- T9934 https://tips.clip-studio.com/en-us/articles/9934
- T563 https://tips.clip-studio.com/en-us/articles/563
- T1217 https://tips.clip-studio.com/en-us/articles/1217
- TIPS-515 https://tips.clip-studio.com/en-us/articles/515

**Third-party and community**
- JOYCE https://community.wacom.com/en-us/switch-from-adobe-photoshop-to-clip-studio-paint/ (artist James Joyce, 2023)
- SIMON https://tips.clip-studio.com/en-us/articles/4626 (community author simonwl)
- MIUS https://tips.clip-studio.com/en-us/articles/2169 (community author Miusmius)
- PARKA https://parkablogs.com/content/clip-studio-paint-vs-photoshop-drawing (Teoh Yi Chie, 2023)
- ASK https://ask.clip-studio.com/en-us/detail?id=12805
- PIPE https://www.pipelinecomics.com/learncsp/undo/
- TITI https://www.titikey.com/en/article/tlsf0a-b349e

---

## 1. Main interface

**Default Ver. 5 Windows layout [D:MINT]**
- Title bar.
- **Command Bar** across the top.
- Left side: a two-column **Tool palette**, the **Tool Group palette**, then the **Color Wheel** above the **Color Set**.
- A thin vertical **Tool Slider palette** between the left dock and the canvas. It holds a size slider and an opacity slider [D:MTS].
- The canvas in the centre.
- Right side: **Navigator** above the **Layer palette**.
- A strip of iconized **Material** tabs on the far right edge.

**Where the other palettes sit**
- History is stacked behind the Layer palette [D:MUNDO].
- In the manual screenshot, the Tool Settings palette appears to be a tab stacked with the Tool Group palette [I].
- Brush Size and Quick Access palettes don't appear in the default screenshot [I]. Open them from the Window menu [D:MBS][D:MQA].

**Ver. 5 changed the default layout and Command Bar** [D:RN5].
- *Window > Workspace > Ver. 4.2 layout* restores the old arrangement.
- Celsys published a support FAQ explaining how to get the old layout back [D:FL5].

**Default Command Bar buttons [D:MCB]:** Open Clip Studio, New, Open, Save, Undo, Redo, Erase, Fill, Scale/Rotate, Flip horizontal, Connect to smartphone, Support, Command Bar settings.

**Palette docks [D:MPAL]**
- Docks sit left, right, or bottom.
- Drag a palette by its title to float it, dock it, or stack it as tabs. A red indicator shows where it will land.
- Each dock has a double-arrow button that hides it and a single-arrow button that collapses it to icons ("simple view").
- An icon in simple view can open as a tab or as a pop-up (*Window > Palette dock > Display … as pop-up*). A pop-up closes when you tap outside it.
- Locking options: *Lock palette height*, *Fix the width of palette dock*, and *Lock palette position*.
- A dock can be fixed to a monitor edge.
- **Tab** hides or shows all palettes.

**Simple Mode is tablet and smartphone only.** It is not available on Windows [D:FSM][D:MSM].
- It was added in Ver. 2.1.0 (July 2023).
- It is a minimal UI: a left toolbar, size and opacity sliders along the bottom, a Layer palette on the right, and one canvas at a time.
- Long-press with a finger picks a colour. A transparent-colour icon works as an eraser.
- Preferences are reduced to about ten items [D:MUSM][D:FPREF].
- Since Ver. 3.0 it has keyboard shortcuts, but they are fixed and cannot be reassigned [D:FSM].
- On Windows touch devices you get a "touch gesture-friendly screen" instead, which resembles tablet Studio Mode [D:MGEST][D:MINT]. *Preferences > Interface > Switch to tablet interface* changes the base layout [D:MPREF].

**Interface scale on Windows**
- *Preferences > Interface > Touch Operations*: turning on *Adjust interface for touch operations* adds **Interface Scaling: Small / Default / Large**. It needs a restart [D:MPREF].
- Otherwise CSP follows Windows text and display scaling. The FAQ fix for oversized UI is the Windows "Make text bigger" slider [D:FDPI].
- The same Interface page has a Dark/Light theme switch and a UI "density" (contrast) slider [D:MPREF].

## 2. Tool structure (Tool group → Tool → Tool Settings)

**The hierarchy [D:MHOW][D:MTP]**
1. The **Tool palette** lists tool groups, such as Pen, Pencil, Brush, Eraser, Airbrush, Decoration, Blend, Liquify, Selection, Auto Select, Fill, Gradient, Operation, Figure, Text, Comic, Ruler, Correct line, Move, and Eyedropper.
2. The **Tool Group palette** lists the tools inside the chosen group, sometimes split into tabs. Each tool is effectively a saved preset.
3. The **Tool Settings palette** shows the commonly used settings.
4. The wrench icon opens **Advanced Tool Settings**, which shows every setting.

**How settings persist [D:MHOW][D:MCBT]**
- Changes are saved to the tool automatically.
- *Save all settings as default* sets a new baseline for the tool. *Reset* returns to that baseline.
- A **padlock** locks a tool. You can still change its settings, but they revert the next time you pick the tool.
- In Advanced Tool Settings, an eye checkbox next to any setting decides whether it also appears in the short Tool Settings palette [D:MCBT].

**Creating and organizing tools [D:MMOVE][D:MADD][D:MCUST]**
- *Duplicate tool* or *Create custom tool* makes a new tool.
- Tools can be dragged between groups. Dropping one on the Tool palette creates a new group.
- Supported imports: `.sut`, `.sutg`, and Photoshop `.abr`.
- Brushes downloaded from ASSETS go into a "Downloads" group.
- The list can show stroke previews, tiles, or text.
- *Add group from default* restores deleted defaults.

**Why people like or dislike it**
- Liked:
  - [S] Joyce, coming from Photoshop, praises saving tools together with their settings for quick access [S:JOYCE].
  - [I] The approach gives one-click recall of tuned brushes.
- Disliked:
  - [S] simonwl, a community TIPS author, opens by citing a viral joke that CSP is "confusing to use" and says the many buttons "can quickly make you feel overwhelmed" [S:SIMON].
  - [I] Settings are split between short and advanced palettes, so they are hard to find.
  - [I] Tools carry hidden state you may have changed by accident.
  - [I] The rename in Ver. 5 has made older tutorials mismatch the current UI.

**New in Ver. 5:** *Pop-up related palettes for each tool*. Tapping a tool-group icon pops up its Tool Group and Tool Settings palettes beside it. Tapping a colour icon pops up the Color Wheel and Color Set [D:RN5][D:MHOW].

## 3. Drawing workflow and brushes

**Tool families [D:MDRAW]**
- **Pen:** variable-width ink pens such as the G-pen, plus uniform-width markers.
- **Pencil** (also called "Sketch"): textured pencils and pastels.
- **Brush:** Watercolor and Thick paint groups, which blend.
- **Airbrush:** spray-type tools.
- **Decoration:** pattern brushes.

**Brush size and opacity**
- Brush Size palette presets [D:MBS], the Tool Slider palette [D:MTS], or the Tool Settings palette.
- **Ctrl+Alt+drag** on the canvas resizes the brush [D:SOP]. Two related preferences:
  - *Start from current size when changing brush size by dragging on canvas* [D:MPREF].
  - *Display brush size adjustment on canvas* shows a circle preview [D:MPREF].
- **[ / ]** step to the next smaller or larger *Brush Size palette preset* [D:SOPT]. They do not change size continuously.
  - The "Drawing and painting" page states the keys the other way round [D:MDRAW], so the manual contradicts itself.
- **Ctrl+[ / Ctrl+]** lower and raise opacity [D:SOPT].
- Shift+drag draws a straight line. Alt+click picks a colour [D:SOP].

**Advanced Tool Settings categories [D:MCBT]:** Brush size, Ink, Color Jitter, Anti-aliasing, Brush shape, Brush tip, Spraying effect, Stroke, Texture, Dual brush, Watercolor edge, Erase, Correction, Starting and ending, Anti-overflow.

**Anti-aliasing [D:GA]**
- Levels: None, Weak, Middle, Strong.
- *Speed and Quality* (Automatic / High / Low) lets large brushes render faster at lower quality [D:GA]. It was added in Ver. 4.2 [D:RN4].

**Vector vs raster layers [D:MVEC]**
- Vector layers store paths and control points. You can re-edit a stroke's width, colour, and brush tip, and the Object tool can scale it without loss.
- Fill, Gradient, Blend, and colour mixing do not work on vector layers.
- The Correct line tools edit vector strokes: Control point, Pinch vector line, Simplify, and Connect.

## 4. Stabilization and smoothing

**Per-tool settings in Correction [D:GC]**

| Setting | Effect |
|---|---|
| Stabilization | Higher values give smoother lines. |
| Adjust by speed | Stabilization varies with pen speed. Modes: *Increase stabilization when drawing slowly* (disabled at Stabilization ≥ 30) and *Reduce stabilization when drawing quickly* (cuts lag). |
| Post correction | Smooths the stroke after it is drawn. On vector layers, higher values mean fewer control points. Can be adjusted by speed and by zoom ("scale"). |
| Bezier curve vs spline | Controls how post correction fits the curve. |
| Taper | Lets the line continue as you lift off. |
| Vector magnet | Snaps new vector lines to earlier ones. |

- [T] The Stabilization slider runs **0–100** [T:TITI]. The manual's "≥30" threshold fits that range [I].
- Stabilization appears in the short Tool Settings palette by default [D:FSTAB].
- [D] Celsys recommends higher values for long strokes and lower values for a "traditional feel" [D:T9934].

**Lag countermeasures**
- *Preferences > Tools > Use stroke preview / Show stroke preview to reduce brush lag* draws a predicted stroke and cursor. Display options are *Automatic* or *Only when stabilization is low* (≤10) [D:MPREF].
- It was added in Ver. 4.2.0 (Dec 2025) for "drawing with a high Stabilization setting" [D:RN4]. The lag FAQ recommends it [D:FLAG].

**Smart Shape (Ver. 5.0)** [D:MSS][D:RN5][D:RNL]
- Hold the pen still at the end of a stroke and it becomes a clean line, arc, or shape.
- Default hold is 0.7 s, adjustable 0.1–1 s.
- A **Smart Shape launcher** offers alternative shapes and an edit mode with handles and control points.
- Since Ver. 5.1 it is also an Edit-menu command, *Correct current stroke*, which can take a shortcut.

**Smart Smoothing is not stroke smoothing.** It is an AI filter for upscaled raster images [D:MSMART].

## 5. Pressure, tilt, and tablet setup

**Three layers of pressure control [D:T1217][D:T9934]**
1. The tablet driver's curve.
2. The app-wide **File > Pen Pressure Settings**.
3. Per-tool dynamics curves.

**Calibrating by drawing [D:MPP][D:FPP]**
1. Draw strokes on the canvas while deliberately varying pressure.
2. Click **Check adjusted settings**.
3. Test-draw, then nudge the result with **Stronger** or **Lighter**, or edit the graph by hand.
4. Click **Done** to apply it to all tools.

The same wizard appears on first launch. *Reset to default* lives in the same dialog [D:FPPR].

**Per-tool dynamics [D:MCBT]**
- Settings such as size, opacity, and density have a dynamics button. It links the setting to **Pen pressure, Tilt, Velocity, or Random**, each with a minimum value and a curve.
- Velocity arrived in Ver. 4.1.0 (Jul 2025) [D:RN4] and is listed as a Ver. 5 feature [D:NEWS5].

**Preferences > Tablet (Windows) [D:MPREF]**
- **Wintab** is for screen and pen tablets. **TabletPC** is for Windows Ink devices.
- *Use mouse mode in tablet driver settings* is an option.
- *Set tablet operation area with application* maps the tablet to a monitor area, either keeping aspect ratio or using the whole tablet. Celsys advises using the driver for this when the driver can do it.
- *Input frequency*: Prefer speed or Prefer quality.

**Pen-display advice from support**
- CSP "obtains coordinates directly from the tablet, allowing it to draw highly accurate lines". Mouse mode trades that precision away to fix offset problems [D:FMM].
- Different scaling on two monitors used to cause pen offset. This was fixed in Ver. 1.11.0, and matching the scaling still helps [D:FMM].
- For delays, select Wintab and uncheck **Use Windows Ink** in the driver [D:FLAG].
- If the stroke start becomes a straight line, set *Input frequency = Prefer quality* [D:FLAG].
- Also in Preferences: *Hover over drawing on applicable devices* and *Reduce pen pressure … with finger/mouse* [D:MPREF].
- [I] On a Kamvas 13 used as a second monitor, apply the same advice: Wintab, Windows Ink off in the Huion driver, and equal scaling on both screens.

## 6. Cursor and brush preview (Preferences > Cursor) [D:MPREF][D:FCUR][D:RNL]

- **Cursor categories**, each set separately: Brush-type, Pen-type, Eraser-type, Figure-type, Fill-type, View-type, and Line-correction-type.
- **Shapes** include None, Brush size, Brush shape, Sight, Single pixel dot, Brush size and Cross, and Tool specific.
  - Ver. 5.1 adds *Brush size and triangle* and *Brush shape and triangle*.
  - A brush with Spraying effect shows a circle even when Brush shape is selected.
- **Additional display when the brush cursor is small:** None, Dot, or Line.
- **Display position of reversed cursor:** *No delay* or *Delay for stabilization*. The second option makes the cursor follow the stabilized position.
- **Change brush cursor:**
  - Do not change.
  - Direction only.
  - Size, thickness & direction, driven by pressure and tilt.
  - The same while the pen is hovering.

## 7. Eraser and quick switching

**The Eraser tool group** (shortcut **E**) [D:STOOL][D:MERA]
- Option: *Erase on all layers*.
- **Vector eraser** modes [D:MERA]:
  - *Erase touched areas*.
  - *Erase up to intersection*, optionally with *Refer all layers*.
  - *Whole line*.

**Transparent colour [D:MSEL][D:MERA]**
- Any brush can erase by switching to the third colour icon, transparent. **C** toggles between the drawing colour and transparent [D:SOPT].
- Celsys notes this gives a "more seamless" erase than eraser tools.
- On vector layers, transparent strokes become vector lines, so use the Eraser there.
- An optional setting makes transparency paint white on layers with transparency locked [D:MPREF].

**Temporary switching**
- Holding a tool's shortcut key switches to that tool until you release it. The setting is *Switch tool temporarily by pressing and holding shortcut key*, with an adjustable press length; the manual screenshot shows 500 ms [D:MPREF][D:STOOL].
- **, / .** select the previous or next tool in the group list [D:SOPT].

**Pen eraser end**
- The Modifier Key Settings dialog lets you assign the eraser end ("tail switch") [D:MMOD].
- A user reported the eraser end was "locked to the eraser tool" until they changed it there [S:ASK].
- If the tool gets stuck on Eraser, the FAQ says to check Wintab and the driver [D:FERA].

## 8. Colour

**Three colour icons:** main, sub, and transparent. They appear under the Tool palette and on the Wheel and Slider palettes. **X** swaps main and sub [D:MSEL][D:SOPT].

**Colour palettes**
- **Color Wheel:** HSV or HLS, with a ring and a square [D:MCW].
- **Color Slider:** RGB, HSV/HLS, and CMYK. Lab was added in Ver. 5.1 [D:MCSL][D:RNL].
- **Color Set:** sets you can edit and import from ASSETS. *Auto-register color in eyedropper* adds every picked colour to the set. Alt+click replaces a tile [D:MCSET].
- **Color History** [D:MCH].
- **Intermediate Color:** a grid that blends between four corner colours [D:MIC].
- **Approximate Color:** a grid of variations along two chosen axes [D:MAC].
- **Color Mixing:** a scratch area to paint and blend colours, with its own brush, blend, and eyedropper tools and a colour-set bar [D:MMIX].

**Eyedropper**
- **Alt+click** inside drawing tools [D:SOP].
- **Right-click** is the global eyedropper by default [D:SOP].
- **I** selects the Eyedropper tool [D:STOOL].
- On Windows you can also pick a colour from anywhere on screen [D:MPSC].

## 9. Layers

**Layer palette [D:MLAY][D:MLB]**
- The properties bar holds:
  - Palette colour (colour tag).
  - Blending mode and opacity.
  - Clip to layer below.
  - Reference layer (lighthouse icon).
  - Draft layer.
  - Lock.
  - Lock transparent pixels (clipping, locking and the paper layer are covered in [D:MLO]).
  - Mask settings.
  - Layer colour.
- The command bar holds New raster, New vector, New folder, a two-pane split view, and more.

**Shortcuts and gestures**
- **Ctrl+Shift+N** new raster layer, **Ctrl+Alt+G** clip, **Ctrl+E** merge down, **Ctrl+G** group, **Alt+] / Alt+[** select the layer above or below [D:SMENU].
- Ctrl+Shift+click on the canvas selects the layer under the cursor [D:SOP].
- Ctrl+click a thumbnail makes a selection from that layer.
- Alt+click the eye icon shows only that layer (solo); doing it again restores visibility.
- Alt+click a new-layer icon opens the New Layer dialog [D:MLAY][D:MLB].

**Reference, draft, and palette colour**
- **Reference layers** let fill and selection tools read your line art while you paint on another layer [D:MLREF].
- **Draft layers** are excluded from fills and exports [D:MLDR].
- Palette colours can tint the matching folder or overlay the canvas [D:MLPC].

**Layer Properties palette** offers Border effect (edge or watercolour edge), Tone, Layer colour, and Expression colour [D:MLPROP]. Blending modes are listed in [D:MBLEND].

## 10. Selections, transforms, and fill

**Selection tools [D:MSA]**
- Tools: Rectangle, Ellipse, Lasso, Polyline, Selection pen, Erase selection, Shrink selection, Magnetic lasso, and Auto select (W).
- Modifiers: Shift adds, Alt subtracts, Shift+Alt intersects.

**Selection Launcher [D:MSL]**
- A grey floating bar that appears under every selection.
- Default buttons: Deselect, Crop, Invert, Expand, Shrink, Delete, Clear outside, Cut & paste, Copy & paste, Move/Transform, Fill, New tone, and Settings.
- You can drag it by its handle; Esc while dragging resets its position.
- It is fully customizable, including nested button groups. Its icons are shared with the Command Bar and Quick Access palette.
- *View > Selection Launcher* toggles it.

**Selection shortcuts [D:SMENU]:** Ctrl+A select all, Ctrl+D deselect, Ctrl+Shift+D reselect, Ctrl+Shift+I invert. Another manual page swaps Deselect and Reselect [D:MSB], another documentation inconsistency.

**Transform [D:SMENU][D:MTBB][D:MTT]**
- **Ctrl+T** Scale/Rotate. **Ctrl+Shift+T** Free Transform.
- The bounding box has a blue rotation handle, an orange handle that rotates only the box (new in Ver. 5), and a movable reference point.
- A **Transform launcher** offers OK and Cancel. You can also confirm with Enter or a double-click and cancel with Esc.
- Mesh and Puppet Warp are available in PRO and EX.

**Liquify tool [D:MLIQ]:** a brush-based tool with Push, Expand, Pinch, Push left/right, and Twirl modes. Alt inverts the mode.

**Fill tools [D:MFILL]**
- *Refer only to editing layer*, *Refer other layers*, *Enclose and fill*, *Lasso fill*, and *Leftover pen*.
- **Close gap** treats small breaks in the line art as closed and has an adjustable size [D:GF].
- **Refer multiple** can use all layers, reference layers, selected layers, or layers in the folder, with exclusions such as draft layers [D:GR].
- *Area scaling* deliberately overfills under the line art [D:MAFILL].

## 11. Canvas navigation [D:SOP][D:SMENU][D:MNAV][D:STOOL]

**Pan and zoom**
- Space+drag pans. H selects the Hand tool.
- Ctrl+Space+click zooms in. Alt+Space+click zooms out.
- Ctrl+Num+ or Ctrl+; zooms in. Ctrl+Num− or Ctrl+− zooms out.
- The mouse wheel zooms.
- **/** selects the Zoom tool.

**Rotate**
- **Shift+Space+drag** with the pen rotates the view, as does Shift+mouse wheel.
- **R** selects the Rotate tool.
- The **−** and **^** keys rotate in 5° steps, adjustable under *Preferences > Canvas > Angle*.
- [I] These defaults suit a Japanese keyboard layout. On a US keyboard ^ needs Shift.

**Reset and flip**
- Ctrl+Alt+0 shows 100%. Ctrl+0 fits to screen. **Ctrl+@** resets the display.
- **Flip horizontal/vertical** is in the Navigator icons and *View > Rotate/Flip*. No default shortcut is listed [D:SMENU].

**Navigator palette:** zoom bar, rotation bar, reset buttons, flip, *Fit to Navigator*. Ver. 5.1 adds *Show canvas frame* and a preference that limits the zoom range [D:RNL].

**Display quality:** *Default* can look pixelated when the view is rotated; *High quality* is slower. *Use fast view while navigating canvas* helps speed [D:MPREF].

## 12. Keyboard and modifier customization

**Shortcut Settings** (*File > Shortcut Settings*, Ctrl+Shift+Alt+K) [D:MSC][D:SMENU]
- Categories: Menu commands, **Pop-up palettes**, Options (size, opacity, colour swaps), Tools, and Auto Actions.
- Pop-up palettes show a palette next to the cursor.
- Side buttons can be captured as shortcuts only for Wacom styluses.

**Modifier Key Settings** (*File > Modifier Key Settings*, Ctrl+Shift+Alt+Y) [D:MMOD]
- You assign actions to Ctrl, Alt, Shift, Space, combinations of them, the mouse wheel, right-click, the tail switch, and touch gestures.
- Available actions: *Change brush size*, *Change tool temporarily*, *Tool aux. operation*, *View operation*, *Show menu*, and *Common*.
- There are two scopes: **General** and **Tool-specific**.
- Tool-specific settings are actually stored per input/output-process combination, so a change affects every tool that shares it.

**Shortcuts are saved in workspaces.** Workspaces can include shortcuts, so switching workspaces can swap your keys [D:MWS].

## 13. Tablet-oriented and reduced hand movement

**Command Bar [D:MCB]**
- Can hold menu commands, pop-up palettes, tools, Auto Actions, and *drawing colours*.
- Supports custom icons and grouped dropdowns.
- Ctrl+drag rearranges buttons.

**Quick Access palette [D:MQA]**
- Named sets of buttons, shown as tiles or a list.
- A **search bar** that finds any tool or feature and adds it.
- Not available in DEBUT.

**Companion Mode [D:MCOMP]**
- Turns a phone into a remote over the same Wi-Fi network, connected by QR code.
- Screens: Quick Access, Color Wheel with size and opacity sliders, a Gesture Pad (pan, two-finger undo, view reset and flip buttons), Sub View (reference image and colours), Color Mixing, and Mode switch.
- No phone plan is required.

**Edge keyboard** is a tablet-only on-screen modifier strip. It is not available on Windows touch devices [D:MEDGE].

**Clip Studio Tabmate 2** is an optional Bluetooth handheld controller [D:MTAB2].

**Touch and pen buttons**
- Windows touch gestures, with an option for Clip Studio or Windows gestures, apply only to touch-capable devices [D:MGEST][D:FGEST].
- [I] The Kamvas 13 is pen-only as far as I know (not verified), so touch gestures would not apply to it.
- Pen side buttons are set in the driver. A pen tip touching is a left click, and the side button usually sends a right click [D:T1217]. CSP's default right-click is the eyedropper [D:SOP]. [I] So by default, a side button pressed on the canvas picks a colour.

## 14. Workspace customization [D:MWS][D:MPAL]

- *Window > Workspace* offers **Illustration**, **Comic**, and **By category**, plus *Reset to Default* and *Ver. 4.2 layout*.
- A workspace stores:
  - palette layout
  - shortcuts
  - Command Bar
  - unit setting
- *Register Workspace* and *Manage Workspace* save and edit layouts. *Reload (name)* restores a saved one.
- An import dialog chooses which parts to apply.
- Workspaces can be registered as materials or downloaded from ASSETS via *Search for workspace material(s)*.
- Palette locks: position, width, and height.
- *Disable palette operation with finger or mouse* prevents accidental palette moves [D:MPREF].

## 15. Distraction-free

- **Tab** hides or shows all palettes.
- **Shift+Tab** hides the title bar and menu bar. On Windows you may need to press it twice to restore them [D:MHTB].
- Since Ver. 5, the canvas tab is hidden when only one canvas is open [D:RN5].
- No dedicated full-screen command was found in the manual [I].

## 16. Undo and redo

**Default keys and buttons [D:SMENU][D:MUNDO]**
- Ctrl+Z undoes. Redo is **Ctrl+Y and Ctrl+Shift+Z** (both are defaults).
- The Command Bar has undo and redo buttons.
- On touch devices, a two-finger tap undoes and a three-finger tap redoes.

**History and revert**
- The History palette jumps to any earlier state.
- *File > Revert* returns to the last save.
- *Edit > Clear work history* (named in Ver. 5) clears the history [D:MNEW].

**Undo settings: Preferences > Performance [D:MPREF]**
- **Undo levels.**
- **Undo interval between actions**, which merges fast consecutive strokes into one undo step.

| Value | Source |
|---|---|
| Manual screenshot: 200 levels, 0 ms | [D:MPREF] |
| Mobile default: 30 levels since Ver. 2.3.4 | [D:FOPT] |
| Range 1–200; interval default 200 ms (conflicts with the screenshot) | [T:PIPE] |

A high undo count can cause lag at the end of strokes. Support advises lowering it, or setting the interval to 0 [D:FLAG].

## 17. Discoverability

**Search**
- There is no menu-wide command search.
- The closest thing is the **Quick Access search bar**, which finds tools and features and shows where each one sits in the Quick Access settings categories [D:MQA].
- The Material palette has keyword, tag, and catalog search, a Recently used list, sorting by Frequency, and pinning [D:MMAT][D:RNL].

**Help and guidance**
- F1 opens Training and Tutorials [D:SMENU].
- On tablets, *Show explanation when tapping icon* explains buttons [D:MPREF].
- Hovering over colour tiles shows their values [D:MCSET].
- The manual's "Settings Glossary" documents every tool setting [D:M0].

**Friction [I]**
- Settings are spread across Tool Settings, Advanced Tool Settings, Preferences, Modifier Keys, and Shortcuts.
- Names changed in Ver. 5.
- Some manual pages contradict each other, such as [ ] and Deselect/Reselect.

## 18. Getting to a first stroke

- CSP opens from the Clip Studio launcher's **Draw** button, or directly [D:MINT].
- First launch shows Privacy Settings and the pen-pressure wizard [D:MPREF][D:MPP].
- It then creates a **plain illustration canvas automatically** [D:MCAN].

**The New dialog [D:MNEWC]**
- Opens with Ctrl+N.
- Project types: Illustration, Webtoon, Comic, Fanzine (EX only), all comic settings, and Animation.
- Presets you can save, units, resolution, expression colour, paper colour, colour profile, and a *Record timelapse* option.
- Ver. 5.1 adds the date to the default file name [D:RNL].

## 19. Frustrations and complexity

**Switching from Photoshop**
- [S] Joyce called the switch "incredibly uncomfortable at first" [S:JOYCE]. His complaints:
  - **B** is shared by Brush, Airbrush, and Decoration, so he got star stamps when he wanted a brush.
  - Pressure felt different from Photoshop.
  - Default opacity behaviour was confusing.
- He advised rebinding hotkeys before doing any artwork. He preferred editing the pressure curve by hand over the drawing-based calibration [S:JOYCE].

**Other friction**
- [S] simonwl: the default UI feels overwhelming; the answer is to strip it down [S:SIMON].
- [D] Several keys are shared between tools: P (Pen/Pencil), B (Brush/Airbrush/Decoration), G (Fill/Gradient), U (Figure/Frame/Ruler), T (Text/Balloon) [D:STOOL].

**Differences from Photoshop [D/I]**
- Brush size is Ctrl+Alt+drag. [ ] step through presets rather than resizing smoothly.
- Right-click is the eyedropper.
- Redo is Ctrl+Y as well as Ctrl+Shift+Z.
- Some defaults, such as **^** and **@**, assume a Japanese keyboard.

**Other complexity**
- [D] Modifier settings are shared across tools with the same input/output process [D:MMOD].
- [D] The layout change in Ver. 5 needed its own FAQ [D:FL5].
- [D] Several customization features are missing from DEBUT: Command Bar customization, Quick Access, Modifier Key Settings, and Advanced Tool Settings editing [D:MCB][D:MQA][D:MMOD][D:MCBT].
- [S] Parka: there's "no perfect UI"; both apps' workspaces are configurable [S:PARKA].

## 20. What CSP does especially well for drawing, and why

1. **Stabilization tuned per tool, with speed awareness and post-correction**, plus stroke preview to hide the lag [D:GC][D:RN4].
   - *Why it works:* line feel depends on the tool, so an inking pen can be smooth while a sketch pencil stays raw, with no global toggle.
   - [S] Mius increases stabilization for inking and calls it an option she "truly appreciate[s]" [S:MIUS].
2. **Pressure calibration by drawing** [D:MPP].
   - *Why it works:* the user's natural strokes set the curve, instead of asking them to read a graph.
   - It stacks with per-tool curves, so a pen can be firm and a brush soft [D:T1217].
3. **The transparent colour as an "eraser mode" for any brush**, toggled with C [D:SOPT][D:MERA].
   - *Why it works:* erasing keeps the brush's own texture and taper, and there is no tool swap.
4. **The vector eraser that erases up to intersections** [D:MERA].
   - *Why it works:* overshot ink lines are cleaned in one swipe.
5. **Fill tools that refer to other layers, close gaps, and offer lasso fill**, plus reference layers and anti-overflow [D:MFILL][D:GF][D:MLREF].
   - *Why it works:* line art and flat colours stay on separate layers without manual masking.
   - [S] Joyce praises fills that respect the line art [S:JOYCE].
6. **Hold-to-switch tools and Ctrl+Alt brush resizing** [D:MPREF][D:SOP].
   - *Why it works:* the hand stays on the pen.
7. **Smart Shape** [D:MSS].
   - *Why it works:* clean shapes come from the same brush and gesture, with no switch to a shape tool.
8. **Contextual floating bars: the Selection Launcher and Transform launcher** [D:MSL][D:MTBB].
   - *Why it works:* the next step appears right where the pen already is.
9. **Precise tablet input** — coordinates are read directly from the tablet via Wintab [D:FMM].

[I] Takeaways for DoodleNook:
- Copy items 1–3, 6, and 8.
- Avoid the two-level settings split and modifier rules shared across tools.
- Offer Photoshop-style key presets up front.
