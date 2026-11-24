#!/usr/bin/env bash
# Builds Dawn's static library from source for Android ABIs with the NDK, in the layout of Dawn's Android release
# archive (<out>/<abi>/libwebgpu_dawn.a), so link-android.sh links either. Same configuration as Dawn's mobile build
# (.github/workflows/dawn-ci.cmake), without samples, tests or tools, and built for size with ThinLTO (see README.md).
# Usage: build-source-android.sh <dir to clone into> <out dir> [abi...]
# The ABIs default to arm64-v8a armeabi-v7a x86_64 x86. Needs ANDROID_NDK_HOME (or ANDROID_NDK_ROOT), cmake and ninja.
set -euo pipefail
src=$1; out=$2; shift 2
abis=("$@")
[ ${#abis[@]} -gt 0 ] || abis=(arm64-v8a armeabi-v7a x86_64 x86)
here=$(cd "$(dirname "$0")" && pwd)
ndk=${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:?Set ANDROID_NDK_HOME}}
commit=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['commit'])" "$here/../dawn.json")
if [ ! -d "$src/.git" ]; then
	git init -q "$src"
	git -C "$src" remote add origin https://dawn.googlesource.com/dawn
fi
if [ "$(git -C "$src" rev-parse HEAD 2>/dev/null)" != "$commit" ]; then
	git -C "$src" fetch -q --depth 1 origin "$commit"
	git -C "$src" checkout -q --force FETCH_HEAD
fi

# The lowest Android version an Uno app supports (Uno.Sdk's SupportedOSPlatformVersion), as in link-android.sh.
api=24
size_flags="-Os -ffunction-sections -fdata-sections -flto=thin -DNDEBUG"
for abi in "${abis[@]}"; do
	build="$src/out/android-$abi"
	cmake -S "$src" -B "$build" -G Ninja \
		-C "$src/.github/workflows/dawn-ci.cmake" \
		-DCMAKE_TOOLCHAIN_FILE="$ndk/build/cmake/android.toolchain.cmake" \
		-DANDROID_ABI=$abi -DANDROID_PLATFORM=android-$api \
		-DCMAKE_BUILD_TYPE=Release \
		-DCMAKE_C_COMPILER_LAUNCHER= -DCMAKE_CXX_COMPILER_LAUNCHER= \
		-DDAWN_BUILD_MONOLITHIC_LIBRARY=STATIC \
		-DDAWN_BUILD_SAMPLES=OFF -DDAWN_BUILD_TESTS=OFF \
		-DTINT_BUILD_TESTS=OFF -DTINT_BUILD_CMD_TOOLS=OFF -DTINT_BUILD_IR_BINARY=OFF -DDAWN_BUILD_PROTOBUF=OFF \
		-DDAWN_ENABLE_SPIRV_VALIDATION=OFF -DTINT_BUILD_SPV_READER=OFF \
		-DCMAKE_C_FLAGS_RELEASE="$size_flags" -DCMAKE_CXX_FLAGS_RELEASE="$size_flags"
	cmake --build "$build" --target webgpu_dawn
	mkdir -p "$out/$abi"
	cp "$(find "$build" -name libwebgpu_dawn.a | head -1)" "$out/$abi/"
	echo "Built $out/$abi/libwebgpu_dawn.a"
done
