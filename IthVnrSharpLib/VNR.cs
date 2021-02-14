using System;
using System.Collections.Generic;
using System.Diagnostics;
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
	public class VNR : MarshalByRefObject
	{
		public const string VnrDll = "vnrhost.dll";

		public override object InitializeLifetimeService() => null;

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate int ThreadEventCallback(IntPtr thread);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate int ProcessEventCallback(int pid);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void ConsoleCallback(string text);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate int ThreadOutputFilterCallback(IntPtr thread, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] value, int len, bool newLine, IntPtr data, bool space);


		// ReSharper disable once CollectionNeverQueried.Local
		private readonly List<Delegate> _antiGcList = new();

		// ReSharper disable once CollectionNeverQueried.Local
		private readonly Dictionary<IntPtr, Delegate> _antiGcDict = new();

		// ReSharper disable InconsistentNaming
		private const string ITH_CLIENT_MUTEX = "VNR_CLIENT";   // ITH_DLL_RUNNING
		private const string ITH_SERVER_MUTEX = "VNR_SERVER";   // ITH_RUNNING
		private const string ITH_SERVER_HOOK_MUTEX = "VNR_SERVER_HOOK";   // original
																																			// ReSharper restore InconsistentNaming
		private static readonly IntPtr InvalidHandleValue = IntPtr.Subtract(IntPtr.Zero, -1);

		public bool Host_HijackProcess(uint pid) => Inner.Host.Host_HijackProcess(pid);
		public bool Host_InjectByPID(uint pid) => Injector.InjectIntoProcess(pid);
		public bool Host_Open(out string errorMessage)
		{
			errorMessage = null;
			var hServerMutex = IthCreateMutex(ITH_SERVER_MUTEX, true, out var present);
			hServerMutex.Dispose();
			if (present)
			{
				errorMessage = "VNR is already running in a different process, try closing it and trying again.";
				return false;
			}
			return Inner.Host.Host_Open();
		}

		private SafeWaitHandle IthCreateMutex(string name, bool initialOwner, out bool exist)
		{
			var ret = new Mutex(initialOwner, name, out var createdNew);
			exist = !createdNew || ret.SafeWaitHandle.DangerousGetHandle() == InvalidHandleValue;
			return ret.SafeWaitHandle;
		}

		#region Host
		public bool Host_Close() => Inner.Host.Host_Close();
		public bool Host_IthInitSystemService() => Inner.Host.Host_IthInitSystemService();
		public bool Host_IthCloseSystemService() => Inner.Host.Host_IthCloseSystemService();
		public bool Host_Start() => Inner.Host.Host_Start();
		public int Host_GetHookManager(ref IntPtr hookManagerPointer) => Inner.Host.Host_GetHookManager(ref hookManagerPointer);
		public int Host_InsertHook(int pid, IntPtr hookParam, string name) => Inner.Host.Host_InsertHook(pid, hookParam, name);
		#endregion

		#region HookManager
		public IntPtr HookManager_GetThreadTable(IntPtr hookManagerPointer) => Inner.HookManager.HookManager_GetThreadTable(hookManagerPointer);
		public IntPtr HookManager_GetProcessRecord(IntPtr hookManager, uint pid) => Inner.HookManager.HookManager_GetProcessRecord(hookManager, pid);
		public void HookManager_RegisterProcessAttachCallback(IntPtr hookManager, ProcessEventCallback callback)
		{
			_antiGcList.Add(callback);
			Inner.HookManager.HookManager_RegisterProcessAttachCallback(hookManager, callback);
		}
		public void HookManager_RegisterProcessDetachCallback(IntPtr hookManager, ProcessEventCallback callback)
		{
			_antiGcList.Add(callback);
			Inner.HookManager.HookManager_RegisterProcessDetachCallback(hookManager, callback);
		}
		public uint HookManager_RegisterThreadCreateCallback(IntPtr hookManager, ThreadEventCallback callback)
		{
			_antiGcList.Add(callback);
			return Inner.HookManager.HookManager_RegisterThreadCreateCallback(hookManager, callback);
		}
		public uint HookManager_RegisterThreadRemoveCallback(IntPtr hookManager, ThreadEventCallback callback)
		{
			_antiGcList.Add(callback);
			return Inner.HookManager.HookManager_RegisterThreadRemoveCallback(hookManager, callback);
		}
		public uint HookManager_RegisterThreadResetCallback(IntPtr hookManager, ThreadEventCallback callback)
		{
			_antiGcList.Add(callback);
			return Inner.HookManager.HookManager_RegisterThreadResetCallback(hookManager, callback);
		}
		#endregion

		#region TextThread
		public IntPtr TextThread_GetThreadParameter(IntPtr textThread) => Inner.TextThread.TextThread_GetThreadParameter(textThread);
		public uint TextThread_GetStatus(IntPtr textThread) => Inner.TextThread.TextThread_GetStatus(textThread);
		public void TextThread_SetStatus(IntPtr textThread, uint status) => Inner.TextThread.TextThread_SetStatus(textThread, status);
		public ushort TextThread_GetNumber(IntPtr textThread) => Inner.TextThread.TextThread_GetNumber(textThread);
		public void TextThread_RegisterOutputCallBack(IntPtr textThread, ThreadOutputFilterCallback callback, IntPtr data)
		{
			if (callback == null) _antiGcDict.Remove(textThread);
			else _antiGcDict[textThread] = callback;
			Inner.TextThread.TextThread_RegisterOutputCallBack(textThread, callback, data);
		}
		#endregion

		#region ThreadTable
		public IntPtr ThreadTable_FindTextThread(IntPtr threadTable, uint number) => Inner.ThreadTable.ThreadTable_FindTextThread(threadTable, number);
		#endregion
		
		private static class Inner
		{
			internal static class Host
			{
				[DllImport(VnrDll)]
				[return: MarshalAs(UnmanagedType.Bool)]
				public static extern bool Host_HijackProcess(uint pid);

				[DllImport(VnrDll)]
				[return: MarshalAs(UnmanagedType.Bool)]
				public static extern bool Host_Open();

				[DllImport(VnrDll)]
				[return: MarshalAs(UnmanagedType.Bool)]
				public static extern bool Host_Close();

				[DllImport(VnrDll)]
				[return: MarshalAs(UnmanagedType.Bool)]
				public static extern bool Host_IthInitSystemService();

				[DllImport(VnrDll)]
				[return: MarshalAs(UnmanagedType.Bool)]
				public static extern bool Host_IthCloseSystemService();

				[DllImport(VnrDll)]
				[return: MarshalAs(UnmanagedType.Bool)]
				public static extern bool Host_Start();

				[DllImport(VnrDll)]
				[return: MarshalAs(UnmanagedType.Bool)]
				public static extern int Host_UnLink(int from);

				[DllImport(VnrDll)]
				public static extern uint Host_AddLink(uint from, uint to);

				[DllImport(VnrDll)]
				public static extern int Host_InsertHook(int pid, IntPtr hookParam, string name);

				[DllImport(VnrDll)]
				public static extern int Host_GetHookManager(ref IntPtr hookManagerPointer);

				[DllImport(VnrDll)]
				public static extern void Host_GetHookName([MarshalAs(UnmanagedType.LPStr)] StringBuilder str, uint parameterPid, uint parameterHook, uint length);
			}

			internal static class HookManager
			{
				[DllImport(VnrDll)]
				public static extern IntPtr HookManager_GetThreadTable(IntPtr hookManager);

				[DllImport(VnrDll)]
				public static extern uint HookManager_RegisterThreadCreateCallback(IntPtr hookManagerPointer, ThreadEventCallback cb);

				[DllImport(VnrDll)]
				public static extern uint HookManager_RegisterThreadRemoveCallback(IntPtr hookManagerPointer, ThreadEventCallback cb);

				[DllImport(VnrDll)]
				public static extern uint HookManager_RegisterThreadResetCallback(IntPtr hookManagerPointer, ThreadEventCallback cb);

				[DllImport(VnrDll)]
				public static extern IntPtr HookManager_GetProcessRecord(IntPtr hookManager, uint pid);

				[DllImport(VnrDll, CallingConvention = CallingConvention.StdCall)]
				public static extern void HookManager_RegisterConsoleCallback(IntPtr hookManager, ConsoleCallback consoleOutput);

				[DllImport(VnrDll)]
				public static extern void HookManager_RegisterAddRemoveLinkCallback(IntPtr hookManager, ThreadEventCallback addRemoveLink);

				[DllImport(VnrDll)]
				public static extern void HookManager_RegisterProcessNewHookCallback(IntPtr hookManager, ProcessEventCallback refreshProfileOnNewHook);

				[DllImport(VnrDll)]
				public static extern void HookManager_RegisterProcessDetachCallback(IntPtr hookManager, ProcessEventCallback removeProcessList);

				[DllImport(VnrDll)]
				public static extern void HookManager_RegisterProcessAttachCallback(IntPtr hookManager, ProcessEventCallback registerProcessList);

				[DllImport(VnrDll, CallingConvention = CallingConvention.StdCall)]
				public static extern void HookManager_AddConsoleOutput(IntPtr hookManager, [MarshalAs(UnmanagedType.LPWStr)] string text);
			}

			internal static class TextThread
			{
				[DllImport(VnrDll, CallingConvention = CallingConvention.StdCall)]
				public static extern void TextThread_RegisterOutputCallBack(IntPtr textThread, ThreadOutputFilterCallback cb, IntPtr data);

				[DllImport(VnrDll)]
				public static extern void TextThread_RegisterFilterCallBack(IntPtr textThread, ThreadOutputFilterCallback cb, IntPtr data);

				[DllImport(VnrDll)]
				public static extern IntPtr TextThread_GetThreadParameter(IntPtr textThread);

				[DllImport(VnrDll)]
				public static extern uint TextThread_GetStatus(IntPtr textThread);

				[DllImport(VnrDll)]
				public static extern void TextThread_SetStatus(IntPtr textThread, uint status);

				[DllImport(VnrDll)]
				public static extern ushort TextThread_GetNumber(IntPtr textThread);

				[DllImport(VnrDll)]
				public static extern bool TextThread_GetEntryString(IntPtr textThread, StringBuilder str, int len);

				[DllImport(VnrDll)]
				public static extern bool SetTextThreadUnicodeStatus(IntPtr textThread, IntPtr processRecord, uint hook);

				[DllImport(VnrDll, CharSet = CharSet.Unicode)]
				public static extern void TextThread_GetLink(IntPtr textThread, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder str, int len);

				[DllImport(VnrDll)]
				[return: MarshalAs(UnmanagedType.LPStr)]
				public static extern string TextThread_GetThreadString(IntPtr textThread);
			}

			internal static class ThreadTable
			{
				[DllImport(VNR.VnrDll)]
				public static extern IntPtr ThreadTable_FindTextThread(IntPtr threadTable, uint number);
			}
			
			[DllImport(VnrDll)]
			public static extern int Settings_GetSplittingInterval();

			[DllImport(VnrDll)]
			public static extern void Settings_SetSplittingInterval(int interval);

			[DllImport(VnrDll)]
			public static extern bool Settings_GetClipboardFlag();

			[DllImport(VnrDll)]
			public static extern void Settings_SetClipboardFlag(bool flag);
		}

		public static class InnerP
		{
			[DllImport(VnrDll)]
			[return: MarshalAs(UnmanagedType.Bool)]
			public static extern bool Host_HandleCreateMutex(uint pid);
		}

		#region Obsolete
		// ReSharper disable UnusedMember.Global

		[Obsolete("Use HookManager.AddLink")]
		public uint Host_AddLink(uint from, uint to) => Inner.Host.Host_AddLink(from, to);
		[Obsolete("Use TextThread.LinkTo = null")]
		public int Host_UnLink(int from) => Inner.Host.Host_UnLink(from);

		[Obsolete("ClipboardFlag handled in C#")]
		public bool Settings_GetClipboardFlag() => Inner.Settings_GetClipboardFlag();
		[Obsolete("ClipboardFlag handled in C#")]
		public void Settings_SetClipboardFlag(bool flag) => Inner.Settings_SetClipboardFlag(flag);

		[Obsolete("SplittingInterval handled in C#")]
		public int Settings_GetSplittingInterval() => Inner.Settings_GetSplittingInterval();
		[Obsolete("SplittingInterval handled in C#")]
		public void Settings_SetSplittingInterval(int interval) => Inner.Settings_SetSplittingInterval(interval);

		[Obsolete("Unused")]
		public void TextThread_RegisterFilterCallBack(IntPtr hookManager, ThreadOutputFilterCallback callback, IntPtr data)
		{
			_antiGcList.Add(callback);
			Inner.TextThread.TextThread_RegisterFilterCallBack(hookManager, callback, data);
		}

		[Obsolete("Using C# Links (HookManager.AddLink)")]
		public void HookManager_RegisterAddRemoveLinkCallback(IntPtr hookManager, ThreadEventCallback callback)
		{
			_antiGcList.Add(callback);
			Inner.HookManager.HookManager_RegisterAddRemoveLinkCallback(hookManager, callback);
		}

		[Obsolete("Use C# Console Thread")]
		public void HookManager_AddConsoleOutput(IntPtr hookManager, string text) => Inner.HookManager.HookManager_AddConsoleOutput(hookManager, text);

		[Obsolete("Unused")]
		public void HookManager_RegisterProcessNewHookCallback(IntPtr hookManager, ProcessEventCallback callback)
		{
			_antiGcList.Add(callback);
			Inner.HookManager.HookManager_RegisterProcessNewHookCallback(hookManager, callback);
		}

		[Obsolete("Use C# Console Thread")]
		public void HookManager_RegisterConsoleCallback(IntPtr hookManager, ConsoleCallback callback)
		{
			_antiGcList.Add(callback);
			Inner.HookManager.HookManager_RegisterConsoleCallback(hookManager, callback);
		}

		[Obsolete("Use TextThread.SetEntryString")]
		public bool TextThread_GetEntryString(IntPtr textThread, ref StringBuilder str, int len) => Inner.TextThread.TextThread_GetEntryString(textThread, str, len);

		[Obsolete("Use TextThread.SetEntryString")]
		public string TextThread_GetThreadString(IntPtr textThread) => Inner.TextThread.TextThread_GetThreadString(textThread);

		[Obsolete("Use TextThread.SetEntryString")]
		public string Host_GetHookName(uint parameterPid, uint parameterHook)
		{
			var str = new StringBuilder(512);
			Inner.Host.Host_GetHookName(str, parameterPid, parameterHook, 512);
			return str.ToString();
		}

		[Obsolete("Use TextThread.SetUnicodeStatus")]
		public bool SetTextThreadUnicodeStatus(IntPtr textThread, IntPtr processRecord, uint hook) => Inner.TextThread.SetTextThreadUnicodeStatus(textThread, processRecord, hook);

		[Obsolete("Use TextThread.GetLink")]
		public string TextThread_GetLink(IntPtr textThread)
		{
			var str = new StringBuilder(512);
			Inner.TextThread.TextThread_GetLink(textThread, str, 512);
			return str.ToString();
		}
		// ReSharper restore UnusedMember.Global
		#endregion

		public void Exit()
		{
			const int timeout = 5000;
			var closeTask = Task.Run(Host_Close);
			var success = closeTask.Wait(timeout);
			if(!success) Debug.WriteLine($"Timed out during {nameof(Host_Close)}");
			var closeSystem = Task.Run(Host_IthCloseSystemService);
			success = closeSystem.Wait(timeout);
			if (!success) Debug.WriteLine($"Timed out during {nameof(Host_IthCloseSystemService)}");
			_antiGcList.Clear();
			_antiGcDict.Clear();
		}
	}
}
