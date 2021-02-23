using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace IthVnrSharpLib
{
	public class PipeAndProcessRecordMap : MarshalByRefObject
	{
		public override object InitializeLifetimeService() => null;
		public HookManagerWrapper HookManager;

		private readonly List<PipeWithProcessRecord> _items = new();

		public IntPtr GetCommandHandle(int pid)
		{
			var record = _items.FirstOrDefault(i => i.Process.pid_register == pid);
			return record?.Cmd ?? IntPtr.Zero;
		}

		internal uint RegisterPipe(IntPtr text, IntPtr cmd, IntPtr thread)
		{
			var lastItemWithoutPipe = _items.LastOrDefault(p => !p.HasPipe);
			if (lastItemWithoutPipe == null) _items.Add(new PipeWithProcessRecord(text, cmd, thread));
			else lastItemWithoutPipe.AddPipe(text, cmd, thread);
			return 0;
		}

		public uint RegisterProcessRecord(IntPtr processRecordPtr, bool success)
		{
			var processRecord = Marshal.PtrToStructure<ProcessRecord>(processRecordPtr);
			if (success) HookManager?.ConsoleOutput($"Failed to attach process: pid_register {processRecord.pid_register}", true);
			var lastItemWithoutProcess = _items.LastOrDefault(p => !p.HasProcess);
			if (lastItemWithoutProcess == null) _items.Add(new PipeWithProcessRecord(processRecord));
			else lastItemWithoutProcess.Process = processRecord;
			return 0;
		}

		public void RemoveProcess(int pid)
		{
			_items.RemoveAll(i => i.Process.pid_register == pid);
		}
	}
}