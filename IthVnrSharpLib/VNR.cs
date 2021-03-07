using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IthVnrSharpLib.Properties;
using Microsoft.Win32.SafeHandles;

// ReSharper disable UnusedMethodReturnValue.Global

namespace IthVnrSharpLib
{
	// ReSharper disable once InconsistentNaming
	[UsedImplicitly]
	public class VNR : MarshalByRefObject, IDisposable
	{
		private const string VnrDllName = "vnrhost.dll";
		private const string HookDllName = "vnrhook.dll";

		private const string IthServerMutex = "VNR_SERVER";
		private const int IhsSize = 0x80;

		private static readonly IntPtr InvalidHandleValue = IntPtr.Subtract(IntPtr.Zero, 1);
		private static readonly Dictionary<Type, int> TypeSizes = new()
		{
			{ typeof(Delegate), 4 },
			{ typeof(IntPtr), 4 },
			{ typeof(bool), 4 },
			{ typeof(int), 4 },
			{ typeof(uint), 4 },
		};

		private bool _hostOpen;
		private bool _disposed;
		private IntPtr _libraryHandle;
		// ReSharper disable CollectionNeverQueried.Local
		private readonly List<object> _antiGcList = new();
		private readonly Dictionary<IntPtr, Delegate> _antiGcDict = new();
		// ReSharper restore CollectionNeverQueried.Local
		private readonly Dictionary<string, Delegate> _externalDelegates = new();

		public override object InitializeLifetimeService() => null;

		#region Delegates
		// ReSharper disable once InconsistentNaming
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr GetThreadCallback(uint num);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int ThreadEventCallback(IntPtr thread);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int ProcessEventCallback(int pid);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int ThreadOutputFilterCallback(IntPtr thread, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] value, int len, bool newLine, IntPtr data, bool space);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SetThreadCallback(uint num, IntPtr textThreadPointer);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint RegisterPipeCallback(IntPtr text, IntPtr cmd, IntPtr thread);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint RegisterProcessRecordCallback(IntPtr processRecord, bool success);
		private delegate int RefPtrReturnIntDelegate(ref IntPtr pointerRef);
		private delegate bool HostOpenDelegate(SetThreadCallback setThread, RegisterPipeCallback registerPipe, RegisterProcessRecordCallback registerProcessRecord);
		private delegate IntPtr RegisterGetThreadDelegate(IntPtr threadTable, GetThreadCallback data);
		private delegate uint RegisterThreadEventDelegate(IntPtr hookManagerPointer, ThreadEventCallback cb);
		private delegate void RegisterOutputDelegate(IntPtr textThread, ThreadOutputFilterCallback cb, IntPtr data);
		private delegate void RegisterProcessEventDelegate(IntPtr hookManager, ProcessEventCallback cb);
		private delegate bool ReturnBoolDelegate();
		private delegate bool UIntReturnBoolDelegate(uint dword);
		private delegate IntPtr PtrAndUIntReturnPtrDelegate(IntPtr ptr, uint dword);
		private delegate IntPtr PtrReturnPtrDelegate(IntPtr ptr);
		private delegate uint PtrReturnUIntDelegate(IntPtr ptr);
		private delegate void PtrAndUIntDelegate(IntPtr ptr, uint dword);
		private delegate ushort PtrReturnUShortDelegate(IntPtr ptr);
		#endregion

		public VNR()
		{
			_libraryHandle = WinAPI.LoadLibrary(VnrDllName);
			if (_libraryHandle == IntPtr.Zero) Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
		}

		private T GetExternalDelegate<T>([CallerMemberName] string functionName = null) where T : Delegate
		{
			Debug.Assert(functionName != null, nameof(functionName) + " != null");
			if (!_externalDelegates.TryGetValue(functionName, out var func))
			{
				var numArgs = GetBytesForArgs(typeof(T));
				var funcName = $"_{functionName}@{numArgs}";
				var functionPointer = WinAPI.GetProcAddress(_libraryHandle, funcName);
				if (functionPointer == IntPtr.Zero) Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
				func = _externalDelegates[functionName] = Marshal.GetDelegateForFunctionPointer(functionPointer, typeof(T));
			}
			return (T)func;
		}
		
		private static int GetBytesForArgs(Type type)
		{
			int total = 0;
			try
			{
				var invoke = type.GetMethod("Invoke");
				if (invoke == null) return total;
				var parameters = invoke.GetParameters();
				foreach (var param in parameters)
				{
					var paramType = param.ParameterType;
					var size = paramType.IsByRef ? 4 : TypeSizes.FirstOrDefault(t => paramType == t.Key || paramType.IsSubclassOf(t.Key)).Value;
					if(size <= 0) { }
					total += size;
				}
				return total;
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
				return -1;
			}
		}
		
