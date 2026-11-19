# B. GIMP painting engine: source study (GIMP 3.2.8 / GEGL 0.4.72)

Sources: `gimp` at tag GIMP_3_2_8 (7addd2b7), `gegl` 0.4.72 (02096df3), `babl` 0.1.128. libmypaint was not available, so only GIMP's calls into it are described. Paths are relative to each repo root and prefixed `gimp:` or `gegl:`.
Labels: **CONFIRMED** means I read it in the source. **INFERRED** means I reasoned it from the source but did not see it stated or measured. I took no performance measurements, so this report contains no performance numbers.

---

## 0. Executive summary

1. **Most of the stroke work happens off the UI thread.** The UI thread receives events, filters them (GimpMotionBuffer), smooths them, and pushes "interpolate to this point" jobs onto a queue. One global **paint thread** consumes the queue and does dab generation and pixel writes. A **10 ms timer on the UI thread** copies the dirty regions into the buffer that the compositor reads, recomposites only those regions, and asks GTK to redraw. (CONFIRMED: `gimp:app/tools/gimppainttool-paint.c`)
2. **Dabs and compositing are separate stages.** The brush core makes an alpha **mask** for each dab (cached, subpixel-shifted). The paint core combines that mask into a per-stroke float **canvas_buffer** ("stroke mask"). One fused per-pixel pass then blends the paint colour onto the layer, reading either from the **pre-stroke snapshot** (CONSTANT mode) or from the current pixels (INCREMENTAL mode). In CONSTANT mode the stroke opacity cannot build up past the dab opacity. (CONFIRMED: `gimppaintcore.c:1265 gimp_paint_core_paste`, `gimppaintcore-loops.cc:1255-1330`)
3. **Spacing is a fraction of brush diameter**, measured in brush space. Dabs are placed by **linear interpolation** between successive event points. Pressure, tilt, wheel and velocity are interpolated linearly per dab. (CONFIRMED: `gimpbrushcore.c:448-802`)
4. **Dynamics are curve lookups.** Each output (opacity, size, angle, colour, hardness, force, aspect, spacing, rate, flow, jitter) has its own set of enabled inputs (pressure, velocity, direction, tilt, wheel, rotation, random, fade). Each input has its own curve, and the output is the **average** of the mapped inputs, not their product. (CONFIRMED: `gimp:app/core/gimpdynamicsoutput.c:491`)
5. **Undo and snapshots are copy-on-write at the tile level.** At stroke start the layer buffer is "duplicated" with `gimp_gegl_buffer_dup` (GEGL shares tile data). At stroke end the stroke's bounding box (aligned to tiles) is copied from that snapshot into a GimpDrawableUndo. Memory is only duplicated for tiles that were actually written. GEGL tiles default to 128×128. (CONFIRMED for the mechanism. The memory claim is INFERRED from COW semantics.)
6. **Everything is CPU.** There is no GPU path in painting, layer-mode compositing, the projection or the display (no OpenCL/GL references in `app/paint`, `app/display`, `gimpprojection.c` or the layer modes). GEGL OpenCL is an "experimental/playground" preference that is off by default and only affects some GEGL operations. The canvas is shown through cairo image surfaces. (CONFIRMED)
7. **Parallelism inside a dab:** the per-pixel paint loops and mask ops use `gegl_parallel_distribute_area`, at a cost of about one thread per 64×64 pixels. Small dabs therefore run single-threaded. (CONFIRMED: `gimppaintcore-loops.cc:42,2205`)

---

## 1. Pipeline diagram

```
 GTK event (GdkEventMotion/Button, axes)              [UI thread]
   │  gimp_display_shell_canvas_tool_events()         display/gimpdisplayshell-tool-events.c:356
   │   ├─ check_device(): device switch → per-device tool preset         :1667
   │   ├─ get_event_coords(): GimpDeviceInfo axes → GimpCoords; pressure through device curve
   │   │                                                widgets/gimpdeviceinfo-coords.c:35, gimpdeviceinfo.c:1396
   │   └─ untransform_event_coords(): window → image coords (+scale/angle/reflect)
   │                                                     display/gimpdisplayshell-transform.c:595
   │  BUTTON_PRESS: event compression OFF for canvas window (EXACT mode); motion_buffer_begin_stroke
   │               → tool_manager_button_press_active → GimpPaintTool::button_press → paint_start
   │  MOTION (button1): [optional gdk_device_get_history()] →
   ▼
 GimpMotionBuffer (display/gimpmotionbuffer.c)
   │  motion_event(): drop sub-screen-pixel moves; velocity (smoothed); direction (smoothed);
   │                  "event fill": Catmull-Rom between sparse events (delays 1 event, 50 ms timeout)
   │  request_stroke() → "stroke" signal per queued coord
   ▼
 gimp_display_shell_buffer_stroke() → tool_manager_motion_active() → GimpPaintTool::motion
   │  gimp_paint_tool_paint_motion()                   tools/gimppainttool-paint.c:531
   │   └─ gimp_paint_core_smooth_coords()  (velocity-windowed Gaussian average of last N points)
   │   └─ gimp_paint_tool_paint_push(interpolate job) → GQueue + GCond
   ▼
 ══════════════ "paint" GThread (one, global) ══════════════
 gimp_paint_core_interpolate() → GimpBrushCore::interpolate        paint/gimpbrushcore.c:448
   │  spacing in brush space (fraction of diameter), dynamic spacing, jitter;
   │  per dab: lerp(x,y,pressure,tilt,wheel,velocity) → gimp_paint_core_paint(MOTION)
   ▼
 GimpPaintbrush::paint → _gimp_paintbrush_motion()              paint/gimppaintbrush.c:219
   │  opacity  = dyn(OPACITY) [*flow for airbrush]; scale/angle/hardness/aspect = dyn(...)
   │  colour   = FG | gradient(dyn COLOR) | pixmap brush;  force = dyn(FORCE) | option
   │  get_paint_buffer(): GimpTempBuf of dab bbox in RGBA float; fill with colour
   ▼
 gimp_brush_core_paste_canvas()                                  paint/gimpbrushcore.c:967
   │  get_brush_mask(): gimp_brush_transform_mask (LRU cache 20) →
   │       subsample (5×5 subpixel variants, 3×3 kernel) | solidify (hard) | pressurize (force)
   ▼
 gimp_paint_core_paste()                                         paint/gimppaintcore.c:1265
   │  CONSTANT:    mask ⊕→ canvas_buffer ("Y float", per-stroke), src = undo snapshot
   │  INCREMENTAL: mask*opacity → paint alpha,               src = current pixels
   │  gimp_paint_core_loops_process(): fused per-pixel pass, layer-mode blend in RGBA float,
   │     selection mask, component mask; gegl_parallel_distribute_area(64×64 px/thread)
   │  writes into drawable->paint_buffer (COW copy of the layer, private to the paint thread)
   │  gimp_drawable_update() → while painting: accumulate copy_region (tile-aligned) and
   │                           update_region (32×32-aligned)                core/gimpdrawable.c:1276
 ═══════════════════════════════════════════════════════════
   ▼  every 10 ms (G_PRIORITY_HIGH_IDLE timer on UI thread, takes paint_mutex)
 gimp_paint_tool_paint_timeout()                                 tools/gimppainttool-paint.c:164
   │  gimp_drawable_flush_paint(): COW-copy copy_region paint_buffer → real buffer;
   │                               emit "update" for update_region rects    core/gimpdrawable.c:2193
   │     → layer stack "update" → gimp_image_invalidate() → projection add_update_area (32×32)
   │  gimp_projection_flush_now(direct): validate dirty rects = gegl_node_blit(image graph)
   │     (layer modes = GEGL point-composer ops, multi-threaded) → projection "update"(now)
   │     → gimp_display_update_area(now) → shell expose_area + render_invalidate_area
   │  gimp_display_flush_now() → gtk_widget_queue_draw_area()
   ▼  GTK frame → canvas "draw"
 gimp_display_shell_draw_image()                                  display/gimpdisplayshell-draw.c:145
   │  chunked; per chunk if not in render_cache_valid: gimp_display_shell_render():
   │  gegl_buffer_get(projection, scale, "cairo-ARGB32", filter) [+colour mgmt / display filters]
   │  → render_cache (cairo image surface) → cairo_paint to window
   ▼
 Button release → paint_end: drain queue (keeps flushing every 10 ms while waiting) → FINISH →
 gimp_paint_core_finish(): crop undo snapshot to stroke bbox (tile-aligned) →
 gimp_drawable_push_undo() inside GIMP_UNDO_GROUP_PAINT + GimpPaintCoreUndo; gimp_drawable_end_paint();
 gimp_image_flush() → idle, chunked projection render for anything still pending.
```

