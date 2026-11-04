# Editors

Any tool that opens SQLite files works with DD Connect. The [README](../README.md#connect-your-editor) covers VS Code with SQLTools, the recommended setup. This page has the rest.

Use the full path to `current.db` from the README's [Check it works](../README.md#3-check-it-works) table.

## Other editors

| Editor | How |
|---|---|
| **SQLite3 Editor** (VS Code) | Open `current.db` in VS Code. Little setup needed. |
| **SQLite Viewer** (VS Code) | Open `current.db` to browse tables (viewing only). |
| **DB Browser for SQLite**, **DBeaver**, **DataGrip** | Open `current.db` as a SQLite database. |
| **Excel**, **Power BI** (ODBC, Windows) | Install the free [SQLite ODBC driver](http://www.ch-werner.de/sqliteodbc/) (`sqliteodbc_w64.exe`). Then add a data source in **ODBC Data Sources (64-bit)** that points at `current.db`. |

## SQLTools through settings.json

If you prefer editing settings to using the SQLTools sidebar:

```json
"sqltools.connections": [
  {
    "name": "Database Detective",
    "driver": "SQLite",
    "database": "C:/Users/YOUR-NAME/AppData/LocalLow/HsuCorp/copOS/Connect/current.db" // use your system's path
  }
]
```
