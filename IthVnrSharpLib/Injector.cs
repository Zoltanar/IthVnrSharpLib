using System;
using System.IO;
using System.Text;
// ReSharper disable InconsistentNaming

namespace IthVnrSharpLib
{
	static class Injector
	{
		public static bool InjectIntoProcess(uint processId)
		{
			bool ret = false;
			try
			{
				bool newlyAttached = VNR.InnerP.Host_HandleCreateMutex(processId);
				if (!newlyAttached) return false;
				// geting the handle of the process - with required privileges
				IntPtr procHandle = WinAPI.OpenProcess(WinAPI.ProcessAccessPriviledges, false, (int)processId);
				// searching for the address of LoadLibraryW and storing it in a pointer
				IntPtr loadLibraryAddr = WinAPI.GetProcAddress(WinAPI.GetModuleHandleA("kernel32.dll"), "LoadLibraryW");
				// name of the dll we want to inject
				string dllName = Path.GetFullPath("vnrhook.dll");
				var dllNameBytes = Encoding.Unicode.GetBytes(dllName);
				uint dataSize = (uint)dllNameBytes.Length;
				// alocating some memory on the target process - enough to store the name of the dll and storing its address in a pointer
				IntPtr allocMemAddress = WinAPI.VirtualAllocEx(procHandle, IntPtr.Zero, dataSize, WinAPI.MEM_COMMIT | WinAPI.MEM_RESERVE, WinAPI.PAGE_READWRITE);
				// writing the name of the dll there
				WinAPI.WriteProcessMemory(procHandle, allocMemAddress, dllNameBytes, dataSize, out _);
				// creating a thread that will call LoadLibraryW with allocMemAddress as argument
				var thread = WinAPI.CreateRemoteThread(procHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero);
				if (thread != IntPtr.Zero)
				{
					WinAPI.WaitForSingleObject(thread, 3000);
					WinAPI.CloseHandle(thread);
					ret = true;
				}

				WinAPI.VirtualFreeEx(procHandle, allocMemAddress, dataSize, WinAPI.AllocationType.Release);
				WinAPI.CloseHandle(procHandle);
			}
			catch (Exception ex)
			{
				StaticHelpers.LogToFile(ex);
			}

			return ret;
		}
	}
}