---

## 2. Stage-by-stage trace

### 2.1 Event entry and GimpCoords (CONFIRMED)
- `GimpCoords` (`gimp:app/core/core-types.h:281-303`) holds the device axes `x, y, pressure, xtilt, ytilt, wheel, distance, rotation, slider`, the synthetic axes `velocity, direction`, and the view transform `xscale, yscale, angle, reflect`. Defaults are set by `GIMP_COORDS_DEFAULT_VALUES` (`:72`).
- Entry point: `gimp_display_shell_canvas_tool_events()` (`gimp:app/display/gimpdisplayshell-tool-events.c:356`). It calls `gimp_display_shell_get_event_coords()` (`:2426` → `gimp_device_info_get_event_coords()`, `gimp:app/widgets/gimpdeviceinfo-coords.c:35`) and then `gimp_display_shell_untransform_event_coords()` (`:2453`).
- **Pressure** goes through the **per-device pressure curve** (`gimp_curve_map_value(info->priv->pressure_curve, …)`, `gimpdeviceinfo.c:1396-1407`). Tilt and wheel are only clamped.
- Window to image: `gimp_display_shell_untransform_coords()` (`gimpdisplayshell-transform.c:595`). It applies the inverse rotation matrix, adds the scroll offset and divides by the zoom. It also writes `xscale/yscale/angle/reflect` into the coords so the paint code can implement "lock brush to view" and screen-space velocity. **Tools always receive image coordinates** (comment at `:2461`).
- On button press (`:576-610`), for `GIMP_MOTION_MODE_EXACT` tools (all paint tools: `gimppainttool.c:162`), GIMP calls `gdk_window_set_event_compression(canvas, FALSE)`. The adjacent comment says "enable motion compression", but the call actually **disables** compression for the duration of the stroke. Compression is turned back on at release (`:750`). The press coords take the **previous** motion's velocity and direction (`:595-602`) so that the derivative dynamics do not spike.
- Motion (`:885-1020`): if `use-event-history` is on (default **FALSE**, `gimp:app/config/gimpdisplayconfig.c:43`), GIMP pulls the intermediate samples with `gdk_device_get_history()`. Otherwise it uses the single event. Motion is ignored while `gimp->busy`.
- Tablet eraser end, from GIMP's point of view: each GdkDevice has a `GimpDeviceInfo`, **which is a subclass of GimpToolPreset** (`gimpdeviceinfo.c:140`). When a different device generates events, `gimp_devices_check_change()` makes it current. If `devices-share-tool` is FALSE (the default, `gimpguiconfig.c:183-188`), `gimp_device_manager_set_current_device()` restores that device's tool, options, brush and colours (`gimpdevicemanager.c:280-315`, `gimpdeviceinfo.c:1046 gimp_device_info_restore_tool`). So the "eraser end" works by remembering a tool **per device**. GIMP has no eraser-specific logic. Which GdkDevice the eraser end appears as depends on the OS/GTK backend, which the other agent covers.

