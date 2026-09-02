#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
APP="$ROOT/bin/Matraca.app"
BIN="$APP/Contents/MacOS/Matraca"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/matraca-delivery.XXXXXX")"
TEXT="$(/usr/bin/python3 -c 'print("Matraca entrega texto longo com espacos, acentos: a\u00e7\u00e3o, caf\u00e9, Unicode \U0001F3A4 e pontua\u00e7\u00e3o; sem cortar nenhum caractere. 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ.", end="")')"

cleanup() {
  for label in matraca.delivery.textedit matraca.delivery.terminal matraca.delivery.chrome matraca.delivery.vscode; do
    launchctl remove "$label" >/dev/null 2>&1 || true
  done
  [[ -z "${CHROME_PID:-}" ]] || kill "$CHROME_PID" >/dev/null 2>&1 || true
  if [[ "${MATRACA_KEEP_SMOKE:-0}" == "1" ]]; then
    echo "Smoke artifacts: $TMP"
  else
    pkill -f "$TMP/profile" >/dev/null 2>&1 || true
    sleep 0.5
    rm -rf "$TMP"
  fi
}
trap cleanup EXIT

bash "$ROOT/pack.sh" >/dev/null

run_delivery() {
  local name="$1"
  local enter="$2"
  local result="$TMP/$name.result"
  local label="matraca.delivery.$name"

  launchctl remove "$label" >/dev/null 2>&1 || true
  launchctl submit -l "$label" -- "$BIN" --delivery-smoke "$TEXT" "$enter" 1500 "$result"

  for _ in {1..300}; do
    [[ -f "$result" ]] && break
    sleep 0.1
  done
  [[ -f "$result" ]] || { echo "$name: smoke did not finish" >&2; return 1; }
  [[ "$(<"$result")" == "Delivered" ]] || { echo "$name: delivery failed" >&2; return 1; }
  launchctl remove "$label" >/dev/null 2>&1 || true
}

assert_file() {
  local name="$1"
  local actual="$2"
  local expected="$3"
  if ! cmp -s "$actual" "$expected"; then
    /usr/bin/python3 - "$actual" "$expected" <<'PY'
import pathlib
import sys

actual = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")
expected = pathlib.Path(sys.argv[2]).read_text(encoding="utf-8")
index = next((i for i, pair in enumerate(zip(actual, expected)) if pair[0] != pair[1]), min(len(actual), len(expected)))
print(f"actual={len(actual)} expected={len(expected)} first_difference={index}", file=sys.stderr)
print(f"actual_tail={actual[max(0, index - 12):index + 24]!r}", file=sys.stderr)
print(f"expected_tail={expected[max(0, index - 12):index + 24]!r}", file=sys.stderr)
PY
    echo "$name: received text differs" >&2
    return 1
  fi
  echo "$name: literal match"
}

printf '%s' "$TEXT" > "$TMP/expected.txt"
printf '%s\n' "$TEXT" > "$TMP/expected-enter.txt"

: > "$TMP/textedit.txt"
open -a TextEdit "$TMP/textedit.txt"
run_delivery textedit false
TEXTEDIT_VALUE="$(osascript -e 'tell application "TextEdit" to get text of front document')"
[[ "$TEXTEDIT_VALUE" == "$TEXT" ]] || { echo "TextEdit: received text differs" >&2; exit 1; }
echo "TextEdit: literal match"
osascript -e 'tell application "TextEdit" to close front document saving no' >/dev/null

cat > "$TMP/receive.py" <<'PY'
import pathlib
import sys

pathlib.Path(sys.argv[1]).write_text(input(), encoding="utf-8")
PY
osascript -e "tell application \"Terminal\" to activate" \
  -e "tell application \"Terminal\" to do script \"/usr/bin/python3 $TMP/receive.py $TMP/terminal.txt\"" >/dev/null
run_delivery terminal true
for _ in {1..50}; do
  [[ -f "$TMP/terminal.txt" ]] && break
  sleep 0.1
done
assert_file Terminal "$TMP/terminal.txt" "$TMP/expected.txt"
osascript -e 'tell application "Terminal" to close front window' >/dev/null

cat > "$TMP/browser.html" <<'HTML'
<!doctype html>
<meta charset="utf-8">
<title>READY</title>
<form onsubmit="location.hash = encodeURIComponent(document.getElementById('target').value); return false">
  <input id="target" autofocus>
</form>
HTML
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
[[ -x "$CHROME" ]] || { echo "Chrome: executable not found" >&2; exit 1; }
"$CHROME" --user-data-dir="$TMP/chrome-profile" --no-first-run --disable-sync \
  --app="file://$TMP/browser.html" >/dev/null 2>&1 &
CHROME_PID=$!
sleep 3
run_delivery chrome true
CHROME_URL="$(osascript -e 'tell application "Google Chrome" to get URL of active tab of front window')"
CHROME_VALUE="$(/usr/bin/python3 - "$CHROME_URL" <<'PY'
import sys
import urllib.parse

print(urllib.parse.unquote(urllib.parse.urlsplit(sys.argv[1]).fragment), end="")
PY
)"
[[ "$CHROME_VALUE" == "$TEXT" ]] || { echo "Chrome: received text differs" >&2; exit 1; }
echo "Chrome: literal match"
kill "$CHROME_PID" >/dev/null 2>&1 || true
wait "$CHROME_PID" 2>/dev/null || true
CHROME_PID=""

VSCODE="/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code"
[[ -x "$VSCODE" ]] || { echo "VS Code: executable not found" >&2; exit 1; }
mkdir -p "$TMP/profile/User" "$TMP/extensions"
cat > "$TMP/profile/User/settings.json" <<'JSON'
{
  "files.autoSave": "afterDelay",
  "files.autoSaveDelay": 100,
  "extensions.autoCheckUpdates": false,
  "extensions.autoUpdate": false,
  "security.workspace.trust.enabled": false,
  "telemetry.telemetryLevel": "off",
  "update.mode": "none",
  "workbench.startupEditor": "none",
  "workbench.welcomePage.walkthroughs.openOnInstall": false
}
JSON
: > "$TMP/vscode.txt"
"$VSCODE" --new-window --disable-extensions --disable-workspace-trust \
  --skip-release-notes --skip-welcome --user-data-dir "$TMP/profile" \
  --extensions-dir "$TMP/extensions" "$TMP/vscode.txt" >/dev/null 2>&1 &
sleep 3
osascript -e 'tell application "Visual Studio Code" to activate' >/dev/null
sleep 1
osascript -e 'tell application "System Events" to key code 53' \
  -e 'tell application "System Events" to keystroke "1" using {command down}' >/dev/null
sleep 1
FRONT_APP="$(osascript -e 'tell application "System Events" to get name of first application process whose frontmost is true')"
[[ "$FRONT_APP" == "Code" ]] || { echo "VS Code: expected front app Code, got $FRONT_APP" >&2; exit 1; }
run_delivery vscode true
sleep 1
assert_file "VS Code" "$TMP/vscode.txt" "$TMP/expected-enter.txt"

echo "Delivery smoke passed in TextEdit, Terminal, Chrome and VS Code."
