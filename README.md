# Uno.Dawn

Native libraries of [Dawn](https://dawn.googlesource.com/dawn), Google's WebGPU implementation, packaged as `Uno.Dawn` for the WebGPU renderer of [Uno Platform](https://platform.uno/).

## What the package holds

| Platform | Library | Where it comes from |
|---|---|---|
| Windows x64, arm64 | `webgpu.dll`, static C++ runtime | built from Dawn's source |
| Linux x64, arm64 | `libwebgpu.so`, glibc 2.34 or later | built from Dawn's source against an Ubuntu 22.04 sysroot |
| macOS x64, arm64 | `libwebgpu.dylib` | linked from Dawn's release |
| Android arm64, arm, x64, x86 | `libwebgpu.so` | linked from Dawn's release |
| iOS device and simulator | `libwebgpu_dawn.a`, linked into the app | Dawn's release |

Dawn's releases ship static libraries only, so every shared library is linked here, exporting the functions of `webgpu.h`.

## Updating Dawn

The Dawn release is pinned in [`dawn.json`](dawn.json): the release tag and its commit. Change both to move to another release; CI builds and packs every platform from them.

## Building locally

The scripts in [`build/`](build) are the ones CI runs ([`.github/workflows/ci.yml`](.github/workflows/ci.yml) shows the order). Each one documents its arguments. In short:

1. `fetch.py headers <dir>`, then `exports.py <headers>/include/dawn/webgpu.h <exports dir>`: the list of functions to export.
2. Per platform:
   - Windows: `build-source-windows.ps1`, then `link-windows.ps1`.
   - Linux: `make-sysroot.sh`, `build-source-linux.sh`, then `link-linux.sh`.
   - Android: `fetch.py android`, then `link-android.sh`.
   - macOS and iOS: `fetch.py apple`, then `link-apple.sh`.
3. `pack.py <payload> <exports> <Dawn source> <output> <version>`. `--partial` packs only the platforms present, for a local test.

## License

The scripts in this repository are licensed under the [Apache 2.0 license](LICENSE.md). Dawn is licensed under the BSD 3-Clause License; the package carries its license and those of the third-party code built into it.
