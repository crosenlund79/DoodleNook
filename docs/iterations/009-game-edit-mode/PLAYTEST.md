# Mission 009 playtest: Game Edit Mode

About 20 minutes. **Use a copy of a game's graphics folder the first time**, so nothing in your real game changes
while you try it. Note anything fiddly, slow or surprising.

1. Download **DoodleNook-win-x64** from the newest green run on [this branch](https://github.com/crosenlund79/DoodleNook/actions/workflows/windows-build.yml?query=branch%3Aiteration%2F009-game-edit-mode), extract it and start `DoodleNook.exe`. The title says **DoodleNook — M009**.
2. **File › Game Edit Mode…** and choose the copied graphics folder. Keep *Back up original PNG files* ticked; *Start*.
3. Look at the catalog: thumbnails, folders, the counts and *Completion 0%*. Try the filters and *Find a file*.
4. Click an image. Change something, press **Ctrl+S**. Check in Explorer:
   - the game's PNG has changed (open it in your game, or any viewer);
   - `_DoodleNook\<same folders>\<name>.dnk` exists;
   - `_DoodleNook\Originals\<same folders>\<name>.png` is the old picture.
5. Back to the catalog (the chip's *Catalog*): the image is **In progress**. Save it again: still In progress, and the
   original backup is unchanged.
6. Add a layer to that image, save, open another image, then come back: your layers are still there.
7. **Mark complete** (chip or tile): the progress bar moves. Save again: still Complete. *Mark in progress* reopens it.
8. Close DoodleNook, start it again, *File › Game Edit Mode…*: same folder, same statuses.
9. Optional: change a PNG with another program, then save it in DoodleNook: it asks before replacing.
10. A `template_` image (e.g. `template_player.png`): edit it, Ctrl+S, choose **Yes — Create replacement**. `player.png`
    appears next to it, `template_player.png` is unchanged, and the next Ctrl+S saves `player.png` without asking. If
    `player.png` already existed, you're asked before it's replaced.

## Questions

1. Is choose folder → browse → edit → save → mark complete → next image comfortable?
2. Is the catalog easy to read? Is anything missing or unnecessary on a tile?
3. Is the status chip at the top right useful while editing, or in the way?
4. Did saving behave exactly as you expected for your game?
5. What would make replacing a whole set of graphics quicker?
6. Is the `template_` question asked at the right moment, and is "Replaced by …" in the catalog clear?