### 2.2 GimpMotionBuffer: filtering, velocity, direction, event fill (CONFIRMED)
`gimp:app/display/gimpmotionbuffer.c`. The constants are at `:37-40`: `VELOCITY_UNIT 3.0`, `EVENT_FILL_PRECISION 6.0`, `SMOOTH_FACTOR 0.3`.
- **Filter** (`:298-304`): the event is dropped if |dx| and |dy| are both under half a screen pixel, expressed in image units (`MIN(1/scale_x, 1/scale_y)/2`).
- **Timestamp smoothing:** `delta_time = 0.7*last_dt + 0.3*dt` (`:273-274`).
- **Velocity** (`:317-332`) is the screen-space distance divided by the smoothed dt, divided by 3, then smoothed with a factor that depends on velocity, and clamped to [0,1]. The first event of a stroke gets velocity 1.0.
- **Direction** (`:334-390`) comes from `gimp_coords_direction()` against an older history point when the step is tiny (history of 4). It is then averaged 50/50 with the previous direction, with wrap-around handling, and normalised to [0,1).
- **Event fill** (`:393-409`, `:540-572`): in EXACT mode, if two consecutive events are at least 6 image px apart, the buffer holds back one event. When the next event arrives it emits Catmull-Rom points (`gimp_coords_interpolate_catmull`, 3 px precision) that also interpolate pressure and velocity. A **50 ms timeout** (`:490-493`, `:575`) flushes the held event if nothing else comes in. Cost: **up to one event of added latency** during fast strokes.
- `request_stroke()` emits a "stroke" signal for each queued coord. `gimp_display_shell_buffer_stroke()` (`tool-events.c:1492`) then calls `tool_manager_motion_active()`.

### 2.3 Paint tool and the paint thread (CONFIRMED)
`gimp:app/tools/gimppainttool.c`, `gimppainttool-paint.c`.
- `gimp_paint_tool_button_press` (`:271`) checks layer locks and alpha (`gimp_paint_tool_check_alpha`) and then calls `gimp_paint_tool_paint_start` (`-paint.c:250`).
- **The thread:** there is one static `GThread *paint_thread` named "paint" for all paint tools (`-paint.c:83-113`). It is created lazily unless the env var `GIMP_NO_PAINT_THREAD` is set. The thread is not used for Shift-click straight lines (`draw_line`).
- **Queue:** `GQueue paint_queue` with a mutex and a condition variable (`:88-90`). `gimp_paint_tool_paint_push()` (`:483`) appends a `PaintItem {func, data}`. The thread pops items and runs each one while holding `paint_mutex` (`:125-160`). The queue is **unbounded** and items are **not coalesced**. INFERRED: if dab rendering falls behind the input rate, the backlog and the visible lag both grow.
- **What runs on which thread:** smoothing (`gimp_paint_core_smooth_coords`, `-paint.c:557`) runs on the UI thread **before** the push. The job (`gimp_paint_tool_paint_interpolate`, `:211`) runs `gimp_paint_core_interpolate()` on the paint thread.
- **Display timer:** `g_timeout_add_full(G_PRIORITY_HIGH_IDLE, DISPLAY_UPDATE_INTERVAL/1000 = 10 ms, gimp_paint_tool_paint_timeout)` (`:45`, `:371-381`). The callback (`:164`) sets `paint_timeout_pending` so the thread waits before starting its next item, takes `paint_mutex` (this blocks until the current item finishes), calls `gimp_drawable_flush_paint()`, releases the lock, and then calls `gimp_projection_flush_now(proj, TRUE)` and `gimp_display_flush_now()`. INFERRED: one large interpolate job, such as a long segment with a big brush, can stall the UI thread for as long as that job takes.
- **Double buffer:** `gimp_drawable_start_paint()` (`gimp:app/core/gimpdrawable.c:2146`) creates `paint_buffer = gimp_gegl_buffer_dup(buffer)`. While `paint_count > 0`, `gimp_drawable_get_buffer()` returns **paint_buffer** (`:1562-1570`), so all paint code writes into this private COW copy. `gimp_drawable_update()` (`:1276`) does not emit signals during painting. Instead it unions a **tile-aligned copy region** and a **32×32-aligned update region** (`PAINT_UPDATE_CHUNK_*`, `:64-65`, `:1303-1358`). `gimp_drawable_flush_paint()` (`:2193`) copies the copy region into the real buffer with `gimp_gegl_buffer_copy`. That copy is tile-COW because the region is tile-aligned and the formats match (`gegl:gegl/buffer/gegl-buffer-access.c:2460-2557`, `gegl_tile_dup` at `gegl/buffer/gegl-tile.c:119`). It then emits UPDATE for the update rects. The result is that the compositor never reads pixels the paint thread is writing.
- `paint_end` (`:385`) pushes a `PAINT_FINISH` sentinel and waits for it. While waiting it keeps running the flush every 10 ms. Then it runs FINISH, `gimp_paint_core_finish` (or `_cancel`), and `gimp_drawable_end_paint`.
- Release (`gimppainttool.c:402`) calls `gimp_image_flush(image)` (`:433`), which schedules idle work for the projection, previews and so on.

### 2.4 Smoothing / stabilisation (CONFIRMED algorithm, INFERRED feel)
`gimp_paint_core_smooth_coords()` (`gimp:app/paint/gimppaintcore.c:1660`). Options are `use-smoothing` (default FALSE), `smoothing-quality` (1-100, default 20) and `smoothing-factor` (3-1000, default 50) (`gimppaintoptions.c:79-80, 462-482`).
- The raw coords are appended to `core->stroke_buffer`, a GArray of the stroke's history.
- Over the last `quality` samples, from newest to oldest, it accumulates `velocity_sum += v_i*100`. The weight is `w_i = (1/(√(2π)·σ)) · exp(-velocity_sum² / (2σ²))` with σ = factor. The output x,y is the weighted mean.
- Effect: the weights depend on **accumulated velocity**, not on the sample index. Slow movement averages many points (strong smoothing). Fast movement collapses the weights onto recent points (less lag). Only x and y are smoothed. Pressure and tilt pass through unchanged.
- INFERRED: this is a trailing weighted average with no catch-up at pen-up, so a smoothed stroke stops short of the release point. No "pull string" or lazy-mouse stabiliser exists.

### 2.5 Paint core lifecycle and buffers (CONFIRMED)
`gimp:app/paint/gimppaintcore.c`
- `gimp_paint_core_start` (`:358`):
  - resets `stroke_buffer`;
  - creates `undo_buffers[drawable] = gimp_gegl_buffer_dup(drawable buffer)` (`:434`). This is a COW snapshot. Because the paint thread is active, it duplicates paint_buffer;
  - optionally creates `saved_proj_buffer` as a dup of the projection, used by clone/heal with sample-merged (`:420`);
  - creates `canvas_buffer = gegl_buffer_new(max drawable size, "Y float")` (`:443-446`). This buffer is **sparse**: unwritten tiles are GEGL's shared zero tile (`gegl:gegl/buffer/gegl-tile-handler-empty.c:125-152`);
  - picks up the selection mask buffer and freezes layer-thumbnail updates (`gimp_viewable_preview_freeze`, `:505`).
