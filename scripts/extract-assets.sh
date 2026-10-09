#!/usr/bin/env bash
# Copies what Flint draws from the owner's Spark APK into assets/ (gitignored, never committed or
# shipped), then builds assets/catalog.json. Given --phone or an .apkm, it first has scripts/decompile.sh --assets
# fetch the app and jadx decode it; given nothing, it reuses what research/ already holds.
# Usage: scripts/extract-assets.sh [--phone | path/to/Spark.apkm]     Needs: python3, and what decompile.sh needs.
set -euo pipefail
cd "$(dirname "$0")/.."
if [ $# -gt 0 ]; then scripts/decompile.sh --assets "$1"; fi
decoded=research/decompiled/resources
splits=research/extracted
[ -f "$decoded/AndroidManifest.xml" ] || { echo "nothing to extract from: run scripts/extract-assets.sh --phone (or path/to/Spark.apkm)" >&2; exit 1; }

rm -rf assets.new && mkdir -p assets.new/res
# Layouts, XML drawables and the one-resolution art (editor faces, pedals, cabs, knobs), colours, dimensions, strings, fonts.
for d in "$decoded"/res/{layout,drawable,values,color,font}*; do cp -r "$d" assets.new/res/; done
# The app's Lottie animations (the connecting screen's rings); res/raw also holds sounds, which are not needed.
mkdir -p assets.new/res/raw && cp "$decoded"/res/raw/*.json assets.new/res/raw/
# The strip and picker icons are density PNGs, in a density split. An .apkm has one per density, and xxhdpi's is the
# sharpest (there is almost no xxxhdpi art). A phone has only its own, holding the closest art there is for its screen,
# so some images may be one density and some another. Its XML is binary AXML, so only images come from it.
split="$splits/split_config.xxhdpi.apk"
[ -f "$split" ] || split=$(compgen -G "$splits/split_config.*dpi.apk" | head -1) || { echo "no density split APK in $splits" >&2; exit 1; }
for dpi in xxxhdpi xxhdpi xhdpi hdpi mdpi; do
  unzip -qoj "$split" "res/drawable-$dpi-v4/*.png" "res/drawable-$dpi-v4/*.webp" -d "assets.new/res/drawable-$dpi" 2>/dev/null || true
done
# The amp panels (background, knob type and parameter per amp), and per chain slot the model sheet's order with each
# model's starting knob values.
cp -r "$decoded/assets/Configuration" "$decoded/assets/ModulePresets" assets.new/

python3 scripts/extract-catalog.py research/decompiled assets.new
python3 - "$decoded/AndroidManifest.xml" > assets.new/manifest.json <<'PY'
import json, re, sys, datetime
xml = open(sys.argv[1]).read()
attr = lambda name: re.search(rf' {name}="([^"]+)"', xml).group(1)
print(json.dumps({"app": attr("package"), "version": attr("android:versionName"), "extracted": datetime.date.today().isoformat()}, indent=2))
PY

rm -rf assets && mv assets.new assets
echo "assets/: $(du -sh assets | cut -f1), $(find assets -type f | wc -l) files, Spark $(python3 -c 'import json;print(json.load(open("assets/manifest.json"))["version"])')"
