# DD Connect

Solve **Database Detective: Minor Crimes Division** cases from your favorite SQL editor.

DD Connect is a small mod that keeps a copy of the case you're playing in one file. You open it once in VS Code (or any SQLite editor) and it stays up to date as you play: new searches, saved tables, website downloads and level changes all show up automatically.

Your save is never touched. The copy is read-only, so nothing you do in your editor can break the game.

> **Unofficial fan mod.** DD Connect is not affiliated with, endorsed by or supported by the makers of Database Detective. Please don't send them bug reports about it. See the [disclaimer](#disclaimer).

---

## Install

DD Connect works on Windows, Linux and macOS. It's the same download for all three, but installing BepInEx is a little different on each.

Linux and macOS support is new and less tested than Windows. If something doesn't work, please [open an issue](../../issues/new/choose).

First, open the game folder: in Steam, right-click **Database Detective** → **Manage** → **Browse local files**.

### 1. Install BepInEx (one time)

BepInEx is the mod loader DD Connect runs on. Get it from the [BepInEx releases page](https://github.com/BepInEx/BepInEx/releases) (version 5.4.23 or newer 5.x).

<details open>
<summary><b>Windows</b></summary>

1. Download **`BepInEx_win_x64_5.4.23.x.zip`**.
2. Extract it into the game folder, so `winhttp.dll` and the `BepInEx` folder sit next to `copOS.exe`.
3. Start the game once, then quit. This lets BepInEx finish setting up.

</details>

<details>
<summary><b>Linux</b></summary>

1. Download **`BepInEx_linux_x64_5.4.23.x.zip`**.
2. Extract it into the game folder, so `run_bepinex.sh` and the `BepInEx` folder sit next to the game's executable.
3. Make the script executable. In a terminal in the game folder, run:
   ```
   chmod +x run_bepinex.sh
   ```
4. In Steam, right-click the game → **Properties** → **General**, and set **Launch Options** to:
   ```
   ./run_bepinex.sh %command%
   ```
5. Start the game once, then quit.

*Running the Windows version through Proton instead?* Follow the **Windows** steps, then set Launch Options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`.

</details>

<details>
<summary><b>macOS</b></summary>

1. Download **`BepInEx_macos_universal_5.4.23.x.zip`**.
2. Extract it into the game folder, so `run_bepinex.sh` and the `BepInEx` folder sit next to the game's `.app`.
3. Make the script executable. In Terminal, in the game folder, run:
   ```
   chmod +x run_bepinex.sh
   ```
4. In Steam, right-click the game → **Properties** → **General**, and set **Launch Options** to:
   ```
   ./run_bepinex.sh %command%
   ```
5. Start the game once, then quit.

</details>

### 2. Install DD Connect

1. Download **`DDConnect-x.y.z.zip`** from the [Releases](../../releases) page.
2. Extract it into the same game folder, merging it with the existing `BepInEx` folder.

### 3. Check it works

Start the game and load a case. A file called **`current.db`** appears in the game's data folder. That's the file your editor opens.

| System | Where `current.db` is |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\HsuCorp\copOS\Connect\current.db` |
| Linux | `~/.config/unity3d/HsuCorp/copOS/Connect/current.db` |
| macOS | `~/Library/Application Support/HsuCorp/copOS/Connect/current.db` |

On Windows, paste the folder part into the File Explorer address bar. If you can't find it, look in `BepInEx/LogOutput.log` in the game folder. The line `Mirroring the current level to ...` shows the exact path on your computer.

---

## Connect your editor

Point your editor at `current.db`, using the full path from the table above. Your editor may need the full path written out, for example:

- **Windows:** `C:\Users\YOUR-NAME\AppData\LocalLow\HsuCorp\copOS\Connect\current.db`
- **Linux:** `/home/YOUR-NAME/.config/unity3d/HsuCorp/copOS/Connect/current.db`
- **macOS:** `/Users/YOUR-NAME/Library/Application Support/HsuCorp/copOS/Connect/current.db`

### VS Code with SQLTools (recommended)

SQLTools gives you table and column autocomplete, a results grid and query history.

1. Install the **SQLTools** and **SQLTools SQLite** extensions.
2. Open the SQLTools sidebar and choose **Add New Connection** → **SQLite**.
3. Name it `Database Detective`, set **Database file** to the path above, and save.
4. Connect. If SQLTools offers to install the SQLite driver's dependencies, accept.
5. Open a new `.sql` file, write a query, and run it with **Ctrl+E Ctrl+E** (**Cmd+E Cmd+E** on macOS).

<details>
<summary>Prefer settings.json?</summary>

```json
"sqltools.connections": [
  {
    "name": "Database Detective",
    "driver": "SQLite",
    "database": "C:/Users/YOUR-NAME/AppData/LocalLow/HsuCorp/copOS/Connect/current.db" // use your system's path
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
| **Excel**, **Power BI** (ODBC, Windows) | Install the free [SQLite ODBC driver](http://www.ch-werner.de/sqliteodbc/) (`sqliteodbc_w64.exe`). Then add a data source in **ODBC Data Sources (64-bit)** that points at `current.db`. |

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

Optional. Edit `BepInEx/config/dev.ddconnect.cfg` in the game folder, which is created on first run.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Turn DD Connect on or off. |
| `MirrorPath` | *(empty)* | Where to put the copy. Empty means the `Connect/current.db` location above. |
| `DebounceMs` | `300` | How long to wait after the game's last change before updating the copy. |

---

## Troubleshooting

**There's no `current.db`.**
Open `BepInEx/LogOutput.log` in the game folder and look for lines starting with `DD Connect`.
- No `DD Connect` lines at all: the mod didn't load. Check that `BepInEx/plugins/DDConnect/DDConnect.dll` exists.
- No log file at all: BepInEx isn't running.
  - **Windows:** check that `winhttp.dll` is next to `copOS.exe`.
  - **Linux and macOS:** check that the Steam Launch Options are set to `./run_bepinex.sh %command%`, and that you ran `chmod +x run_bepinex.sh`.
  - **macOS:** if macOS blocks the downloaded files, run `xattr -dr com.apple.quarantine .` in Terminal, in the game folder.
- A `DD Connect` error line: please open an issue and include the log.

**My editor says the database is read-only.**
That's intended. The copy can be read but not changed.

---

## Uninstall

Delete `BepInEx/plugins/DDConnect` from the game folder. You can also delete the `Connect` folder from the path in [Check it works](#3-check-it-works).

To remove BepInEx completely on Linux or macOS, also clear the Steam Launch Options.

---

## Coming later

Run a query from your editor *inside* the game, so it opens as an in-game result window or saved table.

---

## Disclaimer

DD Connect is an unofficial, fan-made mod. It is not affiliated with, endorsed by, sponsored by or supported by the developers or publishers of *Database Detective: Minor Crimes Division*. All game names, trademarks and content belong to their respective owners.

DD Connect contains no game files or game code. You need your own copy of the game to use it.

DD Connect is released under the [MIT License](LICENSE). The mod is provided as is, without warranty. Modding a game is always at your own risk, so back up your save if you're worried. If something goes wrong, report it in this repository's issues, not to the game's developers.
