# DD Connect bridge protocol

The bridge is a small local server inside the game that tools such as [`ddsql`](../README.md#ddsql-a-sql-prompt-for-the-running-game) use. It is off by default. Turn it on with `Enabled = true` under `[Bridge]` in `BepInEx/config/dev.ddconnect.cfg`, or start the game with the environment variable `DDCONNECT_BRIDGE=1`.

## Finding the game

When the bridge starts, the mod writes `Connect/bridge.json` in the game's data folder (the same folder as `current.db`) and deletes it on a clean exit:

```json
{"version":1,"port":53211,"token":"4f0c…","pid":12345}
```

- `port`: a free port on `127.0.0.1`, different on every launch.
- `token`: 64 hex characters, different on every launch. Every request must include it.
- `pid`: the game's process id. If no process has this id, the file is left over from a crash.

On Linux and macOS the file is readable by your user only. Anything that can read it can control the bridge, which is why it is off by default.

## Wire format

TCP to `127.0.0.1:port`. Each request is one JSON object on one line, ending in `\n`. Each request gets exactly one reply line. A connection can carry many requests, one after another.

Requests are flat objects (no nested values). Limits: 64 KB per line, 4 connections at a time, idle connections are closed after two minutes.

```json
{"id":1,"token":"…","cmd":"query","sql":"SELECT * FROM vehicles LIMIT 5","maxRows":100}
```

Every reply echoes `id` and has `ok`:

```json
{"id":1,"ok":true, …}
{"id":1,"ok":false,"error":{"code":"read_only","message":"…"}}
```

A request with a missing or wrong token gets an `auth` error and the connection is closed. Anything that looks like HTTP is dropped without a reply.

## Error codes

| Code | Meaning |
|---|---|
| `auth` | Missing or wrong token. |
| `bad_request` | Not valid JSON, an unknown `cmd`, or a missing or invalid field. |
| `not_in_level` | No case is open (the game is on the main menu). |
| `read_only` | The statement would change data, or is more than one statement. |
| `sql_error` | SQLite rejected the query (syntax error, unknown table, …). |
| `timeout` | A query ran longer than 5 seconds, or the game did not answer in time. |
| `busy` | The database is locked, or another `save` is still running. |
| `game_refused` | The game refused a `save` (its own text, including "Your taskbar is full") or a `drop`. |
| `unavailable` | The mod could not hook that part of the game. |
| `internal` | Something unexpected; the message says what. |

## Commands

### `ping`

```json
{"id":1,"ok":true,"protocol":1,"version":"0.2.0","level":3,"inLevel":true}
```

`level` is 0 on the main menu. `inLevel` is true when a case is open.

### `state`

```json
{"id":2,"ok":true,"level":3,"inLevel":true,"iconCount":12,"iconLimit":60,"busy":false,
 "tables":[{"name":"vehicles","saved":false},{"name":"red_cars","saved":true,"query":"SELECT …"}]}
```

`tables` lists every table of the case. `saved` is true for tables created from a search, with `query` holding the saved search. With no case open, only `level` and `inLevel` are returned.

### `query`

Request fields: `sql` (one statement), `maxRows` (optional; default 1000, at most 50000).

```json
{"id":3,"ok":true,"columns":["id","color"],"rows":[[1,"red"],[2,null]],
 "rowCount":2,"truncated":false,"elapsedMs":4,"level":3}
```

Runs on the live database of the open case, read-only. The statement must start with `SELECT`, `WITH`, `VALUES`, `EXPLAIN` or `PRAGMA`, must be a single statement, and must not be able to write; anything else is `read_only`. Cells are JSON numbers, strings or `null`; a blob is shown as `"<blob N bytes>"`. `truncated` is true when more rows were available than `maxRows`.

### `save`

Request fields: `name` (letters, digits and underscores, starting with a letter or underscore, 64 characters at most) and `query` (one `SELECT`, no semicolons).

```json
{"id":4,"ok":true,"table":"red_cars","message":"Saved as table red_cars."}
```

Runs the query through the game's own search bar and saves the result as a table, so the game's rules apply: blocked words, name checks, the time limit, the 60-icon limit and the row limit. The table gets its icon and the query is kept for "Restore query". Only one `save` runs at a time. It takes the game's main thread for a moment and puts back whatever the player had typed in the search bar. If the search window is closed, the mod opens it for the search (the game's search cannot run otherwise) and closes it again afterwards.

Every `save` leaves its result window open with a button on the game's taskbar, and the game refuses a search when the taskbar is full (`game_refused`, "Your taskbar is full"). Use `close_windows` to make room.

### `drop`

Request field: `name` (a table name, same rules as `save`).

```json
{"id":5,"ok":true,"table":"red_cars","message":"Deleted table red_cars."}
```

Deletes a table exactly as the game's "Delete table" menu entry does: the saved query, the icon and its position, the table's window and the table itself. The game locks some tables against deletion, and those come back as `game_refused`. A table the game does not lock can be deleted, including one of the case's own; downloading the case again in the game brings it back. There is no way to delete with `query`.

### `close_windows`

```json
{"id":6,"ok":true,"closed":12}
```

Closes every window that has a taskbar button, as pressing each one's close button would (table windows, result windows, the search window if it is open). Tables and icons are not touched.

`save`, `drop` and `close_windows` are the only commands that change the game.
