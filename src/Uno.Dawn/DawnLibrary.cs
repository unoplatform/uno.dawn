using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Uno.Dawn;

/// <summary>
/// What a WebGPU binding written against wgpu-native needs to run on Dawn: telling the two apart, creating a Dawn
/// instance, and stand-ins for the wgpu-native extensions Dawn does not have.
/// </summary>
/// <remarks>
/// Dawn is called through the exports of the library handle passed in, never through <c>[DllImport]</c>, so it works
/// wherever the caller found the library, including a static link into the main program (iOS).
/// </remarks>
public static unsafe class DawnLibrary
{
	/// <summary>
	/// <c>WGPUFeatureName_ImplicitDeviceSynchronization</c>, Dawn's own feature: the device locks itself around every call.
	/// Without it a Dawn device must only be used from one thread at a time, which wgpu-native does not require.
	/// </summary>
	public const uint ImplicitDeviceSynchronizationFeature = 0x00050004;

	private const uint DawnInstanceDescriptorSType = 0x00050006;
	private const uint DawnTogglesDescriptorSType = 0x0005000A;
	private const uint TimedWaitAnyInstanceFeature = 0x00000001;
	private const uint CallbackModeAllowProcessEvents = 0x00000002;
	private const int BackendValidationDisabled = 2;

	/// <summary>Dawn's library file name on the current operating system, as the Uno.Dawn package ships it.</summary>
	public static string FileName
		=> OperatingSystem.IsWindows() ? "webgpu.dll"
		: OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst() ? "libwebgpu.dylib"
		: "libwebgpu.so";

	/// <summary>
	/// Whether a loaded WebGPU library is Dawn rather than wgpu-native. Both implement webgpu.h; only wgpu-native exports
	/// its own extensions, <c>wgpuDevicePoll</c> among them.
	/// </summary>
	public static bool IsDawn(nint library)
		=> library != 0 && !NativeLibrary.TryGetExport(library, "wgpuDevicePoll", out _);

	/// <summary>
	/// Functions with wgpu-native's signatures, keyed by name, to use in place of the library's on Dawn:
	/// <c>wgpuCreateInstance</c> creates the instance with Dawn's descriptor (system shader compiler and Vulkan loader on
	/// Windows, backend validation off, timed waits on), and <c>wgpuQueueSubmitForIndex</c>, <c>wgpuDevicePoll</c>,
	/// <c>wgpuSetLogCallback</c> and <c>wgpuSetLogLevel</c> stand in for wgpu-native's extensions.
	/// </summary>
	/// <remarks>
	/// The submission index and the poll are built on webgpu.h's futures: an index stands for its queue's work-done
	/// event, and a blocking poll waits on it. Dawn has no logging switch through webgpu.h, so the logging functions do
	/// nothing.
	/// </remarks>
	public static IReadOnlyDictionary<string, nint> GetWgpuNativeFunctions(nint library)
	{
		Api.Bind(library);
		return new Dictionary<string, nint>
		{
			["wgpuCreateInstance"] = (nint)(delegate* unmanaged[Cdecl]<nint, nint>)&CreateInstance,
			["wgpuQueueSubmitForIndex"] = (nint)(delegate* unmanaged[Cdecl]<nint, nuint, nint, ulong>)&QueueSubmitForIndex,
			["wgpuDevicePoll"] = (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint, uint>)&DevicePoll,
			["wgpuSetLogCallback"] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&SetLogCallback,
			["wgpuSetLogLevel"] = (nint)(delegate* unmanaged[Cdecl]<int, void>)&SetLogLevel,
		};
	}

	/// <summary>
	/// Size of the adapter toggles <see cref="WriteAdapterToggles"/> writes, or 0 when none are needed on this platform.
	/// </summary>
	public static int AdapterTogglesSize => OperatingSystem.IsWindows() ? sizeof(TogglesDescriptor) : 0;

	/// <summary>
	/// Writes a <c>WGPUDawnTogglesDescriptor</c> to chain onto adapter request options, in front of <paramref name="next"/>.
	/// On Windows it turns off <c>use_dxc</c>: Dawn's D3D12 backend compiles shaders with DXC otherwise, which is not part
	/// of Windows, while FXC (<c>d3dcompiler_47.dll</c>) is. <paramref name="destination"/> must hold
	/// <see cref="AdapterTogglesSize"/> bytes and outlive the request.
	/// </summary>
	public static void WriteAdapterToggles(nint destination, nint next)
	{
		*(TogglesDescriptor*)destination = new TogglesDescriptor
		{
			Next = next,
			SType = DawnTogglesDescriptorSType,
			DisabledToggleCount = 1,
			DisabledToggles = DisabledToggles,
		};
	}

