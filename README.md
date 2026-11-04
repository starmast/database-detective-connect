# DD Connect

Solve **Database Detective: Minor Crimes Division** cases from your favorite SQL editor.

DD Connect is a small mod that keeps a copy of the case you're playing in one file. You open it once in VS Code (or any SQLite editor) and it stays up to date as you play: new searches, saved tables, website downloads and level changes all show up automatically.

The file is only a copy, so changing it can't break the game. Your save changes in only two cases, and you start both yourself: when you create a new table in your editor, DD Connect adds that table to your case (see [Create tables in your editor](#create-tables-in-your-editor)), and when you ask [`ddsql`](#ddsql-a-sql-prompt-for-the-running-game) to `--save` a search or to `drop` a table. You can turn both off in the [settings](docs/settings.md).

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

Using another editor (DB Browser, DBeaver, DataGrip, Excel, Power BI)? See [Editors](docs/editors.md).

---

## Create tables in your editor

Run a normal `CREATE TABLE` in your editor and the table appears on your desktop in the game, as if you had saved it from a search.

```sql
CREATE TABLE red_cars AS
SELECT * FROM vehicles WHERE color = 'red';
```

Within a second or two the table gets an icon in the game. It's saved with your case like any other table. The game's limits apply (valid name, 1 to 50,000 rows, 60 icons), and only new tables are sent. Something didn't arrive? Run `SELECT * FROM _connect_results;` to see why.

More in [Creating tables in your editor](docs/editor-tables.md).

---

## ddsql: a SQL prompt for the running game

`ddsql` is a small command-line tool. It connects to the game while you play and lets you type queries in a terminal, like `snow sql` or `psql`. Results are always current, and it works well in scripts and for AI agents.

1. Download `ddsql-win-x64.exe`, `ddsql-linux-x64` or `ddsql-osx-arm64` from the [latest release](../../releases/latest). Rename it to `ddsql` (or `ddsql.exe`) and put it somewhere on your `PATH`. On Linux and macOS, run `chmod +x ddsql`.
2. In `BepInEx/config/dev.ddconnect.cfg`, under `[Bridge]`, set `Enabled = true`. It's off by default because it opens a port on your own computer.
3. Start the game, open a case, and check: `ddsql connection test`.

```text
ddsql                                   open the prompt
ddsql -q "SELECT * FROM vehicles LIMIT 5"
ddsql -q "SELECT * FROM vehicles WHERE color = 'red'" --save red_cars
```

Queries are read-only. The only way to create a table is `--save NAME`, which runs the search inside the game so its rules apply. See [ddsql](docs/ddsql.md) for the prompt, output formats, cleanup commands (`drop`, `close-windows`) and exit codes.

---

## More

- [Editors](docs/editors.md): DB Browser, DBeaver, DataGrip, Excel and Power BI
- [Creating tables in your editor](docs/editor-tables.md): limits, scratch tables, what to do when a table is refused
- [ddsql](docs/ddsql.md): the full command-line guide
- [Settings](docs/settings.md): the config file
- [Tips](docs/tips.md)
- [Bridge protocol](docs/bridge-protocol.md): for tool authors

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
Either `SendTables` is set to `false` (see [Settings](docs/settings.md)), which makes the copy read-only on purpose, or an older version of DD Connect left the file marked read-only. Starting the game once with `SendTables` on clears that.

More fixes: [creating tables](docs/editor-tables.md#my-table-didnt-show-up) and [ddsql](docs/ddsql.md#troubleshooting).

---

## Uninstall

Delete `BepInEx/plugins/DDConnect` from the game folder. You can also delete the `Connect` folder from the path in [Check it works](#3-check-it-works).

To remove BepInEx completely on Linux or macOS, also clear the Steam Launch Options.

---

## Disclaimer

DD Connect is an unofficial, fan-made mod. It is not affiliated with, endorsed by, sponsored by or supported by the developers or publishers of *Database Detective: Minor Crimes Division*. All game names, trademarks and content belong to their respective owners.

DD Connect contains no game files or game code. You need your own copy of the game to use it.

DD Connect is released under the [MIT License](LICENSE). The mod is provided as is, without warranty. Modding a game is always at your own risk, so back up your save if you're worried. If something goes wrong, report it in this repository's issues, not to the game's developers.