		private static SafeWaitHandle IthCreateMutex(string name, bool initialOwner, out bool exist)
		{
			var ret = new Mutex(initialOwner, name, out var createdNew);
			exist = !createdNew || ret.SafeWaitHandle.DangerousGetHandle() == InvalidHandleValue;
			return ret.SafeWaitHandle;
		}

		#region Host
		public bool Host_HijackProcess(uint pid) => GetExternalDelegate<UIntReturnBoolDelegate>()(pid);
		public bool Host_InjectByPID(uint processId, out string errorMessage)
		{
			bool result;
			Process currentProcess = null;
			IntPtr? procHandle = null;
			IntPtr? allocMemAddress = null;
			uint dataSize = 0;
			try
			{
				currentProcess = Process.GetCurrentProcess();
				if (currentProcess.Id == processId)
				{
					errorMessage = "Attempt to inject into own process denied.";
					return false;
				}
				bool newlyAttached = GetExternalDelegate<UIntReturnBoolDelegate>("Host_HandleCreateMutex")(processId);
				if (!newlyAttached)
				{
					errorMessage = "Mutex already existed (likely already attached to target process).";
					return false;
				}
				// getting the handle of the process - with required privileges
				procHandle = WinAPI.OpenProcess(WinAPI.ProcessAccessPriviledges, false, (int)processId);
				// searching for the address of LoadLibraryW and storing it in a pointer
				var loadLibraryAddress = WinAPI.GetProcAddress(WinAPI.GetModuleHandleA("kernel32.dll"), "LoadLibraryW");
				// name of the dll we want to inject
				string dllName = Path.GetFullPath(HookDllName);
				var dllNameBytes = Encoding.Unicode.GetBytes(dllName);
				dataSize = (uint)dllNameBytes.Length;
				// allocating some memory on the target process - enough to store the name of the dll and storing its address in a pointer
				allocMemAddress = WinAPI.VirtualAllocEx(procHandle.Value, IntPtr.Zero, dataSize,
					WinAPI.MEM_COMMIT | WinAPI.MEM_RESERVE, WinAPI.PAGE_READWRITE);
				// writing the name of the dll there
				WinAPI.WriteProcessMemory(procHandle.Value, allocMemAddress.Value, dllNameBytes, dataSize, out _);
				// creating a thread that will call LoadLibraryW with allocMemAddress as argument
				var thread = WinAPI.CreateRemoteThread(procHandle.Value, IntPtr.Zero, 0, loadLibraryAddress, allocMemAddress.Value, 0,
					IntPtr.Zero);
				if (thread != IntPtr.Zero)
				{
					var waitResult = (WinAPI.WaitReturnCode)WinAPI.WaitForSingleObject(thread, 3000);
					WinAPI.CloseHandle(thread);
					result = waitResult == WinAPI.WaitReturnCode.WAIT_OBJECT_0;
					errorMessage = result != true ? $"Failed while loading {HookDllName} library: {waitResult}" : string.Empty;
				}
				else
				{
					result = false;
					errorMessage = "Failed to create thread to load library.";
				}
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
				errorMessage = $"Failed to inject: {ex}";
				result = false;
			}
			finally
			{
				currentProcess?.Dispose();
				if(allocMemAddress != null) WinAPI.VirtualFreeEx(procHandle.Value, allocMemAddress.Value, dataSize, WinAPI.AllocationType.Release);
				if(procHandle != null) WinAPI.CloseHandle(procHandle.Value);
			}
			return result;
		}

		public bool Host_Open2(SetThreadCallback setThreadCallback, RegisterPipeCallback registerPipeCallback, RegisterProcessRecordCallback registerProcessRecordCallback, out string errorMessage)
		{
			_antiGcList.Add(setThreadCallback);
			_antiGcList.Add(registerPipeCallback);
			_antiGcList.Add(registerProcessRecordCallback);
			errorMessage = null;
			var hServerMutex = IthCreateMutex(IthServerMutex, true, out var present);
			hServerMutex.Dispose();
			if (present)
			{
				errorMessage = "VNR is already running in a different process, try closing it and trying again.";
				return false;
			}
			_hostOpen = GetExternalDelegate<HostOpenDelegate>()(setThreadCallback, registerPipeCallback, registerProcessRecordCallback);
			return _hostOpen;
		}

		public bool Host_Close() => GetExternalDelegate<ReturnBoolDelegate>()();
		public bool Host_Start() => GetExternalDelegate<ReturnBoolDelegate>()();
		public int Host_GetHookManager(ref IntPtr hookManagerPointer) => GetExternalDelegate<RefPtrReturnIntDelegate>()(ref hookManagerPointer);

