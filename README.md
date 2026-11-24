# Uno.Dawn

Native libraries of [Dawn](https://dawn.googlesource.com/dawn), Google's WebGPU implementation, packaged as `Uno.Dawn` for the WebGPU renderer of [Uno Platform](https://platform.uno/).

## What the package holds

| Platform | Library | Where it comes from |
|---|---|---|
| Windows x64, arm64 | `webgpu.dll`, static C++ runtime | built from Dawn's source |
| Linux x64, arm64 | `libwebgpu.so`, glibc 2.34 or later | built from Dawn's source against an Ubuntu 22.04 sysroot |
| macOS x64, arm64 | `libwebgpu.dylib` | linked from Dawn's release |
| Android arm64, arm, x64, x86 | `libwebgpu.so` | built from Dawn's source |
| iOS device and simulator | `libwebgpu_dawn.a`, linked into the app | Dawn's release |

Dawn's releases ship static libraries only, so every shared library is linked here, exporting the functions of `webgpu.h`.

The libraries built from source are built for size, which roughly halves them (Linux x64 11.5 → 5.2 MB, Windows x64 11.1 → 6.7 MB, Android arm64 10.0 → 5.3 MB):

- SPIR-V validation and Tint's SPIR-V reader are left out. Dawn validates the SPIR-V it generates only behind a debug toggle, and the reader serves only SPIR-V shader modules, which WebGPU apps don't create.
- The code is optimized for size, in one section per function and data item so the linker drops what's unused: `-Os` with ThinLTO and identical-code folding on clang, `/O1 /Gy /Gw` on MSVC (whole-program optimization isn't used: the library outgrows what `lib.exe` can hold).

The package also carries `Uno.Dawn.dll` ([`src/Uno.Dawn`](src/Uno.Dawn)), what a WebGPU binding written against wgpu-native needs to run on Dawn: telling the two libraries apart, creating a Dawn instance (its C++ descriptor laid out for each ABI), and stand-ins for the wgpu-native extensions Dawn lacks, such as `wgpuDevicePoll`. It calls Dawn through the exports of a library handle the caller passes, so it works wherever the caller found the library.

## Updating Dawn

The Dawn release is pinned in [`dawn.json`](dawn.json): the release tag and its commit. Change both to move to another release; CI builds and packs every platform from them.

## Building locally

The scripts in [`build/`](build) are the ones CI runs ([`.github/workflows/ci.yml`](.github/workflows/ci.yml) shows the order). Each one documents its arguments. In short:

1. `fetch.py headers <dir>`, then `exports.py <headers>/include/dawn/webgpu.h <exports dir>`: the list of functions to export.
2. Per platform:
   - Windows: `build-source-windows.ps1`, then `link-windows.ps1`.
   - Linux: `make-sysroot.sh`, `build-source-linux.sh`, then `link-linux.sh`.
   - Android: `build-source-android.sh`, then `link-android.sh`.
   - macOS and iOS: `fetch.py apple`, then `link-apple.sh`.
3. `pack.py <payload> <exports> <Dawn source> <output> <version>`. `--partial` packs only the platforms present, for a local test.

## License

The scripts in this repository are licensed under the [Apache 2.0 license](LICENSE.md). Dawn is licensed under the BSD 3-Clause License; the package carries its license and those of the third-party code built into it.
