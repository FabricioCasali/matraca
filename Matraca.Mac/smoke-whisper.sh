#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
APP="${1:-$ROOT/bin/Matraca.app}"
MODEL="${2:-$HOME/Library/Application Support/Matraca/models/ggml-base.bin}"
RESULT="$(mktemp "${TMPDIR:-/tmp}/matraca-whisper-smoke.XXXXXX")"
rm -f "$RESULT"
trap 'rm -f "$RESULT"' EXIT

if [[ ! -d "$APP" ]]; then
  echo "Bundle not found: $APP" >&2
  exit 1
fi
if [[ ! -f "$MODEL" ]]; then
  echo "Whisper model not found: $MODEL" >&2
  exit 1
fi

open -n "$APP" --args --whisper-smoke "$MODEL" "$RESULT"
for _ in {1..900}; do
  [[ -f "$RESULT" ]] && break
  sleep 0.1
done

if [[ ! -f "$RESULT" ]]; then
  echo "Whisper smoke timed out." >&2
  exit 1
fi

jq . "$RESULT"
jq -e '
  .success == true and
  .initializationsAfterLoad == 1 and
  .releasesAfterFirst == 0 and
  .releasesAfterSecond == 0 and
  .releasesAfterDispose == 1 and
  .runCount == 2
' "$RESULT" >/dev/null

echo "Persistent Whisper smoke passed with two in-memory transcriptions."
