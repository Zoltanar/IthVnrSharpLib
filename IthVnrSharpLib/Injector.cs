using System;
using System.Diagnostics;
using System.IO;
using System.Text;
// ReSharper disable InconsistentNaming

namespace IthVnrSharpLib
{
	internal static class Injector
	{
		private static string HookDllName = "vnrhook.dll";

		public static bool InjectIntoProcess(uint processId, out string errorMessage)
		{
			bool result;
			Process currentProcess = null;
			try
			{
				currentProcess = Process.GetCurrentProcess();
				if (currentProcess.Id == processId)
				{
					errorMessage = "Attempt to inject into own process denied.";
					return false;
				}

				bool newlyAttached = VNR.Inner.Host.Host_HandleCreateMutex(processId);
				if (!newlyAttached)
				{

					errorMessage = "Mutex already existed (likely already attached to target process).";
					return false;
				}
				// geting the handle of the process - with required privileges
				var procHandle = WinAPI.OpenProcess(WinAPI.ProcessAccessPriviledges, false, (int) processId);
				// searching for the address of LoadLibraryW and storing it in a pointer
				var loadLibraryAddr = WinAPI.GetProcAddress(WinAPI.GetModuleHandleA("kernel32.dll"), "LoadLibraryW");
				// name of the dll we want to inject
				string dllName = Path.GetFullPath(HookDllName);
				var dllNameBytes = Encoding.Unicode.GetBytes(dllName);
				uint dataSize = (uint) dllNameBytes.Length;
				// allocating some memory on the target process - enough to store the name of the dll and storing its address in a pointer
				var allocMemAddress = WinAPI.VirtualAllocEx(procHandle, IntPtr.Zero, dataSize,
					WinAPI.MEM_COMMIT | WinAPI.MEM_RESERVE, WinAPI.PAGE_READWRITE);
				// writing the name of the dll there
				WinAPI.WriteProcessMemory(procHandle, allocMemAddress, dllNameBytes, dataSize, out _);
				// creating a thread that will call LoadLibraryW with allocMemAddress as argument
				var thread = WinAPI.CreateRemoteThread(procHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0,
					IntPtr.Zero);
				if (thread != IntPtr.Zero)
				{
					var waitResult = (WinAPI.WaitReturnCode) WinAPI.WaitForSingleObject(thread, 3000);
					WinAPI.CloseHandle(thread);
					result = waitResult == WinAPI.WaitReturnCode.WAIT_OBJECT_0;
					errorMessage = result != true ? $"Failed while loading {HookDllName} library: {waitResult}" : string.Empty;
				}
				else
				{
					result = false;
					errorMessage = "Failed to create thread to load library.";
				}

				WinAPI.VirtualFreeEx(procHandle, allocMemAddress, dataSize, WinAPI.AllocationType.Release);
				WinAPI.CloseHandle(procHandle);
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
			}
			return result;
		}
	}
}