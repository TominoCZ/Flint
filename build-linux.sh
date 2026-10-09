#!/usr/bin/env bash
#
# Publishes Flint for Linux as one self-contained, optimised executable: the .NET runtime and every
# managed and native library are bundled inside it, ReadyToRun-compiled for a fast start, with no debug symbols.
#
# Output: artifacts/linux-x64/Flint
#         artifacts/Flint-<version>-linux-x64.tar.gz   (keeps the executable bit; ship this)
#
# Usage: ./build-linux.sh [--arch linux-arm64] [--clean] [--install]
#
# --install also puts this build where daily use runs it: the executable in ~/.local/bin, and a copy of assets/ in
# ~/.local/share/Flint/assets. Its first start adds the application-menu entry. It starts faster than
# run.sh's build, whose code is compiled as it first runs; this one is precompiled (ReadyToRun).
#
set -euo pipefail
cd "$(dirname "$0")"

RID="linux-x64"
CLEAN="false"
INSTALL="false"
while [ $# -gt 0 ]; do
  case "$1" in
    --arch|-a) RID="$2"; shift 2 ;;
    --clean)   CLEAN="true"; shift ;;
    --install) INSTALL="true"; shift ;;
    -h|--help) sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

DOTNET="$(command -v dotnet || echo "$HOME/.dotnet/dotnet")"
OUT="artifacts/$RID"
[ "$CLEAN" = "true" ] && rm -rf artifacts

"$DOTNET" publish ./src/Flint.App/Flint.App.csproj -nologo -v q \
  -c Release -r "$RID" -o "$OUT" \
  -p:SelfContained=true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=true \
  -p:DebugType=none -p:DebugSymbols=false

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
tar -czf "artifacts/Flint-$VERSION-$RID.tar.gz" -C "$OUT" Flint
echo "built $OUT/Flint ($(du -h "$OUT/Flint" | cut -f1)) and artifacts/Flint-$VERSION-$RID.tar.gz"

if [ "$INSTALL" = "true" ]; then
  install -D -m 755 "$OUT/Flint" "$HOME/.local/bin/Flint"
  rm -f "$HOME/.local/bin/SparkControl" # Flint's name before; its data moves over when Flint starts
  DATA="${XDG_DATA_HOME:-$HOME/.local/share}/Flint"
  if [ -f assets/manifest.json ]; then
    # Only our own copy is replaced, and only once the new one is whole.
    rm -rf "$DATA/assets.new" && mkdir -p "$DATA" && cp -r assets "$DATA/assets.new"
    rm -rf "$DATA/assets" && mv "$DATA/assets.new" "$DATA/assets"
  else
    echo "no assets/ here: the app's setup screen takes the artwork from the Spark app when it first starts" >&2
  fi
  echo "installed ~/.local/bin/Flint; start it once to add it to the application menu"
fi
