using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace IthVnrSharpLib
{
	internal static class Injector
	{
		private static readonly int OwnProcessId;

		static Injector()
		{
			var currentProcess = Process.GetCurrentProcess();
			OwnProcessId = currentProcess.Id;
			currentProcess.Dispose();
		}

		/// <summary>
		/// Inject a number of DLLs into the process,in order given.
		/// </summary>
		public static bool Inject(uint processId, out string errorMessage, string[] dlls, Func<uint, bool> isNewlyAttached)
		{
			bool result;
			try
			{
				if (OwnProcessId == processId)
				{
					errorMessage = "Attempt to inject into own process denied.";
					return false;
				}
				bool newlyAttached = isNewlyAttached(processId);
				if (!newlyAttached)
				{
					errorMessage = "Mutex already existed (likely already attached to target process).";
					return false;
				}
				// getting the handle of the process - with required privileges
				// searching for the address of LoadLibraryW and storing it in a pointer
				result = InjectDlls((int)processId, dlls, out errorMessage);
				//result = InjectOld(procHandle, hookEmbed, out errorMessage);
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
				errorMessage = $"Failed to inject: {ex}";
				result = false;
			}
			return result;
		}

		private static bool InjectDlls(int processId, string[] dlls, out string errorMessage)
		{
			errorMessage = string.Empty;
			var memoryHandles = new List<(IntPtr handle, uint size)>();
			var procHandle = IntPtr.Zero;
			try
			{
				procHandle = WinAPI.OpenProcess(WinAPI.ProcessAccessPriviledges, false, processId);
				var loadLibraryAddress = WinAPI.GetProcAddress(WinAPI.GetModuleHandleA("kernel32.dll"), "LoadLibraryW");
				//order of DLLs is important, dependencies should be injected before the dependent library.
				foreach (var dll in dlls)
				{
					var result = InjectDll(procHandle, out errorMessage, dll, loadLibraryAddress, out var dataSize, out var allocMemAddress);
					StaticHelpers.LogToDebug($"{nameof(InjectDll)} ({processId},'{dll}'):{result}");
					memoryHandles.Add((allocMemAddress, dataSize));
					if (!result) return false;
				}
			}
			finally
			{
				foreach (var (handle, size) in memoryHandles.AsEnumerable().Reverse())
				{
					if (handle != IntPtr.Zero) WinAPI.VirtualFreeEx(procHandle, handle, size, WinAPI.AllocationType.Release);
				}
				if (procHandle != IntPtr.Zero) WinAPI.CloseHandle(procHandle);
			}
			return true;
		}

		private static bool InjectDll(IntPtr procHandle, out string errorMessage, string dll, IntPtr loadLibraryAddress,
			out uint dataSize, out IntPtr allocMemAddress)
		{
			dataSize = 0;
			allocMemAddress = IntPtr.Zero;
			string dllName = Path.GetFullPath(dll);
			if (!File.Exists(dllName))
			{
				dllName = Path.GetFullPath(Path.GetFileNameWithoutExtension(dll) + "d" + Path.GetExtension(dll));
				if (!File.Exists(dllName))
				{
					errorMessage = $"DLL did not exist: '{dllName}'";
					return false;
				}
			}
			var dllNameBytes = Encoding.Unicode.GetBytes(dllName);
			dataSize = (uint)dllNameBytes.Length;
			// allocating some memory on the target process - enough to store the name of the dll and storing its address in a pointer
			allocMemAddress = WinAPI.VirtualAllocEx(procHandle, IntPtr.Zero, dataSize,
				WinAPI.MEM_COMMIT | WinAPI.MEM_RESERVE, WinAPI.PAGE_READWRITE);
			// writing the name of the dll there
			WinAPI.WriteProcessMemory(procHandle, allocMemAddress, dllNameBytes, dataSize, out _);
			// creating a thread that will call LoadLibraryW with allocMemAddress as argument
			var thread = WinAPI.CreateRemoteThread(procHandle, IntPtr.Zero, 0, loadLibraryAddress, allocMemAddress, 0, IntPtr.Zero);
			if (thread == IntPtr.Zero)
			{
				errorMessage = "Failed to create thread to load library.";
				return false;
			}
			var waitResult = (WinAPI.WaitReturnCode)WinAPI.WaitForSingleObject(thread, 3000);
			WinAPI.CloseHandle(thread);
			var result = waitResult == WinAPI.WaitReturnCode.WAIT_OBJECT_0;
			errorMessage = result != true ? $"Failed while loading {dll} library (timed out?): {waitResult}" : string.Empty;
			return result;
		}

	}
}