- `gimp_paint_core_paint` (`:311`) calls `pre_paint`, then `paint` for each symmetry origin, then `post_paint`, for each `GimpPaintState` (INIT, MOTION, FINISH).
- `gimp_paint_core_interpolate` (`:854`) sets `cur_coords` and calls the class `interpolate` (the default paints once, `:270`).
- `gimp_paint_core_finish` (`:511`) opens an undo group `GIMP_UNDO_GROUP_PAINT`, pushes a `GimpPaintCoreUndo` (stroke endpoint for Shift-line), crops the undo snapshot to the stroke bbox `x1..x2, y1..y2` aligned to tiles (`GEGL_RECTANGLE_ALIGNMENT_SUPERSET`), copies it into a new buffer and calls `gimp_drawable_push_undo()` (`:603`). If the layer was auto-expanded during the stroke, it instead swaps whole buffers via `gimp_drawable_set_buffer_full`.
- `gimp_paint_core_cancel` (`:696`) copies the snapshot back over the bbox.
- `gimp_paint_core_expand_drawable` (`:890`) handles the "Expand layers" option: it resizes the layer (and its mask), and resizes `canvas_buffer` and the undo snapshot to match.

### 2.6 Brush core: spacing, interpolation, jitter (CONFIRMED)
`gimp_brush_core_interpolate()` (`gimp:app/paint/gimpbrushcore.c:448`)
- The motion vector between last and current coords is projected onto the brush's **x/y axis vectors** (half-width vectors, `gimpbrush.c:177`, `gimpbrushgenerated.c:625`), scaled by `core->scale` and rotated by `core->angle`. `dist = 0.5·sqrt(xd²+yd²)` (`:570`) is therefore the **distance in brush diameters**. Spacing (`brush-spacing`, default 0.1, range 0.01-50, `gimppaintoptions.c:47,236`) is a fraction of the diameter. Distance carries over between events through `paint_core->distance`.
- **Dynamic spacing** (`:526-551`): `spacing + (2 - spacing)·(1 - dyn)`, so the dynamics can only make spacing *larger*, up to 200%.
- A "stripe" special case (`:601-722`) for small brushes avoids gaps and double plots at 1 px.
- Per dab (`:725-790`): x,y use the parameter t. Pressure, tilt, wheel and velocity are **linearly interpolated** with p = n/num_points. Direction is constant over the segment. Fade is recomputed from `pixel_dist`. **Jitter** (`:746-776`) offsets the dab by a random distance in [0, jitter·dyn(JITTER)] at a random angle from a 360-entry LUT, scaled by the brush size. Each dab then calls `gimp_paint_core_paint(MOTION)`.
- `pre_paint` (`:251`) skips zero-motion dabs, skips dabs whose dynamic scale is below 1e-7, selects the pipe brush (`gimp_brush_select_brush`) and flushes the blur caches when hardness changes on non-generated brushes.

### 2.7 Dynamics and the pressure → pixels chain (CONFIRMED)
- `GimpDynamics` (data file `.gdyn`) holds 11 `GimpDynamicsOutput`s: OPACITY, SIZE, ANGLE, COLOR, HARDNESS, FORCE, ASPECT_RATIO, SPACING, RATE, FLOW, JITTER (`gimp:app/core/core-enums.h:277-287`). Each output has 8 input flags and 8 `GimpCurve`s: pressure, velocity, direction, tilt, wheel, rotation, random, fade (`gimpdynamicsoutput.c:76-98`).
- `get_linear_value` (`:491`) returns `Σ curve_i(input_i) / n_enabled`, i.e. an **average**. Velocity enters as `1 - v`. Tilt enters as `1 - |tilt|`. Random is `g_random_double`. Fade is the position along the fade length (`gimp_paint_options_get_fade`, `gimppaintoptions.c:970`).
- `get_angular_value` (`:572`) converts tilt to an angle and handles lock-to-view for direction. `get_aspect_value` is at `:693`.
- The default dynamics is **"Pressure Size"** (`gimpcoreconfig.c:45`), whose only enabled mapping is pressure→size (`data/dynamics/Basic/Pressure-Size.gdyn`). The default brush is "2. Hardness 050" (`:44`). The brush files live in the `gimp-data` submodule, which is not cloned.

**Full chain for pressure:**
`GdkEvent axis` → device pressure curve (`gimpdeviceinfo.c:1405`) → GimpMotionBuffer (unchanged, apart from Catmull fill interpolation) → smoothing (unchanged) → per-dab lerp (`gimpbrushcore.c:732`) → `_gimp_paintbrush_motion()` (`gimppaintbrush.c:219`):
- `opacity *= dyn(OPACITY)` (`:251`). Airbrush first multiplies in `flow·dyn(FLOW)`.
- `gimp_brush_core_eval_transform_dynamics` (`gimpbrushcore.c:1186`): `scale = brush_size / max(brush_w,h)` (`:1198`), then `scale *= dyn(SIZE)` (`:1242`), `angle += dyn(ANGLE)`, `hardness *= dyn(HARDNESS)`, `aspect *= dyn(ASPECT)`.
- `force = dyn(FORCE)` if enabled, else `brush-force` (`:365`).
- `gimp_brush_core_paste_canvas(…, opacity, context_opacity, mode, brush_mode, force, appl_mode)` (`:373`).

`brush_mode` is HARD if "hard edge" is on. It is PRESSURE if force dynamics are enabled or force ≠ 0.5, and SOFT otherwise (`gimppaintoptions.c:1087`). PRESSURE mode remaps the mask with `I' = min(2·force·I, 1)` (`gimpbrushcore-loops.cc:309-340`, using a cached 256-entry LUT for u8).

