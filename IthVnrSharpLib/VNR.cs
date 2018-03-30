using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using IthVnrSharpLib.Properties;
// ReSharper disable UnusedMethodReturnValue.Global

namespace IthVnrSharpLib
{
	// ReSharper disable once InconsistentNaming
	[UsedImplicitly]
	public class VNR : MarshalByRefObject
	{
		public override object InitializeLifetimeService() => null;

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate int ThreadEventCallback(IntPtr thread);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate int ProcessEventCallback(int pid);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate void ConsoleCallback(string text);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		public delegate int ThreadOutputFilterCallback(IntPtr thread, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]byte[] value, int len, bool newLine, IntPtr data, bool space);

		private const string VnrDll = "vnrhost.dll";

		// ReSharper disable once CollectionNeverQueried.Local
		private readonly List<object> _antiGcList = new List<object>();

		public bool Host_HijackProcess(uint pid) => Inner.Host_HijackProcess(pid);
		public bool Host_InjectByPID(uint pid) => Injector.InjectIntoProcess(pid);
		public bool Host_Open() => Inner.Host_Open();
		public bool Host_Close() => Inner.Host_Close();
		public bool Host_IthInitSystemService() => Inner.Host_IthInitSystemService();
		public bool Host_IthCloseSystemService() => Inner.Host_IthCloseSystemService();
		public bool Host_Start() => Inner.Host_Start();
		public int Host_GetHookManager(ref IntPtr hookManagerPointer) => Inner.Host_GetHookManager(ref hookManagerPointer);
		public IntPtr HookManager_FindSingle(IntPtr hookManager, int number) => Inner.HookManager_FindSingle(hookManager, number);
		public IntPtr HookManager_GetProcessRecord(IntPtr hookManager, uint pid) => Inner.HookManager_GetProcessRecord(hookManager, pid);
		public IntPtr TextThread_GetThreadParameter(IntPtr textThread) => Inner.TextThread_GetThreadParameter(textThread);
		public uint TextThread_GetStatus(IntPtr textThread) => Inner.TextThread_GetStatus(textThread);
		public void TextThread_SetStatus(IntPtr textThread, uint status) => Inner.TextThread_SetStatus(textThread, status);
		public ushort TextThread_GetNumber(IntPtr textThread) => Inner.TextThread_GetNumber(textThread);
		public int Host_InsertHook(int pid, IntPtr hookParam, string name) => Inner.Host_InsertHook(pid, hookParam, name);

		public void HookManager_RegisterProcessAttachCallback(IntPtr hookManager, ProcessEventCallback callback)
		{
			_antiGcList.Add(callback);
			Inner.HookManager_RegisterProcessAttachCallback(hookManager, callback);
		}

		public void HookManager_RegisterProcessDetachCallback(IntPtr hookManager, ProcessEventCallback callback)
		{
			_antiGcList.Add(callback);
			Inner.HookManager_RegisterProcessDetachCallback(hookManager, callback);
		}

		public uint HookManager_RegisterThreadCreateCallback(IntPtr hookManager, ThreadEventCallback callback)
		{
			_antiGcList.Add(callback);
			return Inner.HookManager_RegisterThreadCreateCallback(hookManager, callback);
		}

		public uint HookManager_RegisterThreadRemoveCallback(IntPtr hookManager, ThreadEventCallback callback)
		{
			_antiGcList.Add(callback);
			return Inner.HookManager_RegisterThreadRemoveCallback(hookManager, callback);
		}

		public uint HookManager_RegisterThreadResetCallback(IntPtr hookManager, ThreadEventCallback callback)
		{
			_antiGcList.Add(callback);
			return Inner.HookManager_RegisterThreadResetCallback(hookManager, callback);
		}

		public void TextThread_RegisterOutputCallBack(IntPtr hookManager, ThreadOutputFilterCallback callback, IntPtr data)
		{
			_antiGcList.Add(callback);
			Inner.TextThread_RegisterOutputCallBack(hookManager, callback, data);
		}
		

		private static class Inner
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
			public static extern uint HookManager_RegisterThreadCreateCallback(IntPtr hookManagerPointer, ThreadEventCallback cb);

			[DllImport(VnrDll)]
			public static extern uint HookManager_RegisterThreadRemoveCallback(IntPtr hookManagerPointer, ThreadEventCallback cb);

			[DllImport(VnrDll)]
			public static extern uint HookManager_RegisterThreadResetCallback(IntPtr hookManagerPointer, ThreadEventCallback cb);

