# DoodleNook — instructions for every development session

DoodleNook is a cosy Windows drawing, painting, pixel-art and animation application (C# / .NET 10, Avalonia 12,
SkiaSharp). The user's mantra is **LESS IS MORE — BUT CAPABLE WHEN NEEDED. Ease of use first.**

**Before any significant UI or workspace change, read [`docs/design/INTERFACE-PHILOSOPHY.md`](docs/design/INTERFACE-PHILOSOPHY.md)
and run its UI decision checklist.** These rules are permanent, not a preference of one mission.

## Interface philosophy

1. **Ease of use first.** Common creative tasks should take as little effort as possible.
2. **Less is more.** Avoid unnecessary visible controls, panels, settings and decorations.
3. **Power without clutter.** Build useful capabilities without exposing every feature on the main screen.
4. **Daily-use tools stay at hand.** What artists use constantly is immediately available in the relevant workspace.
5. **Contextual functionality.** Show specialised controls when they are relevant, not permanently.
6. **The user customises.** Users choose which optional tools and panels stay visible, per workspace (*Customize workspace*).
7. **Excellent defaults.** A new user is comfortable without changing settings. No setup wizards.
8. **Canvas first.** Drawing space matters more than application controls.
9. **Keep the approved look.** Mission 003 (`docs/design/003-workspace-playground`) is the visual foundation. Don't redesign casually.
10. **Consistency.** Similar tools and interactions behave the same in every workspace.
11. **Discoverability.** Hidden features stay easy to find: clear menus, contextual controls, tooltips with shortcuts.
12. **Protect the drawing experience.** New features must not hurt responsiveness, drawing feel or reliability.
13. **Build real features.** Never ship an enabled button or panel whose advertised behaviour isn't implemented.
14. **The user's preferences win.** Playtest feedback overrides assumptions about ideal workflows.
15. **Not a clone.** Learn from Photoshop, Krita, Clip Studio and Aseprite, but keep DoodleNook's simpler personality.
16. **Every visible control must earn its space.** Before adding a permanent control, ask whether it is needed in everyday use.

## The Personal Tool Principle

"This is my tool." DoodleNook is a personal creative environment, built around its owner's actual interests,
preferences and workflows, not a product trying to satisfy every possible artist.

1. DoodleNook is built primarily for its owner's creative workflows.
2. The owner's actual experience and preferences outrank hypothetical user requirements.
3. Do not add features merely to achieve parity with competing applications.
4. Before proposing a new workspace, determine whether an existing workspace can support the functionality naturally.
5. Before introducing a new subsystem, determine whether existing technology can be reused.
6. Prefer small, focused tools over extensive general-purpose frameworks.
7. Keep advanced functionality accessible without making it permanently visible.
8. Do not implement speculative features without a clear creative purpose.
9. Protect existing interactions that the owner already enjoys.
10. A deliberately omitted feature can represent excellent product design.
11. The application should become easier and more enjoyable to use as it develops, not harder.
12. User playtests and explicit approval determine whether subjective design decisions are successful.

Before adding a significant capability, ask: **"Does this make something the owner actually enjoys doing easier, or
does it merely make DoodleNook bigger?"** Use the answer to decide scope.

## Engineering rules

- `src/DoodleNook.Engine` references only .NET (an architecture test enforces it). UI code lives in `src/DoodleNook.App`.
- The drawing engine is the most important part: don't rewrite it casually, and measure before claiming performance.
- Every document change is undoable. A failed save must never damage the previous file (write to a temp file, then replace).
- `.dnk` is versioned. Old files must keep opening; change the format only with an explicit version and migration.
- No telemetry, analytics, cloud services or AI features. Never ship fake UI.
- Run `dotnet test DoodleNook.slnx` before committing. Windows CI (`.github/workflows/windows-build.yml`) builds the
  portable `DoodleNook-win-x64` artifact; a mission is only delivered when the final commit's run is green.
- Real pen (Huion Kamvas 13) behaviour can only be confirmed by the user. Never mark it as verified.

## Repository conventions

- One branch per mission: `iteration/NNN-short-name`, started from the previous mission's final commit.
- Don't force-push, rewrite history, create or reset `main`, merge into a primary branch, or create public releases
  without the user's explicit approval. Don't commit build outputs or executables.
- Each mission is documented in `docs/iterations/NNN-…/` (README with what was built and verified, PLAYTEST.md).
- Research and design history: `docs/README.md`.