### 2.8 Brush tip representation and caching (CONFIRMED)
- `GimpBrush` (`gimp:app/core/gimpbrush.c`) has a `mask` (GimpTempBuf "Y u8" for loaded brushes, "Y float" for generated ones), an optional `pixmap` ("R'G'B' u8"), a spacing, and x/y axes.
- Loaders (`gimp-data-factories.c:65-110`): `.gbr`, `.gpb` (pixmap), **`.abr` (Photoshop)**, `.jbr` (Paint Shop Pro), `.vbr` (generated), `.gih` (pipe).
- **Generated brushes** (`gimpbrushgenerated.c`): shape CIRCLE/SQUARE/DIAMOND, radius, spikes, hardness, aspect, angle. They are re-rendered analytically at the target size (`transform_mask`, `:346`) using a radial LUT with 4× oversampling (`OVERSAMPLING 4`, `:39`; `calc_lut`, `:411`). The falloff is a "gauss-like" profile with exponent `0.4/(1-hardness)`. So size and hardness changes are exact and do not resample.
- **Bitmap brushes** are bilinearly resampled through a 3×3 matrix (`gimpbrush-transform.cc:121`). Hardness < 1 is implemented as a **blur** of the transformed mask (`gimp_brush_transform_blur`, `:815`), with radius `(1-hardness)·(√0.5-0.5)·min(w,h)` (`:1020`).
- **Caches:**
  - `gimp_brush_transform_mask()` (`gimpbrush.c:651`) uses a `GimpBrushCache`: an MRU list of at most **20 entries** (`gimpbrushcache.c:36`) keyed by **exact equality** of width, height, scale, aspect, angle, reflect and hardness (`:205-240`). INFERRED: with continuous pressure→size, almost every dab has a different double-valued scale, so the cache misses and the mask is re-rendered.
  - The brush core keeps a subsample cache of 5×5 subpixel-shifted variants (`BRUSH_CORE_SUBSAMPLE 4`, `gimpbrushcore.h:23`). Each variant is produced by convolving with a 3×3 kernel table (`gimpbrushcore-kernels.h`; `gimp_brush_core_subsample_mask`, `gimpbrushcore-loops.cc:219`). It is invalidated whenever the transformed mask changes.
  - Hard mode uses `solidify` (any mask value > 0 becomes 1, with 2×2 half-pixel variants; `:572`). The pencil uses this by default (`PENCIL_DEFAULT_HARD TRUE`, `gimppenciloptions.c:31`).
- **Brush pipes** (`.gih`, `gimpbrushpipe.c:254`) choose a sub-brush per dab by incremental, angular, velocity, random, pressure, tilt-x, tilt-y or constant rank.
- **Brush outline:** `gimp_brush_transform_boundary` (`gimpbrush.c:781`) traces the boundary of the transformed mask (`gimpbrush-boundary.c`, `gimp_boundary_find`), caches it, and the draw tool draws it with cairo. During a stroke it is refreshed in `paint_flush` (`gimpbrushtool.c:270-300`).

### 2.9 Applying a dab to the drawable (CONFIRMED)
`gimp_paint_core_paste()` (`gimppaintcore.c:1265`). The default path is `use-applicator = FALSE` (`gimppaintoptions.c:203-209`). The GUI toggle for it is compiled out with `if (FALSE && …)` (`gimppaintoptions-gui.c:136-145`), so **GimpApplicator, a GEGL-graph based path, is debug-only for painting**.
- The **paint buffer** is a dab-bbox GimpTempBuf in the layer-mode working format: "RGBA float" or "R'G'B'A float" with the image's colour space (`gimp-layer-modes.c:~1530-1545`). It is filled with the FG colour, a gradient colour or a pixmap. The fill is skipped if the colour is unchanged and the buffer is reused (`gimppaintbrush.c:328-360`).
- **CONSTANT** ("wash", the default, `DEFAULT_APPLICATION_MODE`, `gimppaintoptions.c:59`):
  - `COMBINE_PAINT_MASK_TO_CANVAS_BUFFER`: for each pixel, `if (op > c) c += (op - c)·mask·op` (`gimppaintcore-loops.cc:1316-1320`), where `op` is the per-dab opacity. The per-stroke mask `c` approaches the dab opacity but never exceeds it. The airbrush uses "stipple" instead: `c += (1-c)·mask·op` (`:1308-1312`), which builds up toward 1.
  - `CANVAS_BUFFER_TO_COMP_MASK`.
  - `DO_LAYER_BLEND` with **src = undo snapshot** (`gimppaintcore.c:1405`). Every dab recomputes `original ⊕ paint·c` with the context opacity and paint mode, so overlapping dabs inside one stroke do not darken.
- **INCREMENTAL** ("Incremental: every stamp has its own opacity", `gimppaintoptions.c:295-301`): `PAINT_MASK_TO_COMP_MASK` with src = current pixels (`:1420`), so overlapping dabs accumulate. Gradient-colour and pixmap brushes force INCREMENTAL (`gimppaintbrush.c:200,209`).
- **Fusion and parallelism:** `gimp_paint_core_loops_process()` (`gimppaintcore-loops.cc:2175`) uses C++ templates to build a specialised fused routine for each combination of algorithm bits and mask format (docs at `:46-110`). It runs `gegl_parallel_distribute_area(roi, 64×64 px per thread)` (`:42, :2205`), and each worker iterates over GEGL buffer iterators. The selection mask and `MASK_COMPONENTS` (channel locks) are handled in the same pass.
- The blend step calls the layer-mode operation's `function` directly (`GimpOperationLayerMode`, `:1871-2010`), so painting supports **every layer mode** as a paint mode.
- Finally the undo bbox is extended and `gimp_drawable_update()` is called (`gimppaintcore.c:1450`).
- `gimp_paint_core_replace()` (`:1465`) is the "replace" variant, where transparent paint makes pixels transparent. It is used by smudge, convolve, dodge/burn and heal.

