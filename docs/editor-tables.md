# Creating tables in your editor

Run a normal `CREATE TABLE` in your editor and the table appears on your desktop in the game, as if you had saved it from a search. The [README](../README.md#create-tables-in-your-editor) has the short version. This page has the details.

```sql
CREATE TABLE red_cars AS
SELECT * FROM vehicles WHERE color = 'red';
```

A table with columns you define yourself works too:

```sql
CREATE TABLE my_notes (suspect TEXT, note TEXT);
INSERT INTO my_notes VALUES ('Dana Hill', 'Seen near the warehouse');
```

Within a second or two the table gets an icon in the game. It's saved with your case like any other table. If some tables are refused, the game shows a short message.

## Things to know

- **Only new tables are sent.** Changing a table that already exists in the copy does nothing, and the next update undoes it.
- **Your case needs to be open in the game.** On the main menu, or with the game closed, new tables are not sent and are removed at the next update.
- **The game keeps the rows, not the query.** The table is a snapshot of the rows when you ran the statement. Right-clicking its icon has no "Restore query" entry. To keep the query too, use [`ddsql --save`](ddsql.md#creating-tables).
- **The game's limits apply.** The name must start with a letter or underscore and use only letters, digits and underscores. The table can't be empty or have more than 50,000 rows, the name can't be one the game reserves or already uses, and your desktop holds at most 60 icons.
- **Rows bypass the in-game search rules.** The words the search box blocks (see [Tips](tips.md)) don't matter here, because the game receives data you built in your own editor.

## My table didn't show up

Run `SELECT * FROM _connect_results;` in your editor. It lists each table that was sent and why any were refused. Refused tables are removed at the next update, so fix the statement and run it again.

If the table was accepted but your editor doesn't list it, refresh the editor's table list.

## Scratch tables

Every new table you create in the copy is sent to the game. For work you don't want on your desktop, use `WITH name AS (...)` or `CREATE TEMP TABLE name AS ...`. Temp tables last until your editor disconnects.

You can turn the whole feature off with `SendTables` in the [settings](settings.md).
