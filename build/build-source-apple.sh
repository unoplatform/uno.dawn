#!/usr/bin/env bash
# Builds Dawn's static library from source for Apple platforms with Xcode's clang. Same configuration as Dawn's Apple
# build (.github/workflows/ci.yml, mobile-apple), without samples, tests or tools, and built for size (see README.md).
# Usage: build-source-apple.sh <dir to clone into> <out dir> [macos|ios|iossimulator...]
# The slices default to all three. Writes <out>/<slice>/libwebgpu_dawn.a: macos holds arm64 and x86_64 as ThinLTO
# bitcode, for link-apple.sh to link into a dylib; ios (arm64) and iossimulator (arm64, x86_64) hold machine code with
# hidden visibility, as Dawn's release does, since apps link them with their own toolchain.
# Needs cmake, ninja and python3.
set -euo pipefail
src=$1; out=$2; shift 2
slices=("$@")
[ ${#slices[@]} -gt 0 ] || slices=(macos ios iossimulator)
here=$(cd "$(dirname "$0")" && pwd)
commit=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['commit'])" "$here/../dawn.json")
if [ ! -d "$src/.git" ]; then
	git init -q "$src"
	git -C "$src" remote add origin https://dawn.googlesource.com/dawn
fi
if [ "$(git -C "$src" rev-parse HEAD 2>/dev/null)" != "$commit" ]; then
	git -C "$src" fetch -q --depth 1 origin "$commit"
	git -C "$src" checkout -q --force FETCH_HEAD
fi

size_flags="-Os -ffunction-sections -fdata-sections -DNDEBUG"
for slice in "${slices[@]}"; do
	# macOS as in Dawn's release; iOS at Uno's default floor, also the oldest Xcode's libc++ still supports.
	case "$slice" in
		macos) platform=(-DCMAKE_OSX_ARCHITECTURES="arm64;x86_64" -DCMAKE_OSX_DEPLOYMENT_TARGET=12.0)
			flags="$size_flags -flto=thin" ;;
		ios) platform=(-DCMAKE_SYSTEM_NAME=iOS -DCMAKE_OSX_SYSROOT=iphoneos -DCMAKE_OSX_ARCHITECTURES=arm64
				-DCMAKE_OSX_DEPLOYMENT_TARGET=15.0)
			flags="$size_flags -fvisibility=hidden -fvisibility-inlines-hidden" ;;
		iossimulator) platform=(-DCMAKE_SYSTEM_NAME=iOS -DCMAKE_OSX_SYSROOT=iphonesimulator
				-DCMAKE_OSX_ARCHITECTURES="arm64;x86_64" -DCMAKE_OSX_DEPLOYMENT_TARGET=15.0)
			flags="$size_flags -fvisibility=hidden -fvisibility-inlines-hidden" ;;
		*) echo "Unknown slice: $slice" >&2; exit 1 ;;
	esac
	build="$src/out/apple-$slice"
	cmake -S "$src" -B "$build" -G Ninja \
		-DDAWN_MOBILE_BUILD=apple \
		-C "$src/.github/workflows/dawn-ci.cmake" \
		"${platform[@]}" \
		-DCMAKE_TRY_COMPILE_TARGET_TYPE=STATIC_LIBRARY \
		-DCMAKE_BUILD_TYPE=Release \
		-DDAWN_BUILD_MONOLITHIC_LIBRARY=STATIC \
		-DDAWN_BUILD_SAMPLES=OFF -DDAWN_BUILD_TESTS=OFF -DDAWN_USE_GLFW=OFF \
		-DTINT_BUILD_TESTS=OFF -DTINT_BUILD_CMD_TOOLS=OFF -DTINT_BUILD_IR_BINARY=OFF -DDAWN_BUILD_PROTOBUF=OFF \
		-DDAWN_ENABLE_SPIRV_VALIDATION=OFF -DTINT_BUILD_SPV_READER=OFF \
		-DCMAKE_C_FLAGS_RELEASE="$flags" -DCMAKE_CXX_FLAGS_RELEASE="$flags" \
		-DCMAKE_OBJC_FLAGS_RELEASE="$flags" -DCMAKE_OBJCXX_FLAGS_RELEASE="$flags"
	cmake --build "$build" --target webgpu_dawn
	mkdir -p "$out/$slice"
	cp "$(find "$build" -name libwebgpu_dawn.a | head -1)" "$out/$slice/"
	echo "Built $out/$slice/libwebgpu_dawn.a ($(lipo -archs "$out/$slice/libwebgpu_dawn.a"))"
done
