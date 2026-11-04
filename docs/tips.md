# Tips

- **Which case am I looking at?** Run `SELECT * FROM _mirror_info;`. It shows the level and when the copy was last updated.
- **New table not showing up?** Refresh your editor's table list. Editors remember the table list, but the data itself is already up to date.
- **What are `result_1`, `result_2`…?** Those are your unsaved in-game searches.
- **Scratch tables.** See [Creating tables in your editor](editor-tables.md#scratch-tables).
- **Pasting queries back into the game.** Your editor allows any SQLite, but the in-game search box blocks some words: `DROP`, `INSERT`, `UPDATE`, `DELETE`, `REPLACE`, `TABLE` and `sqlite_master`.
- **Seeing Level 0 on the main menu?** That's normal. The copy switches to your case once it loads.
- **Workshop (custom) cases.** Custom cases are currently in the game's `workshop` beta branch on Steam. DD Connect mirrors the case you have open, including a custom case from the Workshop, and still works on the regular build. In `_mirror_info` it shows as level `-1`, and `ddsql` calls it "custom case". The game keeps a custom case's tables in the case's own database in the workshop folder, so tables you save or send from your editor are written there too. Downloading the workshop item again may reset them.
