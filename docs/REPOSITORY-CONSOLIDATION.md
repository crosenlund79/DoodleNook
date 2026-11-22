# Repository consolidation — a recommendation

Written in Mission 016 (22 November 2026). **This is a recommendation only.** Nothing described here has been done:
no branch was created, moved, merged, reset or deleted, and the GitHub default branch was not changed. Each step below
needs the owner's explicit approval first.

## How the repository looks today

Checked from a fresh clone on 22 November 2026:

| What | State |
|---|---|
| `main` | Doesn't exist. |
| GitHub default branch | `claude/beautiful-mccarthy-3c2hjm` at `126adb4` (21 Nov). That's the Expansion Pack 1 work, which is older than Missions 015 and 016. Anyone opening the repository on GitHub sees that older state first. |
| Mission branches | `design/003-workspace-playground`, then `iteration/004-the-missing-ink` through `iteration/016-completion-pass`, one per mission. |
| History | 70 commits, 19–22 Nov 2026, all linear. Every remote branch is an ancestor of `iteration/016-completion-pass`; nothing exists only on a side branch. |
| Releases and tags | None. Builds are the `DoodleNook-win-x64` artifacts of the Windows CI runs, kept for GitHub's retention period. |
| CI | `.github/workflows/windows-build.yml` runs on pushes to the listed mission branches, and manually. |

Because the history is linear, consolidating needs **no merges and no history changes**. A branch can simply point at
the final Mission 016 commit, as a fast-forward of everything before it.

## Recommendation

1. **Create `main` at the final Mission 016 commit**, once Mission 016's playtest is acceptable to the owner. It is a
   fast-forward of every existing branch, so no history is rewritten and nothing is lost.
2. **Make `main` the GitHub default branch.** Visitors then see the current DoodleNook, not the Expansion Pack 1 state.
3. **Add `main` to the CI workflow's branch list**, so the artifact is always built from it. Keep `workflow_dispatch`.
4. **Keep every mission branch as it is.** They are the record of what each mission delivered, and every mission
   README links to its own branch and CI runs. If the branch list ever gets in the way, *tags* such as `mission-015` on
   each final commit would keep the record. Tags would replace branch deletion; branches would only be deleted after
   tagging and with approval.
5. **Keep the mission workflow from here on:** a mission branch starts from `main`'s tip. When the owner approves the
   mission, `main` is fast-forwarded to its final commit. Only the owner's approval moves `main`.
6. **No release, no installer, no public build** as part of any of this. The CI artifact stays the way to get the
   program.

## What the owner would need to decide

- Whether to create `main` at all, and at which commit (recommended: Mission 016's final commit, after its playtest).
- Whether to switch the GitHub default branch to `main` (Settings → Branches; only the owner can do this).
- Whether mission branches stay as they are (recommended) or are later replaced by tags.

## Risks and how they are avoided

| Risk | Avoided by |
|---|---|
| Losing work on a side branch | None exists: every branch is contained in `iteration/016-completion-pass` (checked with `git merge-base --is-ancestor` for each). |
| Rewriting history | Only fast-forwards; no rebase, squash or force-push. |
| CI stops building | `main` is added to the workflow's branch list in the same change that creates it. |
| A visitor sees an old state | The default branch is switched to `main`. |
