# DD Connect

Solve **Database Detective: Minor Crimes Division** cases from your favorite SQL editor.

DD Connect is a small mod that keeps a copy of the case you're playing in one file. You open it once in VS Code (or any SQLite editor) and it stays up to date as you play: new searches, saved tables, website downloads and level changes all show up automatically.

Your save is never touched. The copy is read-only, so nothing you do in your editor can break the game.

> **Unofficial fan mod.** DD Connect is not affiliated with, endorsed by or supported by the makers of Database Detective. Please don't send them bug reports about it. See the [disclaimer](#disclaimer).

---

## Install

### 1. Install BepInEx (one time)

BepInEx is the mod loader DD Connect runs on.

1. Download **`BepInEx_win_x64_5.4.23.x.zip`** (any 5.4.23 or newer 5.x version) from the [BepInEx releases page](https://github.com/BepInEx/BepInEx/releases).
2. Open the game folder: in Steam, right-click **Database Detective** → **Manage** → **Browse local files**.
3. Extract the zip into that folder, so `winhttp.dll` and the `BepInEx` folder sit next to `copOS.exe`.
4. Start the game once, then quit. This lets BepInEx finish setting up.

### 2. Install DD Connect

1. Download **`DDConnect-x.y.z.zip`** from the [Releases](../../releases) page.
2. Extract it into the same game folder. When Windows asks, merge the `BepInEx` folders.

### 3. Check it works

Start the game and load a case. Then paste this into the File Explorer address bar:

```
%USERPROFILE%\AppData\LocalLow\HsuCorp\copOS\Connect
```

You should see a file called **`current.db`**. That's the file your editor opens.

---

## Connect your editor

Point your editor at this file. Replace `YOUR-NAME` with your Windows user name:

```
C:\Users\YOUR-NAME\AppData\LocalLow\HsuCorp\copOS\Connect\current.db
```

### VS Code with SQLTools (recommended)

SQLTools gives you table and column autocomplete, a results grid and query history.

1. Install the **SQLTools** and **SQLTools SQLite** extensions.
2. Open the SQLTools sidebar and choose **Add New Connection** → **SQLite**.
3. Name it `Database Detective`, set **Database file** to the path above, and save.
4. Connect. If SQLTools offers to install the SQLite driver's dependencies, accept.
5. Open a new `.sql` file, write a query, and run it with **Ctrl+E Ctrl+E**.

<details>
<summary>Prefer settings.json?</summary>

```json
"sqltools.connections": [
  {
    "name": "Database Detective",
    "driver": "SQLite",
    "database": "C:/Users/YOUR-NAME/AppData/LocalLow/HsuCorp/copOS/Connect/current.db"
  }
]
```

</details>

### Other editors

Any tool that opens SQLite files works:

| Editor | How |
|---|---|
| **SQLite3 Editor** (VS Code) | Open `current.db` in VS Code. Little setup needed. |
| **SQLite Viewer** (VS Code) | Open `current.db` to browse tables (viewing only). |
| **DB Browser for SQLite**, **DBeaver**, **DataGrip** | Open `current.db` as a SQLite database. |
| **Excel**, **Power BI** (ODBC) | Install the free [SQLite ODBC driver](http://www.ch-werner.de/sqliteodbc/) (`sqliteodbc_w64.exe`). Then add a data source in **ODBC Data Sources (64-bit)** that points at `current.db`. |

---

## Tips

- **Which case am I looking at?** Run `SELECT * FROM _mirror_info;`. It shows the level and when the copy was last updated.
- **New table not showing up?** Refresh your editor's table list. Editors remember the table list, but the data itself is already up to date.
- **What are `result_1`, `result_2`…?** Those are your unsaved in-game searches.
- **Keeping results between queries.** You can't save tables into the copy, but you can use `WITH name AS (...)` or `CREATE TEMP TABLE name AS ...`. Temp tables last until your editor disconnects.
- **Pasting queries back into the game.** Your editor allows any SQLite, but the in-game search box blocks some words: `DROP`, `INSERT`, `UPDATE`, `DELETE`, `REPLACE`, `TABLE` and `sqlite_master`.
- **Seeing Level 0 on the main menu?** That's normal. The copy switches to your case once it loads.

---

## Settings

Optional. Edit `BepInEx\config\dev.ddconnect.cfg` in the game folder, which is created on first run.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Turn DD Connect on or off. |
| `MirrorPath` | *(empty)* | Where to put the copy. Empty means the `Connect\current.db` location above. |
| `DebounceMs` | `300` | How long to wait after the game's last change before updating the copy. |

---

## Troubleshooting

**There's no `current.db`.**
Open `BepInEx\LogOutput.log` in the game folder and look for lines starting with `DD Connect`.
- No `DD Connect` lines at all: the mod didn't load. Check that `BepInEx\plugins\DDConnect\DDConnect.dll` exists.
- No log file at all: BepInEx isn't installed correctly. Check that `winhttp.dll` is next to `copOS.exe`.
- A `DD Connect` error line: please open an issue and include the log.

**My editor says the database is read-only.**
That's intended. The copy can be read but not changed.

---

## Uninstall

Delete `BepInEx\plugins\DDConnect` from the game folder. You can also delete the `Connect` folder from the path in [Check it works](#3-check-it-works).

---

## Coming later

Run a query from your editor *inside* the game, so it opens as an in-game result window or saved table.

---

## Disclaimer

DD Connect is an unofficial, fan-made mod. It is not affiliated with, endorsed by, sponsored by or supported by the developers or publishers of *Database Detective: Minor Crimes Division*. All game names, trademarks and content belong to their respective owners.

DD Connect contains no game files or game code. You need your own copy of the game to use it.

DD Connect is released under the [MIT License](LICENSE). The mod is provided as is, without warranty. Modding a game is always at your own risk, so back up your save if you're worried. If something goes wrong, report it in this repository's issues, not to the game's developers.
