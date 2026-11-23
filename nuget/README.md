# Uno.Dawn

Google's [Dawn](https://dawn.googlesource.com/dawn) WebGPU implementation, as the native library of the Uno Platform WebGPU renderer. You don't reference this package yourself: the Uno Platform WebGPU package depends on it, and Dawn is the library the renderer uses unless the app sets `UnoWebGpuNative` to `WgpuNative`.

| Platform | What ships |
|---|---|
| Windows x64, arm64 | `runtimes/<rid>/native/webgpu.dll`, linked against the static C++ runtime |
| Linux x64, arm64 | `runtimes/<rid>/native/libwebgpu.so` (glibc 2.34 or later) |
| macOS x64, arm64 | `runtimes/<rid>/native/libwebgpu.dylib` (macOS 12 or later) |
| Android arm64, arm, x64, x86 | `runtimes/<rid>/native/libwebgpu.so` |
| iOS device and simulator | a static library linked into the app |

`lib/net10.0/Uno.Dawn.dll` holds `DawnLibrary`, what the renderer's binding, written against wgpu-native, needs to run on Dawn.

On Windows, Dawn compiles shaders with the system's `d3dcompiler_47.dll` and uses the system's Vulkan loader, so nothing else ships with it.

The Windows and Linux libraries are built from Dawn's source, and the Android, macOS and iOS ones are linked from Dawn's release builds, by the scripts of the [uno.dawn repository](https://github.com/unoplatform/uno.dawn). Dawn is licensed under the BSD 3-Clause License; `LICENSE.txt` in this package carries it, along with the licenses of the third-party code built into it.
