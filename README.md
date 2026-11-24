# Uno.Dawn

Native libraries of [Dawn](https://dawn.googlesource.com/dawn), Google's WebGPU implementation, packaged as `Uno.Dawn` for the WebGPU renderer of [Uno Platform](https://platform.uno/).

## What the package holds

| Platform | Library | Where it comes from |
|---|---|---|
| Windows x64, arm64 | `webgpu.dll`, static C++ runtime | built from Dawn's source |
| Linux x64, arm64 | `libwebgpu.so`, glibc 2.34 or later | built from Dawn's source against an Ubuntu 22.04 sysroot |
| macOS x64, arm64 | `libwebgpu.dylib` | built from Dawn's source |
| Android arm64, arm, x64, x86 | `libwebgpu.so` | built from Dawn's source |
| iOS device and simulator | `libwebgpu_dawn.a`, linked into the app | built from Dawn's source |

Every library is built from Dawn's source here, in the configuration of Dawn's own release builds; only the headers come from Dawn's release. Each shared library exports the functions of `webgpu.h`. The iOS libraries keep Dawn's hidden visibility, except for those functions, which the binding finds by name in the app.

The libraries are built for size, which roughly halves them compared to Dawn's release configuration (Linux x64 11.5 → 5.2 MB, Windows x64 11.1 → 6.7 MB, Android arm64 10.0 → 5.3 MB, macOS arm64 4.9 → 3.8 MB; an iOS app's binary shrinks by 0.9 MB):

- SPIR-V validation and Tint's SPIR-V reader are left out. Dawn validates the SPIR-V it generates only behind a debug toggle, and the reader serves only SPIR-V shader modules, which WebGPU apps don't create.
- The code is optimized for size, in one section per function and data item so the linker drops what's unused: `-Os` with ThinLTO and identical-code folding on clang (without ThinLTO for the iOS static libraries, which apps link with their own Xcode), `/O1 /Gy /Gw` on MSVC (whole-program optimization isn't used: the library outgrows what `lib.exe` can hold).

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
   - macOS and iOS: `build-source-apple.sh`, then `link-apple.sh`.
3. `pack.py <payload> <exports> <Dawn source> <output> <version>`. `--partial` packs only the platforms present, for a local test.

## License

The scripts in this repository are licensed under the [Apache 2.0 license](LICENSE.md). Dawn is licensed under the BSD 3-Clause License; the package carries its license and those of the third-party code built into it.