### 2.10 Projection (layer compositing) and canvas redraw (CONFIRMED)
- The layer-container "update" signal is connected to `gimp_image_invalidate()` (`gimpimage.c:848`, `:3909`), which calls `gimp_projectable_invalidate()`. That leads to `gimp_projection_add_update_area()`, which aligns the area to 32×32 chunks and unions it into a cairo region (`gimpprojection.c:51-52, 626`).
- `gimp_projection_flush_now(proj, direct)` (`:525`) runs synchronously. For each rect, `paint_area(now=TRUE)` calls `gimp_tile_handler_validate_validate()`, which is `gegl_node_blit(image graph, scale 1.0, rect)` (`gimptilehandlervalidate.c:227-246`). It then emits projection "update(now)" (`:867`).
- `gimp_projection_flush()` (`:503`) is the asynchronous path. It runs idle chunked rendering through a `GimpChunkIterator`, which adapts chunk size to roughly a **1/15 s** budget (`gimpchunkiterator.c:42`) and handles a priority rect (the visible area).
- The projection buffer is a GeglBuffer with a **validate tile handler**: tiles are composited lazily on access, and only level z=0 is validated (`:419`). Mipmap levels come from GEGL's zoom handler. INFERRED: zoomed-out views still need the full-resolution composite of dirty tiles. Group layers own their own projection (`gimpgrouplayer.c:396`). I found no other per-stack composite caches (grep found `gegl:cache` only in gimpapplicator.c).
- The layer modes are `GeglOperationPointComposer3` subclasses (`gimpoperationlayermode.c:128-129`). GEGL parallelises them (`gegl:gegl/operation/gegl-operation-point-composer3.c:326`).
- Display: `gimp_display_update_area(now=TRUE)` (`gimpdisplay.c:749`) calls `gimp_display_paint_area` (`:844`), which calls `gimp_display_shell_expose_area` (`gtk_widget_queue_draw_area`, `gimpdisplayshell-expose.c:38`) and `gimp_display_shell_render_invalidate_area`.
- In the GTK draw handler, `gimp_display_shell_draw_image()` (`gimpdisplayshell-draw.c:145`) splits the exposed area into render-buffer-sized chunks. For chunks missing from the `render_cache_valid` region it calls `gimp_display_shell_render()` (`gimpdisplayshell-render.c:147`), which does `gegl_buffer_get(projection, scale, "cairo-ARGB32", filter)` (`:455`). It uses NEAREST unless zoom-quality is HIGH, the default (`:205-208`). Colour-management transforms and display filters go through a float filter buffer. The result is painted into the `render_cache` cairo surface and then to the window. **All CPU.**

### 2.11 Undo (CONFIRMED)
- Each stroke produces one `GIMP_UNDO_GROUP_PAINT` containing a `GimpPaintCoreUndo` and a `GimpDrawableUndo`. The drawable undo holds a GeglBuffer covering the bbox, cropped to tiles. `gimp_drawable_undo_pop` (`gimpdrawableundo.c:185`) **swaps** pixels, so the same object then serves as the redo record.
- History limits are at least 5 levels (`undo-levels`) and an `undo-size` of 1/8 of physical RAM (`gimpcoreconfig.c:572-592`).
- INFERRED: undo buffers are built by tile-COW copies, so tiles in the bbox that the stroke did not touch share memory with the live layer.

---

## 3. Class hierarchies (CONFIRMED from G_DEFINE_TYPE)

**Paint cores** (`app/paint`):
```
GimpObject
└─ GimpPaintCore                      (start/paint/interpolate/finish; canvas/undo buffers; paste/replace)
   ├─ GimpBrushCore                   (brush, dynamics, spacing/interp, mask transform+caches, jitter)
   │  ├─ GimpPaintbrush               (colour/gradient/pixmap fill; _gimp_paintbrush_motion)
   │  │  ├─ GimpPencil                (empty class; options default hard=TRUE)
   │  │  ├─ GimpAirbrush              (rate timer, flow, stipple accumulation)
   │  │  └─ GimpEraser                (BG colour / ERASE / ANTI_ERASE mode)
   │  ├─ GimpSmudge                   (accumulator buffers, rate/flow, replace INCREMENTAL)
   │  ├─ GimpConvolve                 (3×3 blur/sharpen matrix, replace INCREMENTAL)
   │  ├─ GimpDodgeBurn                (exposure curve, src=orig in CONSTANT, replace)
   │  └─ GimpSourceCore               (source offset/alignment, sample-merged via saved_proj)
   │     ├─ GimpClone
   │     │  └─ GimpPerspectiveClone
   │     └─ GimpHeal                  (Laplace solve, Gauss-Seidel, SSE)
   ├─ GimpInk                         (blob/ellipse geometry, convex hull, 8× subsample, writes canvas_buffer)
   └─ GimpMybrushCore                 (libmypaint brush + GimpMybrushSurface)
GimpUndo → GimpPaintCoreUndo → GimpInkUndo
GimpToolOptions → GimpPaintOptions → {Pencil,Airbrush,Eraser,Smudge,Convolve,DodgeBurn,Ink,Mybrush}Options,
                                     GimpSourceOptions → {Clone, PerspectiveClone}Options
```
**Tools** (`app/tools`):
```
GimpTool → GimpDrawTool → GimpColorTool → GimpPaintTool    (paint thread, timer, smoothing, outline, Shift-line)
   ├─ GimpBrushTool                 (brush outline boundary)
   │  ├─ GimpPaintbrushTool
   │  │  ├─ GimpPencilTool
   │  │  └─ GimpAirbrushTool       (connects "stamp" signal → paint_push)
   │  ├─ GimpEraserTool, GimpSmudgeTool, GimpConvolveTool, GimpDodgeBurnTool
   │  ├─ GimpSourceTool → GimpCloneTool, GimpHealTool
   │  └─ GimpPerspectiveCloneTool  (directly under BrushTool)
   ├─ GimpInkTool
   └─ GimpMybrushTool
```
Shared code across all paint tools: the thread and queue, smoothing, Shift-line, symmetry (`GimpSymmetry`, with paint called once per symmetry stroke), "expand layers", selection mask, channel locks, undo, and alpha/lock checks.

