#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
APP="${1:-$ROOT/bin/Matraca.app}"
RESULT="$(mktemp "${TMPDIR:-/tmp}/matraca-audio-smoke.XXXXXX")"
rm -f "$RESULT"
trap 'rm -f "$RESULT"' EXIT

if [[ ! -d "$APP" ]]; then
  echo "Bundle not found: $APP" >&2
  exit 1
fi

open -n "$APP" --args --audio-smoke 1500 "$RESULT"
for _ in {1..600}; do
  [[ -f "$RESULT" ]] && break
  sleep 0.1
done

if [[ ! -f "$RESULT" ]]; then
  echo "Audio smoke timed out; answer the macOS microphone prompt if it is visible." >&2
  exit 1
fi

jq . "$RESULT"
jq -e '
  .success == true and
  .sampleRate == 16000 and
  .sampleCount > 8000 and
  .streamedSamples == .sampleCount and
  .frameCount > 0 and
  .rms > 0 and
  .peak > 0 and
  .restartSampleCount > 0 and
  .restartStreamedSamples == .restartSampleCount and
  .restartFrameCount > 0 and
  .isCapturingAfterStop == false
' "$RESULT" >/dev/null

echo "AudioQueue smoke passed without persisting audio."
