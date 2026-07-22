#!/bin/bash
# Re-apply Unity-overwritten Gradle/Manifest customizations after Unity build.
# Juga angkat modul unityLibrary dari export penuh Unity (unityexport_tmp).
# Usage: bash .gradle-customizations-backup/restore.sh
set -e
ROOT=/Users/lendra/Documents/codeV/LiaVA/android
BACKUP=$ROOT/.gradle-customizations-backup

# Unity export = project penuh di unityexport_tmp; modul asli di dalamnya
if [ -d "$ROOT/unityexport_tmp/unityLibrary" ]; then
  rm -rf "$ROOT/unityLibrary"
  mv "$ROOT/unityexport_tmp/unityLibrary" "$ROOT/unityLibrary"
  cp -R "$ROOT/unityexport_tmp/shared/." "$ROOT/shared/" 2>/dev/null || true
  rm -rf "$ROOT/unityexport_tmp"
  echo "[restore] unityLibrary module diangkat dari unityexport_tmp"
fi

cp "$BACKUP/build.gradle.root"     "$ROOT/build.gradle"
cp "$BACKUP/build.gradle.launcher" "$ROOT/launcher/build.gradle"
cp "$BACKUP/gradle.properties"     "$ROOT/gradle.properties"
cp "$BACKUP/AndroidManifest.xml"   "$ROOT/launcher/src/main/AndroidManifest.xml"
cp "$BACKUP/strings.xml"           "$ROOT/launcher/src/main/res/values/strings.xml"

echo "[restore] 5 files restored:"
echo "  - build.gradle (root)"
echo "  - launcher/build.gradle"
echo "  - gradle.properties"
echo "  - launcher/src/main/AndroidManifest.xml"
echo "  - launcher/src/main/res/values/strings.xml"
