#!/usr/bin/env bash
# Smoke test for the DD Connect bridge, using ddsql against a running game.
#
# Before running: start the game with the bridge enabled and open a case. This creates one small table in that
# case (smoke_<timestamp>) and has no way to remove it, so use a throwaway save.
#
#   tools/smoke.sh [path-to-ddsql]        (or: DDSQL=path tools/smoke.sh)
#
# Exit status is 0 when every check passed.

set -u

DDSQL="${1:-${DDSQL:-ddsql}}"
stamp="$(date +%s)"
table="smoke_${stamp}"
pass=0
fail=0

check() {
  local label="$1"; shift
  if "$@"; then
    printf 'ok    %s\n' "$label"; pass=$((pass + 1))
  else
    printf 'FAIL  %s\n' "$label"; fail=$((fail + 1))
  fi
}

# run <expected exit code> <ddsql arguments...>; leaves stdout in $out and stderr in $err
run() {
  local expected="$1"; shift
  local errfile; errfile="$(mktemp)"
  out="$("$DDSQL" "$@" 2>"$errfile")"
  local code=$?
  err="$(cat "$errfile")"; rm -f "$errfile"
  [ "$code" -eq "$expected" ]
}

contains() { case "$1" in *"$2"*) return 0 ;; *) return 1 ;; esac; }

echo "Using: $DDSQL"

check "connection test" run 0 connection test
check "state runs" run 0 state --format json
check "state shows an open case" contains "$out" '"inLevel":true'

run 0 -q "SELECT 1 AS one" --format json
check "SELECT returns rows" contains "$out" '"rows":[[1]]'

check "DELETE is refused" run 1 -q "DELETE FROM sqlite_master"
check "CREATE TABLE without --save is refused" run 1 -q "CREATE TABLE ${table}_plain AS SELECT 1"
check "  ...and says how to do it" contains "$err" "--save"
check "ATTACH is refused" run 1 -q "ATTACH DATABASE '/tmp/x.db' AS x"

# A real table to save from: the first ordinary table in the case.
run 0 -q "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite!_%' ESCAPE '!' ORDER BY name LIMIT 1" \
  --format csv --no-header
source_table="$out"
if [ -z "$source_table" ]; then
  echo "FAIL  could not find a table to save from (is a case open?)"; fail=$((fail + 1))
else
  check "--save creates a table" run 0 -q "SELECT * FROM ${source_table} LIMIT 5" --save "$table"
  run 0 state --format json
  check "the new table is listed as a saved search" contains "$out" "\"name\":\"${table}\",\"saved\":true"
  check "saving the same name again is refused" run 1 -q "SELECT * FROM ${source_table} LIMIT 5" --save "$table"
  check "a blocked word is refused" run 1 -q "SELECT DROP FROM ${source_table}" --save "${table}_b"
  check "a semicolon in a saved query is refused" run 1 -q "SELECT 'a;b'" --save "${table}_c"
  check "a bad table name is refused" run 1 -q "SELECT 1" --save "bad name"
  check "the saved table can be read back" run 0 -q "SELECT COUNT(*) AS n FROM ${table}" --format json
fi

# A wrong token must be rejected (exit code 4).
bridge="${DDSQL_CONNECTION:-}"
if [ -z "$bridge" ]; then
  for candidate in \
    "$HOME/AppData/LocalLow/HsuCorp/copOS/Connect/bridge.json" \
    "$HOME/.config/unity3d/HsuCorp/copOS/Connect/bridge.json" \
    "$HOME/Library/Application Support/HsuCorp/copOS/Connect/bridge.json"; do
    [ -f "$candidate" ] && bridge="$candidate" && break
  done
fi
if [ -n "$bridge" ]; then
  bad="$(mktemp)"
  sed 's/"token":"[^"]*"/"token":"wrong"/' "$bridge" > "$bad"
  check "a wrong token is rejected" run 4 --connection "$bad" -q "SELECT 1"
  rm -f "$bad"
else
  echo "skip  wrong-token check (set DDSQL_CONNECTION to the bridge.json path)"
fi

printf '\n%d passed, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]