### Tool-specific notes (CONFIRMED unless marked)
- **Eraser** (`gimperaser.c:107`): the paint colour is the BG colour. The mode is `ANTI_ERASE` if anti-erase is on, `ERASE` if the layer has alpha, and `NORMAL_LEGACY` otherwise (which paints BG). "Hard edge" is the generic `hard` option, which solidifies the mask. There is no separate eraser engine: the eraser is a paint mode applied to the same dab pipeline.
- **Airbrush** (`gimpairbrush.c`): each motion re-arms a `g_timeout` (on the main loop) of `(1000/60)/((rate/100)·dyn(RATE))` ms (`:185`). When it fires it emits "stamp", and the tool pushes `gimp_airbrush_stamp` onto the paint queue (`gimpairbrushtool.c:100-123`). Opacity is `flow·dyn(FLOW)`. The canvas combine uses stipple accumulation.
- **Ink** (`gimpink.c`): does not use brushes or dynamics. The size is `size·(1 + sens·(2p-1))` adjusted for velocity and tilt (`ink_pen_ellipse`, `:494`). The blob is convex-hulled with the previous blob and scanline-rendered at 8× subsampling directly into `canvas_buffer`, then pasted with a NULL mask.
- **MyPaint** (`gimpmybrushcore.c:260-400`): calls `mypaint_brush_stroke_to_2(x, y, pressure, xtilt, ytilt, dt, zoom, rotation, wheel)` once per GIMP motion. libmypaint does its own dab spacing and dynamics. GIMP implements `draw_dab_2` and `get_color_2` directly on the drawable GeglBuffer (`gimpmybrushsurface.c:235, 356`), with lock_alpha, colorize, posterize, selection mask and component mask. The dirty rect from `end_atomic` drives `gimp_drawable_update`. This is effectively a **second, independent paint engine** that shares only the tool/thread/undo plumbing.
- **Heal** runs an iterative Laplace solver for each dab (`gimpheal.c:276-420`), which is expensive (INFERRED).

---

