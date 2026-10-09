#!/usr/bin/env bash
# Regenerates research/ from the Spark app. Personal use only; research/ is gitignored.
# Usage: scripts/decompile.sh [--assets] [path/to/Spark.apkm | --phone]   (default: research/Spark.apkm)
# --phone pulls the app installed on an Android phone over USB (USB debugging on) instead of unpacking an .apkm (an
# APKMirror bundle). --assets stops once jadx is done: that is all scripts/extract-assets.sh needs.
# Needs: java 21+, curl, unzip, python3, and git without --assets.
set -euo pipefail
assets_only=false; [ "${1:-}" = --assets ] && { assets_only=true; shift; }
src="${1:-$(dirname "$0")/../research/Spark.apkm}"; [ "$src" = --phone ] || src="$(realpath -m "$src")"
mkdir -p "$(dirname "$0")/../research" && cd "$(dirname "$0")/../research"
# latest <owner/repo> <asset-name regex>: download URL of the newest release asset matching the regex
latest() { curl -s "https://api.github.com/repos/$1/releases/latest" | python3 -c "import sys,json,re;print([a['browser_download_url'] for a in json.load(sys.stdin)['assets'] if re.fullmatch(sys.argv[1],a['name'])][0])" "$2"; }

mkdir -p tools native refs
[ -x tools/jadx/bin/jadx ] || { curl -sL -o tools/jadx.zip "$(latest skylot/jadx 'jadx-[0-9.]+\.zip')"; unzip -oq tools/jadx.zip -d tools/jadx; rm tools/jadx.zip; }

rm -rf extracted && mkdir extracted
if [ "$src" = --phone ]; then
  adb=$(command -v adb || echo tools/platform-tools/adb)
  [ -x "$adb" ] || { curl -sL -o tools/platform-tools.zip https://dl.google.com/android/repository/platform-tools-latest-linux.zip; unzip -oq tools/platform-tools.zip -d tools; rm tools/platform-tools.zip; }
  echo "waiting for the phone: connect it over USB with USB debugging on, and allow this computer when it asks" >&2
  "$adb" wait-for-device
  # The base APK and the splits Google Play installed for this phone: its CPU, its screen density, its languages.
  apks=$({ "$adb" shell pm path com.positivegrid.spark || true; } | tr -d '\r' | sed -n 's/^package://p')
  [ -n "$apks" ] || { echo "the Spark app is not installed on the phone" >&2; exit 1; }
  for a in $apks; do "$adb" pull "$a" extracted/ > /dev/null; done
else
  unzip -oq "$src" -d extracted
fi

# Java/Kotlin layer + resources
JAVA_OPTS="-Xmx12g" tools/jadx/bin/jadx -d decompiled --show-bad-code --deobf-min 2 -j "$(nproc)" extracted/base.apk > jadx.log 2>&1 || true
$assets_only && { echo "done: research/decompiled"; exit 0; }

ls -d tools/ghidra_*_PUBLIC >/dev/null 2>&1 || { curl -sL -o tools/ghidra.zip "$(latest NationalSecurityAgency/ghidra 'ghidra_.*_PUBLIC_.*\.zip')"; unzip -oq tools/ghidra.zip -d tools; rm tools/ghidra.zip; }
unzip -oqj extracted/split_config.x86_64.apk 'lib/x86_64/*' -d native/x86_64

# Native protocol library: symbols, strings, and decompiled C for every function
nm -DC --defined-only native/x86_64/libpghw-lib.so | awk '{$1="";print}' > native/pghw-symbols.txt
strings -n 6 native/x86_64/libpghw-lib.so > native/pghw-strings.txt
mkdir -p ghidra-proj
"$(ls -d tools/ghidra_*_PUBLIC | head -1)/support/analyzeHeadless" ghidra-proj pghw \
  -import native/x86_64/libpghw-lib.so -overwrite \
  -scriptPath ../scripts -postScript ExportDecomp.java "$PWD/native/pghw-decompiled.c" > ghidra.log 2>&1

# Community references
for r in averagenative/0xSpark40 duboc/spark-web paulhamsh/Spark soundshed/soundshed-app richtamblyn/PGSparkLite; do
  [ -d "refs/${r#*/}" ] || git clone -q --depth 1 "https://github.com/$r.git" "refs/${r#*/}"
done
python3 "../scripts/opcode-table.py" native/pghw-symbols.txt > native/opcode-table.txt
python3 "../scripts/jni-opcodes.py" native/x86_64/libpghw-lib.so native/pghw-decompiled.c > native/jni-opcodes.txt
echo "done: research/decompiled, research/native, research/refs"
