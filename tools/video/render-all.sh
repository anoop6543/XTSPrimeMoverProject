#!/usr/bin/env bash
# Records engine frames and renders the three showcase videos into docs/videos.
# Requirements: .NET 10 SDK, Node 18+, Chromium (CHROMIUM_PATH), ffmpeg with libx264.
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$(cd ../.. && pwd)"
WORK="${WORK:-$PWD/out}"
OUT="$ROOT/docs/videos"
mkdir -p "$WORK" "$OUT"

record() { dotnet run --project "$ROOT/tools/TwinRecorder" -c Release -- --out "$WORK/$1.json" "${@:2}"; }

render() { # name tour
  rm -rf "$WORK/$1"
  node capture.mjs --frames "$WORK/$1.json" --tour "$2" --out "$WORK/$1"
  ffmpeg -loglevel error -y -framerate 30 -i "$WORK/$1/frame_%05d.png" \
    -c:v libx264 -preset slow -crf 23 -pix_fmt yuv420p -movflags +faststart "$OUT/$1.mp4"
  ffmpeg -loglevel error -y -i "$WORK/$1/frame_$(printf %05d "$3").png" -q:v 3 "$OUT/$1.jpg"
}

[[ "${SKIP_RECORD:-0}" == "1" ]] || {
  record xts-line-overview --seconds 30 --fps 30 --warmup 300 --seed 42 --autopilot
  record xts-station-closeups --seconds 40 --fps 30 --warmup 300 --seed 7 --autopilot
  record xts-ai-predictive-maintenance --seconds 36 --fps 30 --speed 3 --warmup 200 --seed 7 --autopilot --fault laser --fault-at 6
}

render xts-line-overview overview 150 &
render xts-station-closeups stations 700 &
render xts-ai-predictive-maintenance ai 560 &
wait
ls -la "$OUT"
