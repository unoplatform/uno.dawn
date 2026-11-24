#!/usr/bin/env bash
# Builds the Apple payload from build-source-apple.sh's output: libwebgpu.dylib per macOS architecture, finishing its
# ThinLTO, and the static library per iOS slice (iOS apps link Dawn into the app image).
# Usage: link-apple.sh <dir with <slice>/libwebgpu_dawn.a> <dir with webgpu_dawn.exp> <out dir> [macos|ios|iossimulator...]
# The slices default to all those present in the input. Writes <out>/osx-arm64, osx-x64 (libwebgpu.dylib) and
# ios-arm64, iossimulator-arm64, iossimulator-x64 (libwebgpu_dawn.a).
set -euo pipefail
src=$1; exports=$2; out=$3; shift 3
slices=("$@")
if [ ${#slices[@]} -eq 0 ]; then
	for s in macos ios iossimulator; do [ -f "$src/$s/libwebgpu_dawn.a" ] && slices+=("$s"); done
fi
[ ${#slices[@]} -gt 0 ] || { echo "No Apple library found to link in $src" >&2; exit 1; }
here=$(cd "$(dirname "$0")" && pwd)
frameworks="-framework Metal -framework QuartzCore -framework IOSurface -framework IOKit -framework Foundation -framework CoreFoundation -framework CoreGraphics"

# One architecture of a library, thinned out of a universal one.
thin() {
	local lib=$1 arch=$2 dst=$3
	if lipo -info "$lib" | grep -q "^Non-fat"; then cp "$lib" "$dst"; else lipo "$lib" -thin "$arch" -output "$dst"; fi
}

for slice in "${slices[@]}"; do
	lib="$src/$slice/libwebgpu_dawn.a"
	case "$slice" in
		macos)
			for entry in "arm64 osx-arm64" "x86_64 osx-x64"; do
				set -- $entry
				arch=$1; rid=$2
				mkdir -p "$out/$rid"
				thin "$lib" "$arch" "$out/$rid/libwebgpu_dawn.a"
				clang++ -arch "$arch" -mmacosx-version-min=12.0 -dynamiclib -flto=thin -o "$out/$rid/libwebgpu.dylib" \
					-Wl,-force_load,"$out/$rid/libwebgpu_dawn.a" -Wl,-exported_symbols_list,"$exports/webgpu_dawn.exp" \
					-Wl,-dead_strip -install_name @rpath/libwebgpu.dylib $frameworks
				rm "$out/$rid/libwebgpu_dawn.a"
				strip -x "$out/$rid/libwebgpu.dylib"
				echo "Linked $out/$rid/libwebgpu.dylib ($(nm -gU "$out/$rid/libwebgpu.dylib" | grep -c ' _wgpu') exported)"
			done ;;
		ios | iossimulator)
			if [ "$slice" = ios ]; then entries=("arm64 ios-arm64"); else entries=("arm64 iossimulator-arm64" "x86_64 iossimulator-x64"); fi
			for entry in "${entries[@]}"; do
				set -- $entry
				arch=$1; rid=$2
				mkdir -p "$out/$rid"
				thin "$lib" "$arch" "$out/$rid/libwebgpu_dawn.thin.a"
				# The library is built with hidden visibility; the binding finds the webgpu.h functions by name.
				python3 "$here/unhide-macho.py" "$out/$rid/libwebgpu_dawn.thin.a" "$exports/webgpu_dawn.exp" "$out/$rid/libwebgpu_dawn.a"
				rm "$out/$rid/libwebgpu_dawn.thin.a"
				ranlib "$out/$rid/libwebgpu_dawn.a"
				echo "Wrote $out/$rid/libwebgpu_dawn.a"
			done ;;
		*) echo "Unknown slice: $slice" >&2; exit 1 ;;
	esac
done
