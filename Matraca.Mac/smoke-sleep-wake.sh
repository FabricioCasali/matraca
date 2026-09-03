#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
APP="$ROOT/bin/Matraca.app"
BIN="$APP/Contents/MacOS/Matraca"
LOG="$HOME/Library/Application Support/Matraca/matraca.log"
CONFIG="$HOME/Library/Application Support/Matraca/appsettings.json"

if [[ ! -x "$BIN" ]]; then
  echo "Package the app first: $ROOT/pack.sh" >&2
  exit 1
fi

if ! pgrep -f "$BIN" >/dev/null; then
  label="io.github.fabriciocasali.matraca.sleep-wake-smoke.$$"
  launchctl submit -l "$label" -- "$BIN"
  sleep 5
fi

[[ -f "$LOG" ]] || { echo "Matraca log not found: $LOG" >&2; exit 1; }

echo "MT-004 sleep/wake manual smoke"
echo "Config: $CONFIG"
echo
read -r -p "Wait until the status menu says Matraca is ready, then press Return... "
start_line=$(( $(wc -l < "$LOG") + 1 ))
backend_loads_before=$(grep -c "Modelo e estado Whisper carregados" "$LOG" || true)
echo "1. With Matraca idle, put the Mac to sleep from the Apple menu and wake it."
read -r -p "Press Return after the idle cycle... "
echo "2. Start a dictation, put the Mac to sleep while it is recording, then wake it."
echo "   The interrupted dictation must produce no text and no Enter."
read -r -p "Press Return after the recording cycle... "
echo "3. Start and stop a dictation, then sleep immediately while Whisper is working."
echo "   No late text or Enter may appear after wake."
read -r -p "Press Return after the transcription cycle... "
echo "4. While asleep, disconnect the configured microphone or change the system default."
echo "   After wake, start a new dictation and verify it uses the configured device or default fallback."
echo "   Edit inputDevice now if desired; the next session must use the saved value without restart."
read -r -p "Press Return after exact post-wake text was verified in the target... "
read -r -p "Did all four checks pass with no focus theft? [y/N] " confirmed
[[ "$confirmed" =~ ^[Yy]$ ]] || { echo "Manual smoke rejected." >&2; exit 1; }

new_log="$(mktemp "${TMPDIR:-/tmp}/matraca-sleep-wake-smoke.XXXXXX")"
trap 'rm -f "$new_log"' EXIT
tail -n "+$start_line" "$LOG" > "$new_log"
sleep_count=$(grep -c "macOS vai entrar em repouso" "$new_log" || true)
wake_count=$(grep -c "macOS retomou do repouso" "$new_log" || true)
resume_count=$(grep -c "Pipeline de ditado retomado apos repouso" "$new_log" || true)
backend_loads_after=$(grep -c "Modelo e estado Whisper carregados" "$LOG" || true)

if (( sleep_count < 3 || wake_count < 3 || resume_count < 3 )); then
  echo "Expected at least three complete sleep/wake transitions; got sleep=$sleep_count wake=$wake_count resume=$resume_count." >&2
  exit 1
fi
if (( backend_loads_after != backend_loads_before )); then
  echo "Whisper backend load count changed from $backend_loads_before to $backend_loads_after." >&2
  echo "Repeat without changing model, language, vocabulary, or GPU configuration." >&2
  exit 1
fi

echo "Sleep/wake smoke passed: paired notifications, resumed pipeline, unchanged Whisper backend count."