	// Kept for the process: Dawn may hold on to the strings.
	private static readonly nint DisabledToggles = StringList("use_dxc");

	// Dawn opens its shader compilers and the Vulkan loader from its own folder and these paths only, never through the
	// system's search order. Windows ships d3dcompiler_47.dll and vulkan-1.dll in System32, so Dawn finds them there
	// instead of the app shipping its own.
	private static readonly nint SearchPaths = OperatingSystem.IsWindows()
		? StringList(Environment.SystemDirectory + System.IO.Path.DirectorySeparatorChar)
		: 0;

	private static nint StringList(params string[] values)
	{
		var list = Marshal.AllocHGlobal(IntPtr.Size * values.Length);
		for (var i = 0; i < values.Length; i++)
		{
			Marshal.WriteIntPtr(list, i * IntPtr.Size, Marshal.StringToCoTaskMemUTF8(values[i]));
		}
		return list;
	}

	private static nint _instance;
	private static long _submissions;
	private static readonly ConcurrentDictionary<ulong, ulong> _pending = new();

	private static int Align(int offset, int alignment) => (offset + alignment - 1) / alignment * alignment;

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static nint CreateInstance(nint descriptor)
	{
		// dawn::native::DawnInstanceDescriptor (DawnNative.h) is C++: it derives from the 2-field chained struct, and
		// outside MSVC the derived fields start in the base's tail padding. So its fields are written at each ABI's offsets.
		var p = IntPtr.Size;
		var count = OperatingSystem.IsWindows() ? Align(p + 4, p) : p + 4;
		var paths = Align(count + 4, p);
		var validation = paths + 2 * p;
		var size = Align(validation + 5, p) + 4 * p;
		var dawn = stackalloc byte[size];
		new Span<byte>(dawn, size).Clear();
		*(uint*)(dawn + p) = DawnInstanceDescriptorSType;
		*(uint*)(dawn + count) = SearchPaths == 0 ? 0u : 1u;
		*(nint*)(dawn + paths) = SearchPaths;
		*(int*)(dawn + validation) = BackendValidationDisabled;

		// The blocking poll waits on a future with a timeout, which Dawn only allows with this instance feature.
		var feature = TimedWaitAnyInstanceFeature;
		var instance = new InstanceDescriptor { NextInChain = (nint)dawn, RequiredFeatureCount = 1, RequiredFeatures = (nint)(&feature) };
		return _instance = Api.CreateInstance((nint)(&instance));
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static ulong QueueSubmitForIndex(nint queue, nuint count, nint commands)
	{
		Api.QueueSubmit(queue, count, commands);
		var index = (ulong)Interlocked.Increment(ref _submissions);
		_pending[index] = Api.QueueOnSubmittedWorkDone(queue, WorkDone((nint)index));
		return index;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static uint DevicePoll(nint device, uint wait, nint wrappedSubmissionIndex)
	{
		if (wait != 0)
		{
			ulong future;
			if (wrappedSubmissionIndex != 0)
			{
				// Already settled (and forgotten) when the event fired during an earlier poll.
				var index = ((WrappedSubmissionIndex*)wrappedSubmissionIndex)->SubmissionIndex;
				future = _pending.TryGetValue(index, out var id) ? id : 0;
			}
			else
			{
				// Everything submitted so far.
				var queue = Api.DeviceGetQueue(device);
				future = Api.QueueOnSubmittedWorkDone(queue, WorkDone(0));
				Api.QueueRelease(queue);
			}

			if (future != 0)
			{
				var info = new FutureWaitInfo { Future = future };
				_ = Api.InstanceWaitAny(_instance, 1, (nint)(&info), ulong.MaxValue);
			}
		}

		Api.InstanceProcessEvents(_instance);
		return 1;
	}

	private static QueueWorkDoneCallbackInfo WorkDone(nint index) => new()
	{
		Mode = CallbackModeAllowProcessEvents,
		Callback = (nint)(delegate* unmanaged[Cdecl]<uint, StringView, nint, nint, void>)&OnWorkDone,
		Userdata1 = index,
	};

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static void OnWorkDone(uint status, StringView message, nint index, nint unused)
	{
		if (index != 0)
		{
			_pending.TryRemove((ulong)index, out _);
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static void SetLogCallback(nint callback, nint userdata)
	{
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static void SetLogLevel(int level)
	{
	}

	// The webgpu.h functions used above, from the library's exports.
	private static class Api
	{
		private static delegate* unmanaged[Cdecl]<nint, nint> _createInstance;
		private static delegate* unmanaged[Cdecl]<nint, nuint, nint, void> _queueSubmit;
		private static delegate* unmanaged[Cdecl]<nint, QueueWorkDoneCallbackInfo, Future> _queueOnSubmittedWorkDone;
		private static delegate* unmanaged[Cdecl]<nint, nint> _deviceGetQueue;
		private static delegate* unmanaged[Cdecl]<nint, void> _queueRelease;
		private static delegate* unmanaged[Cdecl]<nint, nuint, nint, ulong, uint> _instanceWaitAny;
		private static delegate* unmanaged[Cdecl]<nint, void> _instanceProcessEvents;

		public static void Bind(nint library)
		{
			_createInstance = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(library, "wgpuCreateInstance");
			_queueSubmit = (delegate* unmanaged[Cdecl]<nint, nuint, nint, void>)NativeLibrary.GetExport(library, "wgpuQueueSubmit");
			_queueOnSubmittedWorkDone = (delegate* unmanaged[Cdecl]<nint, QueueWorkDoneCallbackInfo, Future>)NativeLibrary.GetExport(library, "wgpuQueueOnSubmittedWorkDone");
			_deviceGetQueue = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(library, "wgpuDeviceGetQueue");
			_queueRelease = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(library, "wgpuQueueRelease");
			_instanceWaitAny = (delegate* unmanaged[Cdecl]<nint, nuint, nint, ulong, uint>)NativeLibrary.GetExport(library, "wgpuInstanceWaitAny");
			_instanceProcessEvents = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(library, "wgpuInstanceProcessEvents");
		}

		public static nint CreateInstance(nint descriptor) => _createInstance(descriptor);

		public static void QueueSubmit(nint queue, nuint count, nint commands) => _queueSubmit(queue, count, commands);

		// Returns the WGPUFuture's id. The struct must be returned as a struct: 32-bit ABIs (armeabi-v7a, x86) return it
		// through a hidden pointer, not in the registers a bare uint64 uses.
		public static ulong QueueOnSubmittedWorkDone(nint queue, QueueWorkDoneCallbackInfo info) => _queueOnSubmittedWorkDone(queue, info).Id;

		public static nint DeviceGetQueue(nint device) => _deviceGetQueue(device);

		public static void QueueRelease(nint queue) => _queueRelease(queue);

		public static uint InstanceWaitAny(nint instance, nuint count, nint infos, ulong timeout) => _instanceWaitAny(instance, count, infos, timeout);

		public static void InstanceProcessEvents(nint instance) => _instanceProcessEvents(instance);
	}

	// webgpu.h structures, as declared there.

	[StructLayout(LayoutKind.Sequential)]
	private struct InstanceDescriptor
	{
		public nint NextInChain;
		public nuint RequiredFeatureCount;
		public nint RequiredFeatures;
		public nint RequiredLimits;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Future
	{
		public ulong Id;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct FutureWaitInfo
	{
		public ulong Future;
		public uint Completed;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct QueueWorkDoneCallbackInfo
	{
		public nint NextInChain;
		public uint Mode;
		public nint Callback;
		public nint Userdata1;
		public nint Userdata2;
	}

	// WGPUWrappedSubmissionIndex (wgpu.h, wgpu-native's header).
	[StructLayout(LayoutKind.Sequential)]
	private struct WrappedSubmissionIndex
	{
		public nint Queue;
		public ulong SubmissionIndex;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct StringView
	{
		public nint Data;
		public nuint Length;
	}

	// WGPUDawnTogglesDescriptor (dawn/webgpu.h): a chained struct, then the enabled and disabled toggle lists.
	[StructLayout(LayoutKind.Sequential)]
	private struct TogglesDescriptor
	{
		public nint Next;
		public uint SType;
		public nuint EnabledToggleCount;
		public nint EnabledToggles;
		public nuint DisabledToggleCount;
		public nint DisabledToggles;
	}
}