## 4. Options, presets and resources (CONFIRMED)
- **GimpPaintOptions properties** (shown by `gimppaintoptions-gui.c`):
  - mode, opacity;
  - brush, size, aspect-ratio, angle, spacing, hardness, force, with "link" toggles (reset to the brush's defaults);
  - lock-to-view, dynamics (enable, fade length/unit/reverse/repeat, colour from gradient and its options);
  - application-mode (Incremental), hard, use-jitter and jitter-amount;
  - use-smoothing, smoothing-quality, smoothing-factor;
  - expand-use, expand-amount, expand-fill-type, expand-mask-fill-type.
  - Per tool: airbrush rate/flow/motion-only, eraser anti-erase, smudge rate/flow/no-erasing/sample-merged, ink geometry, MyPaint radius/opaque/hardness/eraser/no-erasing.
- **Persistence:**
  - Per-tool options are saved to the `tool-options/` directory in the user config (`gimptooloptions.c:374`).
  - **Tool presets** (`.gtp`, `gimptoolpreset-load.h:21`) are S-expression serialised `GimpToolOptions` plus `use-fg-bg/opacity-paint-mode/brush/dynamics/mypaint-brush/gradient/pattern/palette/font` flags (`gimptoolpreset.c:55-66`; example `data/tool-presets/Sketch/Ink-Smooth.gtp`).
  - Brushes, dynamics, gradients and so on are `GimpData` resources loaded by data factories from search paths (`gimp-data-factories.c`).

---

## 5. Threading, CPU and GPU, tiles (summary)

| Concern | What GIMP does | Evidence |
|---|---|---|
| UI thread | events, motion buffer, smoothing, 10 ms flush, projection composite of dirty rects, cairo render | tool-events.c, gimppainttool-paint.c:164 (CONFIRMED) |
| Paint thread | 1 global thread; dab placement, masks, per-pixel blend into private COW buffer | gimppainttool-paint.c:83-160 (CONFIRMED) |
| Data parallel | `gegl_parallel_distribute_area/range` inside mask ops and paint loops (64×64 px per thread); GEGL point ops in projection | gimpbrushcore-loops.cc:42, gimppaintcore-loops.cc:2205, gegl-parallel.c:124 (CONFIRMED) |
| Thread count | `num-processors` → GEGL `threads` (max 64) | gimpgeglconfig.c:117-127, gimp-gegl.c:66-71 (CONFIRMED) |
| GimpAsync / run_async | histograms, previews, line art, fonts: **not** painting | grep (CONFIRMED) |
| GPU | none in paint/display/projection/layer modes; OpenCL is a playground pref, default off, forced off at init | app.c:126-129, gimpgeglconfig.c:153, preferences-dialog.c:1724-1737 (CONFIRMED) |
| Tile size | GEGL default 128×128; GIMP does not override | gegl-config.c:220-231 (CONFIRMED) |
| Memory | tile cache = ½ physical RAM; swap file with "fast" compression | gimpgeglconfig.c:41,133-150 (CONFIRMED) |
| Dirty granularity | drawable update 32×32 chunks; copy region tile-aligned; projection 32×32 chunks; display render cache per screen rect | gimpdrawable.c:64, gimpprojection.c:51 (CONFIRMED) |
| Precision | drawables can be u8/u16/u32/half/float, linear or perceptual; paint math always RGBA float through babl | gimp-layer-modes.c (CONFIRMED) |

---

## 6. Practical conclusions for a simpler C#/.NET + WPF + SkiaSharp app

### 6.1 Ideas worth copying
1. **Separate dab generation from compositing.** Keep "where and how big is each dab" (pure math over input samples) apart from "what does a dab do to pixels". This is what made one dab pipeline serve brush, pencil, airbrush and eraser in GIMP.
2. **Use a per-stroke mask buffer ("canvas_buffer") composited over a pre-stroke snapshot (CONSTANT mode).** This gives the expected "one stroke at 50% opacity stays 50%" behaviour, and it gives undo and cancel for free. In SkiaSharp terms (INFERRED design): render dab alpha into an A8 or float stroke layer. With opacity 1, `SrcOver` reproduces GIMP's `c += (1-c)·m`. For a per-dab opacity cap, implement GIMP's `if (op>c) c += (op-c)·m·op` yourself on the dab ROI. Then composite `snapshot ⊕ colour·stroke·layerOpacity` for the dirty rect only. Offer INCREMENTAL as an option, or reserve it for airbrush-like tools.
3. **Measure spacing as a fraction of brush diameter** with carried-over distance, and **lerp pressure** between samples. Without this, fast strokes look beaded.
4. **Make dynamics small curve lookups.** One curve per (input → output), a 256-sample LUT, and a clear combining rule (GIMP averages; multiplication is also defensible but should be documented). Start with pressure→size and pressure→opacity only, plus velocity if wanted.
5. **Use a motion pre-filter.** Drop sub-pixel moves, smooth the timestamps and velocity, and compute direction from history.
6. **Use velocity-windowed Gaussian smoothing as an optional stabiliser.** It is about 30 lines. Add an end-of-stroke catch-up, which GIMP lacks.
7. **Paint on a worker thread and present on a timer.** One producer-consumer queue. The worker writes into a private buffer. The UI thread, roughly every frame (GIMP uses 10 ms), copies or "publishes" dirty tiles and invalidates only those screen regions. Bound or coalesce the queue, which GIMP does not do.
8. **Store layers as tiles with COW snapshots.** A tile grid (for example 128 or 256 px), immutable tile objects with reference sharing, sparse empty tiles and dirty-tile sets give cheap snapshots, cheap undo (keep the old tile references for tiles touched by the stroke), and redraw proportional to the touched area. GIMP gets this from GEGL. In .NET you would write a small `TileGrid` yourself.
9. **Track dirty rects at every stage** (drawable → composite → screen cache), aligned to chunk or tile boundaries.
10. **Cache masks with subpixel variants for small brushes.** Positioning dabs at subpixel accuracy prevents wobble on thin lines. For simple round brushes, rendering the mask analytically at the exact size (like GIMP's generated brushes) avoids both resampling and blur-for-hardness.
11. **Implement the eraser as a paint mode** ("destination-out" on alpha) through the same dab pipeline, plus a "hard edge" mask threshold.

### 6.2 Too complex for a first version
- Multiple pixel precisions and TRCs (u8/u16/u32/half/float × linear/perceptual) with babl conversion around every dab. Pick **one**: 8-bit premultiplied BGRA, which matches Skia and WPF, or float only if colour accuracy is a goal.
- More than 30 layer modes as paint modes, the template-metaprogrammed fused loops, the debug-only GEGL applicator path, and channel component masks.
- Brush pipes (.gih), ABR/JBR import, pixmap brushes, blur-based hardness for bitmap brushes, and boundary-traced brush outlines (a circle or ellipse outline is enough).
- 11 dynamics outputs × 8 inputs, fade/gradient colour, lock-to-view, symmetry painting, auto-expanding layers.
- **The libmypaint second engine**, heal (Laplace solver), perspective clone, the ink blob engine, the smudge accumulator. These can come later.
- A deep GObject class hierarchy (GimpPaintCore → BrushCore → Paintbrush → Airbrush, mirrored by a tool hierarchy). A flat `BrushEngine` with strategy objects (MaskSource, ColorSource, BlendOp, DynamicsMap) is simpler in C#.
- Per-device tool memory through tool presets. A simpler rule is "pen eraser end → eraser tool".

### 6.3 Likely bottlenecks for a lightweight editor (all INFERRED unless noted)
- **Per-dab overhead with small spacing.** Each dab does a dynamics evaluation, a mask cache lookup, a subsample-variant lookup, buffer setup and a full blend pass. GIMP's mask cache uses exact double keys and only 20 entries (CONFIRMED), so continuous pressure→size defeats it. Mitigation: quantise size and angle into cache buckets, or render round masks analytically per dab with SIMD.
- **Blending through float.** Converting u8 → float → u8 for every dab ROI costs time. Blending natively in 8-bit premultiplied, or keeping layers in float, avoids it.
- **The compositor re-runs the whole layer stack for dirty rects.** GIMP validates dirty projection tiles by blitting the full image graph at scale 1.0 and has no cache of the layers below or above (only group layers have projections). For a simple app, cache "composite of layers below the active layer" and "above" while a stroke is in progress, so each update is three blends.
- **UI-thread stalls.** GIMP's flush timer takes the paint mutex and then composites synchronously on the UI thread, so one long paint job blocks it. Publish immutable tile snapshots instead of using a lock, and let the UI composite only what is visible at the current zoom (mipmaps).
- **COW churn.** Publishing tiles every 10 ms forces the next write to each shared tile to copy it (128×128×bpp). This is acceptable but argues for larger publish intervals (16 ms, tied to vsync) or for double-buffered dirty tiles.
- **Event latency.** Catmull "event fill" delays one event (CONFIRMED). Smoothing adds lag by design. GIMP does not predict ahead. For low latency, draw a provisional "wet" overlay of the newest segment directly on the UI surface.
- **Unbounded queue.** If dab rendering is slower than input, lag grows without limit. Coalesce consecutive motion jobs (merge segments) when the queue is long.

### 6.4 Suggested minimal architecture (INFERRED, for discussion)
`Input (WPF stylus / WM_POINTER)` → `MotionFilter` (drop jitter, velocity, direction) → optional `Smoother` → `StrokeSampler` (spacing = k·diameter, lerp pressure) → `Dab {x,y,size,opacity,hardness,angle}` → worker: `MaskCache/AnalyticRoundMask` → `StrokeMask(tile-sparse A8 or float)` → `Blend(snapshotTiles, strokeMask, color, mode∈{Normal, Erase})` → `LayerTiles (COW)` → dirty tiles → UI: composite(below-cache, active, above-cache) for dirty ∩ visible → `SKSurface` / `WriteableBitmap` region update. Undo = list of (layer, old tile refs). Presets = JSON of brush parameters plus dynamics curves.

---

## 7. Unknowns and limits
- I did not read the libmypaint internals (not cloned), so its dab spacing and dynamics model is not covered here.
- I did not measure latency or throughput. All performance statements are inferences from the code structure.
- How the OS and GTK (Windows Ink/WinTab) map the pen eraser and the event-history backends is the other agent's scope. I only noted that the win32 virtual device returns no history (`gtk3:gdk/win32/gdkdevice-virtual.c:66-75`).
- Exact undo memory accounting with COW-shared tiles (whether GIMP's undo size counts shared tiles) was not verified.
- The default brush files (gimp-data submodule) are not in the clone, so I could not inspect their shape and hardness.
- I confirmed the GEGL zoom-handler mipmap behaviour only through the `z == 0` validate condition. I did not trace mipmap invalidation in GEGL in detail.
