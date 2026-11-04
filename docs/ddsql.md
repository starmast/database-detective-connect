# ddsql

`ddsql` is a small command-line tool. It connects to the game while you play and lets you type queries in a terminal, like `snow sql` or `psql`. Results are always current (no waiting for the copy to refresh), and it works well in scripts and for AI agents. The [README](../README.md#ddsql-a-sql-prompt-for-the-running-game) has the quick start.

## Set it up

1. Download `ddsql-win-x64.exe`, `ddsql-linux-x64` or `ddsql-osx-arm64` from the [latest release](../../../releases/latest). Rename it to `ddsql` (or `ddsql.exe`) and put it somewhere on your `PATH`. On Linux and macOS, run `chmod +x ddsql`.
2. Turn the connection on. In `BepInEx/config/dev.ddconnect.cfg`, under `[Bridge]`, set `Enabled = true`. (Or start the game with the environment variable `DDCONNECT_BRIDGE=1`.)
3. Start the game, open a case, and check: `ddsql connection test`.

The connection is off by default because it opens a port on your own computer (127.0.0.1 only). Every request needs a random token that the game writes to `Connect/bridge.json` each time it starts, so only programs running as you can use it.

## Running queries

```text
ddsql                                   open the prompt
ddsql -q "SELECT * FROM vehicles LIMIT 5"
ddsql -f queries.sql                    run a file of statements
ddsql -                                 run SQL read from standard input
ddsql -q "SELECT 1" --format json       table, json, ndjson or csv
ddsql state                             level, desktop icons and tables
```

Options: `--format` (`table`, `json`, `ndjson`, `csv`), `--max-rows N` (default 1000, at most 50000), `--no-header`, and `--connection FILE` (or the `DDSQL_CONNECTION` environment variable) to use a specific `bridge.json`.

In the prompt, end a statement with `;`. Dot-commands help you around: `.tables`, `.schema [name]`, `.state`, `.save NAME`, `.drop NAME`, `.close`, `.format json`, `.help`, `.quit`.

```text
ddsql [Level 3]> SELECT name, color FROM vehicles
      ...> WHERE color = 'red';
 name   | color
--------+------
 Civic  | red
(1 row, 4 ms)
```

**Queries are read-only.** `INSERT`, `UPDATE`, `DELETE`, `CREATE`, `DROP` and the like are refused, so a query cannot damage your case. Tables are created and deleted only by the explicit commands below.

## Creating tables

Add `--save NAME` to a single `SELECT`:

```text
ddsql -q "SELECT * FROM vehicles WHERE color = 'red'" --save red_cars
```

That runs the search inside the game, exactly as if you had typed it into the search box and ticked "save". The game's rules apply (blocked words, table names, the 60-icon limit), the table gets its icon, and "Restore query" works. In the prompt, `.save red_cars` saves the last `SELECT` the same way.

## Cleaning up while you test

Every `--save` leaves its result window on the game's taskbar, and the game refuses searches once the taskbar is full. Two commands tidy up:

```text
ddsql close-windows                     close the windows that fill the taskbar
ddsql drop red_cars blue_cars           delete tables, as the game's "Delete table" does
```

`drop` goes through the game's own delete, so it removes whatever the game would let you delete there, which can include the case's own tables. Use it on a throwaway save. In the prompt these are `.close` and `.drop NAME`.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | ok |
| 1 | the game refused or failed the query |
| 2 | wrong usage |
| 3 | the game can't be reached |
| 4 | wrong token |

When output is piped, the format defaults to `json`.

## Troubleshooting

**`ddsql` says it can't reach the game.** The game must be running with `Enabled = true` under `[Bridge]` in the settings. If you see a `bridge.json` mentioned as "left over", the game closed without cleaning up; start it again.

**A save says the taskbar is full.** Run `ddsql close-windows`, or close windows in the game.

## For tool authors

Tools and agents can talk to the game directly; see the [bridge protocol](bridge-protocol.md). [`tools/smoke.sh`](../tools/smoke.sh) checks a running game end to end (use a throwaway save, since it creates a table).
