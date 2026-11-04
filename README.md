# DD Connect

Solve **Database Detective: Minor Crimes Division** cases from your favorite SQL editor.

DD Connect is a small mod that keeps a copy of the case you're playing in one file. You open it once in VS Code (or any SQLite editor) and it stays up to date as you play: new searches, saved tables, website downloads and level changes all show up automatically.

The file is only a copy, so changing it can't break the game. Your save changes in only two cases, and you start both yourself: when you create a new table in your editor, DD Connect adds that table to your case (see [Create tables in your editor](#create-tables-in-your-editor)), and when you ask [`ddsql`](#ddsql-a-sql-prompt-for-the-running-game) to `--save` a search or to `drop` a table you made. You can turn both off in the [settings](#settings).

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

## Create tables in your editor

Run a normal `CREATE TABLE` in your editor and the table appears on your desktop in the game, as if you had saved it from a search.

```sql
CREATE TABLE red_cars AS
SELECT * FROM vehicles WHERE color = 'red';
```

A table with columns you define yourself works too:

```sql
CREATE TABLE my_notes (suspect TEXT, note TEXT);
INSERT INTO my_notes VALUES ('Dana Hill', 'Seen near the warehouse');
```

Within a second or two the table gets an icon in the game, and a message tells you what arrived. It's saved with your case like any other table.

Things to know:
- **Only new tables are sent.** Changing a table that already exists in the copy does nothing, and the next update undoes it.
- **Your case needs to be open in the game.** On the main menu, or with the game closed, new tables are not sent and are removed at the next update.
- **The game keeps the rows, not the query.** The table is a snapshot of the rows when you ran the statement. Right-clicking its icon has no "Restore query" entry.
- **The game's limits apply.** The name must start with a letter or underscore and use only letters, digits and underscores. The table can't be empty or have more than 50,000 rows, the name can't be one the game reserves or already uses, and your desktop holds at most 60 icons.
- **Something refused?** Run `SELECT * FROM _connect_results;` to see why. Refused tables are removed at the next update, so fix the statement and run it again.
- **Rows bypass the in-game search rules.** The words the search box blocks (see Tips) don't matter here, because the game receives data you built in your own editor.

---

## ddsql: a SQL prompt for the running game

`ddsql` is a small command-line tool. It connects to the game while you play and lets you type queries in a terminal, like `snow sql` or `psql`. Results are always current (no waiting for the copy to refresh), and it works well in scripts and for AI agents.

### Set it up

1. Download `ddsql-win-x64.exe`, `ddsql-linux-x64` or `ddsql-osx-arm64` from the [latest release](../../releases/latest). Rename it to `ddsql` (or `ddsql.exe`) and put it somewhere on your `PATH`. On Linux and macOS, run `chmod +x ddsql`.
2. Turn the connection on. In `BepInEx/config/dev.ddconnect.cfg`, under `[Bridge]`, set `Enabled = true`. (Or start the game with the environment variable `DDCONNECT_BRIDGE=1`.)
3. Start the game, open a case, and check: `ddsql connection test`.

The connection is off by default because it opens a port on your own computer (127.0.0.1 only). Every request needs a random token that the game writes to `Connect/bridge.json` each time it starts, so only programs running as you can use it.

### Use it

```text
ddsql                                   open the prompt
ddsql -q "SELECT * FROM vehicles LIMIT 5"
ddsql -f queries.sql                    run a file of statements
ddsql -q "SELECT 1" --format json       table, json, ndjson or csv
ddsql state                             level, desktop icons and tables
```

In the prompt, end a statement with `;`. Dot-commands help you around: `.tables`, `.schema [name]`, `.state`, `.format json`, `.help`, `.quit`.

```text
ddsql [Level 3]> SELECT name, color FROM vehicles
      ...> WHERE color = 'red';
 name   | color
--------+------
 Civic  | red
(1 row, 4 ms)
```

**Queries are read-only.** `INSERT`, `UPDATE`, `DELETE`, `CREATE` and the like are refused, so `ddsql` cannot damage your case. There is one way to create a table, and you have to ask for it by name: add `--save NAME` to a single `SELECT`.

```text
ddsql -q "SELECT * FROM vehicles WHERE color = 'red'" --save red_cars
```

That runs the search inside the game, exactly as if you had typed it into the search box and ticked "save". The game's rules apply (blocked words, table names, the 60-icon limit), the table gets its icon, and "Restore query" works. In the prompt, `.save red_cars` saves the last `SELECT` the same way.

**Cleaning up while you test.** Every `--save` leaves its result window on the game's taskbar, and the game refuses searches once the taskbar is full. Two commands tidy up:

```text
ddsql close-windows                     close the windows that fill the taskbar
ddsql drop red_cars blue_cars           delete tables, as the game's "Delete table" does
```

`drop` goes through the game's own delete, so it removes whatever the game would let you delete there, which can include the case's own tables. Use it on a throwaway save. Deleting with SQL (`DROP TABLE`) is still refused. In the prompt these are `.close` and `.drop NAME`.

Exit codes, handy for scripts: `0` ok, `1` the game refused or failed the query, `2` wrong usage, `3` the game can't be reached, `4` wrong token. When output is piped, the format defaults to `json`.

Tool authors and agents can talk to the game directly; see the [bridge protocol](docs/bridge-protocol.md). `tools/smoke.sh` checks a running game end to end (use a throwaway save, since it creates a table).

---

## Tips

- **Which case am I looking at?** Run `SELECT * FROM _mirror_info;`. It shows the level and when the copy was last updated.
- **New table not showing up?** Refresh your editor's table list. Editors remember the table list, but the data itself is already up to date.
- **What are `result_1`, `result_2`…?** Those are your unsaved in-game searches.
- **Scratch tables.** Every new table you create in the copy is sent to the game. For work you don't want on your desktop, use `WITH name AS (...)` or `CREATE TEMP TABLE name AS ...`. Temp tables last until your editor disconnects.
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
| `SendTables` (in `[Editor]`) | `true` | Let tables you create in your editor appear in the game. Turn it off to make the copy read-only again. |
| `Enabled` (in `[Bridge]`) | `false` | Let [`ddsql`](#ddsql-a-sql-prompt-for-the-running-game) and other tools connect to the running game on 127.0.0.1. |

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
Either `SendTables` is set to `false` (see [Settings](#settings)), which makes the copy read-only on purpose, or an older version of DD Connect left the file marked read-only. Starting the game once with `SendTables` on clears that.

**`ddsql` says it can't reach the game.**
The game must be running with `Enabled = true` under `[Bridge]` in the settings. If you see a `bridge.json` mentioned as "left over", the game closed without cleaning up; start it again.

**My table didn't show up in the game.**
Run `SELECT * FROM _connect_results;` in your editor. It lists each table that was sent and why any were refused. See [Create tables in your editor](#create-tables-in-your-editor).

---

## Uninstall

Delete `BepInEx/plugins/DDConnect` from the game folder. You can also delete the `Connect` folder from the path in [Check it works](#3-check-it-works).

To remove BepInEx completely on Linux or macOS, also clear the Steam Launch Options.

---

## Disclaimer

DD Connect is an unofficial, fan-made mod. It is not affiliated with, endorsed by, sponsored by or supported by the developers or publishers of *Database Detective: Minor Crimes Division*. All game names, trademarks and content belong to their respective owners.

DD Connect contains no game files or game code. You need your own copy of the game to use it.

DD Connect is released under the [MIT License](LICENSE). The mod is provided as is, without warranty. Modding a game is always at your own risk, so back up your save if you're worried. If something goes wrong, report it in this repository's issues, not to the game's developers.