			[DllImport(VnrDll, CallingConvention = CallingConvention.StdCall)]
			public static extern void TextThread_RegisterOutputCallBack(IntPtr textThread, ThreadOutputFilterCallback cb, IntPtr data);

			[DllImport(VnrDll)]
			public static extern void TextThread_RegisterFilterCallBack(IntPtr textThread, ThreadOutputFilterCallback cb, IntPtr data);

			[DllImport(VnrDll)]
			public static extern IntPtr HookManager_GetProcessRecord(IntPtr hookManager, uint pid);

			[DllImport(VnrDll)]
			public static extern IntPtr TextThread_GetThreadParameter(IntPtr textThread);

			[DllImport(VnrDll)]
			public static extern IntPtr HookManager_FindSingle(IntPtr hookManager, int number);

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

			[DllImport(VnrDll)]
			public static extern int Settings_GetSplittingInterval();

			[DllImport(VnrDll)]
			public static extern void Settings_SetSplittingInterval(int interval);

			[DllImport(VnrDll)]
			public static extern bool Settings_GetClipboardFlag();

			[DllImport(VnrDll)]
			public static extern void Settings_SetClipboardFlag(bool flag);

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
			public static extern void TextThread_GetLink(IntPtr textThread, [MarshalAs(UnmanagedType.LPWStr)]StringBuilder str, int len);

			[DllImport(VnrDll)]
			[return: MarshalAs(UnmanagedType.LPStr)]
			public static extern string TextThread_GetThreadString(IntPtr textThread);

			[DllImport(VnrDll)]
			public static extern void Host_GetHookName([MarshalAs(UnmanagedType.LPStr)]StringBuilder str, uint parameterPid, uint parameterHook,uint length);
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
		public uint Host_AddLink(uint from, uint to) => Inner.Host_AddLink(from, to);
		[Obsolete("Use TextThread.LinkTo = null")]
		public int Host_UnLink(int from) => Inner.Host_UnLink(from);

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
			Inner.TextThread_RegisterFilterCallBack(hookManager, callback, data);
		}

		[Obsolete("Using C# Links (HookManager.AddLink)")]
		public void HookManager_RegisterAddRemoveLinkCallback(IntPtr hookManager, ThreadEventCallback callback)
		{
			_antiGcList.Add(callback);
			Inner.HookManager_RegisterAddRemoveLinkCallback(hookManager, callback);
		}

		[Obsolete("Use C# Console Thread")]
		public void HookManager_AddConsoleOutput(IntPtr hookManager, string text) => Inner.HookManager_AddConsoleOutput(hookManager, text);
		
		[Obsolete("Unused")]
		public void HookManager_RegisterProcessNewHookCallback(IntPtr hookManager, ProcessEventCallback callback)
		{
			_antiGcList.Add(callback);
			Inner.HookManager_RegisterProcessNewHookCallback(hookManager, callback);
		}

		[Obsolete("Use C# Console Thread")]
		public void HookManager_RegisterConsoleCallback(IntPtr hookManager, ConsoleCallback callback)
		{
			_antiGcList.Add(callback);
			Inner.HookManager_RegisterConsoleCallback(hookManager, callback);
		}
		
		[Obsolete("Use TextThread.SetEntryString")]
		public bool TextThread_GetEntryString(IntPtr textThread, ref StringBuilder str, int len) => Inner.TextThread_GetEntryString(textThread, str, len);

		[Obsolete("Use TextThread.SetEntryString")]
		public string TextThread_GetThreadString(IntPtr textThread) => Inner.TextThread_GetThreadString(textThread);

		[Obsolete("Use TextThread.SetEntryString")]
		public string Host_GetHookName(uint parameterPid, uint parameterHook)
		{
			var str = new StringBuilder(512);
			Inner.Host_GetHookName(str, parameterPid, parameterHook, 512);
			return str.ToString();
		}
		
		[Obsolete("Use TextThread.SetUnicodeStatus")]
		public bool SetTextThreadUnicodeStatus(IntPtr textThread, IntPtr processRecord, uint hook) => Inner.SetTextThreadUnicodeStatus(textThread, processRecord, hook);

		[Obsolete("Use TextThread.GetLink")]
		public string TextThread_GetLink(IntPtr textThread)
		{
			var str = new StringBuilder(512);
			Inner.TextThread_GetLink(textThread, str, 512);
			return str.ToString();
		}
		// ReSharper restore UnusedMember.Global
		#endregion

	}
}