		public unsafe int Host_InsertHook(IntPtr hookParam, string name, IntPtr commandHandle)
		{
			try
			{
				var hookParamName = name?.Substring(0, Math.Min(name.Length, IhsSize));
				var s = new InsertHookStruct
				{
					sp =
					{
						type = (uint) HostCommandType.HOST_COMMAND_NEW_HOOK,
						hp = Marshal.PtrToStructure<HookParam>(hookParam)
					},
					name_buffer = hookParamName == null ? IntPtr.Zero : Marshal.StringToHGlobalAuto(hookParamName)
				};
				WinAPI.NtWriteFile(commandHandle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out _, (IntPtr)(&s), IhsSize,
					IntPtr.Zero, IntPtr.Zero);
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
				return -1;
			}
			return 0;
		}
		#endregion

		#region HookManager
		public IntPtr HookManager_GetProcessRecord(IntPtr hookManager, uint pid) => GetExternalDelegate<PtrAndUIntReturnPtrDelegate>()(hookManager, pid);//Inner.HookManager.HookManager_GetProcessRecord(hookManager, pid);
		public void HookManager_RegisterProcessAttachCallback(IntPtr hookManager, ProcessEventCallback callback)
		{
			_antiGcList.Add(callback);
			GetExternalDelegate<RegisterProcessEventDelegate>()(hookManager, callback);
		}
		public void HookManager_RegisterProcessDetachCallback(IntPtr hookManager, ProcessEventCallback callback)
		{
			_antiGcList.Add(callback);
			GetExternalDelegate<RegisterProcessEventDelegate>()(hookManager, callback);
		}
		public uint HookManager_RegisterThreadCreateCallback(IntPtr hookManager, ThreadEventCallback callback)
		{
			_antiGcList.Add(callback);
			return GetExternalDelegate<RegisterThreadEventDelegate>()(hookManager, callback);
		}
		public uint HookManager_RegisterThreadRemoveCallback(IntPtr hookManager, ThreadEventCallback callback)
		{
			_antiGcList.Add(callback);
			return GetExternalDelegate<RegisterThreadEventDelegate>()(hookManager, callback);
		}
		public uint HookManager_RegisterThreadResetCallback(IntPtr hookManager, ThreadEventCallback callback)
		{
			_antiGcList.Add(callback);
			return GetExternalDelegate<RegisterThreadEventDelegate>()(hookManager, callback);
		}
		public void ThreadTable_RegisterGetThread(IntPtr hookManagerPointer, GetThreadCallback callback)
		{
			_antiGcList.Add(callback);
			GetExternalDelegate<RegisterGetThreadDelegate>("HookManager_RegisterGetThreadCallback")(hookManagerPointer, callback);
		}

		#endregion

		#region TextThread

		public IntPtr TextThread_GetThreadParameter(IntPtr textThread) => GetExternalDelegate<PtrReturnPtrDelegate>()(textThread);
		public uint TextThread_GetStatus(IntPtr textThread) => GetExternalDelegate<PtrReturnUIntDelegate>()(textThread);
		public void TextThread_SetStatus(IntPtr textThread, uint status) => GetExternalDelegate<PtrAndUIntDelegate>()(textThread, status);
		public ushort TextThread_GetNumber(IntPtr textThread) => GetExternalDelegate<PtrReturnUShortDelegate>()(textThread);
		public void TextThread_RegisterOutputCallBack(IntPtr textThread, ThreadOutputFilterCallback callback, IntPtr data)
		{
			if (callback == null) _antiGcDict.Remove(textThread);
			else _antiGcDict[textThread] = callback;
			GetExternalDelegate<RegisterOutputDelegate>()(textThread, callback, data);
		}
		#endregion

		public void Dispose()
		{
			if (_disposed) return;
			try
			{
				const int timeout = 5000;
				if (_hostOpen)
				{
					var closeTask = Task.Run(Host_Close);
					var success = closeTask.Wait(timeout);
					if (!success) StaticHelpers.LogToFile($"Timed out during {nameof(Host_Close)}");
					_hostOpen = false;
				}
				_antiGcList.Clear();
				_antiGcDict.Clear();
				_externalDelegates.Clear();
				WinAPI.FreeLibrary(_libraryHandle);
				_libraryHandle = IntPtr.Zero;
			}
			finally
			{
				_disposed = true;
			}
		}
		
		~VNR() => Dispose();

		[StructLayout(LayoutKind.Sequential)]
		private struct InsertHookStruct
		{
			public SendParam sp;
			public IntPtr name_buffer;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct SendParam
		{
			public uint type;
			public HookParam hp;
		}

		private enum HostCommandType
		{
			// ReSharper disable InconsistentNaming
			// ReSharper disable UnusedMember.Local
			HOST_COMMAND = -1 // null type
			, HOST_COMMAND_NEW_HOOK = 0
			, HOST_COMMAND_REMOVE_HOOK = 1
			, HOST_COMMAND_MODIFY_HOOK = 2
			, HOST_COMMAND_HIJACK_PROCESS = 3
			, HOST_COMMAND_DETACH = 4
			// ReSharper restore InconsistentNaming
			// ReSharper restore UnusedMember.Local
		}
	}
}
