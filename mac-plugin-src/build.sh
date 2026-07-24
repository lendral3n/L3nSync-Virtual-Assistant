#!/bin/bash
# Rebuild LiaWindow.bundle (universal) → Assets/Plugins/macOS/
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
BUN="$DIR/../unity/Assets/Plugins/macOS/LiaWindow.bundle"
rm -rf "$BUN"; mkdir -p "$BUN/Contents/MacOS"
cp "$DIR/Info.plist" "$BUN/Contents/Info.plist"
clang++ -x objective-c++ -arch arm64 -arch x86_64 -mmacosx-version-min=11.0 \
  -framework Cocoa -framework QuartzCore -framework CoreGraphics -bundle \
  "$DIR/LiaWindow.mm" -o "$BUN/Contents/MacOS/LiaWindow"
echo "built: $BUN"
