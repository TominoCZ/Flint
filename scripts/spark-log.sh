#!/usr/bin/env bash
# Live, decoded log of the official Spark app's Bluetooth traffic, read from the phone's logcat over USB.
# Needs USB debugging on the phone (authorise this PC once). Bluetooth HCI snoop is not needed: the app
# logs every byte itself.
#   research/captures/<stamp>.log          raw logcat (the evidence; keep it)
#   research/captures/<stamp>.decoded.log  one line per chunk, plus the app's own BLE log lines
#   research/captures/live{,.decoded}.log  links to the newest pair
set -euo pipefail
cd "$(dirname "$0")/.."
adb=$(command -v adb || echo research/tools/platform-tools/adb)
mkdir -p research/captures
dotnet build tools/SparkLog -c Release -v q -o research/tools/sparklog > /dev/null

stamp=$(date +%Y%m%d-%H%M%S)
ln -sfn "$stamp.log" research/captures/live.log
ln -sfn "$stamp.decoded.log" research/captures/live.decoded.log

tags=('[PGBLE]:V' BLEDATA:V PGBLE:V '[REMOTE]:V' KNOB:V EDIT:V PG.TONE:V TONE_ACTION:V)
since=()   # first attach replays the phone's log buffer; reattaching after a USB drop only takes new lines
while true; do
  "$adb" wait-for-device
  "$adb" logcat -v threadtime -v year "${since[@]}" -s "${tags[@]}" || true
  since=(-T "$(date '+%Y-%m-%d %H:%M:%S.000')")
  echo "--- phone gone at $(date +%T), waiting for it ---" >&2
done | tee -a "research/captures/$stamp.log" \
     | dotnet research/tools/sparklog/SparkLog.dll research/native/opcode-table.txt \
     | tee -a "research/captures/$stamp.decoded.log"
