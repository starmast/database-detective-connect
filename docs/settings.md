# Settings

All settings are optional. Edit `BepInEx/config/dev.ddconnect.cfg` in the game folder, which is created on first run.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Turn DD Connect on or off. |
| `MirrorPath` | *(empty)* | Where to put the copy. Empty means the `Connect/current.db` location in the [README](../README.md#3-check-it-works). |
| `DebounceMs` | `300` | How long to wait after the game's last change before updating the copy. |
| `SendTables` (in `[Editor]`) | `true` | Let [tables you create in your editor](editor-tables.md) appear in the game. Turn it off to make the copy read-only again. |
| `Enabled` (in `[Bridge]`) | `false` | Let [`ddsql`](ddsql.md) and other tools connect to the running game on 127.0.0.1. |
